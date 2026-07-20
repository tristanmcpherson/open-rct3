// Game
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using DryIoc;
using NLog;
using OpenCobra.GDK;
using OpenCobra.GDK.Game;
using OpenCobra.GDK.GUI;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.GDK.Platform;
using OpenRCT3.OpenGL;
using OpenRCT3.Platforms;
using OpenRCT3.Scenario;
using OpenRCT3.Simulation;
using Silk.NET.Input;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;


#if WINDOWS
using System.Windows.Forms;
#elif OSX
using AppKit;
#endif

namespace OpenRCT3;

/// <summary>
/// The game world.
/// </summary>
public class Game : IGame {
  /// <summary>The maximum number of simulation ticks to process in one instant.</summary>
  private const int MaxSimulationTicks = 8;
  /// <summary>The minimum time between lag warning messages.</summary>
  /// <remarks>This prevents spamming the log with warnings about lag.</remarks>
  private readonly TimeSpan lagWarningDebounceInterval = TimeSpan.FromSeconds(10);

  private readonly static Logger logger = LogManager.GetCurrentClassLogger();
  private readonly GameRunLifecycle lifecycle = new();
  private bool isPaused = false;
  private readonly ManualResetEvent resumeSignal = new(true);
  private readonly Stopwatch stopwatch = new();
  private DateTime lastLagWarning = DateTime.Now;
  private IRenderer? renderer = ResolveRenderer(Game.IoC);
  private Scene? ownedScene;
  private Simulation.World? ownedWorld;
  private readonly object cameraControllerGate = new();
  private IInputContext? cameraInput;
  private CameraController? cameraController;
  private bool disposed;

  public static Container IoC => IGame.IoC;
  public static Game? Instance { get; private set; }
  public static bool IsRunning => Instance?.lifecycle.IsRunning ?? false;

  internal static Game? DetachInstance() {
    var instance = Instance;
    Instance = null;
    return instance;
  }

  internal static IRenderer ResolveRenderer(IResolverContext resolver) =>
    resolver.Resolve<IRenderer>();

  internal IRenderer? BoundRenderer => Volatile.Read(ref renderer);

  internal void BindRenderer(IRenderer replacement) =>
    Volatile.Write(ref renderer, replacement);

  internal void UnbindRenderer(IRenderer ownedRenderer) =>
    Interlocked.CompareExchange(ref renderer, null, ownedRenderer);

  internal void BindCameraInput(IInputContext replacement) {
    ArgumentNullException.ThrowIfNull(replacement);

    CameraController? previous;
    lock (cameraControllerGate) {
      if (disposed) return;
      previous = cameraController;
      cameraController = new CameraController(
        Scene.Camera,
        replacement,
        static () => Controller.CaptureKeyboard,
        static () => Controller.CaptureMouse
      );
      cameraInput = replacement;
    }
    previous?.Dispose();
  }

  internal void UnbindCameraInput(IInputContext ownedInput) {
    ArgumentNullException.ThrowIfNull(ownedInput);

    CameraController? controller;
    lock (cameraControllerGate) {
      if (!ReferenceEquals(cameraInput, ownedInput)) return;
      cameraInput = null;
      controller = cameraController;
      cameraController = null;
    }
    controller?.Dispose();
  }

  /// <summary>
  /// Default frame rate of the game loop, in frames per second.
  /// </summary>
  public readonly static int DefaultFrameRate = 60;

  /// <summary>
  /// Raised once the game has started and the game loop is running.
  /// </summary>
  /// <remarks>
  /// The game is started via <see cref="Run"/>.
  /// </remarks>
  public event Action? Started;
  /// <summary>
  /// <para>Raised when the game ends, i.e. when the user quits.</para>
  /// <para>See <see cref="Quit"/>.</para>
  /// </summary>
  public event Action? Exited;

  public AppConfig Config { get; } = AppConfig.Instance;

  public bool IsPaused => isPaused;

  /// <summary>
  /// <para>The time taken to render the last frame, or null if no frame has been rendered yet.</para>
  /// <para>Use <see cref="TargetFrameRate"/> to set the frame rate.</para>
  /// </summary>
  public TimeSpan FrameTime { get; private set; } = TimeSpan.Zero;

  /// <summary>
  /// Target frame rate of the game loop, in frames per second.
  /// </summary>
  public int TargetFrameRate {
    get => Convert.ToInt32(1.0 / TargetFrameTime.TotalSeconds);
    set => TargetFrameTime = TimeSpan.FromSeconds(1.0 / value);
  }

  /// <summary>
  /// Target frame time of the game loop.
  /// </summary>
  public TimeSpan TargetFrameTime { get; private set; } = TimeSpan.FromSeconds(1.0 / 60.0);

  /// <summary>
  /// Target simulation tick rate.
  /// </summary>
  public TimeSpan TargetUpdateRate { get; set; } = TimeSpan.FromSeconds(1.0 / 60.0);

  /// <summary>
  /// Whether the game should use vertical sync (VSync) to limit the frame rate.
  /// </summary>
  public bool VSync { get; set; } = false;

  public Simulation.World World { get; } = new();
  public Scene Scene { get; } = new();

  public Game() {
    ownedScene = Scene;
    ownedWorld = World;

    InitializeOwnedGame(
      Initialize,
      Dispose,
      () => Instance = this);
  }

  private void Initialize() {

    logger.Trace("Creating game world...");
    logger.Warn("Simulation features are unimplemented!");

    // Load the game world
    // TODO: Show a progress bar while loading
    World.Load();
    logger.Debug("Game world loaded");

    // Build texture-batched meshes from the loaded terrain's corner-height grid. Each DAT cell's
    // decoded surface/cliff indices select the matching texture from the terrain catalog.
    Debug.Assert(World.Terrain != null);
    Debug.Assert(World.Terrain.TextureCatalog != null);
    foreach (var batch in TerrainMeshBuilder.BuildBatches(World.Terrain, Vector4.One)) {
      var texture = batch.Kind switch {
        TerrainMaterialKind.Surface => World.Terrain.TextureCatalog.GetSurface(batch.Index),
        TerrainMaterialKind.Cliff => World.Terrain.TextureCatalog.GetCliff(batch.Index),
        _ => throw new ArgumentOutOfRangeException(nameof(batch.Kind), batch.Kind, null),
      };
      var terrainModel = new Model(batch.Mesh) {
        Material = new Textured { AlbedoTexture = texture }
      };
      Scene.Models.Add(terrainModel);
    }
    logger.Debug("Added terrain meshes");

    Debug.Assert(World.Park != null);
    if (World.Park.PathPlacements.Count > 0) {
      foreach (var batch in PathMeshBuilder.BuildBatches(
        World.Park,
        World.Terrain,
        new Vector4(0.72f, 0.64f, 0.50f, 1f),
        new Vector4(0.28f, 0.48f, 0.70f, 1f))) {
        // The batch retains exact RCT3 queue flexi-colour indices. Until the palette is decoded,
        // keep the established ordinary/queue vertex tints instead of inventing a conversion.
        var pathModel = new Model(batch.Mesh) { Material = new Flat() };
        Scene.Models.Add(pathModel);
      }
    }
    logger.Debug("Added {Count} path tiles", World.Park.PathPlacements.Count);

    var installPath = Config.InstallPath
      ?? throw new InvalidOperationException("RCT3 installation path is not configured.");
    var scenery = ScenerySceneLoader.Load(World.Park, World.Terrain, installPath);
    Scene.Models.AddRange(scenery.Models);
    logger.Debug(
      "Added {ModelCount} scenery models from {RenderedCount} placements; " +
      "skipped {SkippedCount} placements ({MissingOverlayCount} missing overlays) and " +
      "{MissingTextureCount} missing-texture batches",
      scenery.Models.Count,
      scenery.Geometry.RenderedPlacementCount,
      scenery.Geometry.SkippedPlacementCount,
      scenery.MissingOverlayPlacementCount,
      scenery.MissingTextureBatchCount);

    // Water is a separate overlay over the terrain. Each decoded DAT WaterManager pool keeps its
    // exact triangle masks and surface height while rendering independently from the terrain mesh.
    foreach (var pool in World.Park.WaterPools) {
      var waterModel = new Model(WaterMeshBuilder.Build(
        World.Terrain,
        pool,
        new Vector4(0.12f, 0.42f, 0.72f, 1f))) {
        Material = new Water()
      };
      Scene.Models.Add(waterModel);
    }
    logger.Debug("Added {Count} water meshes", World.Park.WaterPools.Count);

    // Frame the camera on the loaded terrain's full 3D bounds. Camera's default framing (a small
    // fixed offset from the origin) only suits a toy scene; it doesn't scale to an actual map, so
    // most or all of the terrain otherwise ends up outside the view frustum.
    //
    // TerrainCameraFraming includes the OOB border and scans the real corner-height range. Centering
    // on XYZ keeps elevated maps aimed correctly, while the full 3D diagonal bounds the 45°-azimuth
    // "diamond" without the old buildable-area-only 1.8x heuristic (see CameraFramingTests).
    var framing = TerrainCameraFraming.Calculate(World.Terrain);
    Scene.Camera.Frame(framing.Target, framing.Distance, framing.MinimumDistance);
    logger.Trace("Framed camera on terrain");

    // Keep normal gameplay unchanged while allowing native visual verification to capture the map
    // without an incidental editor panel obscuring it.
    if (GamePresentationOptions.ShowUserInterface) Scene.Windows.Add(new Editor());

    BindCameraInput(IoC.Resolve<IInputContext>());
  }

  /// <summary>
  /// Starts the game loop.
  /// </summary>
  /// <remarks>
  /// The game loop runs at a fixed frame rate, sleeping when ahead of schedule to reduce CPU usage.
  /// </remarks>
  /// <seealso cref="TargetFrameRate"/>
  /// <seealso cref="TargetFrameTime"/>
  /// <seealso href="https://gameprogrammingpatterns.com/game-loop.html"/>
  public void Run() {
    if (!lifecycle.TryStart()) return;

    // Run the game loop
    Started?.Invoke();
    stopwatch.Start();
    var previousTime = stopwatch.Elapsed;
    // Measures wall time that has elapsed since the last frame
    var lag = TimeSpan.Zero;

    // Implements the fixed-update-time-step, variable-rendering pattern to decouple
    // simulation stability (fixed step for physics/AI determinism) from visual
    // smoothness (variable render rate).
    //
    // See https://gameprogrammingpatterns.com/game-loop.html
    while (lifecycle.IsRunning) {
      // Wait for the resume signal if the game is paused
      if (isPaused) {
        resumeSignal.WaitOne();
        logger.Trace("Game resumed");
      }

      var currentTime = stopwatch.Elapsed;
      var elapsed = FrameTime = currentTime - previousTime;
      previousTime = currentTime;
      // FIXME: Ought the game NOT accumulate lag if the game was paused?
      lag += elapsed;

      // Process any pending window events, e.g. input events
#if WINDOWS
      if (!ProcessEventsAndCheckRunning(Application.DoEvents, static () => IsRunning)) break;
#elif OSX
      // FIXME: Pump macOS windowing events
      // See https://duckduckgo.com/?q=osx+how+to+pump+windowing+events+in+a+game+loop&ia=web
      if (!ProcessEventsAndCheckRunning(NSApplication.EnsureUIThread, static () => IsRunning)) break;
#endif

      // Simulation ticks are fixed steps to aid physics/AI determinism
      // For example, a 60Hz target frame-rate would process one tick 60 times per second
      LogLagWarning(lag);
      for (var tickCount = 0; tickCount < MaxSimulationTicks && lag >= TargetFrameTime; tickCount++) {
        Tick(
          delta: TargetFrameTime,
          // Normalize the lag to a percentage representing how far into the
          // simulation step we are (0.0 = just started, 1.0 = just finished)
          interpolation: lag.TotalMilliseconds / TargetFrameTime.TotalMilliseconds);
        lag -= TargetFrameTime;
      }

      // Rendering can happen at arbitrary points between updates, and frames can
      // be dropped if the machine is slow.
      lock (cameraControllerGate) cameraController?.Update(elapsed);
      Scene.Update(delta: elapsed);
      Volatile.Read(ref renderer)?.Render(Scene);

      // Reduce CPU usage by sleeping when ahead of schedule
      var remaining = TargetFrameTime - lag;
      if (remaining > TimeSpan.Zero) {
        var sleepMs = remaining.TotalMilliseconds / 2.0;
        if (sleepMs > 2) Thread.Sleep((int)sleepMs / 2);
      }
    }

    Exited?.Invoke();
    logger.Info("Game exited");
  }

  internal static bool ProcessEventsAndCheckRunning(
    Action processEvents,
    Func<bool> isRunning
  ) {
    processEvents();
    return isRunning();
  }

  public void Pause() {
    isPaused = true;
    resumeSignal.Reset();
  }

  public void Resume() {
    isPaused = false;
    resumeSignal.Set();
  }

  /// <summary>
  /// Try to quit the game.
  /// </summary>
  /// <returns>Whether the game stopped running.</returns>
  public bool Quit() {
    // TODO: Check for unsaved changes and prevent closure
    lifecycle.Stop();
    resumeSignal.Set();

    if (!lifecycle.IsRunning) logger.Info("Exiting game...");
    return !lifecycle.IsRunning;
  }

  public void Dispose() {
    CameraController? controller;
    Scene? scene;
    Simulation.World? world;
    lock (cameraControllerGate) {
      if (disposed) return;
      disposed = true;
      scene = ownedScene;
      world = ownedWorld;
      controller = cameraController;
      ownedScene = null;
      ownedWorld = null;
      cameraInput = null;
      cameraController = null;
    }

    // Dispose GPU-backed scene resources while the graphics context is still alive, then release
    // the world-owned texture catalog and simulation systems.
    try {
      controller?.Dispose();
    } finally {
      DisposeOwnedResources(
        scene == null ? null : scene.Dispose,
        world == null ? null : world.Dispose,
        () => {
          lifecycle.Stop();
          resumeSignal.Set();
          if (ReferenceEquals(Instance, this)) Instance = null;
          GC.SuppressFinalize(this);
        });
    }
  }

  internal static void InitializeOwnedGame(
    Action initialize,
    Action cleanup,
    Action publish
  ) {
    try {
      initialize();
    } catch (Exception primaryError) {
      try {
        cleanup();
      } catch (Exception cleanupError) {
        throw new AggregateException(
          "Game initialization failed and cleanup also reported an error.",
          primaryError,
          cleanupError);
      }
      throw;
    }

    publish();
  }

  internal static void DisposeOwnedResources(
    Action? disposeScene,
    Action? disposeWorld,
    Action clearState
  ) {
    try {
      var releases = new List<Action>();
      if (disposeScene != null) releases.Add(disposeScene);
      if (disposeWorld != null) releases.Add(disposeWorld);
      ResourceReleaser.Run(releases);
    } finally {
      clearState();
    }
  }

  /// <summary>
  /// Advances the simulation.
  /// </summary>
  /// <param name="delta">The time between ticks.</param>
  /// <param name="interpolation">The interpolation fraction.</param>
  private void Tick(TimeSpan delta, double interpolation) {
    // TODO: Advance the simulation logic by a fixed time step
    // TODO: Scheduler.Execute(delta);
  }

  [Conditional("DEBUG")]
  private void LogLagWarning(TimeSpan lag) {
    // TODO: Detect excessive lag and lower the user's target frame-rate
    // TODO: Maybe even show a modal to the user:
    // "You are experiencing excessive lag. Lowering frame-rate to prevent stuttering."
    // "Consider lowering your target frame-rate in the game settings."
    if (lag <= TargetFrameTime || DateTime.Now - lastLagWarning <= lagWarningDebounceInterval) return;

    var details = $"{lag.TotalMilliseconds}ms (target: {TargetFrameTime.TotalMilliseconds}ms)";
    logger.Warn($"Lag has exceeded target frame time budget: {details}");
    lastLagWarning = DateTime.Now;
  }
}

internal sealed class GameRunLifecycle {
  private const int Created = 0;
  private const int Running = 1;
  private const int Stopped = 2;
  private int state = Created;

  public bool IsRunning => Volatile.Read(ref state) == Running;

  public bool TryStart() =>
    Interlocked.CompareExchange(ref state, Running, Created) == Created;

  public void Stop() => Interlocked.Exchange(ref state, Stopped);
}
