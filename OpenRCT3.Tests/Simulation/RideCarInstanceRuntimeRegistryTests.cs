// Ride Car Instance Runtime Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarInstanceRuntimeRegistryTests {
  [Test]
  public void Build_ComposesExactCarsInTrainOrderAndRetainsSavedState() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000, 2_001]);
    var front = Car(
      2_000,
      train.EntryId,
      whichCar: 0,
      RideTrainCarRole.Front,
      trackPiece: 710,
      rearTrackPiece: 711,
      distance: 4.25f,
      reversed: true,
      speed: -2.5f);
    var rear = Car(
      2_001,
      train.EntryId,
      whichCar: 1,
      RideTrainCarRole.Rear,
      trackPiece: 711,
      rearTrackPiece: 710,
      distance: 8.5f,
      reversed: false,
      speed: 3.75f);

    var registry = RideCarInstanceRuntimeRegistry.Build(
      TrainRuntime([ride], [train], resolvedTrack: false),
      [rear, front]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.SavedCarCount, Is.EqualTo(2));
      Assert.That(registry.LinkedCarCount, Is.EqualTo(2));
      Assert.That(registry.UnreferencedCarCount, Is.Zero);
      Assert.That(registry.ResolvedTrackPieceCarCount, Is.Zero);
      Assert.That(registry.ResolvedResourceCarCount, Is.Zero);
      Assert.That(registry.Entries.Select(entry => entry.CarInstanceEntryId),
        Is.EqualTo(new ulong[] { 2_000, 2_001 }));
      Assert.That(registry.Entries.Select(entry => entry.SavedCarIndex),
        Is.EqualTo(new[] { 1, 0 }));
      Assert.That(registry.Entries.Select(entry => entry.RegistryIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(registry.Entries.Select(entry => entry.SavedRole), Is.EqualTo(new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Rear,
      }));
      Assert.That(registry.Entries[0].CarInstance, Is.SameAs(front));
      Assert.That(registry.Entries[0].TrainRuntime.TrainResource.TrainInstance,
        Is.SameAs(train));
      Assert.That(registry.Entries[0].SavedDistance, Is.EqualTo(4.25f));
      Assert.That(registry.Entries[0].SavedReversed, Is.True);
      Assert.That(registry.Entries[0].SavedSpeed, Is.EqualTo(-2.5f));
      Assert.That(registry.Entries[0].TrackStatus,
        Is.EqualTo(RideTrackGeometryStatus.UnsupportedGeometry));
      Assert.That(registry.Entries[0].TrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.UnresolvedTrack));
      Assert.That(registry.Entries[0].RearTrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.UnresolvedTrack));
      Assert.That(registry.Entries[0].ResourceStatus,
        Is.EqualTo(RideCarResourceRuntimeStatus.UnresolvedTrainResource));
      Assert.That(registry.Entries[0].CarResource, Is.Null);
    }
  }

  [Test]
  public void Build_ResolvesBothSavedPieceIdentitiesWithoutCreatingCursorState() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var car = Car(
      2_000,
      train.EntryId,
      whichCar: 0,
      RideTrainCarRole.Front,
      trackPiece: 710,
      rearTrackPiece: 711,
      distance: 9.5f,
      reversed: true,
      speed: 12f);

    var registry = RideCarInstanceRuntimeRegistry.Build(
      TrainRuntime([ride], [train], resolvedTrack: true),
      [car]);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.ResolvedTrackPieceCarCount, Is.EqualTo(1));
      Assert.That(entry.TrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.Resolved));
      Assert.That(entry.TrackPiece.SavedTrackPieceEntryId, Is.EqualTo(710));
      Assert.That(entry.TrackPiece.PieceIndex, Is.EqualTo(0));
      Assert.That(entry.TrackPiece.Piece, Is.Not.Null);
      Assert.That(entry.RearTrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.Resolved));
      Assert.That(entry.RearTrackPiece.SavedTrackPieceEntryId, Is.EqualTo(711));
      Assert.That(entry.RearTrackPiece.PieceIndex, Is.EqualTo(1));
      Assert.That(entry.RearTrackPiece.Piece, Is.Not.Null);
      Assert.That(entry.TrackStatus, Is.EqualTo(RideTrackGeometryStatus.OpenTrack));
      Assert.That(entry.SavedDistance, Is.EqualTo(9.5f));
      Assert.That(entry.SavedReversed, Is.True);
      Assert.That(entry.SavedSpeed, Is.EqualTo(12f));
    }
  }

  [Test]
  public void Build_RetainsCarFromUnreferencedTrainWithoutInventingRuntimeOwner() {
    var ride = Instance(900, 700, []);
    var unreferencedTrain = Train(1_000, ride, 0, [2_000]);
    var car = Car(
      2_000,
      unreferencedTrain.EntryId,
      whichCar: 0,
      RideTrainCarRole.Front);

    var registry = RideCarInstanceRuntimeRegistry.Build(
      TrainRuntime([ride], [unreferencedTrain], resolvedTrack: false),
      [car]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.SavedCarCount, Is.EqualTo(1));
      Assert.That(registry.LinkedCarCount, Is.Zero);
      Assert.That(registry.UnreferencedCarCount, Is.EqualTo(1));
      Assert.That(registry.UnreferencedInstances.Single(), Is.SameAs(car));
    }
  }

  [Test]
  public void Build_RejectsMissingDuplicateOrUnlistedSavedCars() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var runtime = TrainRuntime([ride], [train], resolvedTrack: false);
    var listed = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);
    var duplicate = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);
    var unlisted = Car(2_001, train.EntryId, 1, RideTrainCarRole.Rear);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(runtime, [])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(runtime, [listed, duplicate])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(runtime, [listed, unlisted])));
  }

  [Test]
  public void Build_RejectsWrongOwnerOrderAndPieceOutsideOwningTrack() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var unresolved = TrainRuntime([ride], [train], resolvedTrack: false);
    var wrongOwner = Car(2_000, 1_001, 0, RideTrainCarRole.Front);
    var wrongOrder = Car(2_000, train.EntryId, 1, RideTrainCarRole.Front);
    var wrongPiece = Car(
      2_000,
      train.EntryId,
      0,
      RideTrainCarRole.Front,
      trackPiece: 999,
      rearTrackPiece: 711);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(unresolved, [wrongOwner])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(unresolved, [wrongOrder])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(
        TrainRuntime([ride], [train], resolvedTrack: true),
        [wrongPiece])));
  }

  [Test]
  public void Build_ExposesMissingSavedPieceReferencesAsTypedState() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var car = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);

    var entry = RideCarInstanceRuntimeRegistry.Build(
      TrainRuntime([ride], [train], resolvedTrack: true),
      [car]).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.TrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.MissingSavedReference));
      Assert.That(entry.RearTrackPiece.Status,
        Is.EqualTo(RideCarTrackPieceRuntimeStatus.MissingSavedReference));
      Assert.That(entry.HasResolvedTrackPieces, Is.False);
    }
  }

  [Test]
  public void Build_RetainsExactResolvedConsistRoleAndRicResource() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var car = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);
    var trainRuntime = TrainRuntime(
      [ride],
      [train],
      resolvedTrack: false,
      resolvedTrainResource: true);
    var consist = ResolvedConsist(trainRuntime, RideTrainCarRole.Front);

    var entry = RideCarInstanceRuntimeRegistry.Build(
      trainRuntime,
      [car],
      consist.Registry).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.ResourceStatus, Is.EqualTo(RideCarResourceRuntimeStatus.Resolved));
      Assert.That(entry.HasResolvedResource, Is.True);
      Assert.That(entry.TrainConsist, Is.SameAs(consist.Entry));
      Assert.That(
        entry.ConsistStatus,
        Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.Resolved));
      Assert.That(entry.ConsistCar, Is.SameAs(consist.Car));
      Assert.That(entry.CarResource, Is.SameAs(consist.Car.CarResource));
      Assert.That(entry.CarResource!.Source!.Resource, Is.SameAs(consist.Resource));
    }
  }

  [Test]
  public void Build_RejectsSavedRoleThatDisagreesWithResolvedConsist() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var savedRear = Car(2_000, train.EntryId, 0, RideTrainCarRole.Rear);
    var trainRuntime = TrainRuntime(
      [ride],
      [train],
      resolvedTrack: false,
      resolvedTrainResource: true);
    var consist = ResolvedConsist(trainRuntime, RideTrainCarRole.Front);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        [savedRear],
        consist.Registry)));

    Assert.That(exception!.Message, Does.Contain("disagrees with resolved consist role"));
  }

  [Test]
  public void Build_ExposesUnresolvedRicAsTypedResourceState() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var car = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);
    var trainRuntime = TrainRuntime(
      [ride],
      [train],
      resolvedTrack: false,
      resolvedTrainResource: true);
    var consistEntry = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime.Entries.Single(),
      RideGraph: null,
      TrainGraph: null,
      Roles: null,
      Cars: [],
      RideInstanceTrainConsistRuntimeStatus.UnresolvedRideCarResource);
    var consists = new RideInstanceTrainConsistRuntimeRegistry([consistEntry]);

    var entry = RideCarInstanceRuntimeRegistry.Build(
      trainRuntime,
      [car],
      consists).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        entry.ResourceStatus,
        Is.EqualTo(RideCarResourceRuntimeStatus.UnresolvedCarResource));
      Assert.That(entry.TrainConsist, Is.SameAs(consistEntry));
      Assert.That(entry.ConsistCar, Is.Null);
      Assert.That(entry.CarResource, Is.Null);
    }
  }

  [Test]
  public void Build_EnforcesConfiguredBounds() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, [2_000]);
    var runtime = TrainRuntime([ride], [train], resolvedTrack: false);
    var car = Car(2_000, train.EntryId, 0, RideTrainCarRole.Front);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(
        runtime,
        [car],
        new RideCarInstanceRuntimeRegistryLimits(
          MaximumTrainCount: 0,
          MaximumCarCount: 1))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarInstanceRuntimeRegistry.Build(
        runtime,
        [car],
        new RideCarInstanceRuntimeRegistryLimits(
          MaximumTrainCount: 1,
          MaximumCarCount: 0))));
  }

  private static RideInstanceTrainRuntimeRegistry TrainRuntime(
    IReadOnlyList<DatTrackedRideInstanceData> rides,
    IReadOnlyList<DatRideTrainInstanceData> trains,
    bool resolvedTrack,
    bool resolvedTrainResource = false
  ) {
    var tracks = rides.Select(Track).ToArray();
    var identities = RideInstanceTrackGraph.Build(rides, tracks);
    var geometry = new RideTrackGeometryResolution(
      tracks.Select(track => Geometry(track, resolvedTrack)).ToArray(),
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: resolvedTrack ? 0 : tracks.Length,
      UnsupportedTopologyTrackCount: 0);
    var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(identities, geometry);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      rides,
      trains,
      resolvedTrainResource
        ? [new RideTrainInstanceResourceSource(
          @"Cars\Synthetic\SyntheticTrain",
          new RideTrainResourceSource(
            new OvlFile("SyntheticTrain", FileType.RideTrain, "train.unique.ovl"),
            TrainResource(),
            ["train.unique.ovl"]))]
        : []);
    return RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);
  }

  private static ResolvedConsistFixture ResolvedConsist(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    RideTrainCarRole role
  ) {
    var resource = CarResource("Front");
    var carLink = new RideCarLink(
      role,
      "Front:ric",
      new RideCarResourceSource(
        new OvlFile(resource.Name, FileType.RideCar, "car.unique.ovl"),
        resource,
        ["car.unique.ovl"]),
      []);
    var roleEntry = new RideTrainConsistRoleEntry(
      RuntimeIndex: 0,
      NonLinkIndex: 0,
      role,
      ResourceName: carLink.Reference,
      PeepSlotCount: 1);
    var car = new RideInstanceTrainConsistCarRuntimeEntry(
      roleEntry,
      carLink,
      new RideCarPeepSlotEvidence(
        carLink,
        BodyVisual: null!,
        PeepSlotCount: 1,
        MarkerLods: []));
    var entry = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime.Entries.Single(),
      RideGraph: null,
      TrainGraph: null,
      new RideTrainConsistRoleResolution(1, [roleEntry]),
      [car],
      RideInstanceTrainConsistRuntimeStatus.Resolved);
    return new(new RideInstanceTrainConsistRuntimeRegistry([entry]), entry, car, resource);
  }

  private static RideTrackGeometryLink Geometry(RideTrack track, bool resolvedTrack) {
    if (!resolvedTrack)
      return new(
        track,
        RideTrackGeometryStatus.UnsupportedGeometry,
        Graph: null,
        Circuit: null);

    var first = new TrackNode("first");
    var middle = new TrackNode("middle");
    var last = new TrackNode("last");
    return new(
      track,
      RideTrackGeometryStatus.OpenTrack,
      new TrackGraph(
        [first, middle, last],
        [
          new TrackEdge("track-piece-710", first, middle, Piece(0f, 10f)),
          new TrackEdge("track-piece-711", middle, last, Piece(10f, 20f)),
        ]),
      Circuit: null);
  }

  private static DatTrackedRideInstanceData Instance(
    ulong entryId,
    ulong trackId,
    ulong[] trains
  ) => new(
    entryId,
    $"Ride {entryId}",
    trackId,
    @"Tracks\Synthetic",
    "Synthetic:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData Train(
    ulong entryId,
    DatTrackedRideInstanceData owner,
    int ordinal,
    ulong[] cars
  ) => new(
    entryId,
    @"Cars\Synthetic\SyntheticTrain",
    "SyntheticTrain:rit",
    owner.EntryId,
    ordinal,
    length: 12.5f,
    mass: 1_000f,
    cars);

  private static DatRideCarInstanceData Car(
    ulong entryId,
    ulong trainId,
    int whichCar,
    RideTrainCarRole role,
    ulong trackPiece = 0,
    ulong rearTrackPiece = 0,
    float distance = 0f,
    bool reversed = false,
    float speed = 0f
  ) => new(
    entryId,
    trainId,
    whichCar,
    Convert.ToInt32(role),
    trackPiece,
    rearTrackPiece,
    distance,
    reversed,
    speed);

  private static RideTrack Track(DatTrackedRideInstanceData instance) => new(
    instance.Track,
    direction: 0,
    firstSegmentSourceEntryId: instance.Track + 100,
    lastSegmentSourceEntryId: instance.Track + 100,
    isCircuit: false,
    prototype: false,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: [710, 711],
    segmentSourceEntryIds: [instance.Track + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: instance.EntryId,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static TrackPiece Piece(float startX, float endX) {
    var start = new Vector3(startX, 0f, 0f);
    var end = new Vector3(endX, 0f, 0f);
    var tangent = end - start;
    var halfGauge = Vector3.UnitY * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        tangent,
        start + halfGauge,
        tangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        tangent,
        end + halfGauge,
        tangent,
        0f),
    ]));
  }

  private static RideTrain TrainResource() => new(
    "SyntheticTrain",
    RideTrainVersion.Vanilla,
    "SyntheticTrain",
    "Synthetic train",
    new RideTrainCars(
      "Front:ric",
      null,
      null,
      null,
      null,
      null,
      1,
      1,
      1,
      null),
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

  private static RideCar CarResource(string name) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    "Body:svd",
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

  private sealed record ResolvedConsistFixture(
    RideInstanceTrainConsistRuntimeRegistry Registry,
    RideInstanceTrainConsistRuntimeEntry Entry,
    RideInstanceTrainConsistCarRuntimeEntry Car,
    RideCar Resource);
}
