// Terrain Texture Catalog Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using OpenCobra.GDK;
using OpenCobra.GDK.Assets;
using OpenCobra.GDK.Materials;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TerrainTypeKind = OpenCobra.OVL.Files.TerrainTypeKind;
using TerrainParameters = OpenCobra.OVL.Files.TerrainParameters;

namespace OVL.Tests.GDK;

[TestFixture]
public class TerrainTextureCatalogTests {
  [Test]
  public void Catalog_ComposesBaseAndExpansionPairsByExactTerNumber() {
    var entries = CreateCompleteEntries();
    using var catalog = new TerrainTextureCatalog(entries.Reverse(), true);

    Assert.That(catalog.SurfaceNames,
      Is.EqualTo(Enumerable.Range(0, TerrainTextureCatalog.SurfaceCount)
        .Select(index => $"Terrain_{index:D2}")));
    Assert.That(catalog.CliffNames,
      Is.EqualTo(Enumerable.Range(0, TerrainTextureCatalog.CliffCount)
        .Select(index => $"TerrainCliff{index}")));
    Assert.That(catalog.GetSurface(25), Is.SameAs(catalog.SurfaceTextures[25]));
    Assert.That(catalog.GetSurface(26), Is.SameAs(
      entries.Single(entry => entry.Number == 26
        && entry.Kind != TerrainTypeKind.Cliff).Texture));
    Assert.That(catalog.GetSurface(31), Is.SameAs(catalog.SurfaceTextures[31]));
    Assert.That(catalog.GetCliff(5), Is.SameAs(catalog.CliffTextures[5]));
  }

  [Test]
  public void Catalog_PreservesTerKindsAndParametersByExactNumber() {
    var entries = CreateCompleteEntries();
    var surfaceParameters = new TerrainParameters(11, 12, 0.1f, 0.2f);
    var cliffParameters = new TerrainParameters(21, 22, 0.25f, 0.5f);
    ReplaceEntry(entries, "Terrain_08", CreateEntry(
      "Terrain_08",
      8,
      TerrainTypeKind.GroundBlended,
      TerrainTextureCatalogLayer.Base,
      surfaceParameters));
    ReplaceEntry(entries, "TerrainCliff3", CreateEntry(
      "TerrainCliff3",
      3,
      TerrainTypeKind.Cliff,
      TerrainTextureCatalogLayer.Base,
      cliffParameters));
    using var catalog = new TerrainTextureCatalog(entries.Reverse(), true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(catalog.GetSurfaceKind(8), Is.EqualTo(TerrainTypeKind.GroundBlended));
      Assert.That(catalog.GetSurfaceParameters(8), Is.SameAs(surfaceParameters));
      Assert.That(catalog.GetCliffKind(3), Is.EqualTo(TerrainTypeKind.Cliff));
      Assert.That(catalog.GetCliffParameters(3), Is.SameAs(cliffParameters));
    }
  }

  [TestCase("Terrain_31", "surface index 31")]
  [TestCase("TerrainCliff5", "cliff index 5")]
  public void Catalog_TruncatedTrailingAssetsFailAndDisposeInput(
    string removedName,
    string expectedMessage
  ) {
    var entries = RemoveEntry(CreateCompleteEntries(), removedName);

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(exception!.Message, Does.Contain(expectedMessage));
    Assert.That(entries.Select(entry => entry.Texture.State), Is.All.EqualTo(State.Disposed));
  }

  [TestCase("Terrain_01", "surface index 1")]
  [TestCase("Terrain_27", "surface index 27")]
  public void Catalog_MissingBaseOrExpansionIndexFailsAndDisposesInput(
    string removedName,
    string expectedMessage
  ) {
    var entries = RemoveEntry(CreateCompleteEntries(), removedName);

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(exception!.Message, Does.Contain(expectedMessage));
    Assert.That(entries.Select(entry => entry.Texture.State), Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_DuplicateNumberAcrossPairsFailsAndDisposesInput() {
    var entries = CreateCompleteEntries()
      .Append(CreateSurfaceEntry(26, TerrainTextureCatalogLayer.CompleteEditionExpansion))
      .ToArray();

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(exception!.Message, Does.Contain("duplicate surface index 26"));
    Assert.That(entries.Select(entry => entry.Texture.State), Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_WrongExpansionKindFailsAndDisposesInput() {
    var entries = CreateCompleteEntries();
    ReplaceEntry(entries, "Terrain_26", CreateEntry(
      "Terrain_26",
      26,
      TerrainTypeKind.Cliff,
      TerrainTextureCatalogLayer.CompleteEditionExpansion));

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(exception!.Message, Does.Contain("surface-only overlay"));
    Assert.That(entries.Select(entry => entry.Texture.State), Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_ExpansionNumberOwnedByBasePairFailsClosed() {
    var entries = CreateCompleteEntries();
    ReplaceEntry(entries, "Terrain_26", CreateSurfaceEntry(
      26, TerrainTextureCatalogLayer.Base));

    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(exception!.Message, Does.Contain("Base terrain resource"));
    Assert.That(entries.Select(entry => entry.Texture.State), Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_NullEntryFailsAndDisposesEveryNonNullOwnedTexture() {
    var entries = CreateCompleteEntries().ToList();
    entries.Insert(1, null!);

    Assert.Throws<ArgumentNullException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(
      entries.OfType<TerrainTextureCatalogEntry>().Select(entry => entry.Texture.State),
      Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_NullTextureFailsAndDisposesEveryOtherOwnedTexture() {
    var entries = CreateCompleteEntries();
    entries[1].Texture.Dispose();
    entries[1] = entries[1] with { Texture = null! };

    Assert.Throws<ArgumentNullException>(
      new Action(() => new TerrainTextureCatalog(entries, true)));

    Assert.That(
      entries.OfType<TerrainTextureCatalogEntry>()
        .Select(entry => entry.Texture)
        .OfType<Texture>()
        .Select(texture => texture.State),
      Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Catalog_VanillaPairSupportsBaseAndNamesMissingExpansionPrecisely() {
    using var catalog = new TerrainTextureCatalog(CreateBaseEntries(), false);

    Assert.That(
      catalog.SurfaceTextures,
      Has.Count.EqualTo(TerrainTextureCatalog.BaseSurfaceCount));
    Assert.That(catalog.GetSurface(25).Name, Is.EqualTo("Terrain_25"));
    var exception = Assert.Throws<InvalidDataException>(
      new Action(() => catalog.GetSurface(TerrainTextureCatalog.BaseSurfaceCount)));

    Assert.That(exception!.Message, Does.Contain("Terrain_CT"));
    Assert.That(exception.Message, Does.Contain("indices 0-25"));
  }

  [Test]
  public void Catalog_OutOfRangeAndDisposedAccessFailExplicitly() {
    var catalog = CreateCompleteCatalog();
    var surface = catalog.GetSurface(0);

    var surfaceOutOfRange = Assert.Throws<ArgumentOutOfRangeException>(
      new Action(() => catalog.GetSurface(TerrainTextureCatalog.SurfaceCount)));
    var cliffOutOfRange = Assert.Throws<ArgumentOutOfRangeException>(
      new Action(() => catalog.GetCliff(TerrainTextureCatalog.CliffCount)));
    Assert.That(surfaceOutOfRange!.Message, Does.Contain("Surface index 32"));
    Assert.That(cliffOutOfRange!.Message, Does.Contain("Cliff index 6"));

    catalog.Dispose();

    Assert.That(surface.State, Is.EqualTo(State.Disposed));
    Assert.Throws<ObjectDisposedException>(new Action(() => catalog.GetSurface(0)));
    Assert.Throws<ObjectDisposedException>(new Action(() => _ = catalog.SurfaceNames));
  }

  [Test]
  public void CatalogDisposal_WaitsForTerrainMaterialLeases() {
    var catalog = CreateCompleteCatalog();
    var surface = catalog.GetSurface(11);
    var cliff = catalog.GetCliff(4);
    var surfaceMaterial = new Textured { AlbedoTexture = surface };
    var cliffMaterial = new Textured { AlbedoTexture = cliff };

    catalog.Dispose();

    Assert.That(surface.State, Is.EqualTo(State.Uninitialized));
    Assert.That(cliff.State, Is.EqualTo(State.Uninitialized));
    Assert.Throws<ObjectDisposedException>(new Action(() => catalog.GetSurface(0)));

    surfaceMaterial.Dispose();
    cliffMaterial.Dispose();

    Assert.That(surface.State, Is.EqualTo(State.Disposed));
    Assert.That(cliff.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void Cache_ReturnsSameLiveCatalogAndReloadsAfterDisposal() {
    var loadCount = 0;
    var cache = new TerrainTextureCatalogCache(_ => {
      loadCount++;
      return CreateCompleteCatalog();
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
  public void Cache_DisposeDuringAcquisition_RetriesWithLiveCatalog() {
    var loadCount = 0;
    var pauseAcquisition = 0;
    using var catalogResolved = new ManualResetEventSlim();
    using var continueAcquisition = new ManualResetEventSlim();
    var cache = new TerrainTextureCatalogCache(
      _ => {
        Interlocked.Increment(ref loadCount);
        return CreateCompleteCatalog();
      },
      _ => {
        if (Volatile.Read(ref pauseAcquisition) == 0) return;
        catalogResolved.Set();
        continueAcquisition.Wait();
      });
    var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Terrain_RCT3.common.ovl");
    var first = cache.Get(path);
    Volatile.Write(ref pauseAcquisition, 1);
    var acquisition = Task.Run(() => cache.Get(path));
    TerrainTextureCatalog? acquired = null;

    try {
      Assert.That(catalogResolved.Wait(TimeSpan.FromSeconds(5)), Is.True);
      first.Dispose();
      Volatile.Write(ref pauseAcquisition, 0);
      continueAcquisition.Set();

      acquired = acquisition.GetAwaiter().GetResult();
      Assert.That(acquired, Is.Not.SameAs(first));
      Assert.That(acquired.IsDisposed, Is.False);
      Assert.That(acquired.GetSurface(0).State, Is.Not.EqualTo(State.Disposed));
      Assert.That(loadCount, Is.EqualTo(2));
    } finally {
      Volatile.Write(ref pauseAcquisition, 0);
      continueAcquisition.Set();
      first.Dispose();
      if (acquired != null) acquired.Dispose();
      else if (acquisition.Wait(TimeSpan.FromSeconds(5))) acquisition.Result.Dispose();
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

  [Test]
  public void Loader_PartialCompleteEditionPairFailsBeforeOpeningBaseArchive() {
    var directory = Path.Combine(
      Path.GetTempPath(), $"openrct3-terrain-overlay-{Guid.NewGuid():N}");
    var baseDirectory = Path.Combine(directory, "RCT3");
    var expansionDirectory = Path.Combine(directory, "CT");
    Directory.CreateDirectory(baseDirectory);
    Directory.CreateDirectory(expansionDirectory);
    var baseCommonPath = Path.Combine(baseDirectory, "Terrain_RCT3.common.ovl");
    var expansionUniquePath = Path.Combine(expansionDirectory, "Terrain_CT.unique.ovl");
    File.WriteAllBytes(baseCommonPath, []);
    File.WriteAllBytes(Path.Combine(baseDirectory, "Terrain_RCT3.unique.ovl"), []);
    File.WriteAllBytes(Path.Combine(expansionDirectory, "Terrain_CT.common.ovl"), []);
    try {
      var exception = Assert.Throws<FileNotFoundException>(
        new Action(() => TextureLoader.LoadTerrainCatalog(baseCommonPath)));

      Assert.That(exception!.FileName, Is.EqualTo(expansionUniquePath));
      Assert.That(exception.Message, Does.Contain("Complete Edition terrain unique OVL"));
    } finally {
      Directory.Delete(directory, true);
    }
  }

  private static TerrainTextureCatalog CreateCompleteCatalog() =>
    new(CreateCompleteEntries(), true);

  private static TerrainTextureCatalogEntry[] CreateCompleteEntries() => [
    .. Enumerable.Range(0, TerrainTextureCatalog.BaseSurfaceCount)
      .Select(index => CreateSurfaceEntry(index, TerrainTextureCatalogLayer.Base)),
    .. Enumerable.Range(0, TerrainTextureCatalog.CliffCount)
      .Select(index => CreateEntry(
        $"TerrainCliff{index}",
        index,
        TerrainTypeKind.Cliff,
        TerrainTextureCatalogLayer.Base)),
    .. Enumerable.Range(
        TerrainTextureCatalog.BaseSurfaceCount,
        TerrainTextureCatalog.SurfaceCount - TerrainTextureCatalog.BaseSurfaceCount)
      .Select(index => CreateSurfaceEntry(
        index,
        TerrainTextureCatalogLayer.CompleteEditionExpansion)),
  ];

  private static TerrainTextureCatalogEntry[] CreateBaseEntries() => [
    .. Enumerable.Range(0, TerrainTextureCatalog.BaseSurfaceCount)
      .Select(index => CreateSurfaceEntry(index, TerrainTextureCatalogLayer.Base)),
    .. Enumerable.Range(0, TerrainTextureCatalog.CliffCount)
      .Select(index => CreateEntry(
        $"TerrainCliff{index}",
        index,
        TerrainTypeKind.Cliff,
        TerrainTextureCatalogLayer.Base)),
  ];

  private static TerrainTextureCatalogEntry CreateSurfaceEntry(
    int index,
    TerrainTextureCatalogLayer layer
  ) => CreateEntry(
    $"Terrain_{index:D2}",
    index,
    TerrainTypeKind.GroundUnblended,
    layer);

  private static TerrainTextureCatalogEntry CreateEntry(
    string name,
    int number,
    TerrainTypeKind kind,
    TerrainTextureCatalogLayer layer,
    TerrainParameters? parameters = null
  ) => new(
    name,
    Convert.ToUInt32(number),
    kind,
    parameters ?? new TerrainParameters(0, 0, 0.25f, 0.25f),
    name,
    CreateTexture(name),
    layer,
    layer == TerrainTextureCatalogLayer.Base
      ? "Terrain_RCT3.common.ovl"
      : "Terrain_CT.common.ovl");

  private static TerrainTextureCatalogEntry[] RemoveEntry(
    IEnumerable<TerrainTextureCatalogEntry> entries,
    string textureName
  ) {
    var ownedEntries = entries.ToArray();
    var removed = ownedEntries.Single(entry => entry.Texture.Name == textureName);
    removed.Texture.Dispose();
    return ownedEntries.Where(entry => !ReferenceEquals(entry, removed)).ToArray();
  }

  private static void ReplaceEntry(
    TerrainTextureCatalogEntry[] entries,
    string textureName,
    TerrainTextureCatalogEntry replacement
  ) {
    var index = Array.FindIndex(entries, entry => entry.Texture.Name == textureName);
    entries[index].Texture.Dispose();
    entries[index] = replacement;
  }

  private static Texture CreateTexture(string name) =>
    new(name, 1, 1, new Image<Rgba32>(1, 1));
}
