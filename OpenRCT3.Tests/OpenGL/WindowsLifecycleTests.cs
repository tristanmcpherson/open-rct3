using OpenRCT3.OpenGL;
using Silk.NET.Core.Contexts;

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
  public void InvalidContext_SkipsGpuOwnersButStillReleasesNativeResources() {
    var released = new List<string>();
    var resources = new WindowsSurfaceResourceOwner();
    resources.OwnContext(() => released.Add("context"));
    resources.OwnDeviceContext(() => released.Add("device-context"));
    resources.OwnGl(() => released.Add("gl"));
    resources.OwnInput(() => released.Add("input"));
    resources.OwnController(() => released.Add("controller"));
    resources.OwnRenderer(() => released.Add("renderer"));
    resources.OwnGame(() => released.Add("game"));

    var context = new FakeGlContext(released) { MakesCurrent = false };
    Assert.Throws<AggregateException>(new Action(() => resources.Dispose(context)));

    Assert.That(released, Is.EqualTo(new[] {
      "make-current", "input", "context", "device-context", "gl",
    }));
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
