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
  public void Resolve_UsesFirstSupportedBoneVisualAlternativeAndStopsThere() {
    using var sidArchive = new Ovl("sid.ovl");
    using var visualArchive = new Ovl("visual.ovl");
    using var shapeArchive = new Ovl("shape.ovl");
    using var boneShapeArchive = new Ovl("bone-shape.ovl");
    var item = Item("TownHall", "Front:svd", "Rear:svd");
    var rearShape = Shape("RearMesh");
    var frontShape = BoneShape("bones");
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
      ("bones", FileType.BoneShape, boneShapeArchive),
      ("RearMesh", FileType.StaticShape, shapeArchive));
    var resolver = Resolver(
      resources,
      _ => [item],
      _ => [front, rear],
      _ => [rearShape],
      _ => [frontShape]);

    var result = resolver.Resolve("townhall");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.Item, Is.SameAs(item));
      Assert.That(result?.StaticLods, Is.Empty);
      Assert.That(result?.BoneLods.Select(lod => lod.Visual.Name),
        Is.EqualTo(new[] { "Front" }));
      Assert.That(result?.BoneLods.Select(lod => lod.Shape.Name),
        Is.EqualTo(new[] { "bones" }));
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
  public void Resolve_PreservesSerializedLodOrderWithinEachSupportedType() {
    using var archive = new Ovl("all.ovl");
    var firstBoneLod = Lod(
      "first-bone", SvdLodType.BoneShape, boneShapeRef: "FirstBone:bsh");
    var firstStaticLod = Lod(
      "first-static", SvdLodType.StaticShape, staticShapeRef: "FirstStatic:shs");
    var secondBoneLod = Lod(
      "second-bone", SvdLodType.BoneShape, boneShapeRef: "SecondBone:bsh");
    var secondStaticLod = Lod(
      "second-static", SvdLodType.StaticShape, staticShapeRef: "SecondStatic:shs");
    var visual = Visual(
      "Visual", firstBoneLod, firstStaticLod, secondBoneLod, secondStaticLod);
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("FirstBone", FileType.BoneShape, archive),
      ("SecondBone", FileType.BoneShape, archive),
      ("FirstStatic", FileType.StaticShape, archive),
      ("SecondStatic", FileType.StaticShape, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [visual],
      _ => [Shape("FirstStatic"), Shape("SecondStatic")],
      _ => [BoneShape("FirstBone"), BoneShape("SecondBone")]);

    var result = resolver.Resolve("Item");

    using (Assert.EnterMultipleScope()) {
      Assert.That(result?.StaticLods.Select(lod => lod.Lod),
        Is.EqualTo(new[] { firstStaticLod, secondStaticLod }));
      Assert.That(result?.BoneLods.Select(lod => lod.Lod),
        Is.EqualTo(new[] { firstBoneLod, secondBoneLod }));
    }
  }

  [Test]
  public void Resolve_BoneLodResolvesBansInSvdOrderAndCachesTheArchive() {
    using var archive = new Ovl("all.ovl");
    var animationCalls = 0;
    var shape = BoneShape("Bones", "root");
    var idle = Animation("Idle", "root");
    var loop = Animation("Loop", "root");
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Bones", FileType.BoneShape, archive),
      ("Idle", FileType.BoneAnim, archive),
      ("Loop", FileType.BoneAnim, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [Visual("Visual", Lod(
        "bone",
        SvdLodType.BoneShape,
        boneShapeRef: "Bones:bsh",
        animationRefs: ["Idle:ban", "Loop:ban"]))],
      _ => [],
      _ => [shape],
      _ => { animationCalls++; return [loop, idle]; });

    var first = resolver.Resolve("Item");
    var second = resolver.Resolve("ITEM");

    using (Assert.EnterMultipleScope()) {
      Assert.That(first?.BoneLods.Single().Animations.Select(value => value.Name),
        Is.EqualTo(new[] { "Idle", "Loop" }));
      Assert.That(second?.BoneLods.Single().Animations.Select(value => value.Name),
        Is.EqualTo(new[] { "Idle", "Loop" }));
      Assert.That(animationCalls, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_UsesReferencingArchiveForNestedResources() {
    using var sidArchive = new Ovl("sid.ovl");
    using var visualArchive = new Ovl("visual.ovl");
    using var shapeArchive = new Ovl("shape.ovl");
    var resources = Resources(
      ("Item", FileType.SceneryItem, sidArchive),
      ("Visual", FileType.SceneryItemVisual, visualArchive),
      ("Bones", FileType.BoneShape, shapeArchive),
      ("Idle", FileType.BoneAnim, shapeArchive));
    var ownerLookups = new List<(Ovl Archive, string Name, FileType Type)>();
    SceneryResourceEntry? FindFrom(
      SceneryResourceEntry owner,
      string name,
      FileType type
    ) {
      ownerLookups.Add((owner.Archive, name, type));
      return resources.GetValueOrDefault((name.ToLowerInvariant(), type));
    }
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [Visual("Visual", Lod(
        "bone",
        SvdLodType.BoneShape,
        boneShapeRef: "Bones:bsh",
        animationRefs: ["Idle:ban"]))],
      _ => [],
      _ => [BoneShape("Bones", "root")],
      _ => [Animation("Idle", "root")],
      FindFrom);

    Assert.That(resolver.Resolve("Item"), Is.Not.Null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(ownerLookups[0], Is.EqualTo(
        (sidArchive, "Visual", FileType.SceneryItemVisual)));
      Assert.That(ownerLookups[1], Is.EqualTo(
        (visualArchive, "Bones", FileType.BoneShape)));
      Assert.That(ownerLookups[2], Is.EqualTo(
        (visualArchive, "Idle", FileType.BoneAnim)));
    }
  }

  [Test]
  public void Resolve_CachesEachArchiveDecoder() {
    using var archive = new Ovl("all.ovl");
    var itemCalls = 0;
    var visualCalls = 0;
    var shapeCalls = 0;
    var boneShapeCalls = 0;
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Shape", FileType.StaticShape, archive),
      ("Bones", FileType.BoneShape, archive));
    var resolver = Resolver(
      resources,
      _ => { itemCalls++; return [Item("Item", "Visual:svd")]; },
      _ => { visualCalls++; return [Visual(
        "Visual",
        Lod("static", SvdLodType.StaticShape, staticShapeRef: "Shape:shs"),
        Lod("bone", SvdLodType.BoneShape, boneShapeRef: "Bones:bsh"))]; },
      _ => { shapeCalls++; return [Shape("Shape")]; },
      _ => { boneShapeCalls++; return [BoneShape("Bones")]; });

    Assert.That(resolver.Resolve("Item"), Is.Not.Null);
    Assert.That(resolver.Resolve("ITEM"), Is.Not.Null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(itemCalls, Is.EqualTo(1));
      Assert.That(visualCalls, Is.EqualTo(1));
      Assert.That(shapeCalls, Is.EqualTo(1));
      Assert.That(boneShapeCalls, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_MissingCatalogSidReturnsNullWithoutDecoding() {
    var resolver = Resolver(
      new Dictionary<(string Name, FileType Type), SceneryResourceEntry>(),
      _ => throw new AssertionException("SID decoder should not run."),
      _ => throw new AssertionException("SVD decoder should not run."),
      _ => throw new AssertionException("SHS decoder should not run."),
      _ => throw new AssertionException("BSH decoder should not run."));

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

  [Test]
  public void Resolve_MissingDeclaredBoneShapeFailsClosed() {
    using var archive = new Ovl("all.ovl");
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [Visual(
        "Visual",
        Lod("bone", SvdLodType.BoneShape, boneShapeRef: "Missing:bsh"))],
      _ => [],
      _ => []);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve("Item")));

    Assert.That(exception!.Message, Does.Contain("Missing:bsh"));
  }

  [Test]
  public void Resolve_MissingDeclaredBoneAnimationFailsClosed() {
    using var archive = new Ovl("all.ovl");
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Bones", FileType.BoneShape, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [Visual("Visual", Lod(
        "bone",
        SvdLodType.BoneShape,
        boneShapeRef: "Bones:bsh",
        animationRefs: ["Missing:ban"]))],
      _ => [],
      _ => [BoneShape("Bones", "root")],
      _ => []);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve("Item")));

    Assert.That(exception!.Message, Does.Contain("Missing:ban"));
  }

  [Test]
  public void Resolve_PreservesAnimationChannelsNotPresentInShape() {
    using var archive = new Ovl("all.ovl");
    var resources = Resources(
      ("Item", FileType.SceneryItem, archive),
      ("Visual", FileType.SceneryItemVisual, archive),
      ("Bones", FileType.BoneShape, archive),
      ("Idle", FileType.BoneAnim, archive));
    var resolver = Resolver(
      resources,
      _ => [Item("Item", "Visual:svd")],
      _ => [Visual("Visual", Lod(
        "bone",
        SvdLodType.BoneShape,
        boneShapeRef: "Bones:bsh",
        animationRefs: ["Idle:ban"]))],
      _ => [],
      _ => [BoneShape("Bones", "root")],
      _ => [Animation("Idle", "root", "particlestart01_dustspray")]);

    var result = resolver.Resolve("Item");

    Assert.That(
      result?.BoneLods.Single().Animations.Single().Bones.Select(bone => bone.Name),
      Is.EqualTo(new[] { "root", "particlestart01_dustspray" }));
  }

  private static SceneryVisualResolver Resolver(
    IReadOnlyDictionary<(string Name, FileType Type), SceneryResourceEntry> resources,
    Func<Ovl, IReadOnlyList<SceneryItem>> items,
    Func<Ovl, IReadOnlyList<SceneryItemVisual>> visuals,
    Func<Ovl, IReadOnlyList<StaticShape>> shapes,
    Func<Ovl, IReadOnlyList<BoneShape>>? boneShapes = null,
    Func<Ovl, IReadOnlyList<BoneAnimation>>? boneAnimations = null,
    Func<SceneryResourceEntry, string, FileType, SceneryResourceEntry?>? findFrom = null
  ) => new(
    (name, type) => resources.GetValueOrDefault((name.ToLowerInvariant(), type)),
    items,
    visuals,
    shapes,
    boneShapes ?? (_ => []),
    boneAnimations ?? (_ => []),
    findFrom);

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
    string? boneShapeRef = null,
    string[]? animationRefs = null
  ) => new(
    name,
    type,
    staticShapeRef,
    boneShapeRef,
    null,
    null,
    new SceneryVisualBillboardSettings(0f, 0f, 0f, 0f, 0f, 0f),
    100f,
    animationRefs ?? []);

  private static StaticShape Shape(string name) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [],
    []);

  private static BoneShape BoneShape(string name, params string[] boneNames) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [],
    boneNames.Select((boneName, index) => new BoneShapeBone(
      boneName,
      index - 1,
      Matrix4x4.Identity,
      Matrix4x4.Identity)).ToArray());

  private static BoneAnimation Animation(string name, params string[] boneNames) => new(
    name,
    1f,
    boneNames.Select(boneName => new BoneAnimationBone(boneName, [], [])).ToArray());
}
