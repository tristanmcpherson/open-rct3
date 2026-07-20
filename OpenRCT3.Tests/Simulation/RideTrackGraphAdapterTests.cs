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
  public void Build_RejectsCircuitAndExpansionDerivedOrders() {
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
      Assert.That(circuit.Sample(0f).ContactPoints.Midpoint,
        Is.EqualTo(circuit.Sample(circuit.Length).ContactPoints.Midpoint));
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

  private static RideTrack Track(
    ulong[] pieceIds,
    bool? isCircuit = false,
    bool hasSerializedOrder = true
  ) => new(
    sourceEntryId: 700,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 800,
    isCircuit,
    prototype: false,
    hasSerializedOrder,
    pieceIds,
    segmentSourceEntryIds: [800],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: 900,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static RideTrackPlacement Placement(
    ulong sourceEntryId,
    ulong previous,
    ulong next
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
    ownerReference: 800,
    segmentReference: 800,
    previousPieceReference: previous,
    nextPieceReference: next,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0);

  private static TrackPiece StraightPiece(float offset) {
    var geometry = TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        new Vector3(0f, -0.5f, 0f),
        Vector3.UnitX,
        new Vector3(0f, 0.5f, 0f),
        Vector3.UnitX,
        0f),
      new RailControlPair(
        1f,
        new Vector3(1f, -0.5f, 0f),
        Vector3.UnitX,
        new Vector3(1f, 0.5f, 0f),
        Vector3.UnitX,
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
}
