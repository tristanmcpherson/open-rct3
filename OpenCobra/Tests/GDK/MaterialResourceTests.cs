using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OVL.Tests.GDK;

[TestFixture]
public class MaterialResourceTests {
  [Test]
  public void EquivalentMaterialDefinitions_HaveEqualCacheKeys() {
    using var first = new Flat();
    using var second = new Flat();

    Assert.That(first.CacheKey, Is.EqualTo(second.CacheKey));
  }

  [Test]
  public void EquivalentTextureDefinitions_HaveEqualCacheKeys() {
    using var first = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    using var second = CreateTexture("shared", new Rgba32(10, 20, 30, 255));

    Assert.That(first.CacheKey, Is.EqualTo(second.CacheKey));
  }

  [Test]
  public void DifferentTexturePixels_HaveDifferentCacheKeys() {
    using var first = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    using var second = CreateTexture("shared", new Rgba32(30, 20, 10, 255));

    Assert.That(first.CacheKey, Is.Not.EqualTo(second.CacheKey));
  }

  [Test]
  public void EnsureUploaded_ReusesFirstHandle() {
    using var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var uploads = 0;

    texture.EnsureUploaded(() => {
      uploads++;
      return 42;
    });
    texture.EnsureUploaded(() => {
      uploads++;
      return 84;
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(uploads, Is.EqualTo(1));
      Assert.That(texture.Handle, Is.EqualTo(42));
      Assert.That(texture.State, Is.EqualTo(State.Ready));
    }
  }

  [Test]
  public void DisposingOneMaterial_KeepsSharedTextureAlive() {
    var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var first = new Textured { AlbedoTexture = texture };
    var second = new Textured { AlbedoTexture = texture };
    texture.EnsureUploaded(() => 42);

    first.Dispose();
    first.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Ready));

    second.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void DisposingUninitializedMesh_IsIdempotentWithoutGlContext() {
    var mesh = new Mesh([], []);

    mesh.Dispose();
    mesh.Dispose();

    Assert.That(mesh.State, Is.EqualTo(State.Disposed));
    Assert.Throws<ObjectDisposedException>(new Action(() =>
      mesh.Upload(new Silk.NET.OpenGL.Shader(0))));
  }

  private static Texture CreateTexture(string name, Rgba32 color) {
    var image = new Image<Rgba32>(1, 1, color);
    return new(name, 1, 1, image);
  }
}
