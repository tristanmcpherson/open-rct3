// Ride Circuit Kinematic Simulation Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCircuitKinematicSimulationTests {
  [Test]
  public void Build_PreservesSeedOrderExactRideIdentityAndCallerConfiguredState() {
    var registry = Registry(
      Outcome.ForCircuit(700, 900, Circuit(0f)),
      Outcome.ForCircuit(701, 901, Circuit(10f)),
      Outcome.ForOpen(702, 902, Graph(20f, 22f)));
    var secondOffsets = new List<double> { 0d, 0.25d, 0.75d };
    var firstOffsets = new List<double> { 0d, 0.5d };
    var seeds = new[] {
      new RideCircuitKinematicTrainSeed(901, 1, 0.2d, -2d, secondOffsets),
      new RideCircuitKinematicTrainSeed(900, 0, 0.1d, 3d, firstOffsets),
    };
    var expectedSecondLead = registry.Entries[1].CircuitTraversal!.AtPiece(1, 0.2d);
    var expectedFirstLead = registry.Entries[0].CircuitTraversal!.AtPiece(0, 0.1d);

    var simulation = RideCircuitKinematicSimulation.Build(registry, seeds);
    secondOffsets[0] = 8d;
    firstOffsets.Add(9d);

    using (Assert.EnterMultipleScope()) {
      Assert.That(simulation.TrainCount, Is.EqualTo(2));
      Assert.That(simulation.Trains.Select(train => train.SeedIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(simulation.Trains.Select(train => train.InstanceEntryId),
        Is.EqualTo(new ulong[] { 901, 900 }));
      Assert.That(simulation.Trains.Select(train => train.TrackEntryId),
        Is.EqualTo(new ulong[] { 701, 700 }));
      Assert.That(simulation.Trains[0].RuntimeEntry, Is.SameAs(registry.Entries[1]));
      Assert.That(simulation.Trains[1].RuntimeEntry, Is.SameAs(registry.Entries[0]));
      Assert.That(simulation.Trains[0].State.LeadCursor, Is.EqualTo(expectedSecondLead));
      Assert.That(simulation.Trains[1].State.LeadCursor, Is.EqualTo(expectedFirstLead));
      Assert.That(simulation.Trains[0].State.Speed, Is.EqualTo(-2d));
      Assert.That(simulation.Trains[1].State.Speed, Is.EqualTo(3d));
      Assert.That(simulation.Trains[0].State.OffsetsBehindLead,
        Is.EqualTo(new[] { 0d, 0.25d, 0.75d }));
      Assert.That(simulation.Trains[1].State.OffsetsBehindLead,
        Is.EqualTo(new[] { 0d, 0.5d }));
    }

    var trains = (IList<RideCircuitKinematicTrain>)simulation.Trains;
    Assert.Throws<NotSupportedException>(new Action(() =>
      trains[0] = trains[1]));
  }

  [Test]
  public void Advance_WrapsSignedTrainsAndReturnsAnIndependentOrderedSnapshot() {
    var registry = Registry(
      Outcome.ForCircuit(700, 900, Circuit(0f)),
      Outcome.ForCircuit(701, 901, Circuit(10f)));
    var finalPieceIndex = registry.Entries[0].Circuit!.Pieces.Count - 1;
    var finalPieceLength = registry.Entries[0].Circuit!.Pieces[^1].Piece.Length;
    var simulation = RideCircuitKinematicSimulation.Build(registry, [
      new RideCircuitKinematicTrainSeed(
        900,
        finalPieceIndex,
        finalPieceLength - 0.05d,
        1d,
        [0d, 0.1d]),
      new RideCircuitKinematicTrainSeed(901, 0, 0.05d, -1d, [0d, 0.2d]),
    ]);
    var originalLeads = simulation.Trains.Select(train => train.State.Lead).ToArray();
    var step = TimeSpan.FromSeconds(0.2d);
    var expected = simulation.Trains
      .Select(train => train.State.Advance(step))
      .ToArray();

    var advanced = simulation.Advance(step);

    using (Assert.EnterMultipleScope()) {
      Assert.That(advanced, Is.Not.SameAs(simulation));
      Assert.That(simulation.Trains.Select(train => train.State.Lead),
        Is.EqualTo(originalLeads));
      Assert.That(advanced.Trains.Select(train => train.SeedIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(advanced.Trains[0].RuntimeEntry, Is.SameAs(simulation.Trains[0].RuntimeEntry));
      Assert.That(advanced.Trains[1].RuntimeEntry, Is.SameAs(simulation.Trains[1].RuntimeEntry));
      Assert.That(advanced.Trains[0].State.Lead, Is.EqualTo(expected[0].Lead));
      Assert.That(advanced.Trains[1].State.Lead, Is.EqualTo(expected[1].Lead));
      Assert.That(advanced.Trains[0].State.Poses, Is.EqualTo(expected[0].Poses));
      Assert.That(advanced.Trains[1].State.Poses, Is.EqualTo(expected[1].Poses));
      Assert.That(advanced.Trains[0].State.LeadCursor.PieceIndex, Is.EqualTo(0));
      Assert.That(advanced.Trains[1].State.LeadCursor.PieceIndex,
        Is.EqualTo(registry.Entries[1].Circuit!.Pieces.Count - 1));
      Assert.That(simulation.Advance(TimeSpan.Zero), Is.SameAs(simulation));
    }
  }

  [Test]
  public void Api_AllowsMultipleTrainsPerRideAndRejectsInvalidTargetsAndBounds() {
    var registry = Registry(
      Outcome.ForCircuit(700, 900, Circuit(0f)),
      Outcome.ForOpen(701, 901, Graph(10f, 12f)),
      Outcome.ForSkipped(702, 902));
    var valid = Seed(900);

    Assert.Throws<ArgumentException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [Seed(0)])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [Seed(999)])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [Seed(901)])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [Seed(902)])));
    var multiple = RideCircuitKinematicSimulation.Build(registry, [
      valid,
      valid with { LeadPieceArcLength = 0.2d },
    ]);
    using (Assert.EnterMultipleScope()) {
      Assert.That(multiple.TrainCount, Is.EqualTo(2));
      Assert.That(multiple.Trains.Select(train => train.InstanceEntryId),
        Is.EqualTo(new ulong[] { 900, 900 }));
      Assert.That(multiple.Trains[0].RuntimeEntry,
        Is.SameAs(multiple.Trains[1].RuntimeEntry));
      Assert.That(multiple.Trains.Select(train => train.SeedIndex),
        Is.EqualTo(new[] { 0, 1 }));
    }
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [
        valid with { LeadPieceIndex = 99 },
      ])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [
        valid with { LeadPieceArcLength = double.NaN },
      ])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [
        valid with { Speed = double.PositiveInfinity },
      ])));
    Assert.Throws<ArgumentException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(registry, [
        valid with { ContactOffsetsBehindLead = new[] { 1d, 0d } },
      ])));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(
        registry,
        [valid, Seed(901)],
        new(MaximumTrainCount: 1, MaximumContactCount: 10))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCircuitKinematicSimulation.Build(
        registry,
        [valid with { ContactOffsetsBehindLead = new[] { 0d, 1d } }],
        new(MaximumTrainCount: 1, MaximumContactCount: 1))));

    var simulation = RideCircuitKinematicSimulation.Build(registry, [valid]);
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      simulation.Advance(TimeSpan.FromTicks(-1))));
    var overflowing = RideCircuitKinematicSimulation.Build(registry, [
      valid with { Speed = double.MaxValue },
    ]);
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      overflowing.Advance(TimeSpan.FromSeconds(2d))));
  }

  private static RideCircuitKinematicTrainSeed Seed(ulong instanceEntryId) =>
    new(instanceEntryId, 0, 0.1d, 1d, [0d]);

  private static RideInstanceTrackRuntimeRegistry Registry(params Outcome[] outcomes) {
    var tracks = outcomes.Select(outcome => Track(
      outcome.TrackEntryId,
      outcome.InstanceEntryId,
      outcome.Status switch {
        RideTrackGeometryStatus.Circuit => true,
        RideTrackGeometryStatus.OpenTrack => false,
        _ => null,
      })).ToArray();
    var instances = outcomes.Select(outcome =>
      Instance(outcome.InstanceEntryId, outcome.TrackEntryId)).ToArray();
    var identities = RideInstanceTrackGraph.Build(instances, tracks);
    var geometry = new RideTrackGeometryResolution(
      outcomes.Select((outcome, index) => new RideTrackGeometryLink(
        tracks[index],
        outcome.Status,
        outcome.Graph,
        outcome.Circuit)).ToArray(),
      outcomes.Count(outcome =>
        outcome.Status == RideTrackGeometryStatus.UnresolvedResources),
      outcomes.Count(outcome =>
        outcome.Status == RideTrackGeometryStatus.UnsupportedGeometry),
      outcomes.Count(outcome =>
        outcome.Status == RideTrackGeometryStatus.UnsupportedTopology));
    return RideInstanceTrackRuntimeRegistry.Build(identities, geometry);
  }

  private static DatTrackedRideInstanceData Instance(ulong entryId, ulong trackId) => new(
    entryId,
    $"Ride {entryId}",
    trackId,
    "Tracks\\Synthetic",
    "Synthetic:trr",
    nTrains: 1,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains: [entryId + 100]);

  private static RideTrack Track(
    ulong entryId,
    ulong instanceReference,
    bool? isCircuit
  ) => new(
    entryId,
    direction: 0,
    firstSegmentSourceEntryId: entryId + 100,
    lastSegmentSourceEntryId: entryId + 100,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: isCircuit != null,
    trackPieceSourceEntryIds: [entryId + 200],
    segmentSourceEntryIds: [entryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: instanceReference,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static TrackGraph Graph(float startX, float endX) {
    var start = new TrackNode($"start-{startX}");
    var end = new TrackNode($"end-{endX}");
    return new(
      [start, end],
      [new TrackEdge("edge", start, end, StraightPiece(startX, endX))]);
  }

  private static TrackPiece StraightPiece(float startX, float endX) =>
    new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, new Vector3(startX, 0f, 0f), Vector3.UnitX),
      Pair(1f, new Vector3(endX, 0f, 0f), Vector3.UnitX),
    ]));

  private static TrackCircuit Circuit(float offsetX) => new([
    new TrackCircuitPiece("first", HalfCircuit(offsetX, first: true)),
    new TrackCircuitPiece("second", HalfCircuit(offsetX, first: false)),
  ]);

  private static TrackPiece HalfCircuit(float offsetX, bool first) {
    var offset = Vector3.UnitX * offsetX;
    var start = offset + (first ? Vector3.UnitX : -Vector3.UnitX);
    var end = offset - (first ? Vector3.UnitX : -Vector3.UnitX);
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
      0f);
  }

  private sealed record Outcome(
    ulong TrackEntryId,
    ulong InstanceEntryId,
    RideTrackGeometryStatus Status,
    TrackGraph? Graph,
    TrackCircuit? Circuit
  ) {
    public static Outcome ForCircuit(
      ulong trackEntryId,
      ulong instanceEntryId,
      TrackCircuit circuit
    ) => new(
      trackEntryId,
      instanceEntryId,
      RideTrackGeometryStatus.Circuit,
      null,
      circuit);

    public static Outcome ForOpen(
      ulong trackEntryId,
      ulong instanceEntryId,
      TrackGraph graph
    ) => new(
      trackEntryId,
      instanceEntryId,
      RideTrackGeometryStatus.OpenTrack,
      graph,
      null);

    public static Outcome ForSkipped(ulong trackEntryId, ulong instanceEntryId) => new(
      trackEntryId,
      instanceEntryId,
      RideTrackGeometryStatus.UnresolvedResources,
      null,
      null);
  }
}
