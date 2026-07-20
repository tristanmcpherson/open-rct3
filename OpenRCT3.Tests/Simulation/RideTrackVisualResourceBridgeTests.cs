// Ride Track Visual Resource Bridge Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackVisualResourceBridgeTests {
  private const string TrackPath = "track.unique.ovl";
  private const string VisualPath = "visual.unique.ovl";
  private const string StaticPath = "shape.common.ovl";
  private const string BonePath = "bone.unique.ovl";

  [Test]
  public void Resolve_LinksExactStaticAndBoneLodsAndMaterialIdentities() {
    var staticShape = StaticShape(
      "TrackStatic",
      StaticMesh("rails", "TrackRail:ftx", "SIOpaque:txs"));
    var boneShape = BoneShape(
      "TrackBone",
      BoneMesh("switch", "TrackSwitch:ftx", null));
    var visual = Visual(
      "TrackVisual",
      StaticLod("near", "TrackStatic:shs"),
      BoneLod("moving", "TrackBone:bsh"),
      BillboardLod("far", "TrackBillboard:ftx", "SIAlpha:txs"));
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath, visual)],
      [StaticSource(StaticPath, staticShape)],
      [BoneSource(BonePath, boneShape)],
      [TrackPath, VisualPath, StaticPath, BonePath]);

    var result = RideTrackVisualResourceBridge.Resolve(resources);

    Assert.That(result.Visuals, Has.Count.EqualTo(1));
    var link = result.Visuals.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(link.Section.Source.Resource.Name, Is.EqualTo("TrackSection"));
      Assert.That(link.Scenery.Source!.File.Path, Is.EqualTo(TrackPath));
      Assert.That(link.VisualSource.File.Path, Is.EqualTo(VisualPath));
      Assert.That(link.Lods, Has.Count.EqualTo(2));
      Assert.That(result.StaticLodCount, Is.EqualTo(1));
      Assert.That(result.BoneLodCount, Is.EqualTo(1));
      Assert.That(result.MeshCount, Is.EqualTo(2));
      Assert.That(link.Lods[0].StaticShape, Is.SameAs(staticShape));
      Assert.That(link.Lods[0].StaticShapeSource!.File.Path, Is.EqualTo(StaticPath));
      Assert.That(link.Lods[0].BoneShapeSource, Is.Null);
      Assert.That(link.Lods[1].BoneShape, Is.SameAs(boneShape));
      Assert.That(link.Lods[1].BoneShapeSource!.File.Path, Is.EqualTo(BonePath));
      Assert.That(link.Lods[1].StaticShapeSource, Is.Null);
      Assert.That(link.Lods[0].Materials.Single(), Is.EqualTo(
        new RideTrackVisualMeshMaterialIdentity(
          0,
          "rails",
          "TrackRail:ftx",
          "SIOpaque:txs")));
      Assert.That(link.Lods[1].Materials.Single(), Is.EqualTo(
        new RideTrackVisualMeshMaterialIdentity(
          0,
          "switch",
          "TrackSwitch:ftx",
          null)));
    }
  }

  [Test]
  public void Resolve_RejectsMissingSidTarget() {
    var section = SectionLink(null);
    var resources = new RideTrackVisualResourceSet(
      new TrackSectionResourceGraph([section], 1),
      [Closure(TrackPath)],
      [],
      new RideVisualShapeResourceSet([], []));

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("missing SID"));
  }

  [Test]
  public void Resolve_RejectsMissingVisualTarget() {
    var resources = Resources(
      Sid("TrackSid", "Missing:svd"),
      [],
      [],
      [],
      [TrackPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("references missing 'Missing:svd'"));
  }

  [Test]
  public void Resolve_RejectsVisualOutsideTheTksClosure() {
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath, Visual("TrackVisual", BillboardLod("far", null, null)))],
      [],
      [],
      [TrackPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("only outside its dependency closure"));
  }

  [Test]
  public void Resolve_RejectsAmbiguousVisualInsideTheTksClosure() {
    const string secondVisualPath = "second.unique.ovl";
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [
        VisualSource(VisualPath, Visual("TrackVisual", BillboardLod("first", null, null))),
        VisualSource(secondVisualPath,
          Visual("TrackVisual", BillboardLod("second", null, null))),
      ],
      [],
      [],
      [TrackPath, VisualPath, secondVisualPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("ambiguous between allowed archives"));
  }

  [TestCase("TrackVisual")]
  [TestCase("TrackVisual:svd:svd")]
  [TestCase(" TrackVisual:svd")]
  [TestCase("TrackVisual:shs")]
  public void Resolve_RejectsMalformedSidVisualReference(string reference) {
    var resources = Resources(
      Sid("TrackSid", reference),
      [],
      [],
      [],
      [TrackPath]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));
  }

  [Test]
  public void Resolve_RejectsMissingShapeTarget() {
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath,
        Visual("TrackVisual", StaticLod("near", "Missing:shs")))],
      [],
      [],
      [TrackPath, VisualPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("references missing 'Missing:shs'"));
  }

  [Test]
  public void Resolve_RejectsShapeOutsideTheTksClosure() {
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath,
        Visual("TrackVisual", StaticLod("near", "TrackStatic:shs")))],
      [StaticSource(StaticPath, StaticShape("TrackStatic"))],
      [],
      [TrackPath, VisualPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("only outside its dependency closure"));
  }

  [Test]
  public void Resolve_RejectsAmbiguousShapeInsideTheTksClosure() {
    const string secondStaticPath = "second.common.ovl";
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath,
        Visual("TrackVisual", StaticLod("near", "TrackStatic:shs")))],
      [
        StaticSource(StaticPath, StaticShape("TrackStatic")),
        StaticSource(secondStaticPath, StaticShape("TrackStatic")),
      ],
      [],
      [TrackPath, VisualPath, StaticPath, secondStaticPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("ambiguous between allowed archives"));
  }

  [TestCase("TrackStatic:bsh")]
  [TestCase("TrackStatic:shs:shs")]
  [TestCase("TrackStatic:shs ")]
  public void Resolve_RejectsMalformedShapeReference(string reference) {
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath, Visual("TrackVisual", StaticLod("near", reference)))],
      [StaticSource(StaticPath, StaticShape("TrackStatic"))],
      [],
      [TrackPath, VisualPath, StaticPath]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));
  }

  [Test]
  public void Resolve_RejectsMalformedDecodedMaterialIdentity() {
    var shape = StaticShape(
      "TrackStatic",
      StaticMesh("rails", "TrackRail", "SIOpaque:txs"));
    var resources = Resources(
      Sid("TrackSid", "TrackVisual:svd"),
      [VisualSource(VisualPath,
        Visual("TrackVisual", StaticLod("near", "TrackStatic:shs")))],
      [StaticSource(StaticPath, shape)],
      [],
      [TrackPath, VisualPath, StaticPath]);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualResourceBridge.Resolve(resources)));

    Assert.That(error!.Message, Does.Contain("mesh 'rails' FTX"));
  }

  private static RideTrackVisualResourceSet Resources(
    SceneryItem scenery,
    IReadOnlyList<RideVisualResourceSource> visuals,
    IReadOnlyList<RideStaticShapeResourceSource> staticShapes,
    IReadOnlyList<RideBoneShapeResourceSource> boneShapes,
    IReadOnlyList<string> allowedPaths
  ) => new(
    new TrackSectionResourceGraph(
      [SectionLink(new SceneryItemResourceSource(
        new OvlFile(scenery.Name, FileType.SceneryItem, TrackPath),
        scenery))],
      0),
    [Closure(allowedPaths)],
    visuals,
    new RideVisualShapeResourceSet(staticShapes, boneShapes));

  private static TrackSectionResourceLink SectionLink(
    SceneryItemResourceSource? scenery
  ) {
    var section = Section();
    return new TrackSectionResourceLink(
      new TrackSectionResourceSource(
        new OvlFile(section.Name, FileType.TrackSection, TrackPath),
        section),
      new TrackSectionSceneryLink(section.SceneryItem, scenery),
      []);
  }

  private static OvlResourceDependencyClosure Closure(params string[] paths) =>
    new(TrackPath, paths);

  private static OvlResourceDependencyClosure Closure(IReadOnlyList<string> paths) =>
    new(TrackPath, paths);

  private static TrackSection Section() => new(
    "TrackSection",
    TrackSectionVersion.Vanilla,
    "track section",
    "TrackSid:sid",
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    0,
    0,
    new TrackSectionSplinePair("CarLeft:spl", "CarRight:spl"),
    new TrackSectionSplinePair("JoinLeft:spl", "JoinRight:spl"),
    null,
    null,
    [],
    new TrackSectionAnimations(0, 0, 0, 0, 0, 0, 0, 0, 0, null, null),
    new TrackSectionOptions(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    null,
    null);

  private static SceneryItem Sid(string name, params string[] visuals) => new(
    name,
    default,
    default,
    0,
    1,
    1,
    0f,
    0f,
    0f,
    1f,
    1f,
    1f,
    SidType.RideTrack,
    visuals);

  private static RideVisualResourceSource VisualSource(
    string path,
    SceneryItemVisual visual
  ) => new(
    new OvlFile(visual.Name, FileType.SceneryItemVisual, path),
    visual);

  private static RideStaticShapeResourceSource StaticSource(
    string path,
    StaticShape shape
  ) => new(new OvlFile(shape.Name, FileType.StaticShape, path), shape);

  private static RideBoneShapeResourceSource BoneSource(
    string path,
    BoneShape shape
  ) => new(new OvlFile(shape.Name, FileType.BoneShape, path), shape);

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 0f, 0f, 1f, lods, null);

  private static SceneryItemVisualLod StaticLod(string name, string reference) =>
    new(name, SvdLodType.StaticShape, reference, null, null, null,
      Billboard(), 10f, []);

  private static SceneryItemVisualLod BoneLod(string name, string reference) =>
    new(name, SvdLodType.BoneShape, null, reference, null, null,
      Billboard(), 20f, []);

  private static SceneryItemVisualLod BillboardLod(
    string name,
    string? ftx,
    string? txs
  ) => new(name, SvdLodType.Billboard, null, null, ftx, txs,
    Billboard(), 30f, []);

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

  private static StaticShape StaticShape(
    string name,
    params StaticShapeMesh[] meshes
  ) => new(name, Vector3.Zero, Vector3.One, meshes, []);

  private static BoneShape BoneShape(
    string name,
    params BoneShapeMesh[] meshes
  ) => new(name, Vector3.Zero, Vector3.One, meshes, []);

  private static StaticShapeMesh StaticMesh(
    string name,
    string? ftx,
    string? txs
  ) => new(name, 0, ftx, txs, 0, 0, 3, [], []) {
    IndexLayout = StaticShapeIndexLayout.TriangleList,
  };

  private static BoneShapeMesh BoneMesh(
    string name,
    string? ftx,
    string? txs
  ) => new(name, 0, ftx, txs, 0, 0, 3, [], []) {
    IndexLayout = StaticShapeIndexLayout.TriangleList,
  };
}
