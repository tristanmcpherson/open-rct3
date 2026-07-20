#if MACOS || OSX || NET9_0_MACOS
using OpenRCT3.Platforms.macOS;

namespace OpenRCT3.Tests.Platforms;

[TestFixture]
public class MacInputAdapterTests {
  [Test]
  public void WindowOwnership_RequiresTheExactNonzeroOwnerHandle() {
    var owner = new nint(42);

    Assert.Multiple(new Action(() => {
      Assert.That(MacInputContext.IsOwnedWindow(owner, owner), Is.True);
      Assert.That(MacInputContext.IsOwnedWindow(owner, new nint(43)), Is.False);
      Assert.That(MacInputContext.IsOwnedWindow(owner, nint.Zero), Is.False);
      Assert.That(MacInputContext.IsOwnedWindow(nint.Zero, nint.Zero), Is.False);
    }));
  }

  [Test]
  public void MouseOwnership_RequiresAHitInsideTheGameView() {
    var owner = new nint(42);

    Assert.Multiple(new Action(() => {
      Assert.That(MacInputContext.IsOwnedViewTarget(owner, owner, true), Is.True);
      Assert.That(MacInputContext.IsOwnedViewTarget(owner, owner, false), Is.False);
      Assert.That(MacInputContext.IsOwnedViewTarget(owner, new nint(43), true), Is.False);
    }));
  }

  [Test]
  public void KeyboardOwnership_RequiresTheGameViewInResponderAncestry() {
    var gameView = new nint(42);

    Assert.Multiple(new Action(() => {
      Assert.That(
        MacInputContext.IsViewInAncestry(gameView, new nint[] { 44, 43, 42 }),
        Is.True);
      Assert.That(
        MacInputContext.IsViewInAncestry(gameView, new nint[] { 44, 43 }),
        Is.False);
      Assert.That(
        MacInputContext.IsViewInAncestry(nint.Zero, new nint[] { 44, 43 }),
        Is.False);
    }));
  }

  [Test]
  public void MousePosition_ConvertsAppKitBottomUpYToSharedTopDownY() {
    var position = MacInputContext.ToTopLeftPosition(new(25f, 30f), height: 100f);

    Assert.That(position, Is.EqualTo(new System.Numerics.Vector2(25f, 70f)));
  }
}
#endif
