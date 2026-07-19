using OpenRCT3.OpenGL;
using Silk.NET.Core.Contexts;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenRCT3.Tests.OpenGL;

[TestFixture]
public class WindowsLifecycleTests {
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
    resources.OwnGame(() => released.Add("game"));

    resources.Dispose(new FakeGlContext(released));
    resources.Dispose(new FakeGlContext(released));

    Assert.That(released, Is.EqualTo(new[] {
      "make-current", "game", "renderer", "controller", "input",
      "context", "device-context", "gl",
    }));
  }

  [Test]
  public void HandleRecreation_UsesFreshResourceOwnerAndReleasesEachGenerationOnce() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceCycle();
    var context = new FakeGlContext(released);

    var first = resources.BeginHandle();
    first.OwnContext(() => released.Add("context-1"));
    first.OwnController(() => released.Add("controller-1"));
    resources.EndHandle(context);
    resources.EndHandle(context);

    var second = resources.BeginHandle();
    second.OwnContext(() => released.Add("context-2"));
    second.OwnController(() => released.Add("controller-2"));
    resources.EndHandle(context);
    resources.EndHandle(context);

    Assert.That(released, Is.EqualTo(new[] {
      "make-current", "controller-1", "context-1",
      "make-current", "controller-2", "context-2",
    }));
  }

  [Test]
  public void InvalidContext_RetryReleasesGpuOwnersAndClearsGameInstanceWithoutDuplicates() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    Game? ownedGame = null;
    var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
    var instanceField = typeof(Game).GetField(
      "<Instance>k__BackingField",
      BindingFlags.NonPublic | BindingFlags.Static) ??
      throw new InvalidOperationException("Could not access the game singleton backing field.");
    instanceField.SetValue(null, game);

    resources.OwnGameState(() => {
      ownedGame = Game.DetachInstance();
      released.Add("game-state");
    });
    resources.OwnContext(() => released.Add("context"));
    resources.OwnDeviceContext(() => released.Add("device-context"));
    resources.OwnGl(() => released.Add("gl"));
    resources.OwnInput(() => released.Add("input"));
    resources.OwnController(() => released.Add("controller"));
    resources.OwnRenderer(() => released.Add("renderer"));
    resources.OwnGame(() => {
      Assert.That(ownedGame, Is.SameAs(game));
      released.Add("game");
    });

    var context = new FakeGlContext(released) { MakesCurrent = false };
    try {
      Assert.Throws<AggregateException>(new Action(() => resources.Dispose(context)));
      using (Assert.EnterMultipleScope()) {
        Assert.That(Game.Instance, Is.Null);
        Assert.That(resources.HasPending, Is.True);
        Assert.That(released, Is.EqualTo(new[] {
          "make-current", "game-state", "input", "context", "device-context", "gl",
        }));
      }

      context.MakesCurrent = true;
      resources.Dispose(context);
      resources.Dispose(context);

      using (Assert.EnterMultipleScope()) {
        Assert.That(resources.HasPending, Is.False);
        Assert.That(released, Is.EqualTo(new[] {
          "make-current", "game-state", "input", "context", "device-context", "gl",
          "make-current", "game", "renderer", "controller",
        }));
      }
    } finally {
      instanceField.SetValue(null, null);
    }
  }

  [Test]
  public void GlContextLifetime_ZeroOwnedContextIsNeverCurrent() {
    var lifetime = new WindowsGlContextLifetime(42);

    Assert.That(lifetime.IsCurrent(() =>
      throw new InvalidOperationException("Native current-context query should not run.")),
      Is.False);

    lifetime.SetContext(84);
    using (Assert.EnterMultipleScope()) {
      Assert.That(lifetime.IsCurrent(() => 84), Is.True);
      Assert.That(lifetime.IsCurrent(() => nint.Zero), Is.False);
    }
  }

  [Test]
  public void GlContextLifetime_HandleReleaseSupportsRecreationAndFinalReleaseIsIdempotent() {
    var lifetime = new WindowsGlContextLifetime(42);
    lifetime.SetContext(84);

    var firstContext = lifetime.TakeContext();
    lifetime.SetContext(126);
    var released = lifetime.Release();
    var repeated = lifetime.Release();

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstContext, Is.EqualTo((nint)84));
      Assert.That(released.Library, Is.EqualTo((nint)42));
      Assert.That(released.Context, Is.EqualTo((nint)126));
      Assert.That(repeated, Is.EqualTo(default(WindowsGlContextLifetime.Handles)));
      Assert.That(lifetime.ContextHandle, Is.EqualTo(nint.Zero));
      Assert.That(lifetime.IsCurrent(() => 126), Is.False);
      Assert.Throws<ObjectDisposedException>(new Action(() => lifetime.SetContext(168)));
      Assert.Throws<ObjectDisposedException>(new Action(() => {
        _ = lifetime.LibraryHandle;
      }));
    }
  }

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
