using OpenRCT3.OpenGL;
using Silk.NET.Core.Contexts;

namespace OpenRCT3.Tests.OpenGL;

[TestFixture]
public class RendererResourceCacheTests {
  [Test]
  public void GetOrAdd_CreatesEquivalentResourceOnce() {
    using var cache = new ResourceCache<string, uint>(_ => { });
    var uploads = 0;

    var first = cache.GetOrAdd("same", _ => {
      uploads++;
      return 42;
    });
    var second = cache.GetOrAdd("same", _ => {
      uploads++;
      return 84;
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(first, Is.EqualTo(42));
      Assert.That(second, Is.EqualTo(42));
      Assert.That(uploads, Is.EqualTo(1));
    }
  }

  [Test]
  public void Dispose_ReleasesEachCachedResourceOnce() {
    var released = new List<uint>();
    var cache = new ResourceCache<string, uint>(released.Add);
    cache.GetOrAdd("first", _ => 42);
    cache.GetOrAdd("second", _ => 84);

    cache.Dispose();
    cache.Dispose();

    Assert.That(released, Is.EquivalentTo(new uint[] { 42, 84 }));
  }

  [Test]
  public void Dispose_TransfersOwnershipAndAggregatesReleaseFailures() {
    var released = new List<uint>();
    var cache = new ResourceCache<string, uint>(resource => {
      released.Add(resource);
      if (resource == 42) throw new InvalidOperationException("Injected deletion failure.");
    });
    cache.GetOrAdd("first", _ => 42);
    cache.GetOrAdd("second", _ => 84);

    Assert.Throws<AggregateException>(new Action(cache.Dispose));
    Assert.That(released, Is.EqualTo(new uint[] { 42, 84 }));

    cache.Dispose();
    Assert.That(released, Has.Count.EqualTo(2));
    Assert.Throws<ObjectDisposedException>(new Action(() =>
      cache.GetOrAdd("third", _ => 126)));
  }

  [Test]
  public void GetOrAdd_DoesNotCacheFailedCreation() {
    using var cache = new ResourceCache<string, uint>(_ => { });
    var attempts = 0;

    Assert.Throws<InvalidOperationException>(new Action(() =>
      cache.GetOrAdd("same", _ => {
        attempts++;
        throw new InvalidOperationException("Injected upload failure.");
      })));
    var resource = cache.GetOrAdd("same", _ => {
      attempts++;
      return 42;
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(resource, Is.EqualTo(42));
      Assert.That(attempts, Is.EqualTo(2));
    }
  }

  [Test]
  public void RendererTeardown_MakesContextCurrentBeforeEveryRelease() {
    var order = new List<string>();
    var context = new FakeGlContext(order);

    RendererTeardown.Run(context, [
      () => order.Add("scene"),
      () => order.Add("program"),
      () => order.Add("texture"),
      () => order.Add("context"),
      () => order.Add("gl"),
    ]);

    Assert.That(order, Is.EqualTo(new[] {
      "make-current", "scene", "program", "texture", "context", "gl",
    }));
  }

  [Test]
  public void RendererTeardown_AttemptsLaterReleasesAfterDeletionFailure() {
    var order = new List<string>();
    var context = new FakeGlContext(order);

    Assert.Throws<AggregateException>(new Action(() => RendererTeardown.Run(context, [
      () => throw new InvalidOperationException("Injected program deletion failure."),
      () => order.Add("texture"),
      () => order.Add("context"),
      () => order.Add("gl"),
    ])));

    Assert.That(order, Is.EqualTo(new[] { "make-current", "texture", "context", "gl" }));
  }

  [Test]
  public void RendererTeardown_RejectsContextThatDidNotBecomeCurrent() {
    var order = new List<string>();
    var context = new FakeGlContext(order) { MakesCurrent = false };

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RendererTeardown.Run(context, [() => order.Add("release")])));

    Assert.That(order, Is.EqualTo(new[] { "make-current" }));
  }

  private sealed class FakeGlContext(List<string> order) : IGLContext {
    public bool MakesCurrent { get; set; } = true;
    public bool IsCurrent { get; private set; }
    public nint Handle => 1;
    public IGLContextSource? Source => null;

    public void Clear() { }

    public void MakeCurrent() {
      order.Add("make-current");
      IsCurrent = MakesCurrent;
    }

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
