using OpenRCT3.OpenGL;

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
}
