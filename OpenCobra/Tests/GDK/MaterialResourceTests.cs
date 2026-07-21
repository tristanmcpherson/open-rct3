using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using FlexiTexture = OpenCobra.OVL.Files.FlexiTexture;
using FlexiTextureList = OpenCobra.OVL.Files.FlexiTextureList;

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
  public void TexturedMaterial_UsesNormalsForBasicDirectionalLighting() {
    using var material = new Textured();

    using (Assert.EnterMultipleScope()) {
      Assert.That(material.Shaders.Vertex, Does.Contain("in vec3 a_Normal;"));
      Assert.That(material.Shaders.Vertex, Does.Contain("normalLength > 0.0001"));
      Assert.That(material.Shaders.Vertex, Does.Contain("v_Light = 0.45 + (0.55 * diffuse);"));
      Assert.That(material.Shaders.Fragment,
        Does.Contain("texColor.rgb * v_Color.rgb * v_Light"));
    }
  }

  [Test]
  public void BuiltInMaterials_DeclareTheirRenderState() {
    using var flat = new Flat();
    using var textured = new Textured();
    using var masked = new Textured(MaterialBlendMode.AlphaMask);
    using var blended = new Textured(MaterialBlendMode.Alpha);
    using var depthWritingBlend = new Textured(MaterialBlendMode.Alpha, depthWrite: true);
    using var testedBlend = new Textured(MaterialBlendMode.Alpha, 8);
    using var water = new Water();
    using var chrome = new Chrome();
    using var terrainBase = new TerrainBlend(TerrainBlendPass.Base);
    using var terrainContribution = new TerrainBlend(TerrainBlendPass.Contribution);

    using (Assert.EnterMultipleScope()) {
      Assert.That(flat.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
      Assert.That(flat.RenderState.CullBackFaces, Is.False);
      Assert.That(textured.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
      Assert.That(masked.RenderState, Is.EqualTo(MaterialRenderState.AlphaMask));
      Assert.That(masked.RenderState.IsTransparent, Is.False);
      Assert.That(masked.RenderState.DepthWrite, Is.True);
      Assert.That(masked.AlphaReference, Is.EqualTo(Textured.DefaultAlphaMaskReference));
      Assert.That(masked.Shaders.Fragment,
        Does.Contain("texColor.a <= 208.0 / 255.0"));
      Assert.That(blended.RenderState, Is.EqualTo(MaterialRenderState.AlphaBlend));
      Assert.That(depthWritingBlend.RenderState,
        Is.EqualTo(MaterialRenderState.AlphaBlend with { DepthWrite = true }));
      Assert.That(depthWritingBlend.RenderState.IsTransparent, Is.False);
      Assert.That(blended.Shaders.Fragment, Does.Not.Contain("discard"));
      Assert.That(testedBlend.RenderState, Is.EqualTo(MaterialRenderState.AlphaBlend));
      Assert.That(testedBlend.Shaders.Fragment,
        Does.Contain("texColor.a <= 8.0 / 255.0"));
      Assert.That(water.RenderState, Is.EqualTo(MaterialRenderState.AlphaBlend));
      Assert.That(water.RenderState.IsTransparent, Is.True);
      Assert.That(water.RenderState.DepthWrite, Is.False);
      Assert.That(chrome.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
      Assert.That(chrome.Textures, Is.Empty);
      Assert.That(terrainBase.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
      Assert.That(terrainContribution.RenderState,
        Is.EqualTo(MaterialRenderState.AdditiveContribution));
      Assert.That(terrainContribution.RenderState.DepthMode,
        Is.EqualTo(MaterialDepthMode.Equal));
    }
  }

  [Test]
  public void TerrainBlendMaterial_MultipliesTextureColourByQuantizedVertexWeight() {
    using var terrainBase = new TerrainBlend(TerrainBlendPass.Base);
    using var terrainContribution = new TerrainBlend(TerrainBlendPass.Contribution);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrainBase.Shaders.Fragment,
        Does.Contain("texColor.rgb * v_Tint * v_Light * v_Weight"));
      Assert.That(
        terrainBase.Shaders.Fragment,
        Is.EqualTo(terrainContribution.Shaders.Fragment));
      Assert.That(terrainBase.CacheKey, Is.EqualTo(terrainContribution.CacheKey));
      Assert.That(terrainBase.RenderState.DepthWrite, Is.True);
      Assert.That(terrainContribution.RenderState.DepthWrite, Is.False);
      Assert.That(terrainContribution.RenderState.IsAdditiveContribution, Is.True);
      Assert.That(terrainContribution.RenderState.IsTransparent, Is.False);
    }
  }

  [Test]
  public void Material_BackFaceCullingIsPartOfRenderState() {
    using var material = new Flat { CullBackFaces = true };

    Assert.That(material.RenderState,
      Is.EqualTo(MaterialRenderState.Opaque with { CullBackFaces = true }));
  }

  [Test]
  public void TexturedMaterial_AlphaReferencesDifferentiateShadersAndCacheKeys() {
    using var highMask = new Textured(MaterialBlendMode.AlphaMask);
    using var lowMask = new Textured(MaterialBlendMode.AlphaMask, 100);
    using var blended = new Textured(MaterialBlendMode.Alpha, 8);

    using (Assert.EnterMultipleScope()) {
      Assert.That(highMask.Shaders.Fragment,
        Does.Contain("texColor.a <= 208.0 / 255.0"));
      Assert.That(lowMask.Shaders.Fragment,
        Does.Contain("texColor.a <= 100.0 / 255.0"));
      Assert.That(blended.Shaders.Fragment,
        Does.Contain("texColor.a <= 8.0 / 255.0"));
      Assert.That(highMask.CacheKey, Is.Not.EqualTo(lowMask.CacheKey));
      Assert.That(highMask.CacheKey, Is.Not.EqualTo(blended.CacheKey));
      Assert.That(lowMask.CacheKey, Is.Not.EqualTo(blended.CacheKey));
    }
  }

  [Test]
  public void WaterMaterial_UsesViewDependentRestrainedLighting() {
    using var water = new Water();

    using (Assert.EnterMultipleScope()) {
      Assert.That(water.Shaders.Vertex, Does.Contain("in vec3 a_Normal;"));
      Assert.That(water.Shaders.Vertex, Does.Contain("v_WorldPosition"));
      Assert.That(water.Shaders.Fragment,
        Does.Contain($"uniform vec3 {Water.CameraPositionUniformName};"));
      Assert.That(water.Shaders.Fragment, Does.Contain("float specular"));
      Assert.That(water.Shaders.Fragment, Does.Contain("float fresnel"));
      Assert.That(water.Shaders.Fragment,
        Does.Contain("0.0, 0.60"));
    }
  }

  [Test]
  public void ChromeMaterial_UsesViewDependentProceduralReflection() {
    using var chrome = new Chrome();

    using (Assert.EnterMultipleScope()) {
      Assert.That(chrome.Shaders.Vertex, Does.Contain("in vec3 a_Normal;"));
      Assert.That(chrome.Shaders.Vertex, Does.Contain("v_WorldPosition"));
      Assert.That(chrome.Shaders.Fragment,
        Does.Contain($"uniform vec3 {Water.CameraPositionUniformName};"));
      Assert.That(chrome.Shaders.Fragment,
        Does.Contain("reflect(-viewDirection, normal)"));
      Assert.That(chrome.Shaders.Fragment, Does.Contain("20.0"));
    }
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
  public void PixelMutation_DoesNotChangeUploadSnapshotOrAliasCacheKey() {
    var original = new Rgba32(10, 20, 30, 255);
    var mutation = new Rgba32(30, 20, 10, 255);
    using var texture = CreateTexture("shared", original);
    var originalKey = texture.CacheKey;
    texture.Pixels[0, 0] = mutation;
    using var mutatedTexture = CreateTexture("shared", mutation);
    var uploaded = default(Rgba32);

    texture.EnsureUploaded(pixels => {
      uploaded = pixels[0];
      return 42;
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.CacheKey, Is.EqualTo(originalKey));
      Assert.That(texture.CacheKey, Is.Not.EqualTo(mutatedTexture.CacheKey));
      Assert.That(uploaded, Is.EqualTo(original));
    }
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
  public void UploadFailure_DeletesHandleAndAllowsRetry() {
    using var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var gpu = new FakeTextureGpuApi { FailUpload = true };

    Assert.Throws<InvalidOperationException>(new Action(() => texture.Upload(gpu)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(gpu.Deleted, Is.EqualTo(new uint[] { 1 }));
      Assert.That(texture.Handle, Is.Zero);
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
    }

    gpu.FailUpload = false;
    texture.Upload(gpu);
    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.Handle, Is.EqualTo(2));
      Assert.That(gpu.Uploads, Is.EqualTo(new[] { (1, 1, 1) }));
    }
  }

  [Test]
  public void UploadFailure_RejectsZeroHandleWithoutCachingIt() {
    using var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var gpu = new FakeTextureGpuApi { ReturnZeroHandle = true };

    Assert.Throws<InvalidOperationException>(new Action(() => texture.Upload(gpu)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(gpu.Uploads, Is.Empty);
      Assert.That(gpu.Deleted, Is.Empty);
      Assert.That(texture.Handle, Is.Zero);
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void CatalogOwner_KeepsTextureAliveAndAllowsResharing() {
    var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var catalog = new TestTextureCatalog(texture);
    var first = new Textured { AlbedoTexture = texture };
    var second = new Textured { AlbedoTexture = texture };

    first.Dispose();
    var third = new Textured();
    Assert.DoesNotThrow(new Action(() => third.AlbedoTexture = texture));
    second.Dispose();
    third.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Uninitialized));

    catalog.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void ReplacingTexture_ReleasesPreviousLeaseWithoutLeakingIt() {
    var previous = CreateTexture("previous", new Rgba32(10, 20, 30, 255));
    var replacement = CreateTexture("replacement", new Rgba32(30, 20, 10, 255));
    var material = new Textured { AlbedoTexture = previous };
    previous.Dispose();
    Assert.That(previous.State, Is.EqualTo(State.Uninitialized));

    material.AlbedoTexture = replacement;
    Assert.That(previous.State, Is.EqualTo(State.Disposed));

    material.Dispose();
    Assert.That(replacement.State, Is.EqualTo(State.Uninitialized));
    replacement.Dispose();
    Assert.That(replacement.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void OwnerAndMaterialDisposal_AreIdempotent() {
    var texture = CreateTexture("shared", new Rgba32(10, 20, 30, 255));
    var material = new Textured { AlbedoTexture = texture };

    texture.Dispose();
    texture.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Uninitialized));

    material.Dispose();
    material.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void AnimatedTexture_MixedSizeFramesUseOwnDimensionsAndPixelSpans() {
    var small = new Image<Rgba32>(1, 2, new Rgba32(10, 20, 30, 255));
    var wide = new Image<Rgba32>(3, 1, new Rgba32(30, 20, 10, 255));
    var source = new FlexiTextureList(12, [
      new FlexiTexture(Recolorable.None, small),
      new FlexiTexture(Recolorable.None, wide),
    ]);
    var animated = new AnimatedTexture("mixed", source);
    var gpu = new FakeTextureGpuApi();
    try {
      foreach (var frame in animated) frame.Upload(gpu);

      using (Assert.EnterMultipleScope()) {
        Assert.That(animated[0].Width, Is.EqualTo(1));
        Assert.That(animated[0].Height, Is.EqualTo(2));
        Assert.That(animated[1].Width, Is.EqualTo(3));
        Assert.That(animated[1].Height, Is.EqualTo(1));
        Assert.That(gpu.Uploads, Is.EqualTo(new[] { (1, 2, 2), (3, 1, 3) }));
      }
    } finally {
      foreach (var frame in animated) frame.Dispose();
    }
  }

  private static Texture CreateTexture(string name, Rgba32 color) {
    var image = new Image<Rgba32>(1, 1, color);
    return new(name, 1, 1, image);
  }

  private sealed class TestTextureCatalog(Texture texture) : IDisposable {
    public void Dispose() => texture.Dispose();
  }

  private sealed class FakeTextureGpuApi : Texture.IGpuApi {
    private uint nextHandle = 1;

    public bool FailUpload { get; set; }
    public bool ReturnZeroHandle { get; set; }
    public List<uint> Deleted { get; } = [];
    public List<(int Width, int Height, int PixelCount)> Uploads { get; } = [];

    public uint CreateTexture() => ReturnZeroHandle ? 0 : nextHandle++;

    public void UploadTexture(
      uint handle,
      IReadOnlyList<TextureMipUpload> mipLevels,
      TextureSamplingMode samplingMode
    ) {
      if (FailUpload) throw new InvalidOperationException("Injected texture upload failure.");
      foreach (var mipLevel in mipLevels) {
        if (mipLevel.Pixels.Length != checked(mipLevel.Width * mipLevel.Height))
          throw new InvalidOperationException("Pixel span does not match texture dimensions.");
        Uploads.Add((mipLevel.Width, mipLevel.Height, mipLevel.Pixels.Length));
      }
    }

    public void DeleteTexture(uint handle) => Deleted.Add(handle);
  }
}
