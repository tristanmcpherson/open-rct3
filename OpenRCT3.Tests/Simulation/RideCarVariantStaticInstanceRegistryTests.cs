// Ride Car Variant Static Instance Registry Tests
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
public class RideCarVariantStaticInstanceRegistryTests {
  private const string RidePath = "rides.unique.ovl";
  private const string TrainPath = "trains.unique.ovl";
  private const string CarPath = "cars.unique.ovl";
  private const string VisualPath = "visuals.unique.ovl";
  private const string ShapePath = "shapes.unique.ovl";

  [Test]
  public void Build_NormalBodyComposesExactBorrowedBatchesAndFinitePose() {
    using var fixture = CreateFixture(visualVariant: 0);

    var registry = Build(fixture);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsResolved, Is.True);
      Assert.That(entry.Issues, Is.EqualTo(RideCarVariantStaticInstanceIssue.None));
      Assert.That(entry.CarRuntime, Is.SameAs(fixture.CarRuntime.Entries.Single()));
      Assert.That(entry.SavedCursor, Is.SameAs(fixture.SavedCursors.Entries.Single()));
      Assert.That(entry.VisualSelection, Is.SameAs(fixture.Selections.Entries.Single()));
      Assert.That(entry.VisualTemplate, Is.SameAs(fixture.Templates.Entries.Single()));
      Assert.That(entry.SelectedVariant, Is.EqualTo(RideCarVisualVariant.Normal));
      Assert.That(entry.RequiredBodyRole, Is.EqualTo(RideVisualRole.Body));
      Assert.That(entry.BodyTemplate!.Link, Is.SameAs(fixture.NormalBody));
      Assert.That(entry.BodyTemplate.BoneShape, Is.SameAs(fixture.NormalShape));
      Assert.That(entry.MaterialBatches, Is.SameAs(entry.BodyTemplate.Batches));
      Assert.That(entry.MaterialBatches!.Single(),
        Is.SameAs(entry.VisualTemplate.Template!.Batches.Single()));
      Assert.That(entry.Geometry!.CarLength, Is.EqualTo(2f).Within(0.0001f));
      Assert.That(entry.Pose, Is.Not.Null);
      Assert.That(IsFinite(entry.Pose!.Transform), Is.True);
      Assert.That(IsFinite(entry.Pose.Orientation), Is.True);
      Assert.That(registry.ResolvedCount, Is.EqualTo(1));
      Assert.That(registry.UnresolvedCount, Is.Zero);
    }
  }

  [Test]
  public void Build_WildVariantUsesFlippedBodyGeometryInsteadOfNormalPeepBody() {
    using var fixture = CreateFixture(visualVariant: 1);

    var entry = Build(fixture).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsResolved, Is.True);
      Assert.That(entry.SelectedVariant, Is.EqualTo(RideCarVisualVariant.WildFlipped));
      Assert.That(entry.RequiredBodyRole, Is.EqualTo(RideVisualRole.WildFlippedBody));
      Assert.That(entry.BodyTemplate!.Link, Is.SameAs(fixture.WildBody));
      Assert.That(entry.BodyTemplate.Link, Is.Not.SameAs(fixture.NormalBody));
      Assert.That(entry.BodyTemplate.BoneShape, Is.SameAs(fixture.WildShape));
      Assert.That(entry.CarRuntime.ConsistCar!.PeepSlotEvidence.BodyVisual,
        Is.SameAs(fixture.NormalBody));
      Assert.That(entry.Geometry!.CarLength, Is.EqualTo(3f).Within(0.0001f));
      Assert.That(entry.Pose, Is.Not.Null);
    }
  }

  [Test]
  public void Build_UnsupportedVariantRetainsTypedSelectionBlockerWithoutBodyState() {
    using var fixture = CreateFixture(visualVariant: 2);

    var registry = Build(fixture);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        entry.VisualSelection.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnsupportedVariant));
      Assert.That(
        entry.VisualTemplate.Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed));
      Assert.That(
        entry.Issues,
        Is.EqualTo(RideCarVariantStaticInstanceIssue.UnavailableVisualSelection));
      Assert.That(entry.BodyTemplate, Is.Null);
      Assert.That(entry.MaterialBatches, Is.Null);
      Assert.That(entry.Geometry, Is.Null);
      Assert.That(entry.Pose, Is.Null);
      Assert.That(registry.UnavailableVisualSelectionCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_UnresolvedCursorRetainsSelectedTemplateAndBshGeometry() {
    using var fixture = CreateFixture(
      visualVariant: 1,
      includeTrackPieceData: false);

    var entry = Build(fixture).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.SavedCursor.IsResolved, Is.False);
      Assert.That(
        entry.Issues,
        Is.EqualTo(RideCarVariantStaticInstanceIssue.UnresolvedSavedCursor));
      Assert.That(entry.BodyTemplate, Is.SameAs(entry.VisualTemplate.Template));
      Assert.That(entry.BodyTemplate!.Link, Is.SameAs(fixture.WildBody));
      Assert.That(entry.Geometry, Is.Not.Null);
      Assert.That(entry.Pose, Is.Null);
    }
  }

  [Test]
  public void Build_StaticSelectedBodyExposesBshOnlyGeometryBlocker() {
    using var fixture = CreateFixture(
      visualVariant: 1,
      useStaticSelectedBody: true);

    var registry = Build(fixture);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.VisualSelection.IsSelected, Is.True);
      Assert.That(entry.VisualTemplate.IsResolved, Is.True);
      Assert.That(entry.BodyTemplate!.ShapeKind,
        Is.EqualTo(RideCarVisualTemplateShapeKind.StaticShape));
      Assert.That(
        entry.Issues,
        Is.EqualTo(RideCarVariantStaticInstanceIssue.UnavailableModelGeometry));
      Assert.That(entry.MaterialBatches, Is.Not.Empty);
      Assert.That(entry.Geometry, Is.Null);
      Assert.That(entry.Pose, Is.Null);
      Assert.That(entry.GeometryUnavailableDetail, Does.Contain("not a BSH resource"));
      Assert.That(registry.UnavailableModelGeometryCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_ChangedSelectedLodBecomesTypedTemplateBlocker() {
    using var fixture = CreateFixture(visualVariant: 0);
    var selected = fixture.Selections.Entries.Single();
    var linked = selected.Body!.Lods.Single();
    var copied = linked with { Lod = linked.Lod with { } };
    var changedBody = selected.Body with { Lods = [copied] };
    var changed = selected with {
      Body = changedBody,
      BodyControlFallback = selected.BodyControlFallback! with { Body = changedBody },
    };
    var changedSelections = new RideCarVisualVariantSelectionRegistry(
      [changed],
      [selected.Body!]);
    using var changedTemplates = RideCarVariantVisualTemplateRegistry.Build(changedSelections);

    var registry = RideCarVariantStaticInstanceRegistry.Build(
      fixture.CarRuntime,
      fixture.SavedCursors,
      changedSelections,
      changedTemplates);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.VisualSelection.IsSelected, Is.True);
      Assert.That(
        entry.VisualTemplate.Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.ChangedSelectionIdentity));
      Assert.That(
        entry.Issues,
        Is.EqualTo(RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate));
      Assert.That(entry.BodyTemplate, Is.Null);
      Assert.That(entry.Geometry, Is.Null);
      Assert.That(entry.Pose, Is.Null);
      Assert.That(registry.UnavailableBodyTemplateCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_EqualResolvedContactsExposeTypedStaticPoseBlocker() {
    using var fixture = CreateFixture(
      visualVariant: 0,
      frontWheelDistance: 1f,
      rearWheelDistance: 1f);

    var registry = Build(fixture);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.SavedCursor.IsResolved, Is.True);
      Assert.That(entry.BodyTemplate, Is.Not.Null);
      Assert.That(entry.Geometry, Is.Not.Null);
      Assert.That(entry.Pose, Is.Null);
      Assert.That(
        entry.Issues,
        Is.EqualTo(RideCarVariantStaticInstanceIssue.UnavailableStaticPose));
      Assert.That(entry.StaticPoseUnavailableDetail, Does.Contain("degenerate chord"));
      Assert.That(registry.UnavailableStaticPoseCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_RejectsForeignRegistryIdentityAndConfiguredCarBound() {
    using var first = CreateFixture(visualVariant: 0, prefix: "First");
    using var foreign = CreateFixture(visualVariant: 0, prefix: "Foreign");

    var identityError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVariantStaticInstanceRegistry.Build(
        first.CarRuntime,
        first.SavedCursors,
        foreign.Selections,
        foreign.Templates)));
    var limitError = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVariantStaticInstanceRegistry.Build(
        first.CarRuntime,
        first.SavedCursors,
        first.Selections,
        first.Templates,
        RideCarVariantStaticInstanceRegistryLimits.Default with {
          MaximumCarCount = 0,
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(identityError!.Message, Does.Contain("runtime object identity"));
      Assert.That(limitError!.Message, Does.Contain("car count exceeds the limit 0"));
    }
  }

  private static RideCarVariantStaticInstanceRegistry Build(StaticFixture fixture) =>
    RideCarVariantStaticInstanceRegistry.Build(
      fixture.CarRuntime,
      fixture.SavedCursors,
      fixture.Selections,
      fixture.Templates);

  private static StaticFixture CreateFixture(
    int visualVariant,
    bool includeTrackPieceData = true,
    bool useStaticSelectedBody = false,
    float frontWheelDistance = 2f,
    float rearWheelDistance = 1f,
    string prefix = "Synthetic"
  ) {
    var pieceIds = new ulong[] { 710, 711, 712, 713 };
    var runtimePieces = CirclePieces();
    var circuit = new TrackCircuit(pieceIds.Select((id, index) =>
      new TrackCircuitPiece($"track-piece-{id}", runtimePieces[index])));
    var starts = PieceStarts(circuit);
    var rideInstance = Instance(prefix, [2_000]);
    var trainInstance = Train(
      prefix,
      rideInstance,
      entryId: 2_000,
      cars: [3_000],
      visualVariant);
    var carInstance = Car(
      trainInstance,
      entryId: 3_000,
      pieceIds[0],
      frontWheelDistance,
      rearWheelDistance);
    var track = Track(rideInstance, pieceIds);
    var trackGraph = RideInstanceTrackGraph.Build([rideInstance], [track]);
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

    var normalShape = visualVariant == 0 && useStaticSelectedBody
      ? (object)StaticBody($"{prefix}NormalShape")
      : BoneBody($"{prefix}NormalShape", 2f);
    var wildShape = visualVariant == 1 && useStaticSelectedBody
      ? (object)StaticBody($"{prefix}WildShape")
      : BoneBody($"{prefix}WildShape", 3f);
    var normalVisual = Visual(
      RideVisualRole.Body,
      $"{prefix}NormalBody",
      normalShape);
    var wildVisual = Visual(
      RideVisualRole.WildFlippedBody,
      $"{prefix}WildBody",
      wildShape);
    var carResource = CarResource(
      $"{prefix}Car",
      normalVisual.Link.Reference,
      wildVisual.Link.Reference);
    var trainResource = TrainResource($"{prefix}Train", carResource.Name);
    var trainSource = new RideTrainResourceSource(
      new OvlFile(trainResource.Name, FileType.RideTrain, TrainPath),
      trainResource,
      [TrainPath, CarPath, VisualPath, ShapePath]);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [rideInstance],
      [trainInstance],
      [new RideTrainInstanceResourceSource(
        trainInstance.RideTrainOverlayName,
        trainSource)]);
    var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);

    var carSource = new RideCarResourceSource(
      new OvlFile(carResource.Name, FileType.RideCar, CarPath),
      carResource,
      [CarPath, VisualPath, ShapePath]);
    var carLink = new RideCarLink(
      RideTrainCarRole.Front,
      $"{carResource.Name}:ric",
      carSource,
      [normalVisual.Link, wildVisual.Link]);
    var trainLink = new RideTrainLink(trainResource.Name, trainSource, [carLink]);
    var rideResource = RideResource($"{prefix}Ride", trainResource.Name);
    var rideLink = new TrackedRideResourceLink(
      new TrackedRideResourceSource(
        new OvlFile(rideResource.Name, FileType.TrackedRide, RidePath),
        rideResource,
        [RidePath, TrainPath, CarPath, VisualPath, ShapePath]),
      [trainLink],
      WildSplitter: null);
    var normalBody = new RideCarVisualShapeLink(
      rideLink,
      trainLink,
      carLink,
      normalVisual.Link,
      [normalVisual.Lod]);
    var wildBody = new RideCarVisualShapeLink(
      rideLink,
      trainLink,
      carLink,
      wildVisual.Link,
      [wildVisual.Lod]);
    var role = new RideTrainConsistRoleEntry(
      RuntimeIndex: 0,
      NonLinkIndex: 0,
      RideTrainCarRole.Front,
      carLink.Reference,
      PeepSlotCount: 1);
    var consistCar = new RideInstanceTrainConsistCarRuntimeEntry(
      role,
      carLink,
      new RideCarPeepSlotEvidence(carLink, normalBody, 1, normalBody.Lods));
    var consist = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime.Entries.Single(),
      rideLink,
      trainLink,
      new RideTrainConsistRoleResolution(1, [role]),
      [consistCar],
      RideInstanceTrainConsistRuntimeStatus.Resolved);
    var carRuntime = RideCarInstanceRuntimeRegistry.Build(
      trainRuntime,
      [carInstance],
      new RideInstanceTrainConsistRuntimeRegistry([consist]));

    var rawPieces = includeTrackPieceData
      ? pieceIds.Select((id, index) => TrackPieceData(
        id,
        previous: pieceIds[(index + pieceIds.Length - 1) % pieceIds.Length],
        next: pieceIds[(index + 1) % pieceIds.Length],
        Convert.ToSingle(starts[index]))).ToArray()
      : [];
    var savedCursors = RideCarSavedWheelCursorRegistry.Build(carRuntime, rawPieces);
    var bridge = new RideCarVisualResourceBridgeResult([normalBody, wildBody], 0);
    var selections = RideCarVisualVariantSelector.Build(carRuntime, bridge);
    var templates = RideCarVariantVisualTemplateRegistry.Build(selections);
    return new(
      carRuntime,
      savedCursors,
      selections,
      templates,
      normalBody,
      wildBody,
      normalShape,
      wildShape);
  }

  private static VisualFixture Visual(
    RideVisualRole role,
    string name,
    object shape
  ) {
    SceneryItemVisualLod lod;
    RideVisualShapeLodLink linked;
    switch (shape) {
      case StaticShape staticShape:
        lod = new(
          "body",
          SvdLodType.StaticShape,
          $"{staticShape.Name}:shs",
          null,
          null,
          null,
          Billboard(),
          1f,
          []);
        linked = new(
          lod,
          new RideStaticShapeResourceSource(
            new OvlFile(staticShape.Name, FileType.StaticShape, ShapePath),
            staticShape),
          BoneShapeSource: null);
        break;
      case BoneShape boneShape:
        lod = new(
          "body",
          SvdLodType.BoneShape,
          null,
          $"{boneShape.Name}:bsh",
          null,
          null,
          Billboard(),
          1f,
          []);
        linked = new(
          lod,
          StaticShapeSource: null,
          new RideBoneShapeResourceSource(
            new OvlFile(boneShape.Name, FileType.BoneShape, ShapePath),
            boneShape));
        break;
      default:
        throw new ArgumentException("Shape must be SHS or BSH.", nameof(shape));
    }
    var resource = new SceneryItemVisual(
      name,
      0,
      0f,
      0f,
      0f,
      0f,
      [lod],
      null);
    var link = new RideVisualLink(
      role,
      $"{name}:svd",
      new RideVisualResourceSource(
        new OvlFile(name, FileType.SceneryItemVisual, VisualPath),
        resource));
    return new(link, linked);
  }

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

  private static StaticShape StaticBody(string name) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [StaticMesh()],
    []);

  private static BoneShape BoneBody(string name, float length) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [BoneMesh()],
    [
      Bone("CarFront", new Vector3(length, 0f, 0f)),
      Bone("CarRear", Vector3.Zero),
      Bone("WheelFR", new Vector3(length * 0.75f, 0f, 1f)),
      Bone("WheelFL", new Vector3(length * 0.75f, 0f, -1f)),
      Bone("WheelRR", new Vector3(length * 0.25f, 0f, 0.75f)),
      Bone("WheelRL", new Vector3(length * 0.25f, 0f, -0.75f)),
    ]);

  private static BoneShapeBone Bone(string name, Vector3 position) => new(
    name,
    -1,
    Matrix4x4.Identity,
    Matrix4x4.CreateTranslation(position));

  private static StaticShapeMesh StaticMesh() => new(
    "body",
    0,
    "Body:ftx",
    "Body:txs",
    0,
    0,
    1,
    new[] {
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY, Vector4.One),
    },
    new uint[] { 0, 1, 2 }) {
    IndexLayout = StaticShapeIndexLayout.TriangleList,
    StoredIndexCount = 3,
  };

  private static BoneShapeMesh BoneMesh() {
    var skinning = new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0);
    return new(
      "body",
      0,
      "Body:ftx",
      "Body:txs",
      0,
      0,
      1,
      new[] {
        new BoneShapeVertex(
          Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector4.One, skinning),
        new BoneShapeVertex(
          Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector4.One, skinning),
        new BoneShapeVertex(
          Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY, Vector4.One, skinning),
      },
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3,
    };
  }

  private static DatTrackedRideInstanceData Instance(
    string prefix,
    ulong[] trains
  ) => new(
    entryId: 900,
    name: prefix,
    track: 700,
    trackedRideOverlayName: $@"Cars\{prefix}",
    trackedRideSymbolName: $"{prefix}Ride:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData Train(
    string prefix,
    DatTrackedRideInstanceData ride,
    ulong entryId,
    ulong[] cars,
    int visualVariant
  ) => new(
    entryId,
    rideTrainOverlayName: $@"Cars\{prefix}\{prefix}Train",
    rideTrainSymbolName: $"{prefix}Train:rit",
    trackedRideInstance: ride.EntryId,
    whichTrain: 0,
    length: 4f,
    mass: 1_000f,
    cars,
    whichRideCarSivVariant: visualVariant);

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

  private static RideCar CarResource(
    string name,
    string normalBody,
    string wildBody
  ) => new(
    name,
    RideCarVersion.Wild,
    name,
    name,
    0,
    0,
    normalBody,
    1f,
    MovingVisual: null,
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
    Soaked: null,
    new RideCarWildSettings(
      1,
      wildBody,
      FlippedMovingVisual: null,
      0,
      1f,
      1f,
      null,
      4.1f,
      0,
      0,
      0));

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

  private sealed record VisualFixture(
    RideVisualLink Link,
    RideVisualShapeLodLink Lod);

  private sealed class StaticFixture(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualVariantSelectionRegistry selections,
    RideCarVariantVisualTemplateRegistry templates,
    RideCarVisualShapeLink normalBody,
    RideCarVisualShapeLink wildBody,
    object normalShape,
    object wildShape
  ) : IDisposable {
    public RideCarInstanceRuntimeRegistry CarRuntime { get; } = carRuntime;
    public RideCarSavedWheelCursorRegistry SavedCursors { get; } = savedCursors;
    public RideCarVisualVariantSelectionRegistry Selections { get; } = selections;
    public RideCarVariantVisualTemplateRegistry Templates { get; } = templates;
    public RideCarVisualShapeLink NormalBody { get; } = normalBody;
    public RideCarVisualShapeLink WildBody { get; } = wildBody;
    public object NormalShape { get; } = normalShape;
    public object WildShape { get; } = wildShape;

    public void Dispose() => Templates.Dispose();
  }
}
