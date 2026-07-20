using HidSharp.Reports;
using OpenRCT3.Platforms.Windows;
using Silk.NET.Input;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;

namespace OpenRCT3.Tests.Platforms;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class WindowsInputAdapterTests {
  [Test]
  public void ZeroDevices_CreatesOneLogicalKeyboardAndMouse() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var keyDownCount = 0;
    var mouseMoveCount = 0;
    adapter.Keyboards.Single().KeyDown += (_, _, _) => keyDownCount++;
    adapter.Mice.Single().MouseMove += (_, _) => mouseMoveCount++;

    control.EmitKeyDown(Keys.W);
    control.EmitMouseMove(10, 20);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adapter.Keyboards, Has.Count.EqualTo(1));
      Assert.That(adapter.Mice, Has.Count.EqualTo(1));
      Assert.That(keyDownCount, Is.EqualTo(1));
      Assert.That(mouseMoveCount, Is.EqualTo(1));
    }
  }

  [TestCase(Keys.B, Key.B)]
  [TestCase(Keys.D7, Key.Number7)]
  [TestCase(Keys.Enter, Key.Enter)]
  [TestCase(Keys.Escape, Key.Escape)]
  [TestCase(Keys.Back, Key.Backspace)]
  [TestCase(Keys.Delete, Key.Delete)]
  [TestCase(Keys.F12, Key.F12)]
  [TestCase(Keys.NumPad7, Key.Keypad7)]
  [TestCase(Keys.OemQuestion, Key.Slash)]
  [TestCase(Keys.RControlKey, Key.ControlRight)]
  public void KeyboardEvents_MapNonCameraKeysToSilkKeys(
    Keys windowsKey,
    Key expected
  ) {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var keyboard = adapter.Keyboards.Single();
    var received = Key.Unknown;
    keyboard.KeyDown += (_, key, _) => received = key;

    control.EmitKeyDown(windowsKey);

    using (Assert.EnterMultipleScope()) {
      Assert.That(received, Is.EqualTo(expected));
      Assert.That(keyboard.IsKeyPressed(expected), Is.True);
      Assert.That(keyboard.SupportedKeys, Does.Contain(expected));
    }

    control.EmitKeyUp(windowsKey);
    Assert.That(keyboard.IsKeyPressed(expected), Is.False);
  }

  [Test]
  public void UnsupportedWindowsKey_RemainsUnknownAndIsNotAdvertised() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var keyboard = adapter.Keyboards.Single();
    var received = Key.Space;
    keyboard.KeyDown += (_, key, _) => received = key;

    control.EmitKeyDown(Keys.VolumeUp);

    using (Assert.EnterMultipleScope()) {
      Assert.That(received, Is.EqualTo(Key.Unknown));
      Assert.That(keyboard.SupportedKeys, Does.Not.Contain(Key.Unknown));
    }
  }

  [Test]
  public void MouseButtonEvents_UpdatePositionBeforeNotification() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, [CreateMouseDevice()]);
    var mouse = adapter.Mice.Single();
    var downPosition = Vector2.Zero;
    var upPosition = Vector2.Zero;
    mouse.MouseDown += (sender, _) => downPosition = sender.Position;
    mouse.MouseUp += (sender, _) => upPosition = sender.Position;

    control.EmitMouseDown(MouseButtons.Left, 12, 34);
    control.EmitMouseUp(MouseButtons.Left, 56, 78);

    using (Assert.EnterMultipleScope()) {
      Assert.That(downPosition, Is.EqualTo(new Vector2(12, 34)));
      Assert.That(upPosition, Is.EqualTo(new Vector2(56, 78)));
    }
  }

  [Test]
  public void InterfaceDispose_DetachesEveryControlEventAndIsIdempotent() {
    using var control = new TestControl();
    var adapter = new InputAdapter(
      control,
      [
        CreateKeyboardDevice(),
        CreateKeyboardDevice(),
        CreateMouseDevice(),
        CreateMouseDevice(),
      ]);
    var keyboard = adapter.Keyboards.Single();
    var mouse = adapter.Mice.Single();
    var counts = new EventCounts();
    keyboard.KeyDown += (_, _, _) => counts.KeyDown++;
    keyboard.KeyUp += (_, _, _) => counts.KeyUp++;
    keyboard.KeyChar += (_, _) => counts.KeyChar++;
    mouse.MouseDown += (_, _) => counts.MouseDown++;
    mouse.MouseUp += (_, _) => counts.MouseUp++;
    mouse.Click += (_, _, _) => counts.Click++;
    mouse.DoubleClick += (_, _, _) => counts.DoubleClick++;
    mouse.MouseMove += (_, _) => counts.MouseMove++;
    mouse.Scroll += (_, _) => counts.Scroll++;

    EmitEveryInputEvent(control);
    var beforeDispose = counts.Copy();

    IInputContext context = adapter;
    context.Dispose();
    context.Dispose();
    EmitEveryInputEvent(control);

    using (Assert.EnterMultipleScope()) {
      Assert.That(counts, Is.EqualTo(beforeDispose));
      Assert.That(adapter.Keyboards, Is.Empty);
      Assert.That(adapter.Mice, Is.Empty);
    }
  }

  private static Device CreateKeyboardDevice() => new(
    "keyboard",
    1,
    1,
    "Test keyboard",
    "OpenRCT3 Tests",
    [Convert.ToUInt32(Usage.GenericDesktopKeyboard)]);

  private static Device CreateMouseDevice() => new(
    "mouse",
    1,
    2,
    "Test mouse",
    "OpenRCT3 Tests",
    [Convert.ToUInt32(Usage.GenericDesktopMouse)]);

  private static void EmitEveryInputEvent(TestControl control) {
    control.EmitKeyDown(Keys.W);
    control.EmitKeyUp(Keys.W);
    control.EmitKeyPress('w');
    control.EmitMouseDown(MouseButtons.Left, 10, 20);
    control.EmitClick();
    control.EmitMouseDown(MouseButtons.Left, 30, 40);
    control.EmitDoubleClick();
    control.EmitMouseUp(MouseButtons.Left, 50, 60);
    control.EmitMouseMove(70, 80);
    control.EmitMouseWheel(120, 90, 100);
  }

  private sealed record EventCounts {
    public int KeyDown { get; set; }
    public int KeyUp { get; set; }
    public int KeyChar { get; set; }
    public int MouseDown { get; set; }
    public int MouseUp { get; set; }
    public int Click { get; set; }
    public int DoubleClick { get; set; }
    public int MouseMove { get; set; }
    public int Scroll { get; set; }

    public EventCounts Copy() => this with { };
  }

  private sealed class TestControl : Control {
    public void EmitKeyDown(Keys key) => OnKeyDown(new KeyEventArgs(key));
    public void EmitKeyUp(Keys key) => OnKeyUp(new KeyEventArgs(key));
    public void EmitKeyPress(char character) =>
      OnKeyPress(new KeyPressEventArgs(character));
    public void EmitMouseDown(MouseButtons button, int x, int y) =>
      OnMouseDown(new MouseEventArgs(button, 1, x, y, 0));
    public void EmitMouseUp(MouseButtons button, int x, int y) =>
      OnMouseUp(new MouseEventArgs(button, 1, x, y, 0));
    public void EmitClick() => OnClick(EventArgs.Empty);
    public void EmitDoubleClick() => OnDoubleClick(EventArgs.Empty);
    public void EmitMouseMove(int x, int y) =>
      OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
    public void EmitMouseWheel(int delta, int x, int y) =>
      OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, x, y, delta));
  }
}
