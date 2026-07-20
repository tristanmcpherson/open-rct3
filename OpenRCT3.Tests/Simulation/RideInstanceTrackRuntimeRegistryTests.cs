// Ride Instance Track Runtime Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceTrackRuntimeRegistryTests {
  [Test]
  public void Build_ComposesExactIdentitiesInInstanceOrderAndGatesTraversals() {
    var openTrack = Track(700, 900, isCircuit: false);
    var circuitTrack = Track(701, 901, isCircuit: true);
    var unresolvedTrack = Track(702, 902);
    var unsupportedGeometryTrack = Track(703, 903);
    var unsupportedTopologyTrack = Track(704, 904);
    var instances = new[] {
      Instance(903, 703),
      Instance(900, 700),
      Instance(902, 702),
      Instance(901, 701),
      Instance(904, 704),
    };
    var identities = RideInstanceTrackGraph.Build(
      instances,
      [
        unresolvedTrack,
        circuitTrack,
        unsupportedTopologyTrack,
        openTrack,
        unsupportedGeometryTrack,
      ]);
    var openGraph = Graph(0f, 2f);
    var circuit = Circuit(10f);
    var geometry = new RideTrackGeometryResolution(
      [
        Link(circuitTrack, RideTrackGeometryStatus.Circuit, circuit: circuit),
        Link(unsupportedTopologyTrack, RideTrackGeometryStatus.UnsupportedTopology),
        Link(openTrack, RideTrackGeometryStatus.OpenTrack, graph: openGraph),
        Link(unsupportedGeometryTrack, RideTrackGeometryStatus.UnsupportedGeometry),
        Link(unresolvedTrack, RideTrackGeometryStatus.UnresolvedResources),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 1,
      UnsupportedTopologyTrackCount: 1);

    var registry = RideInstanceTrackRuntimeRegistry.Build(identities, geometry);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.InstanceCount, Is.EqualTo(5));
      Assert.That(registry.ResolvedTrackCount, Is.EqualTo(2));
      Assert.That(registry.OpenTrackCount, Is.EqualTo(1));
      Assert.That(registry.CircuitTrackCount, Is.EqualTo(1));
      Assert.That(registry.UnresolvedResourceTrackCount, Is.EqualTo(1));
      Assert.That(registry.UnsupportedGeometryTrackCount, Is.EqualTo(1));
      Assert.That(registry.UnsupportedTopologyTrackCount, Is.EqualTo(1));
      Assert.That(registry.Entries.Select(entry => entry.DatInstanceIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
      Assert.That(registry.Entries.Select(entry => entry.InstanceEntryId),
        Is.EqualTo(new ulong[] { 903, 900, 902, 901, 904 }));
      Assert.That(registry.Entries.Select(entry => entry.TrackEntryId),
        Is.EqualTo(new ulong[] { 703, 700, 702, 701, 704 }));
      Assert.That(registry.Entries.Select(entry => entry.DatTrackIndex),
        Is.EqualTo(new[] { 3, 2, 4, 0, 1 }));
      Assert.That(registry.Entries.Select(entry => entry.Status), Is.EqualTo(new[] {
        RideTrackGeometryStatus.UnsupportedGeometry,
        RideTrackGeometryStatus.OpenTrack,
        RideTrackGeometryStatus.UnresolvedResources,
        RideTrackGeometryStatus.Circuit,
        RideTrackGeometryStatus.UnsupportedTopology,
      }));
      Assert.That(registry.Entries[0].IsResolved, Is.False);
      Assert.That(registry.Entries[0].GraphTraversal, Is.Null);
      Assert.That(registry.Entries[0].CircuitTraversal, Is.Null);
      Assert.That(registry.Entries[1].Track, Is.SameAs(openTrack));
      Assert.That(registry.Entries[1].Graph, Is.SameAs(openGraph));
      Assert.That(registry.Entries[1].GraphTraversal!.Graph, Is.SameAs(openGraph));
      Assert.That(registry.Entries[1].CircuitTraversal, Is.Null);
      Assert.That(registry.Entries[3].Track, Is.SameAs(circuitTrack));
      Assert.That(registry.Entries[3].Circuit, Is.SameAs(circuit));
      Assert.That(registry.Entries[3].CircuitTraversal!.Circuit, Is.SameAs(circuit));
      Assert.That(registry.Entries[3].GraphTraversal, Is.Null);
      Assert.That(registry.Entries[4].IsResolved, Is.False);
    }
  }

  [Test]
  public void Build_RejectsTrackObjectSubstitutionEvenWhenIdsMatch() {
    var identityTrack = Track(700, 900, isCircuit: false);
    var substitutedTrack = Track(700, 900, isCircuit: false);
    var identities = RideInstanceTrackGraph.Build(
      [Instance(900, 700)],
      [identityTrack]);
    var geometry = new RideTrackGeometryResolution(
      [Link(
        substitutedTrack,
        RideTrackGeometryStatus.OpenTrack,
        graph: Graph(0f, 2f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackRuntimeRegistry.Build(identities, geometry)));

    Assert.That(exception!.Message, Does.Contain("changed exact semantic object identity"));
  }

  [Test]
  public void Build_RejectsTrackCountAndOutcomeConservationFailures() {
    var track = Track(700, 900);
    var identities = RideInstanceTrackGraph.Build(
      [Instance(900, 700)],
      [track]);
    var missingTrack = new RideTrackGeometryResolution(
      [],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var badOutcomes = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.UnresolvedResources)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackRuntimeRegistry.Build(identities, missingTrack)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackRuntimeRegistry.Build(identities, badOutcomes)));
  }

  [Test]
  public void Build_EnforcesEntryAndTraversalPieceBounds() {
    var firstTrack = Track(700, 900, isCircuit: false);
    var secondTrack = Track(701, 901, isCircuit: false);
    var twoIdentities = RideInstanceTrackGraph.Build(
      [Instance(900, 700), Instance(901, 701)],
      [firstTrack, secondTrack]);
    var twoGeometry = new RideTrackGeometryResolution(
      [
        Link(firstTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
        Link(secondTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(2f, 4f)),
      ],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var circuitTrack = Track(702, 902, isCircuit: true);
    var circuitIdentity = RideInstanceTrackGraph.Build(
      [Instance(902, 702)],
      [circuitTrack]);
    var circuitGeometry = new RideTrackGeometryResolution(
      [Link(
        circuitTrack,
        RideTrackGeometryStatus.Circuit,
        circuit: Circuit(10f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideInstanceTrackRuntimeRegistry.Build(
        twoIdentities,
        twoGeometry,
        new(MaximumEntryCount: 1, MaximumPieceCount: 10))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideInstanceTrackRuntimeRegistry.Build(
        circuitIdentity,
        circuitGeometry,
        new(MaximumEntryCount: 1, MaximumPieceCount: 1))));
  }

  private static RideTrackGeometryLink Link(
    RideTrack track,
    RideTrackGeometryStatus status,
    TrackGraph? graph = null,
    TrackCircuit? circuit = null
  ) => new(track, status, graph, circuit);

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
    bool? isCircuit = null
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
}
