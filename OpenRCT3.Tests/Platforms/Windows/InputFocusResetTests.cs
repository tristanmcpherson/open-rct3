using OpenRCT3.Platforms.Windows;
using Silk.NET.Input;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;

namespace OpenRCT3.Tests.Platforms.Windows;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class InputFocusResetTests {
  [Test]
  public void LostFocus_ClearsPressedKeyboardState() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var keyboard = adapter.Keyboards.Single();

    control.EmitKeyDown(Keys.W);
    Assert.That(keyboard.IsKeyPressed(Key.W), Is.True);
    control.EmitLostFocus();

    using (Assert.EnterMultipleScope()) {
      Assert.That(keyboard.IsKeyPressed(Key.W), Is.False);
      Assert.That(keyboard.IsScancodePressed(Convert.ToInt32(Keys.W)), Is.False);
    }
  }

  [Test]
  public void LostFocus_ClearsPressedMouseStateWithoutMovingPointer() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var mouse = adapter.Mice.Single();

    control.EmitMouseDown(MouseButtons.Left, 12, 34);
    Assert.That(mouse.IsButtonPressed(MouseButton.Left), Is.True);
    control.EmitLostFocus();

    using (Assert.EnterMultipleScope()) {
      Assert.That(mouse.IsButtonPressed(MouseButton.Left), Is.False);
      Assert.That(mouse.Position, Is.EqualTo(new Vector2(12, 34)));
    }
  }

  [Test]
  public void MouseCaptureLoss_ClearsPressedAndPendingClickState() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var mouse = adapter.Mice.Single();
    var clicks = 0;
    mouse.Click += (_, _, _) => clicks++;

    control.EmitMouseDown(MouseButtons.Left, 56, 78);
    Assert.That(mouse.IsButtonPressed(MouseButton.Left), Is.True);
    control.EmitMouseCaptureChanged();
    control.EmitClick();

    using (Assert.EnterMultipleScope()) {
      Assert.That(mouse.IsButtonPressed(MouseButton.Left), Is.False);
      Assert.That(mouse.Position, Is.EqualTo(new Vector2(56, 78)));
      Assert.That(clicks, Is.Zero);
    }
  }

  [Test]
  public void CaptureReleaseAfterMouseUp_PreservesPendingClick() {
    using var control = new TestControl();
    using var adapter = new InputAdapter(control, []);
    var mouse = adapter.Mice.Single();
    var clicks = 0;
    mouse.Click += (_, _, _) => clicks++;

    control.Capture = true;
    control.EmitMouseDown(MouseButtons.Left, 90, 100);
    control.EmitMouseUp(MouseButtons.Left, 90, 100);
    control.Capture = false;
    control.EmitMouseCaptureChanged();
    control.EmitClick();

    using (Assert.EnterMultipleScope()) {
      Assert.That(mouse.IsButtonPressed(MouseButton.Left), Is.False);
      Assert.That(clicks, Is.EqualTo(1));
    }
  }

  private sealed class TestControl : Control {
    public void EmitKeyDown(Keys key) => OnKeyDown(new KeyEventArgs(key));
    public void EmitLostFocus() => OnLostFocus(EventArgs.Empty);
    public void EmitMouseDown(MouseButtons button, int x, int y) =>
      OnMouseDown(new MouseEventArgs(button, 1, x, y, 0));
    public void EmitMouseUp(MouseButtons button, int x, int y) =>
      OnMouseUp(new MouseEventArgs(button, 1, x, y, 0));
    public void EmitMouseCaptureChanged() => OnMouseCaptureChanged(EventArgs.Empty);
    public void EmitClick() => OnClick(EventArgs.Empty);
  }
}
