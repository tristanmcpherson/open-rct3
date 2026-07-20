// CameraControllerTests
//
// Authors:
//   - OpenRCT3 Contributors
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenRCT3.Simulation;
using Silk.NET.Input;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class CameraControllerTests {
  private const float Epsilon = 0.001f;

  [Test]
  public void Update_PanIsFrameRateIndependentAcrossEquivalentIntervals() {
    var singleStepCamera = CreateCamera();
    var splitStepCamera = CreateCamera();
    using var singleStep = new CameraController(singleStepCamera);
    using var splitStep = new CameraController(splitStepCamera);
    var input = new CameraControlInput(0f, 1f, 0f, 0f);

    singleStep.Update(TimeSpan.FromSeconds(0.2), input);
    splitStep.Update(TimeSpan.FromSeconds(0.1), input);
    splitStep.Update(TimeSpan.FromSeconds(0.1), input);

    Assert.That(
      Vector3.Distance(singleStepCamera.Target, splitStepCamera.Target),
      Is.EqualTo(0f).Within(Epsilon)
    );
    Assert.That(
      Vector3.Distance(singleStepCamera.Eye, splitStepCamera.Eye),
      Is.EqualTo(0f).Within(Epsilon)
    );
  }

  [TestCase(Key.W, 0f, 1f)]
  [TestCase(Key.Up, 0f, 1f)]
  [TestCase(Key.S, 0f, -1f)]
  [TestCase(Key.Down, 0f, -1f)]
  [TestCase(Key.D, 1f, 0f)]
  [TestCase(Key.Right, 1f, 0f)]
  [TestCase(Key.A, -1f, 0f)]
  [TestCase(Key.Left, -1f, 0f)]
  public void Update_MapsWasdAndArrowKeysToViewRelativePan(
    Key key,
    float expectedRight,
    float expectedForward
  ) {
    var camera = CreateCamera();
    var input = new TestInputContext();
    using var controller = new CameraController(
      camera,
      input,
      static () => false,
      static () => false
    );
    var initialTarget = camera.Target;
    var forward = camera.Target - camera.Eye;
    forward.Z = 0f;
    forward = Vector3.Normalize(forward);
    var right = Vector3.Cross(forward, Vector3.UnitZ);

    input.Keyboard.Press(key);
    controller.Update(TimeSpan.FromSeconds(0.1));

    var movement = Vector3.Normalize(camera.Target - initialTarget);
    var expected = (right * expectedRight) + (forward * expectedForward);
    Assert.That(Vector3.Dot(movement, expected), Is.EqualTo(1f).Within(Epsilon));
  }

  [Test]
  public void Update_QAndERotateInOppositeDirectionsWithoutChangingFraming() {
    var leftCamera = CreateCamera();
    var rightCamera = CreateCamera();
    using var left = new CameraController(leftCamera);
    using var right = new CameraController(rightCamera);
    var initialEye = leftCamera.Eye;
    var initialTarget = leftCamera.Target;

    left.Update(TimeSpan.FromSeconds(0.25), new CameraControlInput(0f, 0f, 1f, 0f));
    right.Update(TimeSpan.FromSeconds(0.25), new CameraControlInput(0f, 0f, -1f, 0f));

    Assert.Multiple(new Action(() => {
      Assert.That(leftCamera.Target, Is.EqualTo(initialTarget));
      Assert.That(rightCamera.Target, Is.EqualTo(initialTarget));
      Assert.That(leftCamera.Distance, Is.EqualTo(100f).Within(Epsilon));
      Assert.That(rightCamera.Distance, Is.EqualTo(100f).Within(Epsilon));
      Assert.That(Vector3.Distance(leftCamera.Eye, initialEye), Is.GreaterThan(0f));
      Assert.That(Vector3.Distance(rightCamera.Eye, initialEye), Is.GreaterThan(0f));
      Assert.That(leftCamera.Eye.Z, Is.EqualTo(rightCamera.Eye.Z).Within(Epsilon));
    }));
  }

  [Test]
  public void Update_RightMouseDragOrbitsWithoutChangingFraming() {
    var camera = CreateCamera();
    var input = new TestInputContext();
    using var controller = new CameraController(
      camera,
      input,
      static () => false,
      static () => false
    );
    var initialEye = camera.Eye;
    var initialTarget = camera.Target;

    input.Mouse.MoveTo(new Vector2(100f, 100f));
    input.Mouse.Press(MouseButton.Right);
    input.Mouse.MoveTo(new Vector2(220f, 160f));
    controller.Update(TimeSpan.Zero);

    Assert.Multiple(new Action(() => {
      Assert.That(camera.Target, Is.EqualTo(initialTarget));
      Assert.That(camera.Distance, Is.EqualTo(100f).Within(Epsilon));
      Assert.That(Vector3.Distance(camera.Eye, initialEye), Is.GreaterThan(0f));
      Assert.That(camera.Eye.Z, Is.EqualTo(initialEye.Z).Within(Epsilon));
    }));
  }

  [Test]
  public void Update_MouseOrbitRequiresRightButtonAndFreshPressAfterCapture() {
    var camera = CreateCamera();
    var input = new TestInputContext();
    var captureMouse = true;
    using var controller = new CameraController(
      camera,
      input,
      static () => false,
      () => captureMouse
    );
    var initialEye = camera.Eye;

    input.Mouse.MoveTo(new Vector2(100f, 100f));
    input.Mouse.Press(MouseButton.Left);
    input.Mouse.MoveTo(new Vector2(140f, 100f));
    input.Mouse.Release(MouseButton.Left);
    input.Mouse.Press(MouseButton.Right);
    input.Mouse.MoveTo(new Vector2(180f, 100f));
    captureMouse = false;
    controller.Update(TimeSpan.Zero);
    input.Mouse.MoveTo(new Vector2(220f, 100f));
    controller.Update(TimeSpan.Zero);
    Assert.That(camera.Eye, Is.EqualTo(initialEye));

    input.Mouse.Release(MouseButton.Right);
    input.Mouse.Press(MouseButton.Right);
    input.Mouse.MoveTo(new Vector2(260f, 100f));
    controller.Update(TimeSpan.Zero);
    Assert.That(Vector3.Distance(camera.Eye, initialEye), Is.GreaterThan(0f));
  }

  [Test]
  public void Update_MouseOrbitRejectsNonFinitePositionsAndBoundsMovement() {
    var camera = CreateCamera();
    var expectedCamera = CreateCamera();
    var input = new TestInputContext();
    using var controller = new CameraController(
      camera,
      input,
      static () => false,
      static () => false
    );

    input.Mouse.MoveTo(Vector2.Zero);
    input.Mouse.Press(MouseButton.Right);
    input.Mouse.MoveTo(new Vector2(float.NaN, 0f));
    input.Mouse.MoveTo(Vector2.Zero);
    input.Mouse.MoveTo(new Vector2(float.MaxValue, 0f));
    input.Mouse.MoveTo(new Vector2(float.MaxValue, 1f));
    controller.Update(TimeSpan.Zero);
    expectedCamera.Orbit(CameraController.MaxPendingMouseOrbitRadians);

    Assert.That(
      Vector3.Distance(camera.Eye, expectedCamera.Eye),
      Is.EqualTo(0f).Within(Epsilon)
    );
  }

  [Test]
  public void Update_MouseWheelZoomIsDiscreteAndBounded() {
    var camera = CreateCamera();
    var input = new TestInputContext();
    using var controller = new CameraController(
      camera,
      input,
      static () => false,
      static () => false
    );

    input.Mouse.ScrollBy(1f);
    controller.Update(TimeSpan.Zero);
    var zoomedDistance = camera.Distance;
    input.Mouse.ScrollBy(float.PositiveInfinity);
    controller.Update(TimeSpan.FromSeconds(0.1));

    Assert.That(zoomedDistance, Is.LessThan(100f));
    Assert.That(camera.Distance, Is.EqualTo(zoomedDistance).Within(Epsilon));

    controller.Update(TimeSpan.Zero, new CameraControlInput(0f, 0f, 0f, 1_000f));
    Assert.That(camera.Distance, Is.EqualTo(CameraController.MinDistance).Within(Epsilon));
    foreach (var _ in Enumerable.Range(0, 3))
      controller.Update(TimeSpan.Zero, new CameraControlInput(0f, 0f, 0f, -1_000f));
    Assert.That(camera.Distance, Is.EqualTo(CameraController.MaxDistance).Within(0.1f));
  }

  [Test]
  public void Update_ExtremeWheelZoomStopsAtTheSceneMinimumDistance() {
    var camera = new Camera();
    camera.Frame(Vector3.Zero, distance: 100f, minimumDistance: 35f);
    using var controller = new CameraController(camera);

    controller.Update(
      TimeSpan.Zero,
      new CameraControlInput(0f, 0f, 0f, ZoomSteps: 1_000f)
    );

    Assert.That(camera.Distance, Is.EqualTo(35f).Within(Epsilon));
  }

  [Test]
  public void Update_DropsCapturedInputWithoutLeavingQueuedWheelMovement() {
    var camera = CreateCamera();
    var input = new TestInputContext();
    var captureKeyboard = true;
    var captureMouse = true;
    using var controller = new CameraController(
      camera,
      input,
      () => captureKeyboard,
      () => captureMouse
    );
    var initialTarget = camera.Target;
    var initialDistance = camera.Distance;

    input.Keyboard.Press(Key.W);
    input.Mouse.ScrollBy(1f);
    controller.Update(TimeSpan.FromSeconds(0.1));
    Assert.That(camera.Target, Is.EqualTo(initialTarget));
    Assert.That(camera.Distance, Is.EqualTo(initialDistance).Within(Epsilon));

    captureKeyboard = false;
    captureMouse = false;
    controller.Update(TimeSpan.FromSeconds(0.1));
    Assert.That(Vector3.Distance(camera.Target, initialTarget), Is.GreaterThan(0f));
    Assert.That(camera.Distance, Is.EqualTo(initialDistance).Within(Epsilon));
  }

  [Test]
  public void Update_RejectsNonFiniteInputAndBoundsLongFrames() {
    var invalidCamera = CreateCamera();
    var boundedCamera = CreateCamera();
    var referenceCamera = CreateCamera();
    using var invalid = new CameraController(invalidCamera);
    using var bounded = new CameraController(boundedCamera);
    using var reference = new CameraController(referenceCamera);
    var initialTarget = invalidCamera.Target;
    var initialEye = invalidCamera.Eye;

    invalid.Update(
      TimeSpan.FromSeconds(0.1),
      new CameraControlInput(float.NaN, 1f, 0f, 1f)
    );
    bounded.Update(
      TimeSpan.FromHours(1),
      new CameraControlInput(0f, 1f, 0f, 0f)
    );
    reference.Update(
      TimeSpan.FromSeconds(CameraController.MaxElapsedSeconds),
      new CameraControlInput(0f, 1f, 0f, 0f)
    );

    Assert.Multiple(new Action(() => {
      Assert.That(invalidCamera.Target, Is.EqualTo(initialTarget));
      Assert.That(invalidCamera.Eye, Is.EqualTo(initialEye));
      Assert.That(
        Vector3.Distance(boundedCamera.Target, referenceCamera.Target),
        Is.EqualTo(0f).Within(Epsilon)
      );
    }));
  }

  [Test]
  public void Dispose_UnsubscribesFromInputEvents() {
    var camera = CreateCamera();
    var input = new TestInputContext();
    var controller = new CameraController(
      camera,
      input,
      static () => false,
      static () => false
    );

    Assert.Multiple(new Action(() => {
      Assert.That(input.Keyboard.KeyDownSubscriberCount, Is.EqualTo(1));
      Assert.That(input.Keyboard.KeyUpSubscriberCount, Is.EqualTo(1));
      Assert.That(input.Mouse.MouseDownSubscriberCount, Is.EqualTo(1));
      Assert.That(input.Mouse.MouseUpSubscriberCount, Is.EqualTo(1));
      Assert.That(input.Mouse.MouseMoveSubscriberCount, Is.EqualTo(1));
      Assert.That(input.Mouse.ScrollSubscriberCount, Is.EqualTo(1));
    }));

    controller.Dispose();

    Assert.Multiple(new Action(() => {
      Assert.That(input.Keyboard.KeyDownSubscriberCount, Is.Zero);
      Assert.That(input.Keyboard.KeyUpSubscriberCount, Is.Zero);
      Assert.That(input.Mouse.MouseDownSubscriberCount, Is.Zero);
      Assert.That(input.Mouse.MouseUpSubscriberCount, Is.Zero);
      Assert.That(input.Mouse.MouseMoveSubscriberCount, Is.Zero);
      Assert.That(input.Mouse.ScrollSubscriberCount, Is.Zero);
    }));
  }

  private static Camera CreateCamera() {
    var camera = new Camera();
    camera.Frame(Vector3.Zero, 100f);
    return camera;
  }

  private sealed class TestInputContext : IInputContext {
    public TestKeyboard Keyboard { get; } = new();
    public TestMouse Mouse { get; } = new();

    public event Action<IInputDevice, bool>? ConnectionChanged;
    public nint Handle => nint.Zero;
    public IReadOnlyList<IGamepad> Gamepads => [];
    public IReadOnlyList<IJoystick> Joysticks => [];
    public IReadOnlyList<IKeyboard> Keyboards => [Keyboard];
    public IReadOnlyList<IMouse> Mice => [Mouse];
    public IReadOnlyList<IInputDevice> OtherDevices => [];
    public void Dispose() { }
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
    public void Press(Key key) => keyDown?.Invoke(this, key, 0);
  }

  private sealed class TestMouse : IMouse {
    private Action<IMouse, MouseButton>? mouseDown;
    private Action<IMouse, MouseButton>? mouseUp;
    private Action<IMouse, Vector2>? mouseMove;
    private Action<IMouse, ScrollWheel>? scroll;

    public int MouseDownSubscriberCount => mouseDown?.GetInvocationList().Length ?? 0;
    public int MouseUpSubscriberCount => mouseUp?.GetInvocationList().Length ?? 0;
    public int MouseMoveSubscriberCount => mouseMove?.GetInvocationList().Length ?? 0;
    public int ScrollSubscriberCount => scroll?.GetInvocationList().Length ?? 0;

    public event Action<IMouse, MouseButton>? MouseDown {
      add => mouseDown += value;
      remove => mouseDown -= value;
    }
    public event Action<IMouse, MouseButton>? MouseUp {
      add => mouseUp += value;
      remove => mouseUp -= value;
    }
    public event Action<IMouse, MouseButton, Vector2>? Click;
    public event Action<IMouse, MouseButton, Vector2>? DoubleClick;
    public event Action<IMouse, Vector2>? MouseMove {
      add => mouseMove += value;
      remove => mouseMove -= value;
    }
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
    public void Press(MouseButton button) => mouseDown?.Invoke(this, button);
    public void Release(MouseButton button) => mouseUp?.Invoke(this, button);
    public void MoveTo(Vector2 position) {
      Position = position;
      mouseMove?.Invoke(this, position);
    }
    public void ScrollBy(float steps) => scroll?.Invoke(this, new ScrollWheel(0f, steps));
  }
}
