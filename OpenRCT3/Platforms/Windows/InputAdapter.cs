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
    }

    public IReadOnlyList<Key> SupportedKeys => [
      Key.W,
      Key.A,
      Key.S,
      Key.D,
      Key.Q,
      Key.E,
      Key.Up,
      Key.Down,
      Key.Left,
      Key.Right,
    ];
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
      lastClickedButton = null;
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
  internal static Key ToSilkKey(this Keys key) => key switch {
    Keys.W => Key.W,
    Keys.A => Key.A,
    Keys.S => Key.S,
    Keys.D => Key.D,
    Keys.Q => Key.Q,
    Keys.E => Key.E,
    Keys.Up => Key.Up,
    Keys.Down => Key.Down,
    Keys.Left => Key.Left,
    Keys.Right => Key.Right,
    _ => Key.Unknown,
  };
}
