// Ride Track Geometry Spatial Index Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackGeometrySpatialIndexTests {
  [Test]
  public void Build_IndexesOnlyResolvedTracksInDatOrderWithExactBounds() {
    var graph = Graph(0f, 2f);
    var circuit = Circuit(10f);
    var resolution = new RideTrackGeometryResolution(
      [
        Link(Track(700), RideTrackGeometryStatus.UnresolvedResources),
        Link(Track(701, false), RideTrackGeometryStatus.OpenTrack, graph: graph),
        Link(Track(702), RideTrackGeometryStatus.UnsupportedGeometry),
        Link(Track(703, true), RideTrackGeometryStatus.Circuit, circuit: circuit),
        Link(Track(704), RideTrackGeometryStatus.UnsupportedTopology),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 1,
      UnsupportedTopologyTrackCount: 1);

    var index = RideTrackGeometrySpatialIndex.Build(resolution);

    using (Assert.EnterMultipleScope()) {
      Assert.That(index.TrackCount, Is.EqualTo(5));
      Assert.That(index.ResolvedTrackCount, Is.EqualTo(2));
      Assert.That(index.UnresolvedResourceTrackCount, Is.EqualTo(1));
      Assert.That(index.UnsupportedGeometryTrackCount, Is.EqualTo(1));
      Assert.That(index.UnsupportedTopologyTrackCount, Is.EqualTo(1));
      Assert.That(index.Entries.Select(entry => entry.DatTrackIndex), Is.EqualTo(new[] { 1, 3 }));
      Assert.That(index.Entries.Select(entry => entry.TrackSourceEntryId),
        Is.EqualTo(new ulong[] { 701, 703 }));
      Assert.That(index.Entries.Select(entry => entry.Status), Is.EqualTo(new[] {
        RideTrackGeometryStatus.OpenTrack,
        RideTrackGeometryStatus.Circuit,
      }));
      Assert.That(index.Entries[0].Bounds,
        Is.EqualTo(TrackBoundsBuilder.FromGraph(graph)));
      Assert.That(index.Entries[0].Bounds.Min, Is.EqualTo(new Vector3(0f, -1f, 0f)));
      Assert.That(index.Entries[0].Bounds.Max, Is.EqualTo(new Vector3(2f, 1f, 0f)));
      Assert.That(index.Entries[1].Bounds,
        Is.EqualTo(TrackBoundsBuilder.FromCircuit(circuit)));
    }
  }

  [Test]
  public void Query_OrdersContainmentAndDistanceWithDatOrderBreakingTies() {
    var resolution = new RideTrackGeometryResolution(
      [
        Link(Track(700, false), RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
        Link(Track(701, false), RideTrackGeometryStatus.OpenTrack, graph: Graph(5f, 7f)),
        Link(Track(702, false), RideTrackGeometryStatus.OpenTrack, graph: Graph(5f, 7f)),
      ],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var index = RideTrackGeometrySpatialIndex.Build(resolution);

    var hits = index.Query(new Vector3(6f, 0f, 0f), maximumResults: 3);

    using (Assert.EnterMultipleScope()) {
      Assert.That(hits.Select(hit => hit.Entry.TrackSourceEntryId),
        Is.EqualTo(new ulong[] { 701, 702, 700 }));
      Assert.That(hits.Select(hit => hit.ContainsPoint),
        Is.EqualTo(new[] { true, true, false }));
      Assert.That(hits.Select(hit => hit.SquaredDistanceToBounds),
        Is.EqualTo(new[] { 0d, 0d, 16d }));
      Assert.That(index.Query(new Vector3(6f, 0f, 0f), 2).Count, Is.EqualTo(2));
    }
  }

  [Test]
  public void Build_UnionsSeparateMultiCircuitBoundsWithoutJoiningTraversals() {
    var track = MultiCircuitTrack();
    var first = Circuit(10f, 900, 901);
    var second = Circuit(30f, 902, 903);
    var link = new RideTrackGeometryLink(
      track,
      RideTrackGeometryStatus.MultiCircuit,
      Graph: null,
      Circuit: null) {
      SegmentCircuits = [
        new(800, Array.AsReadOnly(new ulong[] { 900, 901 }), first),
        new(801, Array.AsReadOnly(new ulong[] { 902, 903 }), second),
      ],
    };
    var resolution = new RideTrackGeometryResolution(
      [link],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    var entry = RideTrackGeometrySpatialIndex.Build(resolution).Entries.Single();
    var firstBounds = TrackBoundsBuilder.FromCircuit(first);
    var secondBounds = TrackBoundsBuilder.FromCircuit(second);

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Status, Is.EqualTo(RideTrackGeometryStatus.MultiCircuit));
      Assert.That(entry.Bounds.Min,
        Is.EqualTo(Vector3.Min(firstBounds.Min, secondBounds.Min)));
      Assert.That(entry.Bounds.Max,
        Is.EqualTo(Vector3.Max(firstBounds.Max, secondBounds.Max)));
      Assert.That(first, Is.Not.SameAs(second));
    }
  }

  [Test]
  public void Build_RejectsOutcomeCountsThatDoNotConserveTracks() {
    var resolution = new RideTrackGeometryResolution(
      [Link(Track(700), RideTrackGeometryStatus.UnresolvedResources)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometrySpatialIndex.Build(resolution)));

    Assert.That(exception!.Message, Does.Contain("do not conserve 1 tracks"));
  }

  [Test]
  public void BuildAndQuery_EnforceAggregateAndRequestBounds() {
    var resolution = new RideTrackGeometryResolution(
      [Link(Track(700, false), RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var requiredSamples = Convert.ToUInt64(
      resolution.Tracks.Single().Graph!.Edges.Single().Piece.BakedSampleCount) * 2ul;
    var insufficientSamples = new RideTrackGeometrySpatialIndexLimits(
      MaximumTrackCount: 1,
      MaximumPieceCount: 1,
      MaximumRailSampleCount: requiredSamples - 1ul,
      MaximumQueryResults: 1);
    var bounded = RideTrackGeometrySpatialIndex.Build(
      resolution,
      insufficientSamples with { MaximumRailSampleCount = requiredSamples });

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrackGeometrySpatialIndex.Build(resolution, insufficientSamples)));
    Assert.Throws<ArgumentException>(new Action(() =>
      bounded.Query(new Vector3(float.NaN, 0f, 0f), 1)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      bounded.Query(Vector3.Zero, 2)));
  }

  private static RideTrackGeometryLink Link(
    RideTrack track,
    RideTrackGeometryStatus status,
    TrackGraph? graph = null,
    TrackCircuit? circuit = null
  ) => new(track, status, graph, circuit);

  private static RideTrack Track(ulong sourceEntryId, bool? isCircuit = null) => new(
    sourceEntryId,
    direction: 0,
    firstSegmentSourceEntryId: sourceEntryId + 100,
    lastSegmentSourceEntryId: sourceEntryId + 100,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: isCircuit != null,
    trackPieceSourceEntryIds: [sourceEntryId + 200],
    segmentSourceEntryIds: [sourceEntryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: sourceEntryId + 300,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static RideTrack MultiCircuitTrack() => new(
    sourceEntryId: 700,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 801,
    isCircuit: null,
    prototype: true,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: [900, 901, 902, 903],
    segmentSourceEntryIds: [800, 801],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: 1_000,
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

  private static TrackCircuit Circuit(
    float offsetX,
    ulong firstPieceId,
    ulong secondPieceId
  ) => new([
    new TrackCircuitPiece(
      $"track-piece-{firstPieceId}",
      HalfCircuit(offsetX, first: true)),
    new TrackCircuitPiece(
      $"track-piece-{secondPieceId}",
      HalfCircuit(offsetX, first: false)),
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
