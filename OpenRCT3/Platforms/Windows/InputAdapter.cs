// Windows Forms Input Adapter
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using HidSharp;
using HidSharp.Reports;
using NLog;
using OpenRCT3.Platforms.Input;
using Silk.NET.Input;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using System.Xml.Linq;

namespace OpenRCT3.Platforms.Windows;

internal readonly record struct Device(
  string DeviceId,
  int VendorId,
  int ProductId,
  string Name,
  string Manufacturer,
  uint[] Usages
);

internal interface IHidDevice : IInputDevice {
  Device Device { get; }
}

internal class HidDevice(int index, Device device) : IHidDevice {
  public Device Device { get; } = device;

  public string Name => Device.Name;

  public int Index { get; } = index;

  // FIXME: Detect disconnections from HidSharp
  public bool IsConnected { get; } = true;
}

public class InputAdapter : InputContext {
  private readonly Device[] devices;

  [SuppressMessage("Style", "IDE0305:Simplify collection initialization", Justification = "Explicitness for clarity")]
  public InputAdapter(Control control) : this(control, GetDevices()) { }

  internal InputAdapter(Control control, IEnumerable<Device> devices) : base(control.Handle) {
    this.devices = devices.ToArray();

    // WinForms provides one logical event stream for each device class. HID
    // discovery supplies representative metadata, not additional subscriptions.
    var keyboardDevice = GetRepresentativeDevice(
      Usage.GenericDesktopKeyboard,
      "winforms-keyboard",
      "Windows Forms Keyboard");
    var mouseDevice = GetRepresentativeDevice(
      Usage.GenericDesktopMouse,
      "winforms-mouse",
      "Windows Forms Mouse");
    var gamepads =
      from device in this.devices
      where device.Usages.Contains((uint)Usage.GenericDesktopGamepad)
      select new GamepadAdapter(this.devices.IndexOf(device), device);

    this.keyboards.Add(new KeyboardAdapter(0, keyboardDevice, control));
    this.mice.Add(new MouseAdapter(0, mouseDevice, control));
    this.gamepads.AddRange(gamepads);
  }

  private Device GetRepresentativeDevice(
    Usage usage,
    string fallbackId,
    string fallbackName
  ) => devices
    .Where(device => device.Usages.Contains(Convert.ToUInt32(usage)))
    .Select(device => (Device?)device)
    .FirstOrDefault() ?? new(
      fallbackId,
      0,
      0,
      fallbackName,
      "OpenRCT3",
      [Convert.ToUInt32(usage)]);

  public override void Dispose() {
    if (IsDisposed) return;

    foreach (var keyboard in keyboards.OfType<IDisposable>()) keyboard.Dispose();
    foreach (var mouse in mice.OfType<IDisposable>()) mouse.Dispose();
    base.Dispose();
  }

  //private IKeyboard? PrimaryKeyboard => devices.FirstOrDefault(dev => dev.Usages.Contains((uint)Usage.GenericDesktopKeyboard));
  //private IMouse? PrimaryMouse => devices.FirstOrDefault(dev => dev.Usages.Contains((uint)Usage.GenericDesktopMouse));
  //private IGamepad? PrimaryGamepad => devices.FirstOrDefault(dev => dev.Usages.Contains((uint)Usage.GenericDesktopGamepad));

  private static IEnumerable<Device> GetDevices() => DeviceList.Local.GetHidDevices()
    .Select(dev => dev.ToDevice(dev.GetDevices()))
    .Where(dev => dev != null).Cast<Device>();

  internal class KeyboardAdapter : HidDevice, IKeyboard, IDisposable {
    private readonly Control control;
    private bool disposed;
    private PressedKey[] pressedKeys = [];

    public event Action<IKeyboard, Key, int>? KeyDown;
    public event Action<IKeyboard, Key, int>? KeyUp;
    public event Action<IKeyboard, char>? KeyChar;

    public KeyboardAdapter(int index, Device device, Control control) : base(index, device) {
      this.control = control;
      control.KeyDown += OnKeyDown;
      control.KeyUp += OnKeyUp;
      control.KeyPress += OnKeyPress;
      control.LostFocus += OnLostFocus;
    }

    public IReadOnlyList<Key> SupportedKeys => KeysExtensions.SupportedKeys;
    public string ClipboardText {
      get => Clipboard.GetText();
      set => Clipboard.SetText(value);
    }

    private void OnKeyDown(object? _, KeyEventArgs e) {
      var key = e.KeyCode.ToSilkKey();
      pressedKeys = [
        .. pressedKeys.Where(pressed => pressed.Key != key),
        new PressedKey(key, e.KeyValue),
      ];
      KeyDown?.Invoke(this, key, e.KeyValue);
    }

    private void OnKeyUp(object? _, KeyEventArgs e) {
      var key = e.KeyCode.ToSilkKey();
      pressedKeys = [.. pressedKeys.Where(pressed => pressed.Key != key)];
      KeyUp?.Invoke(this, key, e.KeyValue);
    }

    private void OnKeyPress(object? _, KeyPressEventArgs e) => KeyChar?.Invoke(this, e.KeyChar);

    private void OnLostFocus(object? _, EventArgs e) => pressedKeys = [];

    public void BeginInput() {}
    public void EndInput() {}
    public bool IsKeyPressed(Key key) => pressedKeys.Select(k => k.Key).Contains(key);
    public bool IsScancodePressed(int scancode) => pressedKeys.Select(k => k.ScanCode).Contains(scancode);

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      control.KeyDown -= OnKeyDown;
      control.KeyUp -= OnKeyUp;
      control.KeyPress -= OnKeyPress;
      control.LostFocus -= OnLostFocus;
      pressedKeys = [];
      GC.SuppressFinalize(this);
    }
  }

  internal class MouseAdapter : HidDevice, IMouse, IDisposable {
    private readonly Control control;
    private bool disposed;
    private MouseState mouse = new();
    private MouseButton? lastClickedButton = null;

    public event Action<IMouse, MouseButton> MouseDown;
    public event Action<IMouse, MouseButton> MouseUp;
    public event Action<IMouse, MouseButton, Vector2> Click;
    public event Action<IMouse, MouseButton, Vector2> DoubleClick;
    public event Action<IMouse, Vector2> MouseMove;
    public event Action<IMouse, ScrollWheel> Scroll;

#pragma warning disable CS8618 // Silk.Net Bug: Mouse events are not nullable
    public MouseAdapter(int index, Device device, Control control) : base(index, device) {
      this.control = control;
      control.MouseDown += OnMouseDown;
      control.MouseUp += OnMouseUp;
      control.Click += OnClick;
      control.DoubleClick += OnDoubleClick;
      control.MouseMove += OnMouseMove;
      control.MouseWheel += OnMouseWheel;
      control.LostFocus += OnLostFocus;
      control.MouseCaptureChanged += OnMouseCaptureChanged;
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

    private void OnMouseDown(object? _, MouseEventArgs e) {
      var button = e.Button.ToMouseButton();
      mouse = mouse.WithPosition(new(e.Location.X, e.Location.Y));
      mouse = mouse.WithButton(button, true);
      lastClickedButton = button;
      MouseDown?.Invoke(this, button);
    }

    private void OnMouseUp(object? _, MouseEventArgs e) {
      var button = e.Button.ToMouseButton();
      mouse = mouse.WithPosition(new(e.Location.X, e.Location.Y));
      mouse = mouse.WithButton(button, false);
      MouseUp?.Invoke(this, button);
    }

    private void OnClick(object? _, EventArgs e) {
      if (lastClickedButton is not { } button) return;
      Click?.Invoke(this, button, mouse.Position);
      lastClickedButton = null;
    }

    private void OnDoubleClick(object? _, EventArgs e) {
      if (lastClickedButton is not { } button) return;
      DoubleClick?.Invoke(this, button, mouse.Position);
      lastClickedButton = null;
    }

    private void OnMouseMove(object? _, MouseEventArgs e) {
      mouse = mouse.WithPosition(new(e.Location.X, e.Location.Y));
      MouseMove?.Invoke(this, mouse.Position);
    }

    private void OnMouseWheel(object? _, MouseEventArgs e) =>
      Scroll?.Invoke(this, new ScrollWheel(
        0,
        (float)e.Delta / SystemInformation.MouseWheelScrollDelta));

    private void OnLostFocus(object? _, EventArgs e) => ResetPressedState(true);

    private void OnMouseCaptureChanged(object? _, EventArgs e) {
      if (control.Capture) return;
      ResetPressedState(mouse.PressedButtons.Length > 0);
    }

    private void ResetPressedState(bool clearPendingClick) {
      mouse = new([], mouse.Position, mouse.Cursor);
      if (clearPendingClick) lastClickedButton = null;
    }

    // FIXME: Get the supported buttons via the Win32 API
    public IReadOnlyList<MouseButton> SupportedButtons => [
      MouseButton.Left,
      MouseButton.Middle,
      MouseButton.Right
    ];
    public IReadOnlyList<ScrollWheel> ScrollWheels => [new ScrollWheel(0, 0)];

    public Vector2 Position {
      get => mouse.Position;
      // FIXME: Set the position via the Win32 API
      set {}
    }
    public ICursor Cursor => throw new NotImplementedException();

    public int DoubleClickTime {
      get => SystemInformation.DoubleClickTime;
      set {}
    }
    public int DoubleClickRange {
      get {
        var size = SystemInformation.DoubleClickSize;
        return Math.Max(size.Width, size.Height);
      }
      set {}
    }

    public bool IsButtonPressed(MouseButton btn) => mouse.PressedButtons.Contains(btn);

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      control.MouseDown -= OnMouseDown;
      control.MouseUp -= OnMouseUp;
      control.Click -= OnClick;
      control.DoubleClick -= OnDoubleClick;
      control.MouseMove -= OnMouseMove;
      control.MouseWheel -= OnMouseWheel;
      control.LostFocus -= OnLostFocus;
      control.MouseCaptureChanged -= OnMouseCaptureChanged;
      ResetPressedState(true);
      GC.SuppressFinalize(this);
    }
  }

  private class GamepadAdapter : HidDevice, IGamepad {
    public event Action<IGamepad, Silk.NET.Input.Button>? ButtonDown;
    public event Action<IGamepad, Silk.NET.Input.Button>? ButtonUp;
    public event Action<IGamepad, Thumbstick>? ThumbstickMoved;
    public event Action<IGamepad, Trigger>? TriggerMoved;

    public GamepadAdapter(int index, Device device) : base(index, device) { }

    public IReadOnlyList<Silk.NET.Input.Button> Buttons => throw new NotImplementedException();
    public IReadOnlyList<Thumbstick> Thumbsticks => throw new NotImplementedException();

    public IReadOnlyList<Trigger> Triggers => throw new NotImplementedException();

    public IReadOnlyList<IMotor> VibrationMotors => throw new NotImplementedException();

    public Deadzone Deadzone { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
  }
}

internal static class HidDeviceExtensions {
  private readonly static Logger logger = LogManager.GetCurrentClassLogger();

  internal static IList<DeviceItem> GetDevices(this HidSharp.HidDevice device) {
    try {
      return device.GetReportDescriptor().DeviceItems;
    } catch (NotSupportedException) {
      return Array.Empty<DeviceItem>();
    }
  }

  /// <summary>
  /// Try to get the details of a USB HID <paramref name="device"/>.
  /// </summary>
  /// <param name="device"></param>
  /// <param name="items">The HID reports for the given <paramref name="device"/></param>
  [SuppressMessage("Style", "IDE0305:Simplify collection initialization", Justification = "Explicitness for clarity")]
  internal static Device? ToDevice(this HidSharp.HidDevice device, IList<DeviceItem> items) {
    var deviceName = device.GetFriendlyName();

    try {
      return new(
        device.DevicePath,
        device.VendorID,
        device.ProductID,
        deviceName,
        device.GetManufacturer(),
        items.SelectMany(item => item.Usages.GetAllValues()).ToArray()
      );
    } catch (Exception e) {
      logger.Warn($"Could not determine USB HID class for {{device}}: {e.Message}", deviceName.Trim());
      return null;
    }
  }
}

internal static class MouseButtonsExtensions {
  internal static MouseButton ToMouseButton(this MouseButtons btn) => btn switch {
    MouseButtons.Left => MouseButton.Left,
    MouseButtons.Right => MouseButton.Right,
    MouseButtons.Middle => MouseButton.Middle,
    _ => throw new NotImplementedException(),
  };
}

internal static class KeysExtensions {
  internal static IReadOnlyList<Key> SupportedKeys { get; } = Enum
    .GetValues<Keys>()
    .Select(ToSilkKey)
    .Where(key => key != Key.Unknown)
    .Distinct()
    .ToArray();

  internal static Key ToSilkKey(this Keys key) => (key & Keys.KeyCode) switch {
    Keys.Space => Key.Space,
    Keys.OemQuotes => Key.Apostrophe,
    Keys.Oemcomma => Key.Comma,
    Keys.OemMinus => Key.Minus,
    Keys.OemPeriod => Key.Period,
    Keys.OemQuestion => Key.Slash,
    Keys.D0 => Key.Number0,
    Keys.D1 => Key.Number1,
    Keys.D2 => Key.Number2,
    Keys.D3 => Key.Number3,
    Keys.D4 => Key.Number4,
    Keys.D5 => Key.Number5,
    Keys.D6 => Key.Number6,
    Keys.D7 => Key.Number7,
    Keys.D8 => Key.Number8,
    Keys.D9 => Key.Number9,
    Keys.OemSemicolon => Key.Semicolon,
    Keys.Oemplus => Key.Equal,
    Keys.A => Key.A,
    Keys.B => Key.B,
    Keys.C => Key.C,
    Keys.D => Key.D,
    Keys.Q => Key.Q,
    Keys.E => Key.E,
    Keys.F => Key.F,
    Keys.G => Key.G,
    Keys.H => Key.H,
    Keys.I => Key.I,
    Keys.J => Key.J,
    Keys.K => Key.K,
    Keys.L => Key.L,
    Keys.M => Key.M,
    Keys.N => Key.N,
    Keys.O => Key.O,
    Keys.P => Key.P,
    Keys.R => Key.R,
    Keys.S => Key.S,
    Keys.T => Key.T,
    Keys.U => Key.U,
    Keys.V => Key.V,
    Keys.W => Key.W,
    Keys.X => Key.X,
    Keys.Y => Key.Y,
    Keys.Z => Key.Z,
    Keys.OemOpenBrackets => Key.LeftBracket,
    Keys.OemPipe => Key.BackSlash,
    Keys.OemCloseBrackets => Key.RightBracket,
    Keys.Oemtilde => Key.GraveAccent,
    Keys.OemBackslash => Key.BackSlash,
    Keys.Escape => Key.Escape,
    Keys.Enter => Key.Enter,
    Keys.Tab => Key.Tab,
    Keys.Back => Key.Backspace,
    Keys.Insert => Key.Insert,
    Keys.Delete => Key.Delete,
    Keys.Left => Key.Left,
    Keys.Right => Key.Right,
    Keys.Down => Key.Down,
    Keys.Up => Key.Up,
    Keys.PageUp => Key.PageUp,
    Keys.PageDown => Key.PageDown,
    Keys.Home => Key.Home,
    Keys.End => Key.End,
    Keys.CapsLock => Key.CapsLock,
    Keys.Scroll => Key.ScrollLock,
    Keys.NumLock => Key.NumLock,
    Keys.PrintScreen => Key.PrintScreen,
    Keys.Pause => Key.Pause,
    Keys.F1 => Key.F1,
    Keys.F2 => Key.F2,
    Keys.F3 => Key.F3,
    Keys.F4 => Key.F4,
    Keys.F5 => Key.F5,
    Keys.F6 => Key.F6,
    Keys.F7 => Key.F7,
    Keys.F8 => Key.F8,
    Keys.F9 => Key.F9,
    Keys.F10 => Key.F10,
    Keys.F11 => Key.F11,
    Keys.F12 => Key.F12,
    Keys.F13 => Key.F13,
    Keys.F14 => Key.F14,
    Keys.F15 => Key.F15,
    Keys.F16 => Key.F16,
    Keys.F17 => Key.F17,
    Keys.F18 => Key.F18,
    Keys.F19 => Key.F19,
    Keys.F20 => Key.F20,
    Keys.F21 => Key.F21,
    Keys.F22 => Key.F22,
    Keys.F23 => Key.F23,
    Keys.F24 => Key.F24,
    Keys.NumPad0 => Key.Keypad0,
    Keys.NumPad1 => Key.Keypad1,
    Keys.NumPad2 => Key.Keypad2,
    Keys.NumPad3 => Key.Keypad3,
    Keys.NumPad4 => Key.Keypad4,
    Keys.NumPad5 => Key.Keypad5,
    Keys.NumPad6 => Key.Keypad6,
    Keys.NumPad7 => Key.Keypad7,
    Keys.NumPad8 => Key.Keypad8,
    Keys.NumPad9 => Key.Keypad9,
    Keys.Decimal => Key.KeypadDecimal,
    Keys.Divide => Key.KeypadDivide,
    Keys.Multiply => Key.KeypadMultiply,
    Keys.Subtract => Key.KeypadSubtract,
    Keys.Add => Key.KeypadAdd,
    Keys.ShiftKey => Key.ShiftLeft,
    Keys.LShiftKey => Key.ShiftLeft,
    Keys.RShiftKey => Key.ShiftRight,
    Keys.ControlKey => Key.ControlLeft,
    Keys.LControlKey => Key.ControlLeft,
    Keys.RControlKey => Key.ControlRight,
    Keys.Menu => Key.AltLeft,
    Keys.LMenu => Key.AltLeft,
    Keys.RMenu => Key.AltRight,
    Keys.LWin => Key.SuperLeft,
    Keys.RWin => Key.SuperRight,
    Keys.Apps => Key.Menu,
    _ => Key.Unknown,
  };
}
