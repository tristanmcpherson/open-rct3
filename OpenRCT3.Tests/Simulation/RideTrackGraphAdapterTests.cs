// Ride Track Graph Adapter Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackGraphAdapterTests {
  [Test]
  public void Build_UsesAuthoritativePieceOrderAndResolvedGeometry() {
    var track = Track([500, 501]);
    var placements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };

    var graph = RideTrackGraphAdapter.Build(
      track,
      placements,
      placement => StraightPiece(placement.SourceEntryId == 500 ? 0f : 1f));

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.Continuity, Is.EqualTo(TrackGraphContinuity.ImportedPiecewise));
      Assert.That(graph.Nodes.Select(node => node.Id), Is.EqualTo(new[] {
        "track-700-boundary-0",
        "track-700-boundary-1",
        "track-700-boundary-2",
      }));
      Assert.That(graph.Edges.Select(edge => edge.Id), Is.EqualTo(new[] {
        "track-piece-500",
        "track-piece-501",
      }));
      Assert.That(graph.Edges[0].From, Is.SameAs(graph.Nodes[0]));
      Assert.That(graph.Edges[0].To, Is.SameAs(graph.Nodes[1]));
      Assert.That(graph.Edges[1].From, Is.SameAs(graph.Nodes[1]));
      Assert.That(graph.Edges[1].To, Is.SameAs(graph.Nodes[2]));
    }
  }

  [Test]
  public void Build_RejectsCircuitAndUnprovenOrders() {
    var placement = Placement(500, previous: 1697, next: 1697);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.Build(
        Track([500], isCircuit: true),
        [placement],
        _ => StraightPiece(0f))));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.Build(
        Track([500], hasSerializedOrder: false),
        [placement],
        _ => StraightPiece(0f))));
  }

  [Test]
  public void Build_AcceptsAuthoritativeLinkDerivedOrderWithoutSerializedList() {
    var placements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };
    var track = Track(
      [500, 501],
      hasSerializedOrder: false,
      hasAuthoritativeOrder: true);

    var graph = RideTrackGraphAdapter.Build(
      track,
      placements,
      placement => StraightPiece(placement.SourceEntryId == 500 ? 0f : 1f));

    Assert.That(graph.Edges.Select(edge => edge.Id), Is.EqualTo(new[] {
      "track-piece-500",
      "track-piece-501",
    }));
  }

  [Test]
  public void Build_RejectsNonReciprocalSerializedPieceOrder() {
    var track = Track([500, 501]);
    var placements = new[] {
      Placement(500, previous: 1697, next: 999),
      Placement(501, previous: 500, next: 1697),
    };

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.Build(track, placements, _ => StraightPiece(0f))));
  }

  [Test]
  public void Build_RejectsBoundaryLinkBackIntoTrack() {
    var track = Track([500]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.Build(
        track,
        [Placement(500, previous: 500, next: 1697)],
        _ => StraightPiece(0f))));
  }

  [Test]
  public void BuildCircuit_UsesSerializedCycleWithoutDuplicatingPieces() {
    var track = Track([500, 501], isCircuit: true);
    var placements = new[] {
      Placement(500, previous: 501, next: 501),
      Placement(501, previous: 500, next: 500),
    };

    var circuit = RideTrackGraphAdapter.BuildCircuit(
      track,
      placements,
      placement => HalfCircuit(placement.SourceEntryId == 500));

    using (Assert.EnterMultipleScope()) {
      Assert.That(circuit.Pieces.Select(piece => piece.Id),
        Is.EqualTo(new[] { "track-piece-500", "track-piece-501" }));
      Assert.That(circuit.Pieces, Has.Count.EqualTo(2));
      Assert.That(circuit.Continuity,
        Is.EqualTo(TrackCircuitContinuity.ImportedPiecewise));
      Assert.That(circuit.Sample(0f).ContactPoints.Midpoint,
        Is.EqualTo(circuit.Sample(circuit.Length).ContactPoints.Midpoint));
    }
  }

  [Test]
  public void BuildCircuit_PreservesNativePiecewiseSeamWithoutWeakeningPositionClosure() {
    var track = Track([500, 501], isCircuit: true);
    var placements = new[] {
      Placement(500, previous: 501, next: 501),
      Placement(501, previous: 500, next: 500),
    };
    TrackPiece CreatePiece(RideTrackPlacement placement, Vector3 offset) =>
      PiecewiseLoopPiece(placement.SourceEntryId == 500, offset);

    var strictPieces = new[] {
      new TrackCircuitPiece("track-piece-500", CreatePiece(placements[0], Vector3.Zero)),
      new TrackCircuitPiece("track-piece-501", CreatePiece(placements[1], Vector3.Zero)),
    };
    Assert.Throws<ArgumentException>(new Action(() => new TrackCircuit(strictPieces)));

    var circuit = RideTrackGraphAdapter.BuildCircuit(
      track,
      placements,
      placement => CreatePiece(placement, Vector3.Zero));

    using (Assert.EnterMultipleScope()) {
      Assert.That(circuit.Continuity,
        Is.EqualTo(TrackCircuitContinuity.ImportedPiecewise));
      Assert.That(circuit.Pieces, Has.Count.EqualTo(2));
      Assert.Throws<ArgumentException>(new Action(() =>
        RideTrackGraphAdapter.BuildCircuit(
          track,
          placements,
          placement => CreatePiece(
            placement,
            placement.SourceEntryId == 501
              ? new Vector3(0f, 0.01f, 0f)
              : Vector3.Zero))));
    }
  }

  [Test]
  public void BuildCircuits_KeepsClosedTrackSegmentsAsSeparateTraversals() {
    var track = Track(
      [500, 501, 600, 601],
      isCircuit: null,
      segmentIds: [800, 801]);
    var placements = new[] {
      Placement(500, previous: 501, next: 501, segmentId: 800),
      Placement(501, previous: 500, next: 500, segmentId: 800),
      Placement(600, previous: 601, next: 601, segmentId: 801),
      Placement(601, previous: 600, next: 600, segmentId: 801),
    };

    var circuits = RideTrackGraphAdapter.BuildCircuits(
      track,
      placements,
      placement => HalfCircuit(placement.SourceEntryId is 500 or 600));

    using (Assert.EnterMultipleScope()) {
      Assert.That(circuits.Select(circuit => circuit.SegmentSourceEntryId),
        Is.EqualTo(new ulong[] { 800, 801 }));
      Assert.That(circuits[0].PieceSourceEntryIds,
        Is.EqualTo(new ulong[] { 500, 501 }));
      Assert.That(circuits[1].PieceSourceEntryIds,
        Is.EqualTo(new ulong[] { 600, 601 }));
      Assert.That(circuits.Select(circuit => circuit.Circuit.Pieces.Count),
        Is.EqualTo(new[] { 2, 2 }));
      Assert.That(circuits[0].Circuit, Is.Not.SameAs(circuits[1].Circuit));
    }
  }

  [Test]
  public void BuildCircuits_GroupsEveryPieceWithOneLinearMembershipRead() {
    const int segmentCount = 512;
    var segmentIds = Enumerable.Range(0, segmentCount)
      .Select(index => 800ul + Convert.ToUInt64(index))
      .ToArray();
    var pieceIds = new List<ulong>(segmentCount * 2);
    var placements = new List<RideTrackPlacement>(segmentCount * 2);
    foreach (var index in Enumerable.Range(0, segmentCount)) {
      var segmentId = segmentIds[index];
      var firstId = 10_000ul + Convert.ToUInt64(index * 2);
      var secondId = firstId + 1ul;
      pieceIds.Add(firstId);
      pieceIds.Add(secondId);
      placements.Add(Placement(firstId, secondId, secondId, segmentId));
      placements.Add(Placement(secondId, firstId, firstId, segmentId));
    }
    var track = Track(
      pieceIds.ToArray(),
      isCircuit: null,
      segmentIds: segmentIds);
    var firstPiece = HalfCircuit(first: true);
    var secondPiece = HalfCircuit(first: false);
    var segmentGroupingReads = 0;
    var createPieceCalls = 0;

    var circuits = RideTrackGraphAdapter.BuildCircuits(
      track,
      placements,
      placement => {
        createPieceCalls++;
        return (placement.SourceEntryId - 10_000ul) % 2ul == 0ul
          ? firstPiece
          : secondPiece;
      },
      placement => {
        segmentGroupingReads++;
        return placement.SegmentReference;
      });

    using (Assert.EnterMultipleScope()) {
      Assert.That(segmentGroupingReads, Is.EqualTo(placements.Count));
      Assert.That(createPieceCalls, Is.EqualTo(placements.Count));
      Assert.That(circuits, Has.Count.EqualTo(segmentCount));
      Assert.That(circuits.SelectMany(circuit => circuit.PieceSourceEntryIds),
        Is.EqualTo(pieceIds));
    }
  }

  [Test]
  public void BuildCircuits_RejectsUnknownMissingAndNoncontiguousSegmentMembership() {
    var twoSegments = new ulong[] { 800, 801 };
    var twoPieces = new ulong[] { 500, 501 };
    var unknown = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.BuildCircuits(
        Track(twoPieces, isCircuit: null, segmentIds: twoSegments),
        [
          Placement(500, 501, 501, segmentId: 800),
          Placement(501, 500, 500, segmentId: 802),
        ],
        _ => HalfCircuit(first: true))));
    var missing = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.BuildCircuits(
        Track(twoPieces, isCircuit: null, segmentIds: twoSegments),
        [
          Placement(500, 501, 501, segmentId: 800),
          Placement(501, 500, 500, segmentId: 800),
        ],
        _ => HalfCircuit(first: true))));
    var noncontiguous = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.BuildCircuits(
        Track([500, 600, 501, 601], isCircuit: null, segmentIds: twoSegments),
        [
          Placement(500, 501, 501, segmentId: 800),
          Placement(501, 500, 500, segmentId: 800),
          Placement(600, 601, 601, segmentId: 801),
          Placement(601, 600, 600, segmentId: 801),
        ],
        _ => HalfCircuit(first: true))));

    using (Assert.EnterMultipleScope()) {
      Assert.That(unknown!.Message, Does.Contain("outside the track"));
      Assert.That(missing!.Message, Does.Contain("contains no TrackPiece references"));
      Assert.That(noncontiguous!.Message,
        Does.Contain("does not retain contiguous TrackSegment order"));
    }
  }

  [Test]
  public void Build_RejectsMissingOrDiscontinuousGeometry() {
    var track = Track([500, 501]);
    var placements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGraphAdapter.Build(track, placements, _ => null)));
    Assert.Throws<ArgumentException>(new Action(() =>
      RideTrackGraphAdapter.Build(
        track,
        placements,
        placement => StraightPiece(placement.SourceEntryId == 500 ? 0f : 2f))));
  }

  [Test]
  public void Build_PreservesNativePiecewiseFramesWithoutWeakeningPositionClosure() {
    var track = Track([500, 501]);
    var placements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };
    TrackPiece CreatePiece(RideTrackPlacement placement, Vector3 offset) =>
      placement.SourceEntryId == 500
        ? StraightPiece(0f, Vector3.UnitX)
        : StraightPiece(
          1f + offset.X,
          Vector3.Normalize(new Vector3(1f, 0.1f, 0f)) * 5f);

    Assert.Throws<ArgumentException>(new Action(() => new TrackGraph(
      [new TrackNode("first"), new TrackNode("join"), new TrackNode("last")],
      [
        new TrackEdge("first", new TrackNode("first"), new TrackNode("join"),
          CreatePiece(placements[0], Vector3.Zero)),
        new TrackEdge("second", new TrackNode("join"), new TrackNode("last"),
          CreatePiece(placements[1], Vector3.Zero)),
      ])));

    var graph = RideTrackGraphAdapter.Build(
      track,
      placements,
      placement => CreatePiece(placement, Vector3.Zero));

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.Continuity, Is.EqualTo(TrackGraphContinuity.ImportedPiecewise));
      Assert.That(graph.Edges, Has.Count.EqualTo(2));
      Assert.Throws<ArgumentException>(new Action(() => RideTrackGraphAdapter.Build(
        track,
        placements,
        placement => CreatePiece(
          placement,
          placement.SourceEntryId == 501 ? new Vector3(0.01f, 0f, 0f) : Vector3.Zero))));
    }
  }

  private static RideTrack Track(
    ulong[] pieceIds,
    bool? isCircuit = false,
    bool hasSerializedOrder = true,
    bool? hasAuthoritativeOrder = null,
    ulong[]? segmentIds = null
  ) => new(
    sourceEntryId: 700,
    direction: 0,
    firstSegmentSourceEntryId: (segmentIds ?? [800UL])[0],
    lastSegmentSourceEntryId: (segmentIds ?? [800UL])[^1],
    isCircuit,
    prototype: false,
    hasSerializedOrder,
    pieceIds,
    segmentSourceEntryIds: segmentIds ?? [800],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: 900,
    flippedTrackSections: null,
    tunnelLightColour: null,
    hasAuthoritativeTrackPieceOrder: hasAuthoritativeOrder);

  private static RideTrackPlacement Placement(
    ulong sourceEntryId,
    ulong previous,
    ulong next,
    ulong segmentId = 800
  ) => new(
    sourceEntryId,
    sceneryPlacementSourceEntryId: sourceEntryId + 100,
    sidDatabaseEntryReference: sourceEntryId + 200,
    symbolName: $"Piece{sourceEntryId}:tks",
    objectKey: $"Piece{sourceEntryId}",
    overlayPath: "Tracks\\Test",
    tileX: 0,
    tileY: 0,
    rotation: Edge.West,
    serializedDirection: 0,
    serializedHeight: 0,
    corner: 0,
    ownerReference: segmentId,
    segmentReference: segmentId,
    previousPieceReference: previous,
    nextPieceReference: next,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0);

  private static TrackPiece StraightPiece(
    float offset,
    Vector3? tangent = null
  ) {
    var railTangent = tangent ?? Vector3.UnitX;
    var geometry = TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        new Vector3(0f, -0.5f, 0f),
        railTangent,
        new Vector3(0f, 0.5f, 0f),
        railTangent,
        0f),
      new RailControlPair(
        1f,
        new Vector3(1f, -0.5f, 0f),
        railTangent,
        new Vector3(1f, 0.5f, 0f),
        railTangent,
        0f),
    ]);
    return new TrackPiece(geometry, Matrix4x4.CreateTranslation(offset, 0f, 0f));
  }

  private static TrackPiece HalfCircuit(bool first) {
    var start = first ? Vector3.UnitX : -Vector3.UnitX;
    var end = -start;
    var startTangent = first ? Vector3.UnitY * 3f : -Vector3.UnitY * 3f;
    var endTangent = -startTangent;
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

  private static TrackPiece PiecewiseLoopPiece(bool first, Vector3 startOffset) {
    var start = first ? Vector3.Zero : Vector3.UnitX + startOffset;
    var end = first ? Vector3.UnitX : Vector3.Zero;
    var startTangent = first ? Vector3.UnitX : Vector3.UnitY;
    var endTangent = first ? Vector3.UnitX : -Vector3.UnitX;
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
}
