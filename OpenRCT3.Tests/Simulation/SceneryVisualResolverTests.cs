// Scenery Visual Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryVisualResolverTests {
  [Test]
  public void Resolve_FallsBackToFirstSupportedVisualAlternativeAndStopsThere() {
    using var sidArchive = new Ovl("sid.ovl");
    using var visualArchive = new Ovl("visual.ovl");
    using var shapeArchive = new Ovl("shape.ovl");
    var item = Item("TownHall", "Front:svd", "Rear:svd");
    var rearShape = Shape("RearMesh");
    var front = Visual(
      "Front",
      Lod("bones", SvdLodType.BoneShape, boneShapeRef: "bones:bsh"));
    var rear = Visual(
      "Rear",
      Lod("rear", SvdLodType.StaticShape, staticShapeRef: "RearMesh:shs"));
    var resources = Resources(
      ("TownHall", FileType.SceneryItem, sidArchive),
      ("Front", FileType.SceneryItemVisual, visualArchive),
      ("Rear", FileType.SceneryItemVisual, visualArchive),
      ("RearMesh", FileType.StaticShape, shapeArchive));
    var resolver = Resolver(
      resources,
      _ => [item],
      _ => [front, rear],
      _ => [rearShape]);

    var result = resolver.Resolve("townhall");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.Item, Is.SameAs(item));
      Assert.That(result?.StaticLods.Select(lod => lod.Visual.Name),
        Is.EqualTo(new[] { "Rear" }));
      Assert.That(result?.StaticLods.Select(lod => lod.Shape.Name),
        Is.EqualTo(new[] { "RearMesh" }));
    }
  }

  [Test]
  public void Resolve_DoesNotResolveLaterAlternativesAfterStaticVisualWins() {
    using var archive = new Ovl("all.ovl");
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Shape", FileType.StaticShape, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd", "Missing:svd")],
      _ => [Visual("Visual", Lod(
        "lod", SvdLodType.StaticShape, staticShapeRef: "Shape:shs"))],
      _ => [Shape("Shape")]);

    var result = resolver.Resolve("Item");

    Assert.That(result?.StaticLods.Select(lod => lod.Visual.Name),
      Is.EqualTo(new[] { "Visual" }));
  }

  [Test]
  public void Resolve_CachesEachArchiveDecoder() {
    using var archive = new Ovl("all.ovl");
    var itemCalls = 0;
    var visualCalls = 0;
    var shapeCalls = 0;
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Shape", FileType.StaticShape, archive));
    var resolver = Resolver(
      resources,
      _ => { itemCalls++; return [Item("Item", "Visual:svd")]; },
      _ => { visualCalls++; return [Visual("Visual", Lod(
        "lod", SvdLodType.StaticShape, staticShapeRef: "Shape:shs"))]; },
      _ => { shapeCalls++; return [Shape("Shape")]; });

    Assert.That(resolver.Resolve("Item"), Is.Not.Null);
    Assert.That(resolver.Resolve("ITEM"), Is.Not.Null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(itemCalls, Is.EqualTo(1));
      Assert.That(visualCalls, Is.EqualTo(1));
      Assert.That(shapeCalls, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_MissingCatalogSidReturnsNullWithoutDecoding() {
    var resolver = Resolver(
      new Dictionary<(string Name, FileType Type), SceneryResourceEntry>(),
      _ => throw new AssertionException("SID decoder should not run."),
      _ => throw new AssertionException("SVD decoder should not run."),
      _ => throw new AssertionException("SHS decoder should not run."));

    Assert.That(resolver.Resolve("Missing"), Is.Null);
  }

  [Test]
  public void Resolve_MissingDeclaredVisualFailsClosed() {
    using var archive = new Ovl("sid.ovl");
    var resources = Resources(("Item", FileType.SceneryItem, archive));
    var resolver = Resolver(resources, _ => [Item("Item", "Missing:svd")], _ => [], _ => []);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve("Item")));

    Assert.That(exception!.Message, Does.Contain("Missing:svd"));
  }

  [Test]
  public void Resolve_WrongDeclaredTagFailsClosed() {
    using var archive = new Ovl("sid.ovl");
    var resources = Resources(("Item", FileType.SceneryItem, archive));
    var resolver = Resolver(resources, _ => [Item("Item", "Wrong:shs")], _ => [], _ => []);

    Assert.Throws<InvalidDataException>(new Action(() => resolver.Resolve("Item")));
  }

  [Test]
  public void Resolve_DuplicateDecodedResourceNamesFailClosed() {
    using var archive = new Ovl("sid.ovl");
    var resources = Resources(("Item", FileType.SceneryItem, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item"), Item("item")],
      _ => [],
      _ => []);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve("Item")));

    Assert.That(exception!.Message, Does.Contain("duplicate SID"));
  }

  private static SceneryVisualResolver Resolver(
    IReadOnlyDictionary<(string Name, FileType Type), SceneryResourceEntry> resources,
    Func<Ovl, IReadOnlyList<SceneryItem>> items,
    Func<Ovl, IReadOnlyList<SceneryItemVisual>> visuals,
    Func<Ovl, IReadOnlyList<StaticShape>> shapes
  ) => new(
    (name, type) => resources.GetValueOrDefault((name.ToLowerInvariant(), type)),
    items,
    visuals,
    shapes);

  private static IReadOnlyDictionary<(string Name, FileType Type), SceneryResourceEntry> Resources(
    params (string Name, FileType Type, Ovl Archive)[] resources
  ) => resources.ToDictionary(
    resource => (resource.Name.ToLowerInvariant(), resource.Type),
    resource => new SceneryResourceEntry(
      resource.Archive,
      new OvlFile(resource.Name, resource.Type, "fixture.unique.ovl")));

  private static SceneryItem Item(string name, params string[] visualRefs) => new(
    name,
    0,
    SidPosition.TileFull,
    0,
    1,
    1,
    0f,
    0f,
    0f,
    4f,
    4f,
    4f,
    SidType.SceneryMisc,
    visualRefs);

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 1f, 0f, 1f, lods, null);

  private static SceneryItemVisualLod Lod(
    string name,
    SvdLodType type,
    string? staticShapeRef = null,
    string? boneShapeRef = null
  ) => new(
    name,
    type,
    staticShapeRef,
    boneShapeRef,
    null,
    null,
    new SceneryVisualBillboardSettings(0f, 0f, 0f, 0f, 0f, 0f),
    100f,
    []);

  private static StaticShape Shape(string name) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [],
    []);
}
