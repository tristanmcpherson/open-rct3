// Scenery Resource Catalog Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryResourceCatalogTests {
  private string installRoot = null!;

  [SetUp]
  public void SetUp() {
    installRoot = Path.Combine(Path.GetTempPath(), $"openrct3-catalog-{Guid.NewGuid():N}");
  }

  [TearDown]
  public void TearDown() {
    if (Directory.Exists(installRoot)) Directory.Delete(installRoot, recursive: true);
  }

  [Test]
  public void Find_LoadsExactPairLazilyAndMatchesNameAndTypeExactlyIgnoringCase() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPair(exact,
      Resource("TownHallLarge", FileType.StaticShape),
      Resource("TownHall", FileType.StaticShape));
    using var catalog = Catalog(source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.Empty);
      Assert.That(source.EnumerationCount, Is.Zero);
    }

    var result = catalog.Find("townhall", "SHS");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("TownHall"));
      Assert.That(result?.File.Type, Is.EqualTo(FileType.StaticShape));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void FindInSiblingOwnerPair_LoadsOnlyExactOwnerAndPreservesTypedEntries() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var owner = PairPath("Style", "Vanilla", "PathOwner");
    var fallback = PairPath("Style", "Vanilla", "fallback");
    source.AddPair(exact);
    source.AddPair(
      owner,
      Resource("PathOwner", FileType.SceneryItemVisual),
      Resource("OtherVisual", FileType.SceneryItemVisual),
      Resource("PathOwner", FileType.StaticShape));
    source.AddPair(fallback, Resource("FallbackVisual", FileType.SceneryItemVisual));
    source.EnumeratedPaths.Add(fallback);
    using var catalog = Catalog(source);

    var entries = catalog.FindInSiblingOwnerPair(
      "PathOwner",
      FileType.SceneryItemVisual);

    using (Assert.EnterMultipleScope()) {
      Assert.That(entries.Select(entry => entry.File.Name),
        Is.EqualTo(new[] { "PathOwner", "OtherVisual" }));
      Assert.That(entries.Select(entry => entry.File.Type),
        Is.All.EqualTo(FileType.SceneryItemVisual));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { owner }));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_LoadsSiblingPairsAsOneUnambiguousSetAndCachesThem() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var alpha = PairPath("Style", "Vanilla", "alpha");
    var zeta = PairPath("Style", "Vanilla", "zeta");
    var orphan = PairPath("Style", "Vanilla", "orphan");
    var nested = PairPath("Style", "Vanilla", "Nested", "hidden");
    source.AddPair(exact);
    source.AddPair(alpha, Resource("AlphaShape", FileType.StaticShape));
    source.AddPair(zeta, Resource("ZetaShape", FileType.StaticShape));
    source.AddCommonOnly(orphan);
    source.AddPair(nested, Resource("HiddenShape", FileType.StaticShape));
    source.EnumeratedPaths.AddRange(new[] { zeta, nested, orphan, exact, alpha, alpha });
    using var catalog = Catalog(source);

    var alphaResult = catalog.Find("alphashape", FileType.StaticShape);

    using (Assert.EnterMultipleScope()) {
      Assert.That(alphaResult?.File.Name, Is.EqualTo("AlphaShape"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, alpha, zeta }));
      Assert.That(source.EnumerationCount, Is.EqualTo(1));
      Assert.That(source.TargetedEnumerationCount, Is.EqualTo(1));
    }

    var zetaResult = catalog.Find("ZETASHAPE", FileType.StaticShape);
    var missingResult = catalog.Find("missing", FileType.StaticShape);

    using (Assert.EnterMultipleScope()) {
      Assert.That(zetaResult?.File.Name, Is.EqualTo("ZetaShape"));
      Assert.That(missingResult, Is.Null);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, alpha, zeta }));
      Assert.That(source.EnumerationCount, Is.EqualTo(1));
      Assert.That(source.TargetedEnumerationCount, Is.EqualTo(2));
    }
  }

  [Test]
  public void Find_ProbesMatchingOwnerFilenameRecursivelyBeforeScanningSiblings() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var nested = PairPath("Style", "Vanilla", "trees", "3dTrees", "LightGreen01");
    var unrelated = PairPath("Style", "Vanilla", "trees", "OtherOwner");
    var outside = PairPath("..", "outside", "LightGreen01");
    var sibling = PairPath("Style", "Vanilla", "fallback");
    source.AddPair(exact);
    source.AddPair(nested, Resource("LightGreen01", FileType.SceneryItemVisual));
    source.AddPair(unrelated, Resource("LightGreen01", FileType.SceneryItemVisual));
    source.AddPair(outside, Resource("LightGreen01", FileType.SceneryItemVisual));
    source.AddPair(sibling, Resource("LightGreen01", FileType.SceneryItemVisual));
    source.TargetedPaths.AddRange(new[] { unrelated, outside, nested });
    source.EnumeratedPaths.Add(sibling);
    using var catalog = Catalog(source);

    var result = catalog.Find("lightgreen01:SVD");
    var cached = catalog.Find("LIGHTGREEN01:svd");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("LightGreen01"));
      Assert.That(cached?.Archive, Is.SameAs(result?.Archive));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, nested }));
      Assert.That(source.TargetedEnumerationCount, Is.EqualTo(1));
      Assert.That(source.TargetedFileNames, Is.EqualTo(new[] { "lightgreen01.common.ovl" }));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_ProbesExactOwnerFilenameAcrossInstallRootBeforeLocalFallbacks() {
    var source = new FakeCatalogSource();
    var safari = PairPath("Style", "Vanilla", "Safari", "Style");
    var colonial = PairPath(
      "Style", "Vanilla", "WallSets", "Colonial", "Col_Wall1_3h");
    var localFallback = PairPath("Style", "Vanilla", "Safari", "Fallback");
    source.AddPair(safari, Resource("Col_Wall1_3h", FileType.SceneryItem));
    source.AddPair(
      colonial,
      Resource("Col_Wall1_3h", FileType.SceneryItemVisual));
    source.AddPair(
      localFallback,
      Resource("Col_Wall1_3h", FileType.SceneryItemVisual));
    source.TargetedPaths.Add(colonial);
    source.EnumeratedPaths.Add(localFallback);
    using var catalog = Catalog(source, @"Style\Vanilla\Safari\Style");

    var result = catalog.FindFrom(
      catalog.Find("Col_Wall1_3h", FileType.SceneryItem)!,
      "Col_Wall1_3h",
      FileType.SceneryItemVisual);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("Col_Wall1_3h"));
      Assert.That(result?.File.Type, Is.EqualTo(FileType.SceneryItemVisual));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { safari, colonial }));
      Assert.That(source.TargetedEnumerationCount, Is.EqualTo(1));
      Assert.That(source.TargetedFileNames,
        Is.EqualTo(new[] { "Col_Wall1_3h.common.ovl" }));
      Assert.That(source.EnumerationCount, Is.Zero);
      Assert.That(source.DescendantEnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_RejectsInstallRootOwnerProbeBeyondThePairLimit() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "Safari", "Style");
    source.AddPair(exact);
    foreach (var index in Enumerable.Range(0, 4_097))
      source.TargetedPaths.Add(PairPath("Owners", index.ToString(), "Missing"));
    using var catalog = Catalog(source, @"Style\Vanilla\Safari\Style");

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("Missing", FileType.SceneryItemVisual)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message,
        Does.Contain("targeted owner pair count exceeds 4096"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
    }
  }

  [TestCase(false)]
  [TestCase(true)]
  public void FindFrom_PrefersTargetedOwnerInsideExactOverlayTree(
    bool reverseEnumeration
  ) {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Themed", "WildWest", "Style");
    var spooky = PairPath(
      "Style", "Themed", "Spooky", "PathExtras", "SkullBin", "SkullBin");
    var wildWest = PairPath(
      "Style", "Themed", "WildWest", "PathExtras", "skullbins", "skullbin");
    source.AddPair(exact, Resource("PlacedBin", FileType.SceneryItem));
    source.AddPair(spooky, Resource("SkullBin", FileType.SceneryItemVisual));
    source.AddPair(wildWest, Resource("skullbin", FileType.SceneryItemVisual));
    source.TargetedPaths.AddRange(reverseEnumeration
      ? new[] { wildWest, spooky }
      : new[] { spooky, wildWest });
    using var catalog = Catalog(source, @"Style\Themed\WildWest\Style");
    var owner = catalog.Find("PlacedBin", FileType.SceneryItem);

    var result = catalog.FindFrom(
      owner!, "SkullBin", FileType.SceneryItemVisual);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("skullbin"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, wildWest }));
    }
  }

  [Test]
  public void FindFrom_RejectsAmbiguousTargetedOwnersOutsideExactOverlayTree() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Themed", "Adventure", "Style");
    var spooky = PairPath(
      "Style", "Themed", "Spooky", "PathExtras", "SkullBin", "SkullBin");
    var wildWest = PairPath(
      "Style", "Themed", "WildWest", "PathExtras", "skullbins", "skullbin");
    source.AddPair(exact, Resource("PlacedBin", FileType.SceneryItem));
    source.AddPair(spooky, Resource("SkullBin", FileType.SceneryItemVisual));
    source.AddPair(wildWest, Resource("skullbin", FileType.SceneryItemVisual));
    source.TargetedPaths.AddRange(new[] { wildWest, spooky });
    using var catalog = Catalog(source, @"Style\Themed\Adventure\Style");
    var owner = catalog.Find("PlacedBin", FileType.SceneryItem);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.FindFrom(owner!, "SkullBin", FileType.SceneryItemVisual)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("defined by both"));
      Assert.That(source.LoadedPaths.Skip(1),
        Is.EquivalentTo(new[] { spooky, wildWest }));
    }
  }

  [Test]
  public void Find_TargetedOwnerPartitionSharesReachablePairLimit() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Themed", "Adventure", "Style");
    var first = PairPath("Owners", "0000", "Missing");
    var child = PairPath("Owners", "0000", "Child");
    source.AddPair(exact);
    source.AddPairWithReferences(first, ["Child"]);
    source.AddPair(child);
    source.TargetedPaths.Add(first);
    foreach (var index in Enumerable.Range(1, 4_095)) {
      var candidate = PairPath("Owners", $"{index:D4}", "Missing");
      source.AddPair(candidate);
      source.TargetedPaths.Add(candidate);
    }
    using var catalog = Catalog(source, @"Style\Themed\Adventure\Style");

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("Missing", FileType.SceneryItemVisual)));

    Assert.That(error?.Message,
      Does.Contain("reachable pair count exceeds 4096"));
  }

  [Test]
  public void Find_RequiresBothFilesInTargetedOwnerPair() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "Safari", "Style");
    var targeted = PairPath("Owners", "Missing", "Missing");
    source.AddPair(exact);
    source.AddCommonOnly(targeted);
    source.TargetedPaths.Add(targeted);
    using var catalog = Catalog(source, @"Style\Vanilla\Safari\Style");

    var error = Assert.Throws<FileNotFoundException>(new Action(() =>
      catalog.Find("Missing", FileType.SceneryItemVisual)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.FileName, Is.EqualTo(UniquePath(targeted)));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
    }
  }

  [Test]
  public void Find_SearchesLoadedNestedOwnerBeforeProbingAnotherFilename() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var nested = PairPath("Style", "Vanilla", "trees", "3dTrees", "LightGreen01");
    source.AddPair(exact);
    source.AddPair(
      nested,
      Resource("LightGreen01", FileType.SceneryItemVisual),
      Resource("LightGreen01LOW", FileType.StaticShape));
    source.TargetedPaths.Add(nested);
    using var catalog = Catalog(source);

    var visual = catalog.Find("LightGreen01:svd");
    var shape = catalog.Find("LightGreen01LOW:shs");

    using (Assert.EnterMultipleScope()) {
      Assert.That(visual?.File.Name, Is.EqualTo("LightGreen01"));
      Assert.That(shape?.File.Name, Is.EqualTo("LightGreen01LOW"));
      Assert.That(shape?.Archive, Is.SameAs(visual?.Archive));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, nested }));
      Assert.That(source.TargetedEnumerationCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void FindFrom_UsesReferencingOwnerWhenLoadedPairsReuseAResourceName() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var left = PairPath("Style", "Vanilla", "LeftOwner");
    var right = PairPath("Style", "Vanilla", "RightOwner");
    source.AddPair(exact);
    source.AddPair(
      left,
      Resource("LeftOwner", FileType.SceneryItemVisual),
      Resource("SharedAnimation", FileType.BoneAnim));
    source.AddPair(
      right,
      Resource("RightOwner", FileType.SceneryItemVisual),
      Resource("SharedAnimation", FileType.BoneAnim));
    source.TargetedPaths.AddRange(new[] { right, left });
    using var catalog = Catalog(source);
    var leftOwner = catalog.Find("LeftOwner", FileType.SceneryItemVisual);
    var rightOwner = catalog.Find("RightOwner", FileType.SceneryItemVisual);

    var leftAnimation = catalog.FindFrom(
      leftOwner!, "SharedAnimation", FileType.BoneAnim);
    var rightAnimation = catalog.FindFrom(
      rightOwner!, "SharedAnimation", FileType.BoneAnim);

    using (Assert.EnterMultipleScope()) {
      Assert.That(leftAnimation?.Archive, Is.SameAs(leftOwner?.Archive));
      Assert.That(rightAnimation?.Archive, Is.SameAs(rightOwner?.Archive));
      Assert.That(rightAnimation?.Archive, Is.Not.SameAs(leftAnimation?.Archive));
    }
  }

  [Test]
  public void FindWithinOwnerClosure_PrefersExactOwnerWhileGlobalCollisionFailsClosed() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Prehistoric", "style");
    var pteradonShape = PairPath("Style", "Prehistoric", "PteradonShape");
    var pteradonTexture = PairPath("Style", "Prehistoric", "PteradonTexture");
    var entranceShape = PairPath("Style", "Prehistoric", "EntranceShape");
    source.AddPair(exact);
    source.AddPairWithReferences(
      pteradonShape,
      ["PteradonTexture"],
      Resource("PteradonShape", FileType.StaticShape));
    source.AddPair(
      pteradonTexture,
      Resource("SharedMaterial", FileType.StaticShape));
    source.AddPair(
      entranceShape,
      Resource("EntranceShape", FileType.BoneShape),
      Resource("SharedMaterial", FileType.StaticShape));
    source.TargetedPaths.AddRange([pteradonShape, entranceShape]);
    using var catalog = Catalog(source, @"Style\Prehistoric\style");
    var owner = catalog.Find("PteradonShape", FileType.StaticShape);
    Assert.That(owner, Is.Not.Null);
    Assert.That(catalog.Find("EntranceShape", FileType.BoneShape), Is.Not.Null);

    var scoped = catalog.FindWithinOwnerClosure(
      owner!,
      "SharedMaterial",
      FileType.StaticShape);
    var globalError = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("SharedMaterial", FileType.StaticShape)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(scoped?.Archive, Is.SameAs(source.LoadedArchives[2]));
      Assert.That(scoped?.File.Name, Is.EqualTo("SharedMaterial"));
      Assert.That(globalError?.Message, Does.Contain("defined by both"));
    }
  }

  [Test]
  public void FindWithinOwnerClosure_RejectsTwoDefinitionsReachableFromShapeOwner() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Prehistoric", "style");
    var shape = PairPath("Style", "Prehistoric", "PteradonShape");
    var firstTexture = PairPath("Style", "Prehistoric", "PteradonTexture");
    var secondTexture = PairPath("Style", "Prehistoric", "EntranceTexture");
    source.AddPair(exact);
    source.AddPairWithReferences(
      shape,
      ["PteradonTexture", "EntranceTexture"],
      Resource("PteradonShape", FileType.StaticShape));
    source.AddPair(
      firstTexture,
      Resource("SharedMaterial", FileType.StaticShape));
    source.AddPair(
      secondTexture,
      Resource("SharedMaterial", FileType.StaticShape));
    source.TargetedPaths.Add(shape);
    using var catalog = Catalog(source, @"Style\Prehistoric\style");
    var owner = catalog.Find("PteradonShape", FileType.StaticShape);
    Assert.That(owner, Is.Not.Null);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.FindWithinOwnerClosure(
        owner!,
        "SharedMaterial",
        FileType.StaticShape)));

    Assert.That(error?.Message, Does.Contain("defined by both"));
  }

  [Test]
  public void Find_FallsBackToRecursiveDescendantWhenOwnerFilenameDiffers() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var unrelated = PairPath("Style", "Vanilla", "PathExtras", "Alpha", "Alpha");
    var owner = PairPath("Style", "Vanilla", "PathExtras", "Bench", "Bench");
    source.AddPair(exact);
    source.AddPair(unrelated, Resource("OtherTexture", FileType.FlexibleTexture));
    source.AddPair(owner, Resource("BenchWood", FileType.FlexibleTexture));
    source.DescendantPaths.AddRange(new[] { owner, exact, unrelated, owner });
    using var catalog = Catalog(source);

    var result = catalog.Find("BenchWood:ftx");
    var cached = catalog.Find("BENCHWOOD:FTX");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("BenchWood"));
      Assert.That(cached?.Archive, Is.SameAs(result?.Archive));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, unrelated, owner }));
      Assert.That(source.DescendantEnumerationCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Find_ResolvesPoolTilesRelativeDependencyBeforeScanning() {
    var source = new FakeCatalogSource();
    var topSpin = PairPath("Style", "Vanilla", "Rides", "TopSpin", "TopSpin");
    var poolTiles = PairPath("Style", "SharedTextures", "PoolTiles");
    source.AddPairWithReferences(
      topSpin,
      [@"..\..\..\SharedTextures\PoolTiles"]);
    source.AddPair(poolTiles, Resource("PoolTile", FileType.FlexibleTexture));
    using var catalog = Catalog(
      source,
      @"Style\Vanilla\Rides\TopSpin\TopSpin");

    var result = catalog.Find("pooltile:ftx");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("PoolTile"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { topSpin, poolTiles }));
      Assert.That(source.EnumerationCount, Is.Zero);
      Assert.That(source.TargetedEnumerationCount, Is.Zero);
      Assert.That(source.DescendantEnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_ResolvesTrack18TransitiveDependenciesInSerializedOrder() {
    var source = new FakeCatalogSource();
    var trackData = PairPath(
      "tracks", "coasters", "Track18", "Invstraightbrakes_data");
    var textures = PairPath("tracks", "coasters", "Track18", "Track18_textures");
    var chain = PairPath("tracks", "SharedTextures", "chain");
    var lowTextures = PairPath("tracks", "SharedTextures", "coaster_LO_textures02");
    var finBrakes = PairPath("tracks", "SharedTextures", "finbrakes");
    source.AddPairWithReferences(trackData, ["Track18_textures"]);
    source.AddPairWithReferences(textures, [
      "../../SharedTextures/chain",
      "../../SharedTextures/coaster_LO_textures02",
      "../../SharedTextures/finbrakes"
    ]);
    source.AddPair(chain);
    source.AddPair(lowTextures);
    source.AddPair(finBrakes, Resource("FinBrake", FileType.FlexibleTexture));
    using var catalog = Catalog(
      source,
      @"tracks\coasters\Track18\Invstraightbrakes_data");

    var result = catalog.Find("finbrake:ftx");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.File.Name, Is.EqualTo("FinBrake"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] {
        trackData,
        textures,
        chain,
        lowTextures,
        finBrakes
      }));
      Assert.That(source.EnumerationCount, Is.Zero);
      Assert.That(source.TargetedEnumerationCount, Is.Zero);
      Assert.That(source.DescendantEnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_NormalizesDependencySuffixAndCaseBreaksCyclesAndCaches() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var alpha = PairPath("Style", "Vanilla", "Dependencies", "Alpha");
    source.AddPairWithReferences(
      exact,
      [@".\Dependencies/ALPHA.Unique.OVL"]);
    source.AddPairWithReferences(
      alpha,
      [@"..\STYLE.Common.OVL"],
      Resource("CycleShape", FileType.StaticShape));
    var catalog = Catalog(source);

    var first = catalog.Find("cycleshape:shs");
    var second = catalog.Find("CycleShape:shs");
    catalog.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(first?.File.Name, Is.EqualTo("CycleShape"));
      Assert.That(second?.Archive, Is.SameAs(first?.Archive));
      Assert.That(source.LoadedPaths, Has.Count.EqualTo(2));
      Assert.That(source.LoadedArchives, Has.Count.EqualTo(2));
      Assert.That(source.LoadedArchives, Has.All.Matches<Ovl>(archive => archive.Count == 0));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_RejectsByteIdenticalRelocationBackedDefinitionsAcrossDependencies() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var first = PairPath("Style", "Vanilla", "Dependencies", "first");
    var second = PairPath("Style", "Vanilla", "Dependencies", "second");
    source.AddPairWithReferences(
      exact,
      ["Dependencies/first", "Dependencies/second"]);
    source.AddPair(first, ResourceWithByte("first.bin", "SharedShape", 17));
    source.AddPair(second, ResourceWithByte("second.bin", "SharedShape", 17));
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("sharedshape:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("defined by both"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, first, second }));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void FlexibleTextureEquivalence_RequiresExactMetadataAndRgbaPixels() {
    using var firstImage = new Image<Rgba32>(2, 1);
    using var secondImage = new Image<Rgba32>(2, 1);
    firstImage[0, 0] = new Rgba32(1, 2, 3, 4);
    firstImage[1, 0] = new Rgba32(5, 6, 7, 8);
    secondImage[0, 0] = new Rgba32(1, 2, 3, 4);
    secondImage[1, 0] = new Rgba32(5, 6, 7, 8);
    var first = new FlexiTextureList(12, [
      new FlexiTexture(Recolorable.None, firstImage)
    ]);
    var second = new FlexiTextureList(12, [
      new FlexiTexture(Recolorable.None, secondImage)
    ]);

    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(first, second),
      Is.True);
    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(
        first,
        second with { Fps = 13 }),
      Is.False);

    secondImage[1, 0] = new Rgba32(5, 6, 7, 9);
    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(first, second),
      Is.False);
  }

  [Test]
  public void FlexibleTextureEquivalence_RequiresMatchingRecolorBackingData() {
    using var firstImage = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 4));
    using var secondImage = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 4));
    var firstPalette = new byte[256 * 4];
    var secondPalette = new byte[256 * 4];
    firstPalette[4] = 1;
    secondPalette[4] = 1;
    byte[] firstIndices = [1];
    byte[] secondIndices = [1];
    var first = new FlexiTextureList(0, [
      new FlexiTexture(Recolorable.First, firstImage) {
        PaletteBgra = firstPalette,
        IndexedPixels = firstIndices,
      }
    ]);
    var second = new FlexiTextureList(0, [
      new FlexiTexture(Recolorable.First, secondImage) {
        PaletteBgra = secondPalette,
        IndexedPixels = secondIndices,
      }
    ]);

    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(first, second),
      Is.True);

    secondIndices[0] = 2;
    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(first, second),
      Is.False);

    secondIndices[0] = 1;
    secondPalette[4] = 2;
    Assert.That(
      SceneryResourceCatalog.AreEquivalentFlexibleTextures(first, second),
      Is.False);
  }

  [Test]
  public void Find_RejectsMateriallyDifferentDefinitionsAcrossDependencies() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var first = PairPath("Style", "Vanilla", "Dependencies", "first");
    var second = PairPath("Style", "Vanilla", "Dependencies", "second");
    source.AddPairWithReferences(
      exact,
      ["Dependencies/first", "Dependencies/second"]);
    source.AddPair(first, ResourceWithByte("first.bin", "SharedShape", 17));
    source.AddPair(second, ResourceWithByte("second.bin", "SharedShape", 18));
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("sharedshape:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("defined by both"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact, first, second }));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_RejectsMateriallyDifferentDuplicateDefinitionsWithinArchive() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPair(
      exact,
      ResourceWithByte("first.bin", "SharedShape", 17),
      ResourceWithByte("second.bin", "SharedShape", 18));
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("sharedshape:shs")));

    Assert.That(error?.Message, Does.Contain("duplicate exact definitions"));
  }

  [Test]
  public void Find_RejectsDependencyOutsideInstallRootBeforeScanning() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPairWithReferences(exact, [@"..\..\..\outside\evil"]);
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("missing:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("leaves the install root"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
      Assert.That(source.FileExistsCount, Is.EqualTo(2));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [TestCase(" bad")]
  [TestCase("bad.ovl")]
  [TestCase("bad*")]
  public void Find_RejectsMalformedDeclaredDependencyBeforeScanning(string reference) {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPairWithReferences(exact, [reference]);
    using var catalog = Catalog(source);

    Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("missing:shs")));
    Assert.That(source.EnumerationCount, Is.Zero);
  }

  [Test]
  public void Find_RejectsIncompleteDeclaredDependencyPair() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var missing = PairPath("Style", "Vanilla", "missing");
    source.AddPairWithReferences(exact, ["missing"]);
    source.AddCommonOnly(missing);
    using var catalog = Catalog(source);

    var error = Assert.Throws<FileNotFoundException>(new Action(() =>
      catalog.Find("missing:shs")));

    Assert.That(error?.FileName, Is.EqualTo(UniquePath(missing)));
  }

  [Test]
  public void Find_RejectsDependencyDepthAboveHardLimit() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPairWithReferences(exact, ["node0"]);
    foreach (var index in Enumerable.Range(0, 65)) {
      var path = PairPath("Style", "Vanilla", $"node{index}");
      var references = index < 64 ? new[] { $"node{index + 1}" } : [];
      source.AddPairWithReferences(path, references);
    }
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("missing:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("dependency depth exceeds 64"));
      Assert.That(source.LoadedPaths, Has.Count.EqualTo(65));
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Find_RejectsDependencyCountAboveHardLimit() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPairWithReferences(
      exact,
      Enumerable.Range(0, 4_097).Select(index => $"node{index}").ToArray());
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("missing:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("dependency count 4097 exceeds 4096"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
    }
  }

  [Test]
  public void Find_RejectsReachablePairCountAboveHardLimit() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var references = Enumerable.Range(0, 4_096)
      .Select(index => $"Dependencies/node{index:D4}")
      .ToArray();
    source.AddPairWithReferences(exact, references);
    foreach (var index in Enumerable.Range(0, references.Length))
      source.AddExistingPairPaths(
        PairPath("Style", "Vanilla", "Dependencies", $"node{index:D4}"));
    using var catalog = Catalog(source);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.Find("missing:shs")));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("reachable pair count exceeds 4096"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { exact }));
    }
  }

  [Test]
  public void Constructor_RejectsOverlayOutsideInstallRootWithoutTouchingSource() {
    var source = new FakeCatalogSource();

    Assert.Throws<ArgumentException>(new Action(() =>
      new SceneryResourceCatalog(
        installRoot,
        Path.Combine("..", "outside", "style"),
        source)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.Empty);
      Assert.That(source.EnumerationCount, Is.Zero);
      Assert.That(source.TargetedEnumerationCount, Is.Zero);
      Assert.That(source.FileExistsCount, Is.Zero);
    }
  }

  [Test]
  public void Find_RequiresBothFilesInExactPairBeforeLoading() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddCommonOnly(exact);
    using var catalog = Catalog(source);

    var error = Assert.Throws<FileNotFoundException>(new Action(() =>
      catalog.Find("TownHall", FileType.StaticShape)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.FileName, Is.EqualTo(UniquePath(exact)));
      Assert.That(source.LoadedPaths, Is.Empty);
      Assert.That(source.EnumerationCount, Is.Zero);
    }
  }

  [Test]
  public void Dispose_DisposesEveryLoadedPairAndRejectsFurtherLookups() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    var alpha = PairPath("Style", "Vanilla", "alpha");
    source.AddPair(exact);
    source.AddPair(alpha, Resource("AlphaShape", FileType.StaticShape));
    source.EnumeratedPaths.Add(alpha);
    var catalog = Catalog(source);
    Assert.That(catalog.Find("AlphaShape", FileType.StaticShape), Is.Not.Null);

    catalog.Dispose();
    catalog.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedArchives, Has.Count.EqualTo(2));
      Assert.That(source.LoadedArchives, Has.All.Matches<Ovl>(archive => archive.Count == 0));
      Assert.Throws<ObjectDisposedException>(new Action(() =>
        catalog.Find("AlphaShape", FileType.StaticShape)));
    }
  }

  [Test]
  public void Find_RejectsUnknownTypeNamesBeforeLoading() {
    var source = new FakeCatalogSource();
    using var catalog = Catalog(source);

    Assert.Throws<ArgumentException>(new Action(() =>
      catalog.Find("TownHall", "not-a-real-type")));
    Assert.That(source.LoadedPaths, Is.Empty);
  }

  [Test]
  public void Find_TaggedReferenceUsesLastSeparatorAndRequiresAnOvlTag() {
    var source = new FakeCatalogSource();
    var exact = PairPath("Style", "Vanilla", "style");
    source.AddPair(exact, Resource("District:TownHall", FileType.StaticShape));
    using var catalog = Catalog(source);

    var result = catalog.Find("district:townhall:SHS");

    Assert.That(result?.File.Name, Is.EqualTo("District:TownHall"));
    Assert.Throws<ArgumentException>(new Action(() => catalog.Find("TownHall")));
    Assert.Throws<ArgumentException>(new Action(() => catalog.Find("TownHall:")));
    Assert.Throws<ArgumentException>(new Action(() => catalog.Find(":shs")));
    Assert.Throws<ArgumentException>(new Action(() => catalog.Find("TownHall:StaticShape")));
    Assert.Throws<ArgumentException>(new Action(() => catalog.Find(" TownHall:shs")));
  }

  [Test]
  public void FileSource_EnumeratesCommonOvlsWithExpectedDirectoryScopeIgnoringSuffixCase() {
    var nestedDirectory = Path.Combine(installRoot, "Nested");
    Directory.CreateDirectory(nestedDirectory);
    var alpha = Path.Combine(installRoot, "alpha.common.ovl");
    var beta = Path.Combine(installRoot, "beta.COMMON.OVL");
    var unique = Path.Combine(installRoot, "alpha.unique.ovl");
    var nested = Path.Combine(nestedDirectory, "hidden.common.ovl");
    var targeted = Path.Combine(nestedDirectory, "LightGreen01.common.ovl");
    File.WriteAllBytes(alpha, []);
    File.WriteAllBytes(beta, []);
    File.WriteAllBytes(unique, []);
    File.WriteAllBytes(nested, []);
    File.WriteAllBytes(targeted, []);

    try {
      var source = new FileSystemSceneryResourceCatalogSource();

      Assert.That(source.EnumerateCommonOvls(installRoot),
        Is.EquivalentTo(new[] { alpha, beta }));
      Assert.That(source.EnumerateMatchingCommonOvls(
          installRoot,
          "lightgreen01.COMMON.OVL"),
        Is.EqualTo(new[] { targeted }));
      Assert.That(source.EnumerateDescendantCommonOvls(installRoot),
        Is.EquivalentTo(new[] { alpha, beta, nested, targeted }));
    } finally {
      Directory.Delete(installRoot, recursive: true);
    }
  }

  private SceneryResourceCatalog Catalog(FakeCatalogSource source) =>
    Catalog(source, @"Style\Vanilla\style");

  private SceneryResourceCatalog Catalog(
    FakeCatalogSource source,
    string overlayFilename
  ) => new(installRoot, overlayFilename, source);

  private string PairPath(params string[] parts) =>
    Path.GetFullPath(Path.Combine(new[] { installRoot }.Concat(parts).ToArray())) +
    ".common.ovl";

  private static string UniquePath(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";

  private static OvlFile Resource(string name, FileType type) => new(name, type, "fixture.ovl");

  private OvlFile ResourceWithByte(string fileName, string name, byte value) {
    Directory.CreateDirectory(installRoot);
    var path = Path.Combine(installRoot, fileName);
    File.WriteAllBytes(path, [value]);
    return new OvlFile(name, FileType.StaticShape, path);
  }

  private sealed class FakeCatalogSource : ISceneryResourceCatalogSource {
    private readonly HashSet<string> existingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Ovl> pairs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Ovl, IReadOnlyList<string>> references =
      new(ReferenceEqualityComparer.Instance);

    public List<string> EnumeratedPaths { get; } = [];
    public List<string> TargetedPaths { get; } = [];
    public List<string> DescendantPaths { get; } = [];
    public List<string> TargetedFileNames { get; } = [];
    public List<string> LoadedPaths { get; } = [];
    public List<Ovl> LoadedArchives { get; } = [];
    public int EnumerationCount { get; private set; }
    public int TargetedEnumerationCount { get; private set; }
    public int DescendantEnumerationCount { get; private set; }
    public int FileExistsCount { get; private set; }

    public void AddPair(string commonPath, params OvlFile[] resources) {
      var archive = new Ovl(Path.GetFileName(commonPath));
      foreach (var resource in resources)
        archive.Add(resource, new OvlEntry(0, 1));
      pairs.Add(commonPath, archive);
      references.Add(archive, []);
      existingPaths.Add(commonPath);
      existingPaths.Add(UniquePath(commonPath));
    }

    public void AddPairWithReferences(
      string commonPath,
      IReadOnlyList<string> externalReferences,
      params OvlFile[] resources
    ) {
      AddPair(commonPath, resources);
      references[pairs[commonPath]] = externalReferences;
    }

    public void AddCommonOnly(string commonPath) => existingPaths.Add(commonPath);

    public void AddExistingPairPaths(string commonPath) {
      existingPaths.Add(commonPath);
      existingPaths.Add(UniquePath(commonPath));
    }

    public bool FileExists(string path) {
      FileExistsCount++;
      return existingPaths.Contains(path);
    }

    public IEnumerable<string> EnumerateCommonOvls(string directory) {
      EnumerationCount++;
      return EnumeratedPaths;
    }

    public IEnumerable<string> EnumerateMatchingCommonOvls(
      string directory,
      string fileName
    ) {
      TargetedEnumerationCount++;
      TargetedFileNames.Add(fileName);
      return TargetedPaths;
    }

    public IEnumerable<string> EnumerateDescendantCommonOvls(string directory) {
      DescendantEnumerationCount++;
      return DescendantPaths;
    }

    public IReadOnlyList<string> GetExternalReferences(Ovl archive) => references[archive];

    public Ovl LoadPair(string commonOvlPath) {
      LoadedPaths.Add(commonOvlPath);
      var archive = pairs[commonOvlPath];
      LoadedArchives.Add(archive);
      return archive;
    }
  }
}
