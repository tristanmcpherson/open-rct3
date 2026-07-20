// Ride Track Visual Scene Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackVisualSceneBuilderTests {
  [Test]
  public void Build_SelectsFirstDeclaredSupportedVisualAndFirstSerializedShapeLod() {
    var firstShape = StaticShape("PrimaryFirst", StaticMesh("first"));
    var secondShape = StaticShape("PrimarySecond", StaticMesh("second"));
    var laterShape = BoneShape("LaterBone", BoneMesh("later"));
    var fixture = Fixture(
      1,
      [
        Visual("BillboardOnly", BillboardLod("billboard")),
        Visual(
          "Primary",
          BillboardLod("primary billboard"),
          StaticLod("primary first", "PrimaryFirst:shs"),
          StaticLod("primary second", "PrimarySecond:shs")),
        Visual("Later", BoneLod("later bone", "LaterBone:bsh")),
      ],
      [firstShape, secondShape],
      [laterShape]);
    var adaptedStatic = new List<string>();
    var adaptedBone = new List<string>();
    var materialLinks = new List<RideTrackVisualLink>();
    var transformCalls = 0;
    var material = new Flat();
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = shape => {
        adaptedStatic.Add(shape.Name);
        return StaticShapeMeshBuilder.BuildBatches(shape);
      },
      AdaptBoneShape = shape => {
        adaptedBone.Add(shape.Name);
        return BoneShapeMeshBuilder.BuildBatches(shape);
      },
      CreatePlacementTransform = (placement, item, terrain) => {
        transformCalls++;
        return RideTrackPlacementTransform.Create(placement, item, terrain);
      },
    };

    var result = RideTrackVisualSceneBuilder.Build(
      fixture.Resolution,
      fixture.Visuals,
      fixture.Terrain,
      (link, _, _) => {
        materialLinks.Add(link);
        return material;
      },
      RideTrackVisualSceneBuilderLimits.Default,
      operations);
    var model = result.Models.Single();
    var expectedTransform = RideTrackPlacementTransform.Create(
      fixture.Placement,
      fixture.Section.Scenery.Source!.Resource,
      fixture.Terrain);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adaptedStatic, Is.EqualTo(new[] { "PrimaryFirst" }));
      Assert.That(adaptedBone, Is.Empty);
      Assert.That(materialLinks.Single().VisualSource.Resource.Name,
        Is.EqualTo("Primary"));
      Assert.That(materialLinks.Single().AllowedArchivePaths,
        Does.Contain(fixture.StaticPath));
      Assert.That(transformCalls, Is.EqualTo(1));
      Assert.That(model.Transform.Matrix, Is.EqualTo(expectedTransform));
      Assert.That(result.PlacementCount, Is.EqualTo(1));
      Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
      Assert.That(result.SkippedPlacementCount, Is.Zero);
      Assert.That(result.MissingMaterialBatchCount, Is.Zero);
      Assert.That(result.ModelCount, Is.EqualTo(1));
      Assert.That(model.Material, Is.SameAs(material));
    }

    var mesh = model.Mesh;
    model.Dispose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(material.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_UsesBoneRestPoseWhenItIsTheFirstSupportedSerializedLod() {
    var firstBone = BoneShape("FirstBone", BoneMesh("bone"));
    var laterStatic = StaticShape("LaterStatic", StaticMesh("static"));
    var fixture = Fixture(
      2,
      [Visual(
        "Primary",
        BillboardLod("billboard"),
        BoneLod("first bone", "FirstBone:bsh"),
        StaticLod("later static", "LaterStatic:shs"))],
      [laterStatic],
      [firstBone]);
    var adaptedStatic = 0;
    var adaptedBone = 0;
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = shape => {
        adaptedStatic++;
        return StaticShapeMeshBuilder.BuildBatches(shape);
      },
      AdaptBoneShape = shape => {
        adaptedBone++;
        return BoneShapeMeshBuilder.BuildBatches(shape);
      },
    };

    var result = RideTrackVisualSceneBuilder.Build(
      fixture.Resolution,
      fixture.Visuals,
      fixture.Terrain,
      (_, _, _) => new Flat(),
      RideTrackVisualSceneBuilderLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adaptedStatic, Is.Zero);
      Assert.That(adaptedBone, Is.EqualTo(1));
      Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
      Assert.That(result.ModelCount, Is.EqualTo(1));
    }
    result.Models.Single().Dispose();
  }

  [Test]
  public void Build_CountsUnresolvedUnsupportedAndMissingMaterialPlacementsAsSkipped() {
    var missingShape = StaticShape(
      "MissingMaterials",
      StaticMesh("first"),
      StaticMesh("second"));
    var missing = Fixture(
      3,
      [Visual("MissingVisual", StaticLod("shape", "MissingMaterials:shs"))],
      [missingShape],
      []);
    var unsupported = Fixture(
      4,
      [Visual("BillboardOnly", BillboardLod("billboard"))],
      [],
      []);
    var unresolved = UnresolvedPlacement(5);
    var resolution = new RideTrackResourceResolution(
      [unresolved, .. missing.Resolution.Placements, .. unsupported.Resolution.Placements],
      1);
    var visuals = new RideTrackVisualResourceBridgeResult(
      [.. missing.Visuals.Visuals, .. unsupported.Visuals.Visuals]);
    var adaptedMeshes = new List<Mesh>();
    var transformCalls = 0;
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = shape => {
        var batches = StaticShapeMeshBuilder.BuildBatches(shape);
        adaptedMeshes.AddRange(batches.Select(batch => batch.Mesh));
        return batches;
      },
      CreatePlacementTransform = (placement, item, terrain) => {
        transformCalls++;
        return RideTrackPlacementTransform.Create(placement, item, terrain);
      },
    };

    var result = RideTrackVisualSceneBuilder.Build(
      resolution,
      visuals,
      missing.Terrain,
      (_, _, _) => null,
      RideTrackVisualSceneBuilderLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Models, Is.Empty);
      Assert.That(result.PlacementCount, Is.EqualTo(3));
      Assert.That(result.RenderedPlacementCount, Is.Zero);
      Assert.That(result.SkippedPlacementCount, Is.EqualTo(3));
      Assert.That(result.MissingMaterialBatchCount, Is.EqualTo(2));
      Assert.That(transformCalls, Is.EqualTo(1));
      Assert.That(adaptedMeshes, Has.Count.EqualTo(2));
      Assert.That(adaptedMeshes.Select(mesh => mesh.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_PartialMaterialResolutionRendersPlacementAndSkipsOnlyMissingBatch() {
    var shape = StaticShape(
      "Partial",
      StaticMesh("missing"),
      StaticMesh("resolved"));
    var fixture = Fixture(
      6,
      [Visual("Primary", StaticLod("shape", "Partial:shs"))],
      [shape],
      []);
    var batches = new List<StaticShapeMeshBatch>();
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = source => {
        var result = StaticShapeMeshBuilder.BuildBatches(source);
        batches.AddRange(result);
        return result;
      },
    };

    var result = RideTrackVisualSceneBuilder.Build(
      fixture.Resolution,
      fixture.Visuals,
      fixture.Terrain,
      (_, _, batch) => batch.SourceMeshIndex == 0 ? null : new Flat(),
      RideTrackVisualSceneBuilderLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
      Assert.That(result.SkippedPlacementCount, Is.Zero);
      Assert.That(result.MissingMaterialBatchCount, Is.EqualTo(1));
      Assert.That(result.ModelCount, Is.EqualTo(1));
      Assert.That(batches[0].Mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(result.Models.Single().Mesh, Is.SameAs(batches[1].Mesh));
    }
    result.Models.Single().Dispose();
  }

  [Test]
  public void Build_FailureDisposesTransferredAndPendingResources() {
    var shape = StaticShape(
      "Failure",
      StaticMesh("first"),
      StaticMesh("second"));
    var fixture = Fixture(
      7,
      [Visual("Primary", StaticLod("shape", "Failure:shs"))],
      [shape],
      []);
    var meshes = new List<Mesh>();
    var materials = new List<Flat>();
    var modelCalls = 0;
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = source => {
        var batches = StaticShapeMeshBuilder.BuildBatches(source);
        meshes.AddRange(batches.Select(batch => batch.Mesh));
        return batches;
      },
      CreateModel = mesh => {
        modelCalls++;
        if (modelCalls == 2) throw new InvalidOperationException("model failure");
        return new Model(mesh);
      },
    };

    var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrackVisualSceneBuilder.Build(
        fixture.Resolution,
        fixture.Visuals,
        fixture.Terrain,
        (_, _, _) => {
          var material = new Flat();
          materials.Add(material);
          return material;
        },
        RideTrackVisualSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Is.EqualTo("model failure"));
      Assert.That(meshes, Has.Count.EqualTo(2));
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(materials, Has.Count.EqualTo(2));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_FirstMaterialFailureDisposesEveryAlreadyAdaptedBatchMesh() {
    var shape = StaticShape(
      "EarlyFailure",
      StaticMesh("first"),
      StaticMesh("second"));
    var fixture = Fixture(
      9,
      [Visual("Primary", StaticLod("shape", "EarlyFailure:shs"))],
      [shape],
      []);
    var meshes = new List<Mesh>();
    var operations = RideTrackVisualSceneBuilderOperations.Default with {
      AdaptStaticShape = source => {
        var batches = StaticShapeMeshBuilder.BuildBatches(source);
        meshes.AddRange(batches.Select(batch => batch.Mesh));
        return batches;
      },
    };

    var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrackVisualSceneBuilder.Build(
        fixture.Resolution,
        fixture.Visuals,
        fixture.Terrain,
        (_, _, _) => throw new InvalidOperationException("material failure"),
        RideTrackVisualSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Is.EqualTo("material failure"));
      Assert.That(meshes, Has.Count.EqualTo(2));
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_RejectsVisualSetWhoseExactTksIdentityWasDuplicated() {
    var shape = StaticShape("Shape", StaticMesh("mesh"));
    var fixture = Fixture(
      10,
      [Visual("Primary", StaticLod("shape", "Shape:shs"))],
      [shape],
      []);
    var duplicated = new RideTrackVisualResourceBridgeResult(
      [fixture.Visuals.Visuals.Single(), fixture.Visuals.Visuals.Single()]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackVisualSceneBuilder.Build(
        fixture.Resolution,
        duplicated,
        fixture.Terrain,
        (_, _, _) => new Flat(),
        RideTrackVisualSceneBuilderLimits.Default,
        RideTrackVisualSceneBuilderOperations.Default)));

    Assert.That(exception!.Message, Does.Contain("visual links for 1 declared alternatives"));
  }

  private static FixtureData Fixture(
    int identity,
    IReadOnlyList<SceneryItemVisual> visuals,
    IReadOnlyList<StaticShape> staticShapes,
    IReadOnlyList<BoneShape> boneShapes
  ) {
    var name = $"Track{identity}";
    var trackPath = $"track{identity}.unique.ovl";
    var visualPath = $"visual{identity}.unique.ovl";
    var staticPath = $"static{identity}.unique.ovl";
    var bonePath = $"bone{identity}.unique.ovl";
    var sectionResource = Section(name);
    var sceneryResource = Sid(
      name,
      visuals.Select(visual => $"{visual.Name}:svd").ToArray());
    var sectionFile = new OvlFile(name, FileType.TrackSection, trackPath);
    var section = new TrackSectionResourceLink(
      new TrackSectionResourceSource(sectionFile, sectionResource),
      new TrackSectionSceneryLink(
        sectionResource.SceneryItem,
        new SceneryItemResourceSource(
          new OvlFile(name, FileType.SceneryItem, trackPath),
          sceneryResource)),
      []);
    var resources = new RideTrackVisualResourceSet(
      new TrackSectionResourceGraph([section], 0),
      [new OvlResourceDependencyClosure(
        trackPath,
        [trackPath, visualPath, staticPath, bonePath])],
      visuals.Select(visual => new RideVisualResourceSource(
        new OvlFile(visual.Name, FileType.SceneryItemVisual, visualPath),
        visual)).ToArray(),
      new RideVisualShapeResourceSet(
        staticShapes.Select(shape => new RideStaticShapeResourceSource(
          new OvlFile(shape.Name, FileType.StaticShape, staticPath),
          shape)).ToArray(),
        boneShapes.Select(shape => new RideBoneShapeResourceSource(
          new OvlFile(shape.Name, FileType.BoneShape, bonePath),
          shape)).ToArray()));
    var bridge = RideTrackVisualResourceBridge.Resolve(resources);
    var placement = Placement(identity, name);
    var placementSource = new RideTrackSectionResourceSource(
      placement.OverlayPath,
      sectionFile,
      sectionResource);
    var resourceLink = new RideTrackResourceLink(
      new RideTrackSectionResourceLink(placement, placementSource),
      section,
      []);
    return new FixtureData(
      new RideTrackResourceResolution([resourceLink], 0),
      bridge,
      section,
      placement,
      new Terrain(4, 4),
      staticPath);
  }

  private static RideTrackResourceLink UnresolvedPlacement(int identity) {
    var placement = Placement(identity, $"Unresolved{identity}");
    return new RideTrackResourceLink(
      new RideTrackSectionResourceLink(placement, null),
      null,
      []);
  }

  private static RideTrackPlacement Placement(int identity, string name) => new(
    sourceEntryId: Convert.ToUInt64(500 + identity),
    sceneryPlacementSourceEntryId: Convert.ToUInt64(100 + identity),
    sidDatabaseEntryReference: Convert.ToUInt64(200 + identity),
    symbolName: $"{name}:tks",
    objectKey: name,
    overlayPath: $"Tracks\\Test{identity}",
    tileX: 1,
    tileY: 1,
    rotation: Edge.East,
    serializedDirection: 2,
    serializedHeight: identity,
    corner: 0,
    ownerReference: Convert.ToUInt64(600 + identity),
    segmentReference: Convert.ToUInt64(700 + identity),
    previousPieceReference: 0,
    nextPieceReference: 0,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 1,
    flexiColour1: 2,
    flexiColour2: 3);

  private static TrackSection Section(string name) => new(
    name,
    TrackSectionVersion.Vanilla,
    name,
    $"{name}:sid",
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

  private static SceneryItem Sid(string name, IReadOnlyList<string> visuals) => new(
    name,
    SidFlags.GroundChange,
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
    SidType.RideTrack,
    visuals);

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 0f, 0f, 1f, lods, null);

  private static SceneryItemVisualLod StaticLod(string name, string reference) =>
    new(name, SvdLodType.StaticShape, reference, null, null, null,
      Billboard(), 10f, []);

  private static SceneryItemVisualLod BoneLod(string name, string reference) =>
    new(name, SvdLodType.BoneShape, null, reference, null, null,
      Billboard(), 10f, []);

  private static SceneryItemVisualLod BillboardLod(string name) =>
    new(name, SvdLodType.Billboard, null, null, null, null,
      Billboard(), 100f, []);

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

  private static StaticShape StaticShape(
    string name,
    params StaticShapeMesh[] meshes
  ) => new(name, new Vector3(-1f), new Vector3(1f), meshes, []);

  private static StaticShapeMesh StaticMesh(string name) {
    var indices = new uint[] { 0, 2, 1 };
    return new StaticShapeMesh(
      name,
      0,
      $"{name}:ftx",
      "SIOpaque:txs",
      0,
      0,
      3,
      TriangleVertices(),
      indices) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = Convert.ToUInt32(indices.Length),
    };
  }

  private static BoneShape BoneShape(
    string name,
    params BoneShapeMesh[] meshes
  ) => new(name, new Vector3(-1f), new Vector3(1f), meshes, []);

  private static BoneShapeMesh BoneMesh(string name) {
    var indices = new uint[] { 0, 2, 1 };
    return new BoneShapeMesh(
      name,
      0,
      $"{name}:ftx",
      "SIOpaque:txs",
      0,
      0,
      3,
      BoneVertices(),
      indices) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = Convert.ToUInt32(indices.Length),
      LogicalIndexCount = Convert.ToUInt32(indices.Length),
    };
  }

  private static StaticShapeVertex[] TriangleVertices() => [
    StaticVertex(Vector3.Zero),
    StaticVertex(new Vector3(0f, 0f, -1f)),
    StaticVertex(Vector3.UnitX),
  ];

  private static StaticShapeVertex StaticVertex(Vector3 position) => new(
    position,
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One);

  private static BoneShapeVertex[] BoneVertices() => [
    BoneVertex(Vector3.Zero),
    BoneVertex(new Vector3(0f, 0f, -1f)),
    BoneVertex(Vector3.UnitX),
  ];

  private static BoneShapeVertex BoneVertex(Vector3 position) => new(
    position,
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One,
    new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0));

  private sealed record FixtureData(
    RideTrackResourceResolution Resolution,
    RideTrackVisualResourceBridgeResult Visuals,
    TrackSectionResourceLink Section,
    RideTrackPlacement Placement,
    Terrain Terrain,
    string StaticPath
  );
}
