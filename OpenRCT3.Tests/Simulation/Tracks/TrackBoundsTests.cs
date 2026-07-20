// Track Bounds Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackBoundsTests {
  [Test]
  public void FromPiece_IncludesBothRailsWithoutGuessedExpansion() {
    var piece = Piece(
      Vector3.Zero,
      new Vector3(10f, 0f, 0f),
      new Vector3(10f, 0f, 0f),
      new Vector3(10f, 0f, 0f),
      leftOffset: new Vector3(0f, -2f, -1f),
      rightOffset: new Vector3(0f, 3f, 1f));

    var bounds = TrackBoundsBuilder.FromPiece(piece);

    using (Assert.EnterMultipleScope()) {
      AssertVector(bounds.Min, new Vector3(0f, -2f, -1f));
      AssertVector(bounds.Max, new Vector3(10f, 3f, 1f));
      AssertVector(bounds.Center, new Vector3(5f, 0.5f, 0f));
    }
  }

  [Test]
  public void FromPiece_IncludesRetainedInteriorCurveExtrema() {
    var piece = Piece(
      Vector3.Zero,
      new Vector3(10f, 0f, 0f),
      new Vector3(10f, 20f, 0f),
      new Vector3(10f, -20f, 0f),
      leftOffset: -Vector3.UnitZ,
      rightOffset: Vector3.UnitZ);

    var bounds = TrackBoundsBuilder.FromPiece(piece);

    using (Assert.EnterMultipleScope()) {
      Assert.That(piece.BakedSampleCount, Is.GreaterThan(2));
      AssertVector(bounds.Min, new Vector3(0f, 0f, -1f));
      AssertVector(bounds.Max, new Vector3(10f, 5f, 1f));
    }
  }

  [Test]
  public void FromGraph_UnionsEveryEdgePiece() {
    var first = new TrackNode("first");
    var join = new TrackNode("join");
    var last = new TrackNode("last");
    var firstPiece = StraightPiece(0f, 10f);
    var secondPiece = StraightPiece(10f, 20f);
    var graph = new TrackGraph(
      [first, join, last],
      [
        new TrackEdge("first-edge", first, join, firstPiece),
        new TrackEdge("second-edge", join, last, secondPiece),
      ]);

    var bounds = TrackBoundsBuilder.FromGraph(graph);

    using (Assert.EnterMultipleScope()) {
      AssertVector(bounds.Min, new Vector3(0f, -1f, 0f));
      AssertVector(bounds.Max, new Vector3(20f, 1f, 0f));
    }
  }

  [Test]
  public void FromCircuit_UnionsEveryClosedPiece() {
    var circuit = Circle();

    var bounds = TrackBoundsBuilder.FromCircuit(circuit);

    using (Assert.EnterMultipleScope()) {
      AssertVector(bounds.Min, new Vector3(-1f, -1f, -0.5f));
      AssertVector(bounds.Max, new Vector3(1f, 1f, 0.5f));
      AssertVector(bounds.Center, Vector3.Zero);
    }
  }

  [Test]
  public void CompositeAndPieceBudgetsFailBeforeUnboundedSampling() {
    var first = new TrackNode("first");
    var join = new TrackNode("join");
    var last = new TrackNode("last");
    var firstPiece = StraightPiece(0f, 10f);
    var secondPiece = StraightPiece(10f, 20f);
    var graph = new TrackGraph(
      [first, join, last],
      [
        new TrackEdge("first-edge", first, join, firstPiece),
        new TrackEdge("second-edge", join, last, secondPiece),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackBoundsBuilder.FromPiece(
        firstPiece,
        new TrackBoundsLimits(1, 3))));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackBoundsBuilder.FromGraph(
        graph,
        new TrackBoundsLimits(1, 100))));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackBoundsBuilder.FromGraph(
        graph,
        new TrackBoundsLimits(10, 7))));
  }

  [Test]
  public void Api_RejectsNullAndEmptyComposites() {
    var isolated = new TrackNode("isolated");
    var empty = new TrackGraph([isolated], []);

    Assert.Throws<ArgumentNullException>(new Action(() =>
      TrackBoundsBuilder.FromPiece(null!)));
    Assert.Throws<ArgumentNullException>(new Action(() =>
      TrackBoundsBuilder.FromGraph(null!)));
    Assert.Throws<ArgumentNullException>(new Action(() =>
      TrackBoundsBuilder.FromCircuit(null!)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackBoundsBuilder.FromGraph(empty)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TrackBoundsBuilder.FromPiece(
        StraightPiece(0f, 10f),
        new TrackBoundsLimits(0, 1))));
  }

  private static TrackPiece StraightPiece(float startX, float endX) => Piece(
    new Vector3(startX, 0f, 0f),
    new Vector3(endX, 0f, 0f),
    new Vector3(endX - startX, 0f, 0f),
    new Vector3(endX - startX, 0f, 0f),
    leftOffset: -Vector3.UnitY,
    rightOffset: Vector3.UnitY);

  private static TrackCircuit Circle() {
    var scale = 1.5f;
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new TrackCircuit([
      new TrackCircuitPiece(
        "north-east",
        Piece(
          Vector3.UnitX,
          Vector3.UnitY,
          Vector3.UnitY * scale,
          -Vector3.UnitX * scale,
          -halfGauge,
          halfGauge)),
      new TrackCircuitPiece(
        "north-west",
        Piece(
          Vector3.UnitY,
          -Vector3.UnitX,
          -Vector3.UnitX * scale,
          -Vector3.UnitY * scale,
          -halfGauge,
          halfGauge)),
      new TrackCircuitPiece(
        "south-west",
        Piece(
          -Vector3.UnitX,
          -Vector3.UnitY,
          -Vector3.UnitY * scale,
          Vector3.UnitX * scale,
          -halfGauge,
          halfGauge)),
      new TrackCircuitPiece(
        "south-east",
        Piece(
          -Vector3.UnitY,
          Vector3.UnitX,
          Vector3.UnitX * scale,
          Vector3.UnitY * scale,
          -halfGauge,
          halfGauge)),
    ]);
  }

  private static TrackPiece Piece(
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent,
    Vector3 leftOffset,
    Vector3 rightOffset
  ) => new(TrackPieceGeometry.FromHandAuthored([
    new RailControlPair(
      0f,
      start + leftOffset,
      startTangent,
      start + rightOffset,
      startTangent,
      0f),
    new RailControlPair(
      1f,
      end + leftOffset,
      endTangent,
      end + rightOffset,
      endTangent,
      0f),
  ]));

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
    }
  }
}
