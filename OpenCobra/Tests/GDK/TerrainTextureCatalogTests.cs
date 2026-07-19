// Terrain Texture Catalog Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using OpenCobra.GDK.Assets;
using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OVL.Tests.GDK;

[TestFixture]
public class TerrainTextureCatalogTests {
  [Test]
  public void Catalog_OrdersNamesAndMapsIndices() {
    using var catalog = CreateCatalog(
      "Terrain_02", "TerrainCliff1", "Terrain_00", "TerrainCliff0", "Terrain_01");

    Assert.That(catalog.SurfaceNames, Is.EqualTo(new[] {
      "Terrain_00", "Terrain_01", "Terrain_02",
    }));
    Assert.That(catalog.CliffNames, Is.EqualTo(new[] { "TerrainCliff0", "TerrainCliff1" }));
    Assert.That(catalog.GetSurface(1), Is.SameAs(catalog.SurfaceTextures[1]));
    Assert.That(catalog.GetCliff(0), Is.SameAs(catalog.CliffTextures[0]));
  }

  [Test]
  public void Catalog_MissingIndexFailsAndDisposesInput() {
    var textures = new[] {
      CreateTexture("Terrain_00"),
      CreateTexture("Terrain_02"),
      CreateTexture("TerrainCliff0"),
    };

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(textures)));

    Assert.That(exception!.Message, Does.Contain("Terrain_01"));
    Assert.That(textures.Select(texture => texture.State), Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_OutOfRangeAndDisposedAccessFailExplicitly() {
    var catalog = CreateCatalog("Terrain_00", "TerrainCliff0");
    var surface = catalog.GetSurface(0);

    var outOfRange = Assert.Throws<ArgumentOutOfRangeException>(
      new Action(() => catalog.GetSurface(1)));
    Assert.That(outOfRange!.Message, Does.Contain("Surface index 1"));

    catalog.Dispose();

    Assert.That(surface.State, Is.EqualTo(State.Disposed));
    Assert.Throws<ObjectDisposedException>(new Action(() => catalog.GetSurface(0)));
    Assert.Throws<ObjectDisposedException>(new Action(() => _ = catalog.SurfaceNames));
  }

  [Test]
  public void Cache_ReturnsSameLiveCatalogAndReloadsAfterDisposal() {
    var loadCount = 0;
    var cache = new TerrainTextureCatalogCache(_ => {
      loadCount++;
      return CreateCatalog("Terrain_00", "TerrainCliff0");
    });
    var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Terrain_RCT3.common.ovl");

    var first = cache.Get(path);
    var equivalentPath = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    var second = cache.Get(equivalentPath);

    Assert.That(second, Is.SameAs(first));
    Assert.That(loadCount, Is.EqualTo(1));

    first.Dispose();
    var reloaded = cache.Get(path);
    try {
      Assert.That(reloaded, Is.Not.SameAs(first));
      Assert.That(loadCount, Is.EqualTo(2));
    } finally {
      reloaded.Dispose();
    }
  }

  [Test]
  public void Cache_DoesNotRetainFailedLoads() {
    var loadCount = 0;
    var cache = new TerrainTextureCatalogCache(_ => {
      loadCount++;
      throw new FileNotFoundException("missing pair");
    });
    var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Terrain_RCT3.common.ovl");

    Assert.Throws<FileNotFoundException>(new Action(() => cache.Get(path)));
    Assert.Throws<FileNotFoundException>(new Action(() => cache.Get(path)));

    Assert.That(loadCount, Is.EqualTo(2));
  }

  [Test]
  public void Loader_MissingPairFailsBeforeOpeningArchive() {
    var missingPath = Path.Combine(
      TestContext.CurrentContext.WorkDirectory,
      Guid.NewGuid().ToString("N"),
      "Terrain_RCT3.common.ovl");

    var exception = Assert.Throws<FileNotFoundException>(
      new Action(() => TextureLoader.LoadTerrainCatalog(missingPath)));

    Assert.That(exception!.FileName, Is.EqualTo(Path.GetFullPath(missingPath)));
  }

  [Test]
  public void Loader_MissingUniquePairFailsBeforeOpeningCommonArchive() {
    var directory = Path.Combine(
      Path.GetTempPath(), $"openrct3-terrain-catalog-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var commonPath = Path.Combine(directory, "Terrain_RCT3.common.ovl");
    File.WriteAllBytes(commonPath, []);
    try {
      var exception = Assert.Throws<FileNotFoundException>(
        new Action(() => TextureLoader.LoadTerrainCatalog(commonPath)));

      Assert.That(exception!.FileName,
        Is.EqualTo(Path.Combine(directory, "Terrain_RCT3.unique.ovl")));
    } finally {
      Directory.Delete(directory, true);
    }
  }

  private static TerrainTextureCatalog CreateCatalog(params string[] names) =>
    new(names.Select(CreateTexture));

  private static Texture CreateTexture(string name) =>
    new(name, 1, 1, new Image<Rgba32>(1, 1));
}
