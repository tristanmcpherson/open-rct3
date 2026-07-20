using DryIoc;
using OpenCobra.GDK;
using OpenCobra.GDK.GUI;
using OpenCobra.GDK.Numerics;
using OpenCobra.GDK.Platform;
using OpenRCT3.OpenGL;
using OpenRCT3.Platforms;
using OpenRCT3.Platforms.Windows;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenRCT3.Tests.OpenGL;

[TestFixture]
public class WindowsLifecycleTests {
  [Test]
  public void FirstFramePresentation_PreparesSceneBeforeForcingSynchronousPaint() {
    var order = new List<string>();

    WindowsFramePresentation.Present(
      () => order.Add("prepare-scene"),
      () => order.Add("invalidate"),
      () => order.Add("update"));

    Assert.That(order, Is.EqualTo(new[] {
      "prepare-scene", "invalidate", "update",
    }));
  }

  [Test]
  public void StartupFramePresentation_ForcesPaintWithoutAReadyScene() {
    var order = new List<string>();

    WindowsFramePresentation.Present(
      prepareFrame: null,
      () => order.Add("invalidate"),
      () => order.Add("update"));

    Assert.That(order, Is.EqualTo(new[] { "invalidate", "update" }));
  }

  [Test]
  public void CloseCoordinator_WaitsForGameLoopBeforeAllowingFinalClose() {
    var coordinator = new GameLoopCloseCoordinator();
    var gameLoop = new TaskCompletionSource();
    var marshaled = new List<Action>();
    var order = new List<string>();
    var quitCount = 0;
    coordinator.Track(gameLoop.Task);

    bool RequestClose() => coordinator.ShouldCancelClose(
      () => {
        quitCount++;
        order.Add("quit");
        return true;
      },
      marshaled.Add,
      () => order.Add("surface-dispose"),
      () => order.Add("final-close"));

    var firstCloseCancelled = RequestClose();
    var repeatedCloseCancelled = RequestClose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstCloseCancelled, Is.True);
      Assert.That(repeatedCloseCancelled, Is.True);
      Assert.That(quitCount, Is.EqualTo(1));
      Assert.That(marshaled, Is.Empty);
      Assert.That(order, Is.EqualTo(new[] { "quit" }));
    }

    gameLoop.SetResult();
    Assert.That(marshaled, Has.Count.EqualTo(1));
    marshaled.Single().Invoke();

    var finalCloseCancelled = RequestClose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(finalCloseCancelled, Is.False);
      Assert.That(quitCount, Is.EqualTo(1));
      Assert.That(order, Is.EqualTo(new[] {
        "quit", "surface-dispose", "final-close",
      }));
    }
  }

  [Test]
  public void CloseCoordinator_RejectedQuitCanBeRetried() {
    var coordinator = new GameLoopCloseCoordinator();
    var quitAllowed = false;
    var marshaled = new List<Action>();
    var finalCloseCount = 0;

    var rejectedCloseCancelled = coordinator.ShouldCancelClose(
      () => quitAllowed,
      marshaled.Add,
      () => { },
      () => finalCloseCount++);
    quitAllowed = true;
    var acceptedCloseCancelled = coordinator.ShouldCancelClose(
      () => quitAllowed,
      marshaled.Add,
      () => { },
      () => finalCloseCount++);

    using (Assert.EnterMultipleScope()) {
      Assert.That(rejectedCloseCancelled, Is.True);
      Assert.That(acceptedCloseCancelled, Is.True);
      Assert.That(marshaled, Has.Count.EqualTo(1));
      Assert.That(finalCloseCount, Is.Zero);
    }

    marshaled.Single().Invoke();
    using (Assert.EnterMultipleScope()) {
      Assert.That(finalCloseCount, Is.EqualTo(1));
      Assert.That(coordinator.ShouldCancelClose(
        () => throw new InvalidOperationException("Quit should not repeat."),
        marshaled.Add,
        () => { },
        () => finalCloseCount++), Is.False);
    }
  }

  [Test]
  public void CloseCoordinator_SurfaceFailureCanRetryBeforeFinalClose() {
    var coordinator = new GameLoopCloseCoordinator();
    var marshaled = new List<Action>();
    var disposeAttempts = 0;
    var closeCount = 0;

    bool RequestClose() => coordinator.ShouldCancelClose(
      () => true,
      marshaled.Add,
      () => {
        disposeAttempts++;
        if (disposeAttempts == 1)
          throw new InvalidOperationException("Injected surface disposal failure.");
      },
      () => closeCount++);

    Assert.That(RequestClose(), Is.True);
    var firstAttempt = marshaled.Single();
    marshaled.Clear();
    Assert.Throws<InvalidOperationException>(new Action(firstAttempt));

    Assert.That(RequestClose(), Is.True);
    marshaled.Single().Invoke();

    using (Assert.EnterMultipleScope()) {
      Assert.That(disposeAttempts, Is.EqualTo(2));
      Assert.That(closeCount, Is.EqualTo(1));
      Assert.That(RequestClose(), Is.False);
    }
  }

  [Test]
  public void CloseCoordinator_MarshalFailureIsReportedAndCanRetry() {
    var coordinator = new GameLoopCloseCoordinator();
    var marshalAttempts = 0;
    var closeCount = 0;
    var failures = new List<Exception>();

    bool RequestClose() => coordinator.ShouldCancelClose(
      () => true,
      action => {
        marshalAttempts++;
        if (marshalAttempts == 1)
          throw new InvalidOperationException("Injected BeginInvoke failure.");
        action();
      },
      () => { },
      () => closeCount++,
      failures.Add);

    var failedMarshalCloseCancelled = RequestClose();
    var retryCloseCancelled = RequestClose();
    var finalCloseCancelled = RequestClose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(failedMarshalCloseCancelled, Is.True);
      Assert.That(retryCloseCancelled, Is.True);
      Assert.That(finalCloseCancelled, Is.False);
      Assert.That(marshalAttempts, Is.EqualTo(2));
      Assert.That(closeCount, Is.EqualTo(1));
      Assert.That(failures, Has.Count.EqualTo(1));
      Assert.That(failures.Single().Message, Is.EqualTo("Injected BeginInvoke failure."));
    }
  }

  [TestCase("GetDC")]
  [TestCase("PixelFormat")]
  [TestCase("Context")]
  [TestCase("GLApi")]
  public void PartialInitialization_ReleasesEveryAcquiredNativeResource(string failureStage) {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    resources.OwnContext(() => released.Add("context"));
    if (failureStage != "GetDC")
      resources.OwnDeviceContext(() => released.Add("device-context"));

    resources.Dispose(new FakeGlContext(released));

    var expected = failureStage == "GetDC"
      ? new[] { "context" }
      : new[] { "context", "device-context" };
    Assert.That(released, Is.EqualTo(expected));
  }

  [Test]
  public void GlApiAcquisition_ReleasesWrapperAfterContextAndDeviceContext() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    resources.OwnContext(() => released.Add("context"));
    resources.OwnDeviceContext(() => released.Add("device-context"));
    resources.OwnGl(() => released.Add("gl"));

    resources.Dispose(new FakeGlContext(released));

    Assert.That(released, Is.EqualTo(new[] { "context", "device-context", "gl" }));
  }

  [Test]
  public void FullInitialization_MakesContextCurrentAndPreservesReleaseOrder() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    resources.OwnContext(() => released.Add("context"));
    resources.OwnDeviceContext(() => released.Add("device-context"));
    resources.OwnGl(() => released.Add("gl"));
    resources.OwnInput(() => released.Add("input"));
    resources.OwnController(() => released.Add("controller"));
    resources.OwnRenderer(() => released.Add("renderer"));

    resources.Dispose(new FakeGlContext(released));
    resources.Dispose(new FakeGlContext(released));

    Assert.That(released, Is.EqualTo(new[] {
      "make-current", "renderer", "controller", "input",
      "context", "device-context", "gl",
    }));
  }

  [Test]
  public void HandleRecreation_PreservesGameAndRebindsSecondRendererUntilFinalDisposal() {
    using var resumeSignal = new ManualResetEvent(true);
    var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
    SetGameField(game, "lifecycle", new GameRunLifecycle());
    SetGameField(game, "resumeSignal", resumeSignal);
    SetGameField(game, "cameraControllerGate", new object());
    var firstRenderer = new FakeRenderer();
    var secondRenderer = new FakeRenderer();
    SetGameInstance(game);

    try {
      game.BindRenderer(firstRenderer);
      game.UnbindRenderer(firstRenderer);
      firstRenderer.Dispose();

      using (Assert.EnterMultipleScope()) {
        Assert.That(Game.Instance, Is.SameAs(game));
        Assert.That(game.BoundRenderer, Is.Null);
        Assert.That(firstRenderer.DisposeCount, Is.EqualTo(1));
      }

      game.BindRenderer(secondRenderer);
      using (Assert.EnterMultipleScope()) {
        Assert.That(Game.Instance, Is.SameAs(game));
        Assert.That(game.BoundRenderer, Is.SameAs(secondRenderer));
        Assert.That(secondRenderer.DisposeCount, Is.Zero);
      }

      var ownedGame = Game.DetachInstance();
      ownedGame?.Dispose();
      Assert.That(Game.Instance, Is.Null);
    } finally {
      SetGameInstance(null);
    }
  }

  [Test]
  [Apartment(ApartmentState.STA)]
  public void FinalDisposal_AfterContextWasReleased_DetachesGameAndIsIdempotent() {
    using var resumeSignal = new ManualResetEvent(true);
    var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
    SetGameField(game, "lifecycle", new GameRunLifecycle());
    SetGameField(game, "resumeSignal", resumeSignal);
    SetGameField(game, "cameraControllerGate", new object());
    var surface = new GLSurface();
    SetGameInstance(game);

    surface.Context.Dispose();
    Assert.DoesNotThrow(new Action(surface.Dispose));
    Assert.DoesNotThrow(new Action(surface.Dispose));

    using (Assert.EnterMultipleScope()) {
      Assert.That(Game.Instance, Is.Null);
      Assert.That(GetGameDisposed(game), Is.True);
    }
  }

  [Test]
  [Apartment(ApartmentState.STA)]
  public void HandleRecreation_ReplacesEveryOwnerManagedContainerRegistration() {
    using var container = new Container();
    using var surface = new GLSurface();
    var first = RegisterGeneration(container, surface);
    var second = RegisterGeneration(container, surface);

    using (Assert.EnterMultipleScope()) {
      Assert.That(container.Resolve<IGraphicsSurface>(), Is.SameAs(second.Surface));
      Assert.That(container.Resolve<GL>(), Is.SameAs(second.Gl));
      Assert.That(container.Resolve<IGLContext>(), Is.SameAs(second.Context));
      Assert.That(container.Resolve<IInputContext>(), Is.SameAs(second.Input));
      Assert.That(container.Resolve<Controller>(), Is.SameAs(second.Controller));
      Assert.That(container.Resolve<IRenderer>(), Is.SameAs(second.Renderer));
      Assert.That(Game.ResolveRenderer(container), Is.SameAs(second.Renderer));
      Assert.That(first.Context.DisposeCount, Is.Zero);
      Assert.That(first.Input.DisposeCount, Is.Zero);
      Assert.That(first.Renderer.DisposeCount, Is.Zero);
    }

    container.Dispose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(first.Context.DisposeCount, Is.Zero);
      Assert.That(first.Input.DisposeCount, Is.Zero);
      Assert.That(first.Renderer.DisposeCount, Is.Zero);
      Assert.That(second.Context.DisposeCount, Is.Zero);
      Assert.That(second.Input.DisposeCount, Is.Zero);
      Assert.That(second.Renderer.DisposeCount, Is.Zero);
    }
  }

  [Test]
  public void CameraInputReplacement_UnsubscribesOldGenerationAndFinalDisposal() {
    using var resumeSignal = new ManualResetEvent(true);
    var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
    SetGameField(game, "lifecycle", new GameRunLifecycle());
    SetGameField(game, "resumeSignal", resumeSignal);
    SetGameField(game, "cameraControllerGate", new object());
    SetGameField(game, "<Scene>k__BackingField", new TestScene());
    var first = new TestInputContext();
    var second = new TestInputContext();
    var final = new TestInputContext();
    var afterDisposal = new TestInputContext();

    game.BindCameraInput(first);
    game.BindCameraInput(second);
    game.UnbindCameraInput(first);

    using (Assert.EnterMultipleScope()) {
      Assert.That(first.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(first.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(first.Mouse.ScrollSubscriberCount, Is.Zero);
      Assert.That(second.Keyboard.KeyDownSubscriberCount, Is.EqualTo(1));
      Assert.That(second.Keyboard.KeyUpSubscriberCount, Is.EqualTo(1));
      Assert.That(second.Mouse.ScrollSubscriberCount, Is.EqualTo(1));
    }

    game.UnbindCameraInput(second);
    game.BindCameraInput(final);

    using (Assert.EnterMultipleScope()) {
      Assert.That(second.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(second.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(second.Mouse.ScrollSubscriberCount, Is.Zero);
      Assert.That(final.Keyboard.KeyDownSubscriberCount, Is.EqualTo(1));
      Assert.That(final.Keyboard.KeyUpSubscriberCount, Is.EqualTo(1));
      Assert.That(final.Mouse.ScrollSubscriberCount, Is.EqualTo(1));
    }

    game.Dispose();
    game.BindCameraInput(afterDisposal);

    using (Assert.EnterMultipleScope()) {
      Assert.That(second.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(second.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(second.Mouse.ScrollSubscriberCount, Is.Zero);
      Assert.That(final.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(final.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(final.Mouse.ScrollSubscriberCount, Is.Zero);
      Assert.That(afterDisposal.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(afterDisposal.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(afterDisposal.Mouse.ScrollSubscriberCount, Is.Zero);
    }
  }

  [Test]
  public void InvalidContext_RetryKeepsNativeChainAndReleasesOriginalGenerationOnce() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceCycle();
    var ownedResources = resources.BeginHandle();
    var context = new StatefulGlContext(released) { MakesCurrent = false };
    ownedResources.OwnContext(context.ReleaseContext);
    ownedResources.OwnDeviceContext(context.ReleaseDeviceContext);
    ownedResources.OwnGl(() => released.Add("gl"));
    ownedResources.OwnInput(() => released.Add("input"));
    ownedResources.OwnController(() => released.Add("controller"));
    ownedResources.OwnRenderer(() => {
      Assert.That(context.IsCurrent, Is.True);
      Assert.That(context.NativeContextAlive, Is.True);
      Assert.That(context.DeviceContextAlive, Is.True);
      released.Add("renderer");
    });

    Assert.Throws<AggregateException>(new Action(() => resources.EndHandle(context)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.True);
      Assert.That(context.NativeContextAlive, Is.True);
      Assert.That(context.DeviceContextAlive, Is.True);
      Assert.That(released, Is.EqualTo(new[] { "make-current" }));
    }

    context.MakesCurrent = true;
    resources.EndHandle(context);
    resources.EndHandle(context);
    var nextGeneration = resources.BeginHandle();
    nextGeneration.OwnContext(() => released.Add("next-context"));
    resources.EndHandle(new FakeGlContext(released));

    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.False);
      Assert.That(context.NativeContextAlive, Is.False);
      Assert.That(context.DeviceContextAlive, Is.False);
      Assert.That(released, Is.EqualTo(new[] {
        "make-current", "make-current", "renderer", "controller", "input",
        "delete-context", "context", "release-device-context", "device-context", "gl",
        "next-context",
      }));
    }
  }

  [Test]
  public void ContextDeleteFailure_RetainsContextAndHdcForStatefulRetry() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    var context = new StatefulGlContext(released) { DeleteSucceeds = false };
    resources.OwnRenderer(() => released.Add("renderer"));
    resources.OwnContext(context.ReleaseContext);
    resources.OwnDeviceContext(context.ReleaseDeviceContext);
    resources.OwnGl(() => released.Add("gl"));

    Assert.Throws<AggregateException>(new Action(() => resources.Dispose(context)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.True);
      Assert.That(context.NativeContextAlive, Is.True);
      Assert.That(context.DeviceContextAlive, Is.True);
      Assert.That(context.DeleteAttempts, Is.EqualTo(1));
      Assert.That(released, Is.EqualTo(new[] {
        "make-current", "renderer", "delete-context",
      }));
    }

    context.DeleteSucceeds = true;
    resources.Dispose(context);
    resources.Dispose(context);

    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.False);
      Assert.That(context.NativeContextAlive, Is.False);
      Assert.That(context.DeviceContextAlive, Is.False);
      Assert.That(context.DeleteAttempts, Is.EqualTo(2));
      Assert.That(released, Is.EqualTo(new[] {
        "make-current", "renderer", "delete-context",
        "make-current", "delete-context", "context",
        "release-device-context", "device-context", "gl",
      }));
    }
  }

  [Test]
  public void RendererReleaseFailure_RetainsContextAndHdcForStatefulRetry() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    var context = new StatefulGlContext(released);
    var rendererAttempts = 0;
    resources.OwnRenderer(() => {
      rendererAttempts++;
      released.Add("renderer");
      if (rendererAttempts == 1)
        throw new InvalidOperationException("Injected mesh deletion failure.");
    });
    resources.OwnContext(context.ReleaseContext);
    resources.OwnDeviceContext(context.ReleaseDeviceContext);
    resources.OwnGl(() => released.Add("gl"));

    Assert.Throws<AggregateException>(new Action(() => resources.Dispose(context)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.True);
      Assert.That(context.NativeContextAlive, Is.True);
      Assert.That(context.DeviceContextAlive, Is.True);
      Assert.That(released, Is.EqualTo(new[] { "make-current", "renderer" }));
    }

    resources.Dispose(context);
    resources.Dispose(context);
    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.False);
      Assert.That(context.NativeContextAlive, Is.False);
      Assert.That(context.DeviceContextAlive, Is.False);
      Assert.That(rendererAttempts, Is.EqualTo(2));
      Assert.That(released, Is.EqualTo(new[] {
        "make-current", "renderer", "make-current", "renderer",
        "delete-context", "context", "release-device-context", "device-context", "gl",
      }));
    }
  }

  [Test]
  public void DeviceContextReleaseFailure_RetainsHdcAndLaterOwnersForRetry() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    var context = new StatefulGlContext(released) { DeviceReleaseSucceeds = false };
    resources.OwnContext(context.ReleaseContext);
    resources.OwnDeviceContext(context.ReleaseDeviceContext);
    resources.OwnGl(() => released.Add("gl"));

    Assert.Throws<AggregateException>(new Action(() => resources.Dispose(context)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.True);
      Assert.That(context.NativeContextAlive, Is.False);
      Assert.That(context.DeviceContextAlive, Is.True);
      Assert.That(context.DeviceReleaseAttempts, Is.EqualTo(1));
    }

    context.DeviceReleaseSucceeds = true;
    resources.Dispose(context);
    resources.Dispose(context);

    using (Assert.EnterMultipleScope()) {
      Assert.That(resources.HasPending, Is.False);
      Assert.That(context.DeviceContextAlive, Is.False);
      Assert.That(context.DeviceReleaseAttempts, Is.EqualTo(2));
      Assert.That(released, Is.EqualTo(new[] {
        "make-current", "delete-context", "context", "release-device-context",
        "release-device-context", "device-context", "gl",
      }));
    }
  }

  [Test]
  public void GlContextLifetime_ZeroOwnedContextIsNeverCurrent() {
    var lifetime = new WindowsGlContextLifetime(42);

    Assert.That(lifetime.IsCurrent(() =>
      throw new InvalidOperationException(
        "Native current-context query should not run.")),
      Is.False);

    lifetime.SetContext(84);
    using (Assert.EnterMultipleScope()) {
      Assert.That(lifetime.IsCurrent(() => 84), Is.True);
      Assert.That(lifetime.IsCurrent(() => nint.Zero), Is.False);
    }
  }

  [Test]
  public void GlContextLifetime_FailedDeleteRetainsHandleAndFinalReleaseIsIdempotent() {
    var lifetime = new WindowsGlContextLifetime(42);
    lifetime.SetContext(84);
    var deleteAttempts = 0;
    var libraryReleases = 0;

    var failedDelete = lifetime.TryReleaseContext(handle => {
      deleteAttempts++;
      Assert.That(handle, Is.EqualTo((nint)84));
      return false;
    });
    lifetime.RetainContext(126);
    var releasedHandles = new List<nint>();
    var releasedContext = lifetime.TryReleaseContext(handle => {
      deleteAttempts++;
      releasedHandles.Add(handle);
      return true;
    });
    var releasedLibrary = lifetime.TryReleaseLibrary(handle => {
      libraryReleases++;
      Assert.That(handle, Is.EqualTo((nint)42));
      return true;
    });
    var repeated = lifetime.TryReleaseLibrary(_ => {
      libraryReleases++;
      return true;
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(failedDelete, Is.False);
      Assert.That(releasedContext, Is.True);
      Assert.That(releasedLibrary, Is.True);
      Assert.That(repeated, Is.True);
      Assert.That(deleteAttempts, Is.EqualTo(3));
      Assert.That(releasedHandles, Is.EqualTo(new nint[] { 84, 126 }));
      Assert.That(libraryReleases, Is.EqualTo(1));
      Assert.That(lifetime.ContextHandle, Is.EqualTo(nint.Zero));
      Assert.That(lifetime.IsCurrent(() => 84), Is.False);
      Assert.Throws<ObjectDisposedException>(new Action(() => lifetime.SetContext(168)));
      Assert.Throws<ObjectDisposedException>(new Action(() => {
        _ = lifetime.LibraryHandle;
      }));
    }
  }

  private static HandleGeneration RegisterGeneration(
    Container container,
    GLSurface surface
  ) {
    var gl = (GL)RuntimeHelpers.GetUninitializedObject(typeof(GL));
    var context = new StatefulGlContext([]);
    var input = new TestInputContext();
    var controller = (Controller)RuntimeHelpers.GetUninitializedObject(
      typeof(Controller));
    var renderer = new FakeRenderer();

    WindowsSurfaceRegistrations.ReplaceGraphics(container, surface, gl, context);
    WindowsSurfaceRegistrations.ReplaceInput(container, input);
    WindowsSurfaceRegistrations.ReplaceController(container, controller);
    WindowsSurfaceRegistrations.ReplaceRenderer(container, renderer);
    return new(surface, gl, context, input, controller, renderer);
  }

  private static void SetGameInstance(Game? game) {
    var instanceField = typeof(Game).GetField(
      "<Instance>k__BackingField",
      BindingFlags.NonPublic | BindingFlags.Static) ??
      throw new InvalidOperationException(
        "Could not access the game singleton backing field.");
    instanceField.SetValue(null, game);
  }

  private static bool GetGameDisposed(Game game) {
    var disposedField = typeof(Game).GetField(
      "disposed",
      BindingFlags.NonPublic | BindingFlags.Instance) ??
      throw new InvalidOperationException(
        "Could not access the game disposal state.");
    return (bool)(disposedField.GetValue(game) ?? false);
  }

  private static void SetGameField(Game game, string fieldName, object value) {
    var field = typeof(Game).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic) ??
      throw new InvalidOperationException($"Could not access Game.{fieldName}.");
    field.SetValue(game, value);
  }

  private sealed class FakeRenderer : IRenderer {
    public State State { get; private set; } = State.Ready;
    public Size FramebufferSize { get; set; }
    public int MsaaSamples => 0;
    public int DisposeCount { get; private set; }

    public void Initialize() => State = State.Ready;
    public void Render(Scene scene) { }

    public void Dispose() {
      DisposeCount++;
      State = State.Disposed;
    }
  }

  private sealed class TestInputContext : IInputContext {
    public TestKeyboard Keyboard { get; } = new();
    public TestMouse Mouse { get; } = new();
    public int DisposeCount { get; private set; }

    public event Action<IInputDevice, bool>? ConnectionChanged;
    public nint Handle => nint.Zero;
    public IReadOnlyList<IGamepad> Gamepads => [];
    public IReadOnlyList<IJoystick> Joysticks => [];
    public IReadOnlyList<IKeyboard> Keyboards => [Keyboard];
    public IReadOnlyList<IMouse> Mice => [Mouse];
    public IReadOnlyList<IInputDevice> OtherDevices => [];
    public void Dispose() => DisposeCount++;
  }

  private sealed class TestScene : Scene {
    public TestScene() : base(null, null) { }
  }

  private sealed class TestKeyboard : IKeyboard {
    private Action<IKeyboard, Key, int>? keyDown;
    private Action<IKeyboard, Key, int>? keyUp;

    public int KeyDownSubscriberCount => keyDown?.GetInvocationList().Length ?? 0;
    public int KeyUpSubscriberCount => keyUp?.GetInvocationList().Length ?? 0;

    public event Action<IKeyboard, Key, int>? KeyDown {
      add => keyDown += value;
      remove => keyDown -= value;
    }
    public event Action<IKeyboard, Key, int>? KeyUp {
      add => keyUp += value;
      remove => keyUp -= value;
    }
    public event Action<IKeyboard, char>? KeyChar;

    public string Name => "Test Keyboard";
    public int Index => 0;
    public bool IsConnected => true;
    public IReadOnlyList<Key> SupportedKeys => Enum.GetValues<Key>();
    public string ClipboardText { get; set; } = string.Empty;

    public void BeginInput() { }
    public void EndInput() { }
    public bool IsKeyPressed(Key key) => false;
    public bool IsScancodePressed(int scancode) => false;
  }

  private sealed class TestMouse : IMouse {
    private Action<IMouse, ScrollWheel>? scroll;

    public int ScrollSubscriberCount => scroll?.GetInvocationList().Length ?? 0;

    public event Action<IMouse, MouseButton>? MouseDown;
    public event Action<IMouse, MouseButton>? MouseUp;
    public event Action<IMouse, MouseButton, Vector2>? Click;
    public event Action<IMouse, MouseButton, Vector2>? DoubleClick;
    public event Action<IMouse, Vector2>? MouseMove;
    public event Action<IMouse, ScrollWheel>? Scroll {
      add => scroll += value;
      remove => scroll -= value;
    }

    public string Name => "Test Mouse";
    public int Index => 0;
    public bool IsConnected => true;
    public IReadOnlyList<MouseButton> SupportedButtons => [];
    public IReadOnlyList<ScrollWheel> ScrollWheels => [new ScrollWheel(0f, 0f)];
    public Vector2 Position { get; set; }
    public ICursor Cursor => null!;
    public int DoubleClickTime { get; set; }
    public int DoubleClickRange { get; set; }

    public void BeginInput() { }
    public void EndInput() { }
    public bool IsButtonPressed(MouseButton button) => false;
  }

  private sealed class StatefulGlContext(List<string> released) : IGLContext {
    public bool MakesCurrent { get; set; } = true;
    public bool DeleteSucceeds { get; set; } = true;
    public bool DeviceReleaseSucceeds { get; set; } = true;
    public bool NativeContextAlive { get; private set; } = true;
    public bool DeviceContextAlive { get; private set; } = true;
    public int DeleteAttempts { get; private set; }
    public int DeviceReleaseAttempts { get; private set; }
    public int DisposeCount { get; private set; }
    public bool IsCurrent { get; private set; }
    public nint Handle => NativeContextAlive ? 1 : nint.Zero;
    public IGLContextSource? Source => null;

    public void MakeCurrent() {
      released.Add("make-current");
      if (!NativeContextAlive || !DeviceContextAlive)
        throw new InvalidOperationException(
          "The original native context chain is unavailable.");
      IsCurrent = MakesCurrent;
    }

    public void ReleaseContext() {
      if (!IsCurrent) MakeCurrent();
      DeleteAttempts++;
      released.Add("delete-context");
      IsCurrent = false;
      if (!DeleteSucceeds)
        throw new InvalidOperationException(
          "Injected native context deletion failure.");
      NativeContextAlive = false;
      released.Add("context");
    }

    public void ReleaseDeviceContext() {
      if (NativeContextAlive)
        throw new InvalidOperationException(
          "Cannot release the HDC before its GL context.");
      DeviceReleaseAttempts++;
      released.Add("release-device-context");
      if (!DeviceReleaseSucceeds)
        throw new InvalidOperationException(
          "Injected device-context release failure.");
      DeviceContextAlive = false;
      released.Add("device-context");
    }

    public void Clear() { }
    public void SwapBuffers() { }
    public void SwapInterval(int interval) { }
    public void Dispose() => DisposeCount++;
    public nint GetProcAddress(string proc, int? slot = null) => nint.Zero;

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null) {
      addr = nint.Zero;
      return false;
    }
  }

  private sealed record HandleGeneration(
    IGraphicsSurface Surface,
    GL Gl,
    StatefulGlContext Context,
    TestInputContext Input,
    Controller Controller,
    FakeRenderer Renderer
  );

  private sealed class FakeGlContext(List<string> released) : IGLContext {
    public bool MakesCurrent { get; set; } = true;
    public bool IsCurrent { get; private set; }
    public nint Handle => 1;
    public IGLContextSource? Source => null;

    public void MakeCurrent() {
      released.Add("make-current");
      IsCurrent = MakesCurrent;
    }

    public void Clear() { }
    public void SwapBuffers() { }
    public void SwapInterval(int interval) { }
    public void Dispose() { }
    public nint GetProcAddress(string proc, int? slot = null) => nint.Zero;

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null) {
      addr = nint.Zero;
      return false;
    }
  }
}
