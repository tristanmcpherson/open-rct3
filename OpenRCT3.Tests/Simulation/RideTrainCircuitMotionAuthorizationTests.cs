// Ride Train Circuit Motion Authorization Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

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

  private static (
    RideInstanceTrainRuntimeEntry Train,
    RideInstanceTrackRuntimeEntry Track) CircuitRuntime() {
    var track = Track(isCircuit: true);
    var instance = Instance();
    var circuit = Circuit();
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      new(instance, track),
      new(track, RideTrackGeometryStatus.Circuit, null, circuit),
      null,
      new TrackCircuitTraversal(circuit));
    return (TrainRuntime(instance, trackRuntime), trackRuntime);
  }

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

  private static TrackPiece HalfCircuit(bool first) {
    var start = first ? Vector3.UnitX : -Vector3.UnitX;
    var end = -start;
    var startTangent = first ? Vector3.UnitY * 3f : -Vector3.UnitY * 3f;
    return new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, start, startTangent, Vector3.UnitZ * 0.5f),
      Pair(1f, end, -startTangent, Vector3.UnitZ * 0.5f),
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
}
