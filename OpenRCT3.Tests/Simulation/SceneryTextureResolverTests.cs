// Scenery Texture Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryTextureResolverTests {
  [Test]
  public void TryResolve_UsesFirstFrameDisposesUnusedFramesAndCachesIgnoringCase() {
    var fixture = new CatalogFixture(Resource("Clouds"));
    using var catalog = fixture.Catalog;
    var first = Image(2, 2, new Rgba32(10, 20, 30, 255));
    var second = Image(1, 1, new Rgba32(40, 50, 60, 128));
    var third = Image(1, 2, new Rgba32(70, 80, 90, 64));
    var decodeCount = 0;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => {
      decodeCount++;
      return new FlexiTextureList(12, [
        new FlexiTexture(Recolorable.Second, first),
        new FlexiTexture(Recolorable.None, second),
        new FlexiTexture(Recolorable.Third, third),
      ]);
    });

    var found = resolver.TryResolve("clouds:FTX", out var texture);
    var foundAgain = resolver.TryResolve("CLOUDS:ftx", out var cached);

    using (Assert.EnterMultipleScope()) {
      Assert.That(found, Is.True);
      Assert.That(foundAgain, Is.True);
      Assert.That(cached, Is.SameAs(texture));
      Assert.That(texture!.Name, Is.EqualTo("Clouds"));
      Assert.That(texture.Width, Is.EqualTo(2));
      Assert.That(texture.Height, Is.EqualTo(2));
      Assert.That(texture.Recolorable, Is.EqualTo(Recolorable.Second));
      Assert.That(texture.Pixels[0, 0], Is.EqualTo(new Rgba32(10, 20, 30, 255)));
      Assert.That(decodeCount, Is.EqualTo(1));
      Assert.Throws<ObjectDisposedException>(new Action(() => _ = second[0, 0]));
      Assert.Throws<ObjectDisposedException>(new Action(() => _ = third[0, 0]));
    }
  }

  [Test]
  public void TryResolve_FlexiColoursCreateAndCacheDistinctActiveVariants() {
    var fixture = new CatalogFixture(Resource("Banner"));
    using var catalog = fixture.Catalog;
    var decodeCount = 0;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => {
      decodeCount++;
      return new FlexiTextureList(0, [IndexedFrame(Recolorable.First, 43)]);
    });

    var original = resolver.Resolve("Banner:ftx");
    var green = resolver.Resolve("Banner:ftx", new SceneryFlexiColours(11, 2, 3));
    var red = resolver.Resolve("Banner:ftx", new SceneryFlexiColours(28, 2, 3));
    var greenAgain = resolver.Resolve("banner:FTX", new SceneryFlexiColours(11, 2, 3));

    using (Assert.EnterMultipleScope()) {
      Assert.That(green, Is.SameAs(greenAgain));
      Assert.That(green, Is.Not.SameAs(original));
      Assert.That(red, Is.Not.SameAs(original));
      Assert.That(red, Is.Not.SameAs(green));
      Assert.That(red.Pixels[0, 0], Is.Not.EqualTo(green.Pixels[0, 0]));
      Assert.That(red.CacheKey, Is.Not.EqualTo(green.CacheKey));
      Assert.That(decodeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void TryResolve_FlexiColourCacheIgnoresInactiveChannels() {
    var fixture = new CatalogFixture(Resource("Flag"));
    using var catalog = fixture.Catalog;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) =>
      new FlexiTextureList(0, [IndexedFrame(Recolorable.First, 43)]));

    var first = resolver.Resolve("Flag:ftx", new SceneryFlexiColours(7, 1, 2));
    var inactiveChanged = resolver.Resolve(
      "Flag:ftx",
      new SceneryFlexiColours(7, 30, 31));

    Assert.That(inactiveChanged, Is.SameAs(first));
  }

  [Test]
  public void TryResolve_NonRecolorableFrameUsesRawTextureForEverySelection() {
    var fixture = new CatalogFixture(Resource("Stone"));
    using var catalog = fixture.Catalog;
    var decodeCount = 0;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => {
      decodeCount++;
      return new FlexiTextureList(0, [
        new FlexiTexture(
          Recolorable.None,
          Image(1, 1, new Rgba32(10, 20, 30, 255)))
      ]);
    });

    var raw = resolver.Resolve("Stone:ftx");
    var selected = resolver.Resolve("Stone:ftx", new SceneryFlexiColours(31, 30, 29));

    using (Assert.EnterMultipleScope()) {
      Assert.That(selected, Is.SameAs(raw));
      Assert.That(decodeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Dispose_ReleasesRawAndVariantOwnersWhileMaterialLeaseSurvives() {
    var fixture = new CatalogFixture(Resource("Trim"));
    using var catalog = fixture.Catalog;
    var resolver = new SceneryTextureResolver(catalog, (_, _) =>
      new FlexiTextureList(0, [IndexedFrame(Recolorable.First, 43)]));
    var raw = resolver.Resolve("Trim:ftx");
    var leased = resolver.Resolve("Trim:ftx", new SceneryFlexiColours(11, 0, 0));
    var unleased = resolver.Resolve("Trim:ftx", new SceneryFlexiColours(28, 0, 0));
    var material = new Textured { AlbedoTexture = leased };

    resolver.Dispose();
    resolver.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(raw.State, Is.EqualTo(State.Disposed));
      Assert.That(leased.State, Is.EqualTo(State.Uninitialized));
      Assert.That(unleased.State, Is.EqualTo(State.Disposed));
    }

    material.Dispose();
    Assert.That(leased.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void Dispose_ReleasesOwnerButMaterialLeaseKeepsTextureAlive() {
    var fixture = new CatalogFixture(Resource("Glass"));
    using var catalog = fixture.Catalog;
    var resolver = new SceneryTextureResolver(catalog, (_, _) =>
      new FlexiTextureList(0, [
        new FlexiTexture(Recolorable.None, Image(1, 1, new Rgba32(1, 2, 3, 128))),
      ]));
    var texture = resolver.Resolve("Glass:ftx");
    var material = new Textured(MaterialBlendMode.Alpha) { AlbedoTexture = texture };
    using var opaque = new Textured();

    resolver.Dispose();
    resolver.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
      Assert.That(material.RenderState, Is.EqualTo(MaterialRenderState.AlphaBlend));
      Assert.That(opaque.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
      Assert.That(material.CacheKey, Is.EqualTo(opaque.CacheKey));
      Assert.Throws<ObjectDisposedException>(new Action(() =>
        resolver.TryResolve("Glass:ftx", out _)));
    }

    material.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void TryResolve_ReturnsFalseOnlyForMissingOptionalReferences() {
    var fixture = new CatalogFixture();
    using var catalog = fixture.Catalog;
    var decodeCount = 0;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => {
      decodeCount++;
      throw new AssertionException("A missing resource must not be decoded.");
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(resolver.TryResolve(null, out var absent), Is.False);
      Assert.That(absent, Is.Null);
      Assert.That(resolver.TryResolve("", out absent), Is.False);
      Assert.That(absent, Is.Null);
      Assert.That(resolver.TryResolve("Missing:ftx", out absent), Is.False);
      Assert.That(absent, Is.Null);
      Assert.That(decodeCount, Is.Zero);
      Assert.Throws<ArgumentException>(new Action(() =>
        resolver.TryResolve("Missing", out _)));
      Assert.Throws<ArgumentException>(new Action(() =>
        resolver.TryResolve("Missing:shs", out _)));
    }
  }

  [Test]
  public void TryResolve_PropagatesMalformedFtxDataAndDoesNotCacheFailure() {
    var fixture = new CatalogFixture(Resource("Broken"));
    using var catalog = fixture.Catalog;
    var decodeCount = 0;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => {
      decodeCount++;
      throw new InvalidDataException("Injected malformed FTX data.");
    });

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.TryResolve("Broken:ftx", out _)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.TryResolve("broken:FTX", out _)));
    Assert.That(decodeCount, Is.EqualTo(2));
  }

  [Test]
  public void TryResolve_DecodesCommittedCustomOvlFixture() {
    var root = Path.Combine(
      RepositoryRoot(),
      "OpenCobra", "Tests", "Fixtures", "OVL", "CustomScenery",
      "RadiatorSprings-TownHall", "RS-TownHall");
    using var catalog = new SceneryResourceCatalog(root, @"misc\RS-TownHall");
    using var resolver = new SceneryTextureResolver(catalog);

    var found = resolver.TryResolve("rs-base:FTX", out var texture);
    var foundAgain = resolver.TryResolve("RS-BASE:ftx", out var cached);

    using (Assert.EnterMultipleScope()) {
      Assert.That(found, Is.True);
      Assert.That(foundAgain, Is.True);
      Assert.That(cached, Is.SameAs(texture));
      Assert.That(texture!.Name, Is.EqualTo("RS-Base"));
      Assert.That(texture.Width, Is.GreaterThan(0));
      Assert.That(texture.Height, Is.EqualTo(texture.Width));
      Assert.That(texture.Pixels[0, 0], Is.TypeOf<Rgba32>());
    }
  }

  [Test]
  public void Resolve_MissingRequiredReferenceThrows() {
    var fixture = new CatalogFixture();
    using var catalog = fixture.Catalog;
    using var resolver = new SceneryTextureResolver(catalog, (_, _) => default);

    Assert.Throws<KeyNotFoundException>(new Action(() => resolver.Resolve("Missing:ftx")));
  }

  private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
    TestContext.CurrentContext.TestDirectory,
    "..", "..", "..", ".."));

  private static Image<Rgba32> Image(int width, int height, Rgba32 color) =>
    new(width, height, color);

  private static FlexiTexture IndexedFrame(Recolorable recolorable, byte index) {
    var palette = new byte[256 * 4];
    palette[index * 4] = byte.MaxValue;
    return new FlexiTexture(
      recolorable,
      Image(1, 1, new Rgba32(0, 0, byte.MaxValue, byte.MaxValue))) {
      PaletteBgra = palette,
      IndexedPixels = new[] { index },
    };
  }

  private static OvlFile Resource(string name) =>
    new(name, FileType.FlexibleTexture, "fixture.common.ovl");

  private sealed class CatalogFixture {
    public SceneryResourceCatalog Catalog { get; }

    public CatalogFixture(params OvlFile[] resources) {
      var root = Path.Combine(Path.GetTempPath(), $"openrct3-textures-{Guid.NewGuid():N}");
      var commonPath = Path.Combine(root, "style.common.ovl");
      var archive = new Ovl("style.common.ovl");
      foreach (var resource in resources)
        archive.Add(resource, new OvlEntry(0, 1));
      Catalog = new SceneryResourceCatalog(
        root,
        "style",
        new SinglePairSource(commonPath, archive));
    }
  }

  private sealed class SinglePairSource(string commonPath, Ovl archive)
    : ISceneryResourceCatalogSource {
    public bool FileExists(string path) =>
      string.Equals(path, commonPath, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(path, UniquePath(commonPath), StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> EnumerateCommonOvls(string directory) => [];

    public IEnumerable<string> EnumerateMatchingCommonOvls(
      string directory,
      string fileName
    ) => [];

    public IEnumerable<string> EnumerateDescendantCommonOvls(string directory) => [];

    public Ovl LoadPair(string commonOvlPath) => archive;

    private static string UniquePath(string path) =>
      path[..^".common.ovl".Length] + ".unique.ovl";
  }
}
