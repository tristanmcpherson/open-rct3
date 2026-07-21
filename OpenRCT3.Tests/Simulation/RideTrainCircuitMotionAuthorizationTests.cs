// Ride Train Circuit Motion Authorization Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainCircuitMotionAuthorizationTests {
  [Test]
  public void Authorize_ReturnsExactTraversalForReciprocalCircuit() {
    var runtime = CircuitRuntime();

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      runtime.Train,
      runtime.Track);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit));
      Assert.That(result.IsAuthorized, Is.True);
      Assert.That(result.Traversal, Is.SameAs(runtime.Track.CircuitTraversal));
    }
  }

  [Test]
  public void Authorize_ReturnsExactTraversalForReciprocalImportedPiecewiseCircuit() {
    var runtime = CircuitRuntime(PiecewiseCircuit());

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      runtime.Train,
      runtime.Track);

    using (Assert.EnterMultipleScope()) {
      Assert.That(runtime.Track.Circuit!.Continuity,
        Is.EqualTo(TrackCircuitContinuity.ImportedPiecewise));
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit));
      Assert.That(result.IsAuthorized, Is.True);
      Assert.That(result.Traversal, Is.SameAs(runtime.Track.CircuitTraversal));
    }
  }

  [Test]
  public void Authorize_ReturnsUnprovenFallbackForOpenTrack() {
    var track = Track(isCircuit: false);
    var instance = Instance();
    var graph = Graph();
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      new(instance, track),
      new(track, RideTrackGeometryStatus.OpenTrack, graph, null),
      new TrackGraphTraversal(graph),
      null);
    var trainRuntime = TrainRuntime(instance, trackRuntime);

    var result = RideTrainCircuitMotionAuthorization.Authorize(trainRuntime, trackRuntime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.UnprovenEndpointFallback));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.Traversal, Is.Null);
    }
  }

  [Test]
  public void Authorize_RejectsForeignRuntimeObjectEvenWhenIdsMatch() {
    var runtime = CircuitRuntime();
    var foreign = runtime.Track with { };

    var result = RideTrainCircuitMotionAuthorization.Authorize(runtime.Train, foreign);

    Assert.That(result.Status, Is.EqualTo(
      RideTrainCircuitMotionAuthorizationStatus.ForeignTrackRuntime));
  }

  [Test]
  public void Authorize_RejectsTrainMissingFromRideForwardListEvenWhenBackpointerMatches() {
    var runtime = CircuitRuntime();
    var ride = runtime.Train.TrainResource.RideInstance;
    var forgedTrain = new DatRideTrainInstanceData(
      2_000,
      "Cars\\Synthetic\\ForgedTrain",
      "ForgedTrain:rit",
      ride.EntryId,
      whichTrain: 0,
      length: 12.5f,
      mass: 1_000f,
      cars: []);
    var forgedRuntime = runtime.Train with {
      TrainResource = new RideTrainInstanceResourceLink(
        ride,
        forgedTrain,
        Ordinal: 0,
        Source: null),
    };

    var result = RideTrainCircuitMotionAuthorization.Authorize(forgedRuntime, runtime.Track);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.ForeignTrackIdentity));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.Traversal, Is.Null);
    }
  }

  [Test]
  public void Authorize_RejectsChangedCircuitTraversalIdentity() {
    var track = Track(isCircuit: true);
    var instance = Instance();
    var geometryCircuit = Circuit();
    var foreignCircuit = Circuit();
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      new(instance, track),
      new(track, RideTrackGeometryStatus.Circuit, null, geometryCircuit),
      null,
      new TrackCircuitTraversal(foreignCircuit));
    var trainRuntime = TrainRuntime(instance, trackRuntime);

    var result = RideTrainCircuitMotionAuthorization.Authorize(trainRuntime, trackRuntime);

    Assert.That(result.Status, Is.EqualTo(
      RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity));
  }

  [Test]
  public void Authorize_SelectsOneExactSavedCursorCircuit() {
    var fixture = MultiCircuitFixture(1, 1);

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      fixture.Train,
      fixture.Track,
      fixture.RenderedCars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit));
      Assert.That(result.IsAuthorized, Is.True);
      Assert.That(result.Traversal,
        Is.SameAs(fixture.Track.SegmentCircuitTraversals[1].Traversal));
    }
  }

  [Test]
  public void Authorize_MultiCircuitRetainsInteriorLinkWithoutRenderedBody() {
    var fixture = MultiCircuitFixtureWithInteriorLink(1, 1, 1);

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      fixture.Train,
      fixture.Track,
      fixture.RuntimeCars,
      fixture.RenderedCars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.RuntimeCars, Has.Count.EqualTo(3));
      Assert.That(fixture.RenderedCars, Has.Count.EqualTo(2));
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit));
      Assert.That(result.Traversal,
        Is.SameAs(fixture.Track.SegmentCircuitTraversals[1].Traversal));
    }
  }

  [Test]
  public void Authorize_LegacyRenderedCarsNullElementFailsWithTypedOutcome() {
    var fixture = MultiCircuitFixture(1);
    var renderedCars = fixture.RenderedCars.ToArray();
    renderedCars[0] = null!;

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      fixture.Train,
      fixture.Track,
      renderedCars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(
          RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.Traversal, Is.Null);
    }
  }

  [Test]
  public void Authorize_RejectsSavedConsistAcrossSeparateCircuits() {
    var fixture = MultiCircuitFixture(0, 1);

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      fixture.Train,
      fixture.Track,
      fixture.RenderedCars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.CrossCircuitSavedConsist));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.Traversal, Is.Null);
    }
  }

  [Test]
  public void Authorize_RejectsForeignSavedCursorTraversalIdentity() {
    var fixture = MultiCircuitFixture(1);
    var renderedCars = fixture.RenderedCars.ToArray();
    var entry = renderedCars[0];
    var foreignTraversal = new TrackCircuitTraversal(
      fixture.Track.SegmentCircuitTraversals[1].Circuit);
    var front = ForeignContact(entry.SavedCursor.Front, foreignTraversal);
    var rear = ForeignContact(entry.SavedCursor.Rear, foreignTraversal);
    renderedCars[0] = entry with {
      SavedCursor = entry.SavedCursor with { Front = front, Rear = rear },
    };

    var result = RideTrainCircuitMotionAuthorization.Authorize(
      fixture.Train,
      fixture.Track,
      renderedCars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.Traversal, Is.Null);
    }
  }

  private static (
    RideInstanceTrainRuntimeEntry Train,
    RideInstanceTrackRuntimeEntry Track) CircuitRuntime(TrackCircuit? circuit = null) {
    var track = Track(isCircuit: true);
    var instance = Instance();
    circuit ??= Circuit();
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      new(instance, track),
      new(track, RideTrackGeometryStatus.Circuit, null, circuit),
      null,
      new TrackCircuitTraversal(circuit));
    return (TrainRuntime(instance, trackRuntime), trackRuntime);
  }

  private static MultiCircuitAuthorizationFixture MultiCircuitFixture(
    params int[] carCircuitIndices
  ) => MultiCircuitFixture(carCircuitIndices, linkOrdinal: null);

  private static MultiCircuitAuthorizationFixture MultiCircuitFixtureWithInteriorLink(
    params int[] carCircuitIndices
  ) => MultiCircuitFixture(carCircuitIndices, linkOrdinal: 1);

  private static MultiCircuitAuthorizationFixture MultiCircuitFixture(
    IReadOnlyList<int> carCircuitIndices,
    int? linkOrdinal
  ) {
    var pieceIds = new ulong[] { 9_001, 9_002, 9_003, 9_004 };
    var carIds = Enumerable.Range(0, carCircuitIndices.Count)
      .Select(index => 3_000ul + Convert.ToUInt64(index))
      .ToArray();
    var ride = new DatTrackedRideInstanceData(
      900,
      "Synthetic multi-circuit ride",
      track: 700,
      "Tracks\\Synthetic",
      "Synthetic:trr",
      nTrains: 1,
      nCarsPerTrain: carIds.Length - (linkOrdinal.HasValue ? 1 : 0),
      trainSelection: 0,
      trains: [1_000]);
    var savedTrain = new DatRideTrainInstanceData(
      1_000,
      "Cars\\Synthetic\\SyntheticTrain",
      "SyntheticTrain:rit",
      ride.EntryId,
      whichTrain: 0,
      length: 12.5f,
      mass: 1_000f,
      cars: carIds);
    var track = new RideTrack(
      700,
      direction: 0,
      firstSegmentSourceEntryId: 800,
      lastSegmentSourceEntryId: 801,
      isCircuit: true,
      prototype: false,
      hasSerializedTrackPieceOrder: true,
      trackPieceSourceEntryIds: pieceIds,
      segmentSourceEntryIds: [800, 801],
      flexiColour0: 0,
      flexiColour1: 0,
      flexiColour2: 0,
      trackedRideInstanceReference: ride.EntryId,
      flippedTrackSections: null,
      tunnelLightColour: null,
      serializedIsCircuit: false,
      hasAuthoritativeTrackPieceOrder: true);
    var first = SegmentCircuit(pieceIds[0], pieceIds[1]);
    var second = SegmentCircuit(pieceIds[2], pieceIds[3]);
    var geometry = new RideTrackGeometryLink(
      track,
      RideTrackGeometryStatus.MultiCircuit,
      Graph: null,
      Circuit: null) {
      SegmentCircuits = [
        new(800, Array.AsReadOnly(pieceIds.Take(2).ToArray()), first),
        new(801, Array.AsReadOnly(pieceIds.Skip(2).ToArray()), second),
      ],
    };
    var identities = RideInstanceTrackGraph.Build([ride], [track]);
    var resolution = new RideTrackGeometryResolution(
      [geometry],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var trackRegistry = RideInstanceTrackRuntimeRegistry.Build(identities, resolution);
    var trainResources = RideTrainInstanceResourceRegistry.Build([ride], [savedTrain], []);
    var trainRegistry = RideInstanceTrainRuntimeRegistry.Build(trackRegistry, trainResources);
    var train = trainRegistry.Entries.Single();
    var savedCars = carCircuitIndices.Select((circuitIndex, ordinal) => {
      var pieceId = circuitIndex == 0 ? pieceIds[0] : pieceIds[2];
      var role = ordinal == linkOrdinal
        ? RideTrainCarRole.Link
        : RideTrainCarRole.Front;
      return new DatRideCarInstanceData(
        carIds[ordinal],
        savedTrain.EntryId,
        whichCar: ordinal,
        whichRideTrainCar: Convert.ToInt32(role),
        frontWheelDistance: 0f,
        rearWheelDistance: 0f,
        trackPiece: pieceId,
        rearTrackPiece: pieceId,
        distance: 0f,
        reversed: false,
        speed: 0f,
        length: 4f,
        mass: 100f,
        positionValid: true);
    }).ToArray();
    var carRegistry = RideCarInstanceRuntimeRegistry.Build(trainRegistry, savedCars);
    var rawPieces = new[] {
      TrackPieceData(pieceIds[0], 800, pieceIds[1], pieceIds[1], 0f),
      TrackPieceData(pieceIds[1], 800, pieceIds[0], pieceIds[0], 100f),
      TrackPieceData(pieceIds[2], 801, pieceIds[3], pieceIds[3], 0f),
      TrackPieceData(pieceIds[3], 801, pieceIds[2], pieceIds[2], 100f),
    };
    var cursors = RideCarSavedWheelCursorRegistry.Build(carRegistry, rawPieces);
    var runtimeCars = carRegistry.Entries.ToArray();
    var alignedCursors = cursors.Entries.ToArray();
    if (linkOrdinal.HasValue) {
      var links = runtimeCars.Select((runtime, index) => new RideCarLink(
        runtime.SavedRole,
        $"car-{index}:ric",
        Source: null,
        Visuals: Array.Empty<RideVisualLink>())).ToArray();
      var roles = runtimeCars.Select((runtime, index) =>
        runtime.SavedRole == RideTrainCarRole.Link
          ? new RideTrainConsistRoleEntry(
            index,
            null,
            runtime.SavedRole,
            links[index].Reference,
            0,
            false)
          : new RideTrainConsistRoleEntry(
            index,
            index,
            runtime.SavedRole,
            links[index].Reference,
            1)).ToArray();
      var consistCars = roles.Select((role, index) =>
        new RideInstanceTrainConsistCarRuntimeEntry(
          role,
          links[index],
          new RideCarPeepSlotEvidence(
            links[index],
            null!,
            role.PeepSlotCount,
            Array.Empty<RideVisualShapeLodLink>()))).ToArray();
      var consist = new RideInstanceTrainConsistRuntimeEntry(
        train,
        null,
        new RideTrainLink("synthetic-train", null, links),
        new RideTrainConsistRoleResolution(carIds.Length - 1, roles),
        Array.AsReadOnly(consistCars),
        RideInstanceTrainConsistRuntimeStatus.Resolved);
      runtimeCars = runtimeCars.Select((runtime, index) => runtime with {
        CarResource = links[index],
        TrainConsist = consist,
        ConsistCar = consistCars[index],
      }).ToArray();
      alignedCursors = alignedCursors.Select((cursor, index) =>
        cursor with { CarRuntime = runtimeCars[index] }).ToArray();
    }
    var renderedCars = alignedCursors
      .Where(cursor => cursor.CarRuntime.SavedRole != RideTrainCarRole.Link)
      .Select(cursor =>
      new RideCarStaticInstanceEntry(
        cursor.RegistryIndex,
        cursor.CarRuntime,
        cursor,
        RideCarStaticInstanceIssue.None,
        BodyTemplate: null,
        Geometry: null,
        Pose: null,
        GeometryUnavailableDetail: null,
        StaticPoseUnavailableDetail: null)).ToArray();
    return new(train, trackRegistry.Entries.Single(), runtimeCars, renderedCars);
  }

  private static RideCarSavedWheelContactCursor ForeignContact(
    RideCarSavedWheelContactCursor contact,
    TrackCircuitTraversal traversal
  ) {
    var original = contact.Cursor!.Value;
    var cursor = traversal.AtPiece(original.PieceIndex, original.PieceArcLength);
    return contact with { Cursor = cursor, Sample = cursor.Sample() };
  }

  private static DatTrackPieceData TrackPieceData(
    ulong id,
    ulong segment,
    ulong previous,
    ulong next,
    float startDistance
  ) => new(
    entryId: id,
    flexiColourField: new DatSceneryFlexiColour(0, 0, 0),
    next,
    owner: segment,
    platformPiece: 0,
    prev: previous,
    reversed: false,
    sidDatabaseEntry: 1,
    symbolName: "Synthetic:tks",
    sceneryItem: 1,
    sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1),
    segment,
    startDistance,
    startDistanceBackwardsSpline: 100f,
    userAngleDegrees: 0);

  private static TrackCircuit SegmentCircuit(ulong firstId, ulong secondId) => new([
    new TrackCircuitPiece($"track-piece-{firstId}", HalfCircuit(first: true)),
    new TrackCircuitPiece($"track-piece-{secondId}", HalfCircuit(first: false)),
  ]);

  private static RideInstanceTrainRuntimeEntry TrainRuntime(
    DatTrackedRideInstanceData instance,
    RideInstanceTrackRuntimeEntry trackRuntime
  ) {
    var train = new DatRideTrainInstanceData(
      1_000,
      "Cars\\Synthetic\\SyntheticTrain",
      "SyntheticTrain:rit",
      instance.EntryId,
      whichTrain: 0,
      length: 12.5f,
      mass: 1_000f,
      cars: []);
    return new(
      0,
      trackRuntime,
      new RideTrainInstanceResourceLink(instance, train, Ordinal: 0, Source: null));
  }

  private static DatTrackedRideInstanceData Instance() => new(
    900,
    "Ride 900",
    track: 700,
    "Tracks\\Synthetic",
    "Synthetic:trr",
    nTrains: 1,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains: [1_000]);

  private static RideTrack Track(bool isCircuit) => new(
    700,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 801,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: false,
    trackPieceSourceEntryIds: [9_001, 9_002],
    segmentSourceEntryIds: [800, 801],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: 900,
    flippedTrackSections: null,
    tunnelLightColour: null,
    serializedIsCircuit: false,
    hasAuthoritativeTrackPieceOrder: true);

  private static TrackGraph Graph() {
    var start = new TrackNode("start");
    var end = new TrackNode("end");
    return new(
      [start, end],
      [new TrackEdge("track-piece-9001", start, end, StraightPiece())]);
  }

  private static TrackPiece StraightPiece() =>
    new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, Vector3.Zero, Vector3.UnitX),
      Pair(1f, Vector3.UnitX * 2f, Vector3.UnitX),
    ]));

  private static TrackCircuit Circuit() => new([
    new TrackCircuitPiece("track-piece-9001", HalfCircuit(first: true)),
    new TrackCircuitPiece("track-piece-9002", HalfCircuit(first: false)),
  ]);

  private static TrackCircuit PiecewiseCircuit() => TrackCircuit.CreateImportedPiecewise([
    new ImportedTrackCircuitPiece(
      "track-piece-9001",
      PiecewiseHalfCircuit(first: true),
      PreviousId: "track-piece-9002",
      NextId: "track-piece-9002"),
    new ImportedTrackCircuitPiece(
      "track-piece-9002",
      PiecewiseHalfCircuit(first: false),
      PreviousId: "track-piece-9001",
      NextId: "track-piece-9001"),
  ]);

  private static TrackPiece HalfCircuit(bool first) {
    var start = first ? Vector3.UnitX : -Vector3.UnitX;
    var end = -start;
    var startTangent = first ? Vector3.UnitY * 3f : -Vector3.UnitY * 3f;
    return new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, start, startTangent, Vector3.UnitZ * 0.5f),
      Pair(1f, end, -startTangent, Vector3.UnitZ * 0.5f),
    ]));
  }

  private static TrackPiece PiecewiseHalfCircuit(bool first) {
    var start = first ? Vector3.Zero : Vector3.UnitX;
    var end = first ? Vector3.UnitX : Vector3.Zero;
    var startTangent = first ? Vector3.UnitX : Vector3.UnitY;
    var endTangent = first ? Vector3.UnitX : -Vector3.UnitX;
    return new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, start, startTangent, Vector3.UnitZ * 0.5f),
      Pair(1f, end, endTangent, Vector3.UnitZ * 0.5f),
    ]));
  }

  private static RailControlPair Pair(
    float parameter,
    Vector3 center,
    Vector3 tangent,
    Vector3? halfGauge = null
  ) {
    var gauge = halfGauge ?? Vector3.UnitY;
    return new(
      parameter,
      center - gauge,
      tangent,
      center + gauge,
      tangent,
      BankRadians: 0f);
  }

  private sealed record MultiCircuitAuthorizationFixture(
    RideInstanceTrainRuntimeEntry Train,
    RideInstanceTrackRuntimeEntry Track,
    IReadOnlyList<RideCarInstanceRuntimeEntry> RuntimeCars,
    IReadOnlyList<RideCarStaticInstanceEntry> RenderedCars
  );
}
