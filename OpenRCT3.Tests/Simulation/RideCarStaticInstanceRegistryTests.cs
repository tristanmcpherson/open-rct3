// Ride Car Static Instance Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarStaticInstanceRegistryTests {
  private const string RidePath = "rides.unique.ovl";
  private const string TrainPath = "trains.unique.ovl";
  private const string CarPath = "cars.unique.ovl";
  private const string VisualPath = "visuals.unique.ovl";
  private const string ShapePath = "shapes.unique.ovl";

  [Test]
  public void Build_ComposesExactBorrowedIdentitiesIntoFiniteStaticPose() {
    using var fixture = CreateFixture();

    var registry = RideCarStaticInstanceRegistry.Build(
      fixture.CarRuntime,
      fixture.SavedCursors,
      fixture.VisualTemplates);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.CarCount, Is.EqualTo(1));
      Assert.That(registry.ResolvedCount, Is.EqualTo(1));
      Assert.That(registry.UnresolvedCount, Is.Zero);
      Assert.That(entry.RegistryIndex, Is.Zero);
      Assert.That(entry.IsResolved, Is.True);
      Assert.That(entry.Issues, Is.EqualTo(RideCarStaticInstanceIssue.None));
      Assert.That(entry.CarRuntime, Is.SameAs(fixture.CarRuntime.Entries.Single()));
      Assert.That(entry.SavedCursor, Is.SameAs(fixture.SavedCursors.Entries.Single()));
      Assert.That(entry.BodyTemplate, Is.SameAs(fixture.VisualTemplates.Templates.Single()));
      Assert.That(entry.BodyTemplate!.Link.Car, Is.SameAs(entry.CarRuntime.CarResource));
      Assert.That(entry.BodyTemplate.Link.Train,
        Is.SameAs(entry.CarRuntime.TrainConsist!.TrainGraph));
      Assert.That(entry.BodyTemplate.Link.Ride,
        Is.SameAs(entry.CarRuntime.TrainConsist.RideGraph));
      Assert.That(entry.MaterialBatches, Is.SameAs(entry.BodyTemplate.Batches));
      Assert.That(entry.MaterialBatches!.Single(),
        Is.SameAs(entry.BodyTemplate.Batches.Single()));
      Assert.That(entry.Geometry, Is.Not.Null);
      Assert.That(entry.Pose, Is.Not.Null);
      Assert.That(entry.Pose!.FrontContact, Is.EqualTo(entry.SavedCursor.Front.Sample));
      Assert.That(entry.Pose.RearContact, Is.EqualTo(entry.SavedCursor.Rear.Sample));
      Assert.That(IsFinite(entry.Pose.Transform), Is.True);
      Assert.That(IsFinite(entry.Pose.Orientation), Is.True);
      Assert.That(entry.GeometryUnavailableDetail, Is.Null);
      Assert.That(entry.StaticPoseUnavailableDetail, Is.Null);
    }
  }

  [Test]
  public void Build_EqualResolvedWheelContactsExposeUnavailableStaticPose() {
    using var fixture = CreateFixture(
      includeResolvedPeer: true,
      frontWheelDistance: 1f,
      rearWheelDistance: 1f);

    var registry = RideCarStaticInstanceRegistry.Build(
      fixture.CarRuntime,
      fixture.SavedCursors,
      fixture.VisualTemplates);
    var entry = registry.Entries[0];
    var resolvedPeer = registry.Entries[1];

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.SavedCursors.ResolvedCarCount, Is.EqualTo(2));
      Assert.That(fixture.SavedCursors.ResolvedContactCount, Is.EqualTo(4));
      Assert.That(entry.SavedCursor.Front.Sample,
        Is.EqualTo(entry.SavedCursor.Rear.Sample));
      Assert.That(entry.Issues,
        Is.EqualTo(RideCarStaticInstanceIssue.UnavailableStaticPose));
      Assert.That(registry.UnavailableStaticPoseCount, Is.EqualTo(1));
      Assert.That(registry.ResolvedCount, Is.EqualTo(1));
      Assert.That(entry.BodyTemplate, Is.SameAs(fixture.VisualTemplates.Templates.Single()));
      Assert.That(entry.Geometry, Is.Not.Null);
      Assert.That(entry.Pose, Is.Null);
      Assert.That(entry.GeometryUnavailableDetail, Is.Null);
      Assert.That(entry.StaticPoseUnavailableDetail,
        Does.Contain("degenerate chord"));
      Assert.That(resolvedPeer.Issues, Is.EqualTo(RideCarStaticInstanceIssue.None));
      Assert.That(resolvedPeer.IsResolved, Is.True);
      Assert.That(resolvedPeer.Pose, Is.Not.Null);
      Assert.That(resolvedPeer.BodyTemplate, Is.SameAs(entry.BodyTemplate));
    }
  }

  [Test]
  public void Build_RetainsTemplateAndGeometryWhenSavedCursorIsUnresolved() {
    using var fixture = CreateFixture(includeTrackPieceData: false);

    var entry = RideCarStaticInstanceRegistry.Build(
      fixture.CarRuntime,
      fixture.SavedCursors,
      fixture.VisualTemplates).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Issues,
        Is.EqualTo(RideCarStaticInstanceIssue.UnresolvedSavedCursor));
      Assert.That(entry.IsResolved, Is.False);
      Assert.That(entry.SavedCursor.IsResolved, Is.False);
      Assert.That(entry.BodyTemplate, Is.SameAs(fixture.VisualTemplates.Templates.Single()));
      Assert.That(entry.MaterialBatches, Is.SameAs(entry.BodyTemplate!.Batches));
      Assert.That(entry.Geometry, Is.Not.Null);
      Assert.That(entry.Pose, Is.Null);
    }
  }

  [Test]
  public void Build_ExposesUnavailableResourceTemplateAndGeometryAsTypedOutcomes() {
    using var unresolvedResource = CreateFixture(resolveCarResource: false);
    using var missingTemplate = CreateFixture(includeTemplate: false);
    using var staticTemplate = CreateFixture(useStaticBody: true);

    var resourceEntry = RideCarStaticInstanceRegistry.Build(
      unresolvedResource.CarRuntime,
      unresolvedResource.SavedCursors,
      unresolvedResource.VisualTemplates).Entries.Single();
    var templateEntry = RideCarStaticInstanceRegistry.Build(
      missingTemplate.CarRuntime,
      missingTemplate.SavedCursors,
      missingTemplate.VisualTemplates).Entries.Single();
    var geometryEntry = RideCarStaticInstanceRegistry.Build(
      staticTemplate.CarRuntime,
      staticTemplate.SavedCursors,
      staticTemplate.VisualTemplates).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(resourceEntry.Issues,
        Is.EqualTo(RideCarStaticInstanceIssue.UnresolvedCarResource));
      Assert.That(resourceEntry.BodyTemplate, Is.Null);
      Assert.That(resourceEntry.Geometry, Is.Null);
      Assert.That(resourceEntry.Pose, Is.Null);
      Assert.That(templateEntry.Issues,
        Is.EqualTo(RideCarStaticInstanceIssue.MissingBodyTemplate));
      Assert.That(templateEntry.SavedCursor.IsResolved, Is.True);
      Assert.That(templateEntry.BodyTemplate, Is.Null);
      Assert.That(templateEntry.Pose, Is.Null);
      Assert.That(geometryEntry.Issues,
        Is.EqualTo(RideCarStaticInstanceIssue.UnavailableModelGeometry));
      Assert.That(geometryEntry.BodyTemplate, Is.Not.Null);
      Assert.That(geometryEntry.MaterialBatches,
        Is.SameAs(geometryEntry.BodyTemplate!.Batches));
      Assert.That(geometryEntry.Geometry, Is.Null);
      Assert.That(geometryEntry.Pose, Is.Null);
      Assert.That(geometryEntry.GeometryUnavailableDetail, Does.Contain("not a BSH"));
    }
  }

  [Test]
  public void Build_RejectsForeignTemplateParentIdentityAndConfiguredBounds() {
    using var foreign = CreateFixture(useForeignTemplateParent: true);
    using var bounded = CreateFixture();

    var identityError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticInstanceRegistry.Build(
        foreign.CarRuntime,
        foreign.SavedCursors,
        foreign.VisualTemplates)));
    var limitError = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarStaticInstanceRegistry.Build(
        bounded.CarRuntime,
        bounded.SavedCursors,
        bounded.VisualTemplates,
        RideCarStaticInstanceRegistryLimits.Default with { MaximumCarCount = 0 })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(identityError!.Message, Does.Contain("foreign RIC, RIT, or TRR"));
      Assert.That(limitError!.Message, Does.Contain("car count exceeds the limit 0"));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_ComposesSevenExactStaticCarsInSavedConsistOrder() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(Directory.Exists(installRoot), Is.True, installRoot);
    var mapPath = Path.Combine(installRoot!, "Campaigns", "Base", "BoxOffice.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);
      RideTrackTopologyLoader.Load(park, data.RideTracks, data.TrackSegments);
      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        data.TrackedRideInstances,
        data.RideTrainInstances);
      var trackResources = loaded.Catalog.ResolveAll(park.RideTrackPlacements);
      var geometry = RideTrackGeometryResolver.Resolve(
        terrain,
        park.RideTracks,
        trackResources);
      var trackGraph = RideInstanceTrackGraph.Build(
        data.TrackedRideInstances,
        park.RideTracks);
      var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(trackGraph, geometry);
      var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(
        trackRuntime,
        loaded.RideResources.TrainInstances);
      var consists = RideInstanceTrainConsistRuntimeRegistry.Build(
        trainRuntime,
        loaded.RideResources);
      var cars = RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        data.RideCarInstances,
        consists);
      var cursors = RideCarSavedWheelCursorRegistry.Build(cars, data.TrackPieces);
      using var templates = RideCarVisualTemplateRegistry.Build(
        loaded.RideResources.CarVisuals);

      var instances = RideCarStaticInstanceRegistry.Build(cars, cursors, templates);
      var expectedRoles = new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Rear,
      };
      var expectedShapes = new[] {
        "StreamLinedMonorailFrontHI",
        "MonoLink",
        "StreamLinedMonorailMiddleHI",
        "MonoLink",
        "StreamLinedMonorailMiddleHI",
        "MonoLink",
        "StreamLinedMonorailRearHI",
      };

      TestContext.Progress.WriteLine(
        "BoxOffice static cars: " + string.Join(", ", instances.Entries.Select(entry =>
          $"{entry.CarInstanceEntryId}:{entry.SavedRole}:" +
          $"{entry.BodyTemplate?.BoneShape?.Name}:{entry.Issues}")));
      using (Assert.EnterMultipleScope()) {
        Assert.That(instances.CarCount, Is.EqualTo(7));
        Assert.That(instances.ResolvedCount, Is.EqualTo(7));
        Assert.That(instances.UnresolvedCount, Is.Zero);
        Assert.That(instances.Entries.Select(entry => entry.CarInstanceEntryId),
          Is.EqualTo(loaded.RideResources.TrainInstances.Links.Single().TrainInstance.Cars));
        Assert.That(instances.Entries.Select(entry => entry.SavedRole),
          Is.EqualTo(expectedRoles));
        Assert.That(instances.Entries.Select(entry => entry.BodyTemplate!.BoneShape!.Name),
          Is.EqualTo(expectedShapes));
        Assert.That(instances.Entries.All(entry => entry.IsResolved), Is.True);
        Assert.That(instances.Entries.All(entry => entry.MaterialBatches!.Count > 0), Is.True);
        Assert.That(instances.Entries.All(entry => IsFinite(entry.Pose!.Transform)), Is.True);
        Assert.That(instances.Entries.All(entry => IsFinite(entry.Pose!.Orientation)), Is.True);
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static StaticFixture CreateFixture(
    bool includeTrackPieceData = true,
    bool resolveCarResource = true,
    bool includeTemplate = true,
    bool useStaticBody = false,
    bool useForeignTemplateParent = false,
    bool includeResolvedPeer = false,
    float frontWheelDistance = 2f,
    float rearWheelDistance = 1f
  ) {
    var pieceIds = new ulong[] { 710, 711, 712, 713 };
    var runtimePieces = CirclePieces();
    var circuit = new TrackCircuit(pieceIds.Select((id, index) =>
      new TrackCircuitPiece($"track-piece-{id}", runtimePieces[index])));
    var starts = PieceStarts(circuit);
    var trainIds = includeResolvedPeer
      ? new ulong[] { 2_000, 2_001 }
      : new ulong[] { 2_000 };
    var ride = Instance(trainIds);
    var trains = trainIds.Select((id, index) => Train(
      ride,
      id,
      index,
      [3_000ul + Convert.ToUInt64(index)])).ToArray();
    var cars = trains.Select((train, index) => Car(
      train,
      3_000ul + Convert.ToUInt64(index),
      pieceIds[0],
      index == 0 ? frontWheelDistance : 2f,
      index == 0 ? rearWheelDistance : 1f)).ToArray();
    var track = Track(ride, pieceIds);
    var trackGraph = RideInstanceTrackGraph.Build([ride], [track]);
    var trackGeometry = new RideTrackGeometryResolution(
      [new RideTrackGeometryLink(
        track,
        RideTrackGeometryStatus.Circuit,
        Graph: null,
        circuit)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(trackGraph, trackGeometry);

    var visual = Visual("SyntheticBodyVisual", useStaticBody);
    var carResource = CarResource("SyntheticCar", visual.Name);
    var trainResource = TrainResource("SyntheticTrain", carResource.Name);
    var trainSource = new RideTrainResourceSource(
      new OvlFile(trainResource.Name, FileType.RideTrain, TrainPath),
      trainResource,
      [TrainPath, CarPath, VisualPath, ShapePath]);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [ride],
      trains,
      resolveCarResource
        ? [new RideTrainInstanceResourceSource(
          trains[0].RideTrainOverlayName,
          trainSource)]
        : []);
    var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);

    var visualSource = new RideVisualResourceSource(
      new OvlFile(visual.Name, FileType.SceneryItemVisual, VisualPath),
      visual);
    var visualLink = new RideVisualLink(
      RideVisualRole.Body,
      $"{visual.Name}:svd",
      visualSource);
    var carSource = new RideCarResourceSource(
      new OvlFile(carResource.Name, FileType.RideCar, CarPath),
      carResource,
      [CarPath, VisualPath, ShapePath]);
    var carLink = new RideCarLink(
      RideTrainCarRole.Front,
      $"{carResource.Name}:ric",
      carSource,
      [visualLink]);
    var trainLink = new RideTrainLink(trainResource.Name, trainSource, [carLink]);
    var rideResource = RideResource("SyntheticRide", trainResource.Name);
    var rideSource = new TrackedRideResourceSource(
      new OvlFile(rideResource.Name, FileType.TrackedRide, RidePath),
      rideResource,
      [RidePath, TrainPath, CarPath, VisualPath, ShapePath]);
    var rideLink = new TrackedRideResourceLink(rideSource, [trainLink], null);
    var bodyEvidenceLink = CreateBodyLink(
      rideLink,
      trainLink,
      carLink,
      visualLink,
      useStaticBody);

    RideCarInstanceRuntimeRegistry carRuntime;
    if (resolveCarResource) {
      var consists = trainRuntime.Entries.Select(runtime => {
        var role = new RideTrainConsistRoleEntry(
          RuntimeIndex: 0,
          NonLinkIndex: 0,
          RideTrainCarRole.Front,
          carLink.Reference,
          PeepSlotCount: 1);
        var consistCar = new RideInstanceTrainConsistCarRuntimeEntry(
          role,
          carLink,
          new RideCarPeepSlotEvidence(
            carLink,
            bodyEvidenceLink,
            1,
            bodyEvidenceLink.Lods));
        return new RideInstanceTrainConsistRuntimeEntry(
          runtime,
          rideLink,
          trainLink,
          new RideTrainConsistRoleResolution(1, [role]),
          [consistCar],
          RideInstanceTrainConsistRuntimeStatus.Resolved);
      }).ToArray();
      carRuntime = RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        cars,
        new RideInstanceTrainConsistRuntimeRegistry(consists));
    } else {
      carRuntime = RideCarInstanceRuntimeRegistry.Build(trainRuntime, cars);
    }

    var rawPieces = includeTrackPieceData
      ? pieceIds.Select((id, index) => TrackPieceData(
        id,
        previous: pieceIds[(index + pieceIds.Length - 1) % pieceIds.Length],
        next: pieceIds[(index + 1) % pieceIds.Length],
        Convert.ToSingle(starts[index]))).ToArray()
      : [];
    var savedCursors = RideCarSavedWheelCursorRegistry.Build(carRuntime, rawPieces);

    RideCarVisualResourceBridgeResult bridge;
    if (!resolveCarResource || !includeTemplate) {
      bridge = new RideCarVisualResourceBridgeResult([], 0);
    } else {
      var templateTrain = trainLink;
      var templateRide = rideLink;
      if (useForeignTemplateParent) {
        templateTrain = new RideTrainLink(trainResource.Name, trainSource, [carLink]);
        templateRide = new TrackedRideResourceLink(rideSource, [templateTrain], null);
      }
      bridge = new RideCarVisualResourceBridgeResult(
        [useForeignTemplateParent
          ? CreateBodyLink(
            templateRide,
            templateTrain,
            carLink,
            visualLink,
            useStaticBody)
          : bodyEvidenceLink],
        0);
    }
    return new(
      carRuntime,
      savedCursors,
      RideCarVisualTemplateRegistry.Build(bridge));
  }

  private static RideCarVisualShapeLink CreateBodyLink(
    TrackedRideResourceLink ride,
    RideTrainLink train,
    RideCarLink car,
    RideVisualLink visual,
    bool useStaticBody
  ) {
    var lod = visual.Source!.Resource.Lods.Single();
    var linkedLod = useStaticBody
      ? new RideVisualShapeLodLink(lod, StaticSource(StaticBody()), null)
      : new RideVisualShapeLodLink(lod, null, BoneSource(BoneBody()));
    return new RideCarVisualShapeLink(ride, train, car, visual, [linkedLod]);
  }

  private static SceneryItemVisual Visual(string name, bool useStaticBody) => new(
    name,
    0,
    0f,
    0f,
    0f,
    0f,
    [new SceneryItemVisualLod(
      "body",
      useStaticBody ? SvdLodType.StaticShape : SvdLodType.BoneShape,
      useStaticBody ? "SyntheticBody:shs" : null,
      useStaticBody ? null : "SyntheticBody:bsh",
      null,
      null,
      new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
      1f,
      [])],
    null);

  private static RideStaticShapeResourceSource StaticSource(StaticShape shape) =>
    new(new OvlFile(shape.Name, FileType.StaticShape, ShapePath), shape);

  private static RideBoneShapeResourceSource BoneSource(BoneShape shape) =>
    new(new OvlFile(shape.Name, FileType.BoneShape, ShapePath), shape);

  private static StaticShape StaticBody() => new(
    "SyntheticBody",
    Vector3.Zero,
    Vector3.One,
    [StaticMesh()],
    []);

  private static BoneShape BoneBody() => new(
    "SyntheticBody",
    Vector3.Zero,
    Vector3.One,
    [BoneMesh()],
    [
      Bone("CarFront", new Vector3(2f, 0f, 0f)),
      Bone("CarRear", Vector3.Zero),
      Bone("WheelFR", new Vector3(1.5f, 0f, 1f)),
      Bone("WheelFL", new Vector3(1.5f, 0f, -1f)),
      Bone("WheelRR", new Vector3(0.5f, 0f, 0.75f)),
      Bone("WheelRL", new Vector3(0.5f, 0f, -0.75f)),
    ]);

  private static BoneShapeBone Bone(string name, Vector3 position) => new(
    name,
    -1,
    Matrix4x4.Identity,
    Matrix4x4.CreateTranslation(position));

  private static StaticShapeMesh StaticMesh() {
    var vertices = new[] {
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY, Vector4.One),
    };
    return new StaticShapeMesh(
      "body",
      0,
      "Body:ftx",
      "Body:txs",
      0,
      0,
      1,
      vertices,
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
    };
  }

  private static BoneShapeMesh BoneMesh() {
    var skinning = new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0);
    var vertices = new[] {
      new BoneShapeVertex(
        Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY, Vector4.One, skinning),
    };
    return new BoneShapeMesh(
      "body",
      0,
      "Body:ftx",
      "Body:txs",
      0,
      0,
      1,
      vertices,
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3,
    };
  }

  private static DatTrackedRideInstanceData Instance(ulong[] trains) => new(
    entryId: 900,
    name: "Synthetic",
    track: 700,
    trackedRideOverlayName: @"Cars\Synthetic",
    trackedRideSymbolName: "SyntheticRide:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData Train(
    DatTrackedRideInstanceData ride,
    ulong entryId,
    int whichTrain,
    ulong[] cars
  ) => new(
    entryId,
    rideTrainOverlayName: @"Cars\Synthetic\SyntheticTrain",
    rideTrainSymbolName: "SyntheticTrain:rit",
    trackedRideInstance: ride.EntryId,
    whichTrain,
    length: 4f,
    mass: 1_000f,
    cars);

  private static DatRideCarInstanceData Car(
    DatRideTrainInstanceData train,
    ulong entryId,
    ulong trackPiece,
    float frontWheelDistance,
    float rearWheelDistance
  ) => new(
    entryId,
    rideTrainInstance: train.EntryId,
    whichCar: 0,
    whichRideTrainCar: Convert.ToInt32(RideTrainCarRole.Front),
    frontWheelDistance,
    rearWheelDistance,
    trackPiece,
    rearTrackPiece: trackPiece,
    distance: 1.5f,
    reversed: false,
    speed: 0f);

  private static RideTrack Track(
    DatTrackedRideInstanceData ride,
    ulong[] pieceIds
  ) => new(
    sourceEntryId: ride.Track,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 800,
    isCircuit: true,
    prototype: false,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: pieceIds,
    segmentSourceEntryIds: [800],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: ride.EntryId,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static DatTrackPieceData TrackPieceData(
    ulong id,
    ulong previous,
    ulong next,
    float start
  ) => new(
    entryId: id,
    flexiColourField: new DatSceneryFlexiColour(0, 0, 0),
    next,
    owner: 800,
    platformPiece: 0,
    prev: previous,
    reversed: false,
    sidDatabaseEntry: 1,
    symbolName: "Synthetic:tks",
    sceneryItem: 1,
    sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1),
    segment: 800,
    startDistance: start,
    startDistanceBackwardsSpline: 100f,
    userAngleDegrees: 0);

  private static RideCar CarResource(string name, string visualName) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    $"{visualName}:svd",
    1f,
    null,
    -1f,
    new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideCarBobbingSettings(0, 0f, 0f, 0f),
    new RideCarAnimationSettings(
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0f, 0, 0f, 0f, 0f, 0f),
    null,
    null);

  private static RideTrain TrainResource(string name, string carName) => new(
    name,
    RideTrainVersion.Vanilla,
    name,
    name,
    new RideTrainCars($"{carName}:ric", null, null, null, null, null, 1, 1, 1, null),
    new RideTrainSpeedSettings(0f, 0f, 0f),
    new RideTrainCameraSettings(
      0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideTrainWaterSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    0,
    new RideTrainUnknownSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    null,
    null,
    null,
    null,
    null);

  private static TrackedRide RideResource(string name, string trainName) => new(
    name,
    TrackedRideVersion.Vanilla,
    [],
    [trainName],
    null,
    null,
    null,
    null!,
    null!,
    null!,
    null!,
    null!,
    null,
    null);

  private static TrackPiece[] CirclePieces() {
    const float radius = 10f;
    const float tangent = 16.568542f;
    return [
      CurvedPiece(
        new(radius, 0f, 0f),
        new(0f, radius, 0f),
        new(0f, tangent, 0f),
        new(-tangent, 0f, 0f)),
      CurvedPiece(
        new(0f, radius, 0f),
        new(-radius, 0f, 0f),
        new(-tangent, 0f, 0f),
        new(0f, -tangent, 0f)),
      CurvedPiece(
        new(-radius, 0f, 0f),
        new(0f, -radius, 0f),
        new(0f, -tangent, 0f),
        new(tangent, 0f, 0f)),
      CurvedPiece(
        new(0f, -radius, 0f),
        new(radius, 0f, 0f),
        new(tangent, 0f, 0f),
        new(0f, tangent, 0f)),
    ];
  }

  private static TrackPiece CurvedPiece(
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent
  ) {
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        startTangent,
        start + halfGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        endTangent,
        end + halfGauge,
        endTangent,
        0f),
    ]));
  }

  private static double[] PieceStarts(TrackCircuit circuit) {
    var starts = new double[circuit.Pieces.Count + 1];
    foreach (var index in Enumerable.Range(0, circuit.Pieces.Count))
      starts[index + 1] = starts[index] + circuit.Pieces[index].Piece.Length;
    return starts;
  }

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static bool IsFinite(Quaternion value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private sealed class StaticFixture(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualTemplateRegistry visualTemplates
  ) : IDisposable {
    public RideCarInstanceRuntimeRegistry CarRuntime { get; } = carRuntime;
    public RideCarSavedWheelCursorRegistry SavedCursors { get; } = savedCursors;
    public RideCarVisualTemplateRegistry VisualTemplates { get; } = visualTemplates;

    public void Dispose() => VisualTemplates.Dispose();
  }
}
