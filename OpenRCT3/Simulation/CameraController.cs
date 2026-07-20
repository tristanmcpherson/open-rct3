// Camera Controller
//
// Authors:
//   - OpenRCT3 Contributors
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using Silk.NET.Input;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

internal readonly record struct CameraControlInput(
  float PanRight,
  float PanForward,
  float Rotation,
  float ZoomSteps,
  bool CaptureKeyboard = false,
  bool CaptureMouse = false,
  float MouseOrbitRadians = 0f
);

/// <summary>
/// Converts desktop keyboard and mouse state into deterministic camera movement.
/// </summary>
internal sealed class CameraController : IDisposable {
  internal const float MinDistance = 2f;
  internal const float MaxDistance = 100_000f;
  internal const float MaxElapsedSeconds = 0.25f;
  internal const float MouseOrbitRadiansPerPixel = MathF.PI / 720f;
  internal const float MaxMouseMovePixels = 720f;
  internal const float MaxPendingMouseOrbitRadians = MathF.PI;

  private const float MinPanSpeed = 8f;
  private const float MaxPanSpeed = 1_500f;
  private const float PanSpeedPerDistance = 0.5f;
  private const float RotationSpeed = MathF.PI / 2f;
  private const float ZoomFactorPerStep = 0.85f;
  private const float MaxPendingZoomSteps = 32f;

  private readonly Camera camera;
  private readonly Func<bool> captureKeyboard;
  private readonly Func<bool> captureMouse;
  private readonly object inputGate = new();
  private readonly HashSet<Key> pressedKeys = [];
  private IKeyboard? keyboard;
  private IMouse? mouse;
  private float pendingZoomSteps;
  private float pendingMouseOrbitRadians;
  private Vector2? lastMousePosition;
  private bool mouseOrbitActive;
  private bool disposed;

  public CameraController(
    Camera camera,
    IInputContext input,
    Func<bool> captureKeyboard,
    Func<bool> captureMouse
  ) : this(camera, captureKeyboard, captureMouse) {
    ArgumentNullException.ThrowIfNull(input);

    keyboard = input.Keyboards.FirstOrDefault();
    mouse = input.Mice.FirstOrDefault();
    if (keyboard != null) {
      keyboard.KeyDown += Keyboard_KeyDown;
      keyboard.KeyUp += Keyboard_KeyUp;
    }
    if (mouse != null) {
      mouse.MouseDown += Mouse_MouseDown;
      mouse.MouseUp += Mouse_MouseUp;
      mouse.MouseMove += Mouse_MouseMove;
      mouse.Scroll += Mouse_Scroll;
    }
  }

  internal CameraController(Camera camera) : this(
    camera,
    static () => false,
    static () => false
  ) { }

  private CameraController(
    Camera camera,
    Func<bool> captureKeyboard,
    Func<bool> captureMouse
  ) {
    ArgumentNullException.ThrowIfNull(camera);
    ArgumentNullException.ThrowIfNull(captureKeyboard);
    ArgumentNullException.ThrowIfNull(captureMouse);
    this.camera = camera;
    this.captureKeyboard = captureKeyboard;
    this.captureMouse = captureMouse;
  }

  public void Update(TimeSpan delta) {
    CameraControlInput input;
    lock (inputGate) {
      if (disposed) return;
      var mouseCaptured = IsCaptured(captureMouse);
      if (mouseCaptured) {
        mouseOrbitActive = false;
        lastMousePosition = null;
        pendingMouseOrbitRadians = 0f;
      }
      input = new CameraControlInput(
        PanRight: Axis(Key.D, Key.Right, Key.A, Key.Left),
        PanForward: Axis(Key.W, Key.Up, Key.S, Key.Down),
        Rotation: Axis(Key.Q, Key.E),
        ZoomSteps: pendingZoomSteps,
        CaptureKeyboard: IsCaptured(captureKeyboard),
        CaptureMouse: mouseCaptured,
        MouseOrbitRadians: pendingMouseOrbitRadians
      );
      pendingZoomSteps = 0f;
      pendingMouseOrbitRadians = 0f;
    }

    Update(delta, input);
  }

  internal void Update(TimeSpan delta, CameraControlInput input) {
    if (disposed || !IsFinite(input)) return;

    var elapsedSeconds = delta.TotalSeconds;
    if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d) return;
    var elapsed = Convert.ToSingle(Math.Min(elapsedSeconds, MaxElapsedSeconds));

    var rotation = input.CaptureKeyboard ? 0f : Math.Clamp(input.Rotation, -1f, 1f);
    var keyboardOrbit = rotation * RotationSpeed * elapsed;
    var mouseOrbit = input.CaptureMouse
      ? 0f
      : Math.Clamp(
        input.MouseOrbitRadians,
        -MaxPendingMouseOrbitRadians,
        MaxPendingMouseOrbitRadians
      );
    var orbit = keyboardOrbit + mouseOrbit;
    if (orbit != 0f) camera.Orbit(orbit);

    if (!input.CaptureKeyboard && elapsed > 0f)
      Pan(input.PanRight, input.PanForward, elapsed);

    if (!input.CaptureMouse) Zoom(input.ZoomSteps);
  }

  public void Dispose() {
    IKeyboard? boundKeyboard;
    IMouse? boundMouse;
    lock (inputGate) {
      if (disposed) return;
      disposed = true;
      boundKeyboard = keyboard;
      boundMouse = mouse;
      keyboard = null;
      mouse = null;
      pressedKeys.Clear();
      pendingZoomSteps = 0f;
      pendingMouseOrbitRadians = 0f;
      lastMousePosition = null;
      mouseOrbitActive = false;
    }

    if (boundKeyboard != null) {
      boundKeyboard.KeyDown -= Keyboard_KeyDown;
      boundKeyboard.KeyUp -= Keyboard_KeyUp;
    }
    if (boundMouse != null) {
      boundMouse.MouseDown -= Mouse_MouseDown;
      boundMouse.MouseUp -= Mouse_MouseUp;
      boundMouse.MouseMove -= Mouse_MouseMove;
      boundMouse.Scroll -= Mouse_Scroll;
    }
  }

  private void Pan(float panRight, float panForward, float elapsed) {
    var pan = new Vector2(
      Math.Clamp(panRight, -1f, 1f),
      Math.Clamp(panForward, -1f, 1f)
    );
    if (pan.LengthSquared() > 1f) pan = Vector2.Normalize(pan);
    if (pan == Vector2.Zero) return;

    var forward = camera.Target - camera.Eye;
    forward.Z = 0f;
    var forwardLength = forward.Length();
    var distance = camera.Distance;
    if (!float.IsFinite(forwardLength) || forwardLength <= float.Epsilon) return;
    if (!float.IsFinite(distance) || distance <= 0f) return;

    forward /= forwardLength;
    var right = Vector3.Cross(forward, Vector3.UnitZ);
    var speed = Math.Clamp(distance * PanSpeedPerDistance, MinPanSpeed, MaxPanSpeed);
    var offset = ((right * pan.X) + (forward * pan.Y)) * speed * elapsed;
    if (!IsFinite(offset)) return;
    if (!IsFinite(camera.Target + offset) || !IsFinite(camera.Eye + offset)) return;
    camera.Pan(offset);
  }

  private void Zoom(float steps) {
    if (steps == 0f) return;

    var boundedSteps = Math.Clamp(steps, -MaxPendingZoomSteps, MaxPendingZoomSteps);
    var factor = MathF.Pow(ZoomFactorPerStep, boundedSteps);
    var minimumDistance = MathF.Max(MinDistance, camera.MinimumDistance);
    var maximumDistance = MathF.Max(MaxDistance, minimumDistance);
    var distance = Math.Clamp(camera.Distance * factor, minimumDistance, maximumDistance);
    if (float.IsFinite(distance)) camera.SetDistance(distance);
  }

  private float Axis(Key positive, Key alternatePositive, Key negative, Key alternateNegative) {
    var positivePressed = pressedKeys.Contains(positive)
      || pressedKeys.Contains(alternatePositive);
    var negativePressed = pressedKeys.Contains(negative)
      || pressedKeys.Contains(alternateNegative);
    return (positivePressed ? 1f : 0f) - (negativePressed ? 1f : 0f);
  }

  private float Axis(Key positive, Key negative) =>
    (pressedKeys.Contains(positive) ? 1f : 0f)
    - (pressedKeys.Contains(negative) ? 1f : 0f);

  private void Keyboard_KeyDown(IKeyboard _, Key key, int scanCode) {
    lock (inputGate) {
      if (!disposed) pressedKeys.Add(key);
    }
  }

  private void Keyboard_KeyUp(IKeyboard _, Key key, int scanCode) {
    lock (inputGate) pressedKeys.Remove(key);
  }

  private void Mouse_Scroll(IMouse _, ScrollWheel wheel) {
    if (!float.IsFinite(wheel.Y)) return;
    lock (inputGate) {
      if (disposed) return;
      pendingZoomSteps = Math.Clamp(
        pendingZoomSteps + wheel.Y,
        -MaxPendingZoomSteps,
        MaxPendingZoomSteps
      );
    }
  }

  private void Mouse_MouseDown(IMouse source, MouseButton button) {
    if (button != MouseButton.Right) return;

    var position = source.Position;
    lock (inputGate) {
      if (disposed) return;
      pendingMouseOrbitRadians = 0f;
      if (IsCaptured(captureMouse)) {
        mouseOrbitActive = false;
        lastMousePosition = null;
        return;
      }

      mouseOrbitActive = true;
      lastMousePosition = IsFinite(position) ? position : null;
    }
  }

  private void Mouse_MouseUp(IMouse _, MouseButton button) {
    if (button != MouseButton.Right) return;

    lock (inputGate) {
      mouseOrbitActive = false;
      lastMousePosition = null;
    }
  }

  private void Mouse_MouseMove(IMouse _, Vector2 position) {
    lock (inputGate) {
      if (disposed || !mouseOrbitActive) return;
      if (!IsFinite(position)) {
        lastMousePosition = null;
        return;
      }
      if (lastMousePosition is not { } previous) {
        lastMousePosition = position;
        return;
      }

      lastMousePosition = position;
      var deltaX = position.X - previous.X;
      if (!float.IsFinite(deltaX)) return;
      var boundedDelta = Math.Clamp(deltaX, -MaxMouseMovePixels, MaxMouseMovePixels);
      pendingMouseOrbitRadians = Math.Clamp(
        pendingMouseOrbitRadians + (boundedDelta * MouseOrbitRadiansPerPixel),
        -MaxPendingMouseOrbitRadians,
        MaxPendingMouseOrbitRadians
      );
    }
  }

  private static bool IsCaptured(Func<bool> capture) {
    try {
      return capture();
    } catch (InvalidOperationException) {
      return true;
    }
  }

  private static bool IsFinite(CameraControlInput input) =>
    float.IsFinite(input.PanRight)
    && float.IsFinite(input.PanForward)
    && float.IsFinite(input.Rotation)
    && float.IsFinite(input.ZoomSteps)
    && float.IsFinite(input.MouseOrbitRadians);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
