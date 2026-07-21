// Game Window
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2025-2026 OpenRCT3 Contributors. All rights reserved.

using DryIoc;
using DryIoc.ImTools;
using NLog;
using OpenCobra.GDK;
using OpenCobra.GDK.Platform;
using OpenRCT3.Simulation;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Storage;
using Windows.System;
using Windowing = Silk.NET.Windowing;

namespace OpenRCT3.Platforms.Windows;

internal partial class GameWindow : Form, IWindow {
  private readonly static Logger logger = LogManager.GetCurrentClassLogger();

  private readonly Dictionary<Delegate, EventHandler> handlerMap = [];
  private readonly GameLoopCloseCoordinator closeCoordinator = new();
  private readonly ManualResetEvent rendererCreated = new(false);
  private readonly Stopwatch stopwatch = new();
  private IRenderer? renderer;
  private CancellationTokenSource closing = new();
  private bool isClosing = false;

  private event Action<double>? UpdateView;

  public GameWindow() {
    logger.Trace("Initializing game window...");
    InitializeComponent();

    SystemMenu.AddItems(this);

    Game.IoC.RegisterInstance<IWindow>(this);
    Game.IoC.Register<IInputContext>(
      Reuse.Scoped,
      Made.Of(r => ServiceInfo.Of<IWindow>(), window => window.CreateInput(Arg.Of<IWindow>())),
      // The input abstraction is kinda heavy, so let services dispose it
      Setup.With(trackDisposableTransient: true, allowDisposableTransient: true)
    );
    InputWindowExtensions.Add(this);

    // Required: The OpenGL surface is not created otherwise
    if (!Visible) Show();
    Initialize();
    logger.Trace("Game window created");
  }

  public string Title { get => base.Text; set => Text = value; }

  protected override bool ShowWithoutActivation => IsAutomationEnabled;

  protected override CreateParams CreateParams {
    get {
      const int WS_EX_NOACTIVATE = 134217728;
      var parameters = base.CreateParams;
      if (IsAutomationEnabled) parameters.ExStyle |= WS_EX_NOACTIVATE;
      return parameters;
    }
  }

  public Dpi Dpi {
    get {
      using var g = CreateGraphics();
      return new Dpi(g.DpiX / 96f, g.DpiY / 96f);
    }
  }

  #region IView Properties
  // FIXME: This doesn't take the pixel density into account
  [Category("GPU")]
  public Vector2D<int> FramebufferSize => new(glSurface.ClientSize.Width, glSurface.ClientSize.Height);

  [Category("Behavior")]
  public bool IsClosing => isClosing;

  [Category("Behavior")]
  public double Time => stopwatch.Elapsed.TotalSeconds;

  [Category("GPU")]
  public bool IsInitialized => true;

  [Category("Behavior")]
  public bool ShouldSwapAutomatically {
    get => false;
    set {}
  }

  [Category("Behavior")]
  public bool IsEventDriven {
    get => true;
    set {}
  }

  [Category("Behavior")]
  public bool IsContextControlDisabled {
    get => false;
    set {}
  }

  [Category("Behavior")]
  public double FramesPerSecond {
    get => Game.Instance?.TargetFrameRate ?? 60;
    set => Game.Instance?.TargetFrameRate = Convert.ToInt32(
      Math.Round(value, 0, MidpointRounding.AwayFromZero)
    );
  }

  [Category("Behavior")]
  public double UpdatesPerSecond {
    get => Game.Instance is { } game
      ? GameLoopTiming.UpdatesPerSecond(game.TargetUpdateRate)
      : 60;
    set {
      var targetUpdateRate = GameLoopTiming.UpdateInterval(value);
      if (Game.Instance != null) Game.Instance.TargetUpdateRate = targetUpdateRate;
    }
  }

  [Browsable(false)]
  public Windowing.GraphicsAPI API {
    get {
      var flags = Windowing.ContextFlags.Default;
      if (glSurface.Settings.Flags.HasFlag(ContextFlagMask.ForwardCompatibleBit))
        flags |= Windowing.ContextFlags.ForwardCompatible;
      if (glSurface.Settings.Flags.HasFlag(ContextFlagMask.DebugBit))
        flags |= Windowing.ContextFlags.Debug;

      return new Windowing.GraphicsAPI {
        API = Windowing.ContextAPI.OpenGL,
        Profile = glSurface.Settings.Profile switch {
          ContextProfileMask.CompatibilityProfileBit => Windowing.ContextProfile.Compatability,
          ContextProfileMask.CoreProfileBit => Windowing.ContextProfile.Core,
          _ => throw new InvalidOperationException()
        },
        Flags = flags,
        Version = new Windowing.APIVersion(glSurface.Settings.Version)
      };
    }
  }

  [Category("GPU")]
  public bool VSync {
    get => Game.Instance?.VSync ?? false;
    set => Game.Instance?.VSync = value;
  }

  [Category("GPU")]
  public Windowing.VideoMode VideoMode => new(FramebufferSize, Game.Instance!.TargetFrameRate);

  [Category("GPU")]
  public int? PreferredDepthBufferBits => OpenGL.GLContext.PreferredDepthBufferBits;

  [Category("GPU")]
  public int? PreferredStencilBufferBits => OpenGL.GLContext.PreferredStencilBufferBits;

  [Category("GPU")]
  public Vector4D<int>? PreferredBitDepth => new(OpenGL.GLContext.PreferredColorDepth);

  [Category("GPU")]
  public int? Samples => renderer?.MsaaSamples;

  [Browsable(false)]
  public IGLContext? GLContext => glSurface.Context;

  [Category("GPU")]
  public IVkSurface? VkSurface => null;

  [Browsable(false)]
  public INativeWindow? Native => null;

  [Category("GPU")]
  Vector2D<int> Windowing.IViewProperties.Size => new(ClientSize.Width, ClientSize.Height);
  #endregion

  #region IView Events
  public event Action<Vector2D<int>>? FramebufferResize;
  public event Action<bool>? FocusChanged;
  public event Action<double>? Render;

  event Action<Vector2D<int>>? Windowing.IView.Resize {
    add {
      if (value == null) return;
      void handler(object? s, EventArgs e) => value(FramebufferSize);
      handlerMap[value] = handler;
      Resize += handler;
    }
    remove {
      handlerMap.Remove(value!);
      Resize -= handlerMap[value!];
    }
  }

  event Action? Windowing.IView.Closing {
    add {
      if (value == null) return;
      // Wait for our own Closing event handlers to complete
      void handler(object? s, EventArgs e) => Invoke(() => {
        if (isClosing) value();
      });
      handlerMap[value] = handler;
      Closing += handler;
    }
    remove {
      handlerMap.Remove(value!);
      Closing -= handlerMap[value!].To<CancelEventHandler>();
    }
  }

  event Action? Windowing.IView.Load {
    add {
      if (value == null) return;
      void handler(object? s, EventArgs e) => value();
      handlerMap[value] = handler;
      Load += handler;
    }
    remove {
      handlerMap.Remove(value!);
      Load -= handlerMap[value!];
    }
  }

  event Action<double>? Windowing.IView.Update {
    add => UpdateView += value;
    remove => UpdateView -= value;
  }
  #endregion

  // FIXME: This method ought be extracted into a platform-independent base-class.
  public void Start() {
    stopwatch.Start();
    Debug.Assert(renderer != null, "Renderer should be created before starting the game.");
    // Keyboard adapters subscribe to the OpenGL surface, so select it before the game loop starts.
    // Mouse clicks also focus it, but camera keys should work before the first click.
    glSurface.Select();
    if (Game.Instance == null) {
      logger.Trace("Starting game...");
      var game = new Game();
      // Game construction finishes loading and framing the scene before the worker loop starts.
      // Force that first scene through WM_PAINT now so startup never depends on a later resize,
      // activation, or other incidental Windows message to present the back buffer.
      glSurface.PresentFrame(() => game.Scene.Update(game.TargetFrameTime));
      logger.Debug("Presented initial scene frame");
      closeCoordinator.Track(Task.Run(game.Run));
    } else {
      logger.Trace("Resuming game...");
      Game.Instance.Resume();
    }
  }

  internal object GetAutomationState() {
    var game = Game.Instance
      ?? throw new InvalidOperationException("The game is not running.");
    var camera = game.Scene.Camera;
    var mapPath = Environment.GetEnvironmentVariable("OPENRCT3_MAP_PATH")
      ?? game.Config.MapPath;
    var pathBounds = GetPathBounds(game);
    return new {
      process_id = Environment.ProcessId,
      map_path = mapPath,
      framebuffer = new[] { FramebufferSize.X, FramebufferSize.Y },
      camera_eye = new[] { camera.Eye.X, camera.Eye.Y, camera.Eye.Z },
      camera_target = new[] { camera.Target.X, camera.Target.Y, camera.Target.Z },
      camera_distance = camera.Distance,
      camera_minimum_distance = camera.MinimumDistance,
      scene_model_count = game.Scene.Models.Count,
      path_bounds = pathBounds,
      path_surface_groups = GetPathSurfaceGroups(game),
      is_paused = game.IsPaused,
      is_closing = IsClosing
    };
  }

  internal object ApplyAutomationCamera(GameAutomationCameraRequest request) {
    var game = Game.Instance
      ?? throw new InvalidOperationException("The game is not running.");
    var targetValues = new[] { request.TargetX, request.TargetY, request.TargetZ };
    if (targetValues.Any(value => value.HasValue) && targetValues.Any(value => !value.HasValue))
      throw new ArgumentException("target_x, target_y, and target_z must be supplied together.");
    if (targetValues.All(value => value.HasValue)) {
      var target = new System.Numerics.Vector3(
        request.TargetX!.Value,
        request.TargetY!.Value,
        request.TargetZ!.Value);
      game.Scene.Camera.Pan(target - game.Scene.Camera.Target);
    }
    if (request.Distance.HasValue) game.Scene.Camera.SetDistance(request.Distance.Value);
    using var controller = new CameraController(game.Scene.Camera);
    controller.Update(
      TimeSpan.FromSeconds(request.PanSeconds),
      new CameraControlInput(
        request.PanRight,
        request.PanForward,
        0f,
        request.ZoomSteps,
        MouseOrbitRadians: request.OrbitDegrees * MathF.PI / 180f,
        MouseElevationRadians: request.ElevationDegrees * MathF.PI / 180f));
    game.Scene.Camera.Update(glSurface.AspectRatio);
    glSurface.PresentFrame();
    return GetAutomationState();
  }

  internal object FrameAutomationTerrain() {
    var game = Game.Instance
      ?? throw new InvalidOperationException("The game is not running.");
    var terrain = game.World.Terrain
      ?? throw new InvalidOperationException("The game has no loaded terrain.");
    var framing = TerrainCameraFraming.Calculate(terrain);
    game.Scene.Camera.Frame(framing.Target, framing.Distance, framing.MinimumDistance);
    game.Scene.Camera.Update(glSurface.AspectRatio);
    glSurface.PresentFrame();
    return GetAutomationState();
  }

  internal byte[] CaptureAutomationFrame() => glSurface.CaptureFramePng();

  internal object SetAutomationPaused(bool paused) {
    var game = Game.Instance
      ?? throw new InvalidOperationException("The game is not running.");
    if (paused) game.Pause();
    else game.Resume();
    return GetAutomationState();
  }

  internal object ResizeAutomationViewport(GameAutomationViewportRequest request) {
    request.Validate();
    ClientSize = new System.Drawing.Size(request.Width, request.Height);
    var game = Game.Instance
      ?? throw new InvalidOperationException("The game is not running.");
    game.Scene.Camera.Update(glSurface.AspectRatio);
    glSurface.PresentFrame();
    return GetAutomationState();
  }

  internal void RequestAutomationShutdown() => Close();

  #region IView Methods
  public void Initialize() {
    if (glSurface.IsHandleCreated) rendererCreated.Set();
    logger.Trace("Waiting for renderer instantiation...");
    rendererCreated.WaitOne();
    logger.Trace("Renderer created");
  }

  public void Reset() {
    Game.Instance?.Pause();
    Hide();
    Controls.Clear();
    glSurface.Dispose();
    glSurface = null;
  }

  public void Run(Action onFrame) {
    while (!isClosing) {
      DoEvents();
      onFrame();
    }
  }

  void Windowing.IView.Focus() => Focus();

  public void ContinueEvents() => Invoke(Application.DoEvents);
  public void DoEvents() => Application.DoEvents();
  public void DoRender() => Render?.Invoke(Game.Instance!.FrameTime.TotalMilliseconds);
  public void DoUpdate() => UpdateView?.Invoke(Game.Instance!.FrameTime.TotalMilliseconds);
  public Vector2D<int> PointToClient(Vector2D<int> point) {
    var pt = base.PointToClient(new(point.X, point.Y));
    return new(pt.X, pt.Y);
  }

  public Vector2D<int> PointToFramebuffer(Vector2D<int> point) {
    // FIXME: This does not take into account the screen's DPI
    var pt = glSurface.PointToClient(new(point.X, point.Y));
    return new(pt.X, pt.Y);
  }

  public Vector2D<int> PointToScreen(Vector2D<int> point) {
    var pt = base.PointToScreen(new(point.X, point.Y));
    return new(pt.X, pt.Y);
  }
  #endregion

  #region IInputPlatform Members
  public bool IsApplicable(Windowing.IView view) => view is GameWindow;
  // The GL surface (not the form) is the control that actually receives mouse input,
  // since it is docked to fill the form's entire client area.
  public IInputContext CreateInput(Windowing.IView view) => new InputAdapter(glSurface);
  #endregion

  protected override void WndProc(ref Message m) {
    var hasCommand = SystemMenu.TryGetCommand(ref m, out var command);
    if (!hasCommand) {
      base.WndProc(ref m);
      return;
    }

    // Handle custom system menu command
    switch (command) {
      case Command.OpenLog:
        var log = AppConfig.LogPath;
        if (Path.Exists(log)) Task.Run(async () => {
          var file = await StorageFile.GetFileFromPathAsync(log);
          await Launcher.LaunchFileAsync(file);
        }).Wait(cancellationToken: closing.Token);
        break;
      default:
        base.WndProc(ref m);
        break;
    }
  }

  private void GameWindow_GotFocus(object sender, EventArgs e) {
    var game = Game.Instance;
    if (game?.IsPaused ?? false) game.Resume();
    FocusChanged?.Invoke(true);
  }

  private void GameWindow_LostFocus(object sender, EventArgs e) {
    var game = Game.Instance;
    var isPaused = game?.IsPaused ?? false;
    if (!isPaused) game?.Resume();
    FocusChanged?.Invoke(false);
  }

  private void GameWindow_FormClosing(object sender, FormClosingEventArgs e) {
    e.Cancel = closeCoordinator.ShouldCancelClose(
      () => Game.Instance?.Quit() ?? true,
      action => {
        if (!IsDisposed && !Disposing) BeginInvoke(action);
      },
      glSurface.Dispose,
      Close,
      error => logger.Error(error, "Game loop failed during shutdown."));
    isClosing = e.Cancel == false;
    if (isClosing) closing.Cancel();
  }

  private void GlSurface_SurfaceCreated(IGraphicsSurface surface, IRenderer renderer) {
    this.renderer = renderer;
    rendererCreated.Set();
  }

  private void GlSurface_Resize(object sender, EventArgs e) => FramebufferResize?.Invoke(FramebufferSize);

  private static bool IsAutomationEnabled => !string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable(GameAutomationPipeServer.PipeEnvironmentVariable));

  private static object? GetPathBounds(Game game) {
    var park = game.World.Park;
    var terrain = game.World.Terrain;
    if (park == null || terrain == null || park.PathPlacements.Count == 0) return null;
    var placements = park.PathPlacements;
    var minimumX = placements.Min(placement => placement.TileX);
    var maximumX = placements.Max(placement => placement.TileX);
    var minimumY = placements.Min(placement => placement.TileY);
    var maximumY = placements.Max(placement => placement.TileY);
    var centerTileX = (minimumX + maximumX + 1f) / 2f;
    var centerTileY = (minimumY + maximumY + 1f) / 2f;
    var centerX = terrain.Origin.X + (centerTileX * terrain.TileSize.X);
    var centerY = terrain.Origin.Y + (centerTileY * terrain.TileSize.Y);
    var centerPlacement = placements.MinBy(placement =>
      MathF.Abs(placement.TileX + 0.5f - centerTileX)
      + MathF.Abs(placement.TileY + 0.5f - centerTileY));
    var centerZ = Terrain.CornerHeightToWorldZ(centerPlacement.Tile.RaisedHeight);
    if (!centerPlacement.Tile.Raised) {
      centerZ = 0f;
      var corners = terrain.GetCorners(centerPlacement.TileX, centerPlacement.TileY);
      foreach (var corner in corners) centerZ += Terrain.CornerHeightToWorldZ(corner.Height);
      centerZ /= corners.Length;
    }
    return new {
      minimum_tile = new[] { minimumX, minimumY },
      maximum_tile = new[] { maximumX, maximumY },
      world_center = new[] { centerX, centerY, Convert.ToSingle(centerZ) }
    };
  }

  private static object[] GetPathSurfaceGroups(Game game) {
    var park = game.World.Park;
    var terrain = game.World.Terrain;
    if (park == null || terrain == null) return [];
    return park.PathPlacements
      .GroupBy(placement => new {
        surface = placement.Tile.SurfaceSystemName ?? "(unresolved)",
        queue = placement.Tile.IsQueue
      })
      .OrderBy(group => group.Key.surface, StringComparer.Ordinal)
      .ThenBy(group => group.Key.queue)
      .Select(group => {
        var minimumX = group.Min(placement => placement.TileX);
        var maximumX = group.Max(placement => placement.TileX);
        var minimumY = group.Min(placement => placement.TileY);
        var maximumY = group.Max(placement => placement.TileY);
        var centerTileX = (minimumX + maximumX + 1f) / 2f;
        var centerTileY = (minimumY + maximumY + 1f) / 2f;
        var centerPlacement = group.MinBy(placement =>
          MathF.Abs(placement.TileX + 0.5f - centerTileX)
          + MathF.Abs(placement.TileY + 0.5f - centerTileY));
        return (object)new {
          surface = group.Key.surface,
          queue = group.Key.queue,
          count = group.Count(),
          minimum_tile = new[] { minimumX, minimumY },
          maximum_tile = new[] { maximumX, maximumY },
          world_center = new[] {
            terrain.Origin.X + (centerTileX * terrain.TileSize.X),
            terrain.Origin.Y + (centerTileY * terrain.TileSize.Y),
            GetPathWorldZ(centerPlacement, terrain)
          }
        };
      })
      .ToArray();
  }

  private static float GetPathWorldZ(PathPlacement placement, Terrain terrain) {
    if (placement.Tile.Raised)
      return Terrain.CornerHeightToWorldZ(placement.Tile.RaisedHeight);
    var height = 0f;
    var corners = terrain.GetCorners(placement.TileX, placement.TileY);
    foreach (var corner in corners) height += Terrain.CornerHeightToWorldZ(corner.Height);
    return height / corners.Length;
  }
}

internal sealed class GameLoopCloseCoordinator {
  private readonly object gate = new();
  private Task gameTask = Task.CompletedTask;
  private bool closePending;
  private bool finalClose;

  public void Track(Task task) {
    ArgumentNullException.ThrowIfNull(task);
    lock (gate) gameTask = task;
  }

  public bool ShouldCancelClose(
    Func<bool> requestQuit,
    Action<Action> marshal,
    Action prepareFinalClose,
    Action requestFinalClose,
    Action<Exception>? handleFailure = null
  ) {
    Task task;
    lock (gate) {
      if (finalClose) return false;
      if (closePending) return true;
      closePending = true;
      task = gameTask;
    }

    bool canQuit;
    try {
      canQuit = requestQuit();
    } catch {
      lock (gate) closePending = false;
      throw;
    }
    if (!canQuit) {
      lock (gate) closePending = false;
      return true;
    }

    _ = task.ContinueWith(
      completed => {
        var error = completed.Exception;
        try {
          marshal(() => {
            try {
              if (error != null) handleFailure?.Invoke(error);
              prepareFinalClose();
              lock (gate) finalClose = true;
              requestFinalClose();
            } catch {
              ResetPendingClose();
              throw;
            }
          });
        } catch (Exception marshalError) {
          ResetPendingClose();
          handleFailure?.Invoke(marshalError);
        }
      },
      CancellationToken.None,
      TaskContinuationOptions.ExecuteSynchronously,
      TaskScheduler.Default);
    return true;
  }

  private void ResetPendingClose() {
    lock (gate) {
      finalClose = false;
      closePending = false;
    }
  }
}
