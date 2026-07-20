// Track Circuit Traversal Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackCircuitTraversalTests {
  [Test]
  public void Constructor_ExposesExactStartAndClosedEndpointIdentities() {
    var circuit = Circle();
    var traversal = new TrackCircuitTraversal(circuit);

    using (Assert.EnterMultipleScope()) {
      Assert.That(traversal.Length,
        Is.EqualTo(circuit.Pieces.Sum(piece => Convert.ToDouble(piece.Piece.Length))));
      Assert.That(traversal.Start.IsInitialized, Is.True);
      Assert.That(traversal.Start.PieceIndex, Is.Zero);
      Assert.That(traversal.Start.PieceArcLength, Is.Zero);
      Assert.That(traversal.ClosedEndpoint.PieceIndex, Is.EqualTo(3));
      Assert.That(traversal.ClosedEndpoint.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(circuit.Pieces[3].Piece.Length)));
      Assert.That(traversal.ClosedEndpoint.CircuitArcLength,
        Is.EqualTo(traversal.Length));
    }
  }

  [Test]
  public void AtCircuitArcLength_PreservesCircuitBoundaryConventions() {
    var circuit = Circle();
    var traversal = new TrackCircuitTraversal(circuit);
    var secondStart = circuit.Pieces[0].Piece.Length;

    var secondEntry = traversal.AtCircuitArcLength(secondStart);
    var closedEndpoint = traversal.AtCircuitArcLength(circuit.Length);

    using (Assert.EnterMultipleScope()) {
      Assert.That(secondEntry.PieceIndex, Is.EqualTo(1));
      Assert.That(secondEntry.PieceArcLength, Is.Zero);
      Assert.That(closedEndpoint.PieceIndex, Is.EqualTo(3));
      Assert.That(closedEndpoint.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(circuit.Pieces[3].Piece.Length)));
    }
  }

  [Test]
  public void Advance_PreservesDirectionalIdentityAtExactPieceBoundaries() {
    var traversal = new TrackCircuitTraversal(Circle());
    var firstLength = Convert.ToDouble(traversal.Circuit.Pieces[0].Piece.Length);
    var secondLength = Convert.ToDouble(traversal.Circuit.Pieces[1].Piece.Length);

    var firstExit = traversal.Start.Advance(firstLength);
    var insideSecond = firstExit.Advance(secondLength * 0.25d);
    var secondEntry = traversal.AtPiece(1, secondLength * 0.25d)
      .Advance(-(secondLength * 0.25d));
    var beforeSecond = traversal.AtPiece(1, 0d).Advance(-(firstLength * 0.25d));

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstExit.PieceIndex, Is.Zero);
      Assert.That(firstExit.PieceArcLength, Is.EqualTo(firstLength));
      Assert.That(insideSecond.PieceIndex, Is.EqualTo(1));
      Assert.That(insideSecond.PieceArcLength, Is.EqualTo(secondLength * 0.25d));
      Assert.That(secondEntry.PieceIndex, Is.EqualTo(1));
      Assert.That(secondEntry.PieceArcLength, Is.Zero);
      Assert.That(beforeSecond.PieceIndex, Is.Zero);
      Assert.That(beforeSecond.PieceArcLength,
        Is.EqualTo(firstLength * 0.75d).Within(0.000000000001d));
    }
  }

  [Test]
  public void Advance_WrapsBothDirectionsAndReducesWholeLaps() {
    var traversal = new TrackCircuitTraversal(Circle());
    var lastIndex = traversal.Circuit.Pieces.Count - 1;
    var lastLength = Convert.ToDouble(traversal.Circuit.Pieces[lastIndex].Piece.Length);
    var delta = Math.Min(0.125d, lastLength * 0.25d);
    var middle = traversal.AtPiece(2, traversal.Circuit.Pieces[2].Piece.Length * 0.5d);

    var backwardWrap = traversal.Start.Advance(-delta);
    var forwardWrap = traversal.ClosedEndpoint.Advance(delta);
    var hugeForward = middle.Advance((traversal.Length * 1024d) + delta);
    var reducedForward = middle.Advance(delta);

    using (Assert.EnterMultipleScope()) {
      Assert.That(backwardWrap.PieceIndex, Is.EqualTo(lastIndex));
      Assert.That(backwardWrap.PieceArcLength,
        Is.EqualTo(lastLength - delta).Within(0.000000000001d));
      Assert.That(forwardWrap.PieceIndex, Is.Zero);
      Assert.That(forwardWrap.PieceArcLength, Is.EqualTo(delta));
      Assert.That(middle.Advance(traversal.Length), Is.EqualTo(middle));
      Assert.That(middle.Advance(-traversal.Length), Is.EqualTo(middle));
      Assert.That(hugeForward.PieceIndex, Is.EqualTo(reducedForward.PieceIndex));
      Assert.That(hugeForward.PieceArcLength,
        Is.EqualTo(reducedForward.PieceArcLength).Within(0.000000001d));
    }
  }

  [Test]
  public void Sample_UsesRetainedPieceLocalIdentityWithoutSpatialIntegration() {
    var traversal = new TrackCircuitTraversal(Circle());
    var firstPiece = traversal.Circuit.Pieces[0];
    var firstExit = traversal.AtPiece(0, firstPiece.Piece.Length);
    var expected = firstPiece.Piece.SampleContactPoints(firstPiece.Piece.Length);

    var sample = firstExit.Sample();
    var repeated = firstExit.Sample();
    var followingEntry = traversal.AtPiece(1, 0d).Sample();

    using (Assert.EnterMultipleScope()) {
      Assert.That(sample.PieceIndex, Is.Zero);
      Assert.That(sample.CircuitPiece, Is.SameAs(firstPiece));
      Assert.That(sample.PieceArcLength, Is.EqualTo(firstPiece.Piece.Length));
      Assert.That(sample.ContactPoints, Is.EqualTo(expected));
      Assert.That(repeated, Is.EqualTo(sample));
      Assert.That(followingEntry.PieceIndex, Is.EqualTo(1));
      Assert.That(followingEntry.PieceArcLength, Is.Zero);
      Assert.That(followingEntry.ContactPoints.Midpoint.X,
        Is.EqualTo(sample.ContactPoints.Midpoint.X).Within(0.000001f));
      Assert.That(followingEntry.ContactPoints.Midpoint.Y,
        Is.EqualTo(sample.ContactPoints.Midpoint.Y).Within(0.000001f));
    }
  }

  [Test]
  public void Advance_RetainsSubFloatDistanceUntilSampling() {
    var traversal = new TrackCircuitTraversal(Circle());
    const double step = 0.000000001d;
    var repeated = traversal.Start;
    foreach (var _ in Enumerable.Range(0, 1_000))
      repeated = repeated.Advance(step);
    var once = traversal.Start.Advance(step * 1_000d);

    using (Assert.EnterMultipleScope()) {
      Assert.That(repeated.PieceIndex, Is.EqualTo(once.PieceIndex));
      Assert.That(repeated.PieceArcLength,
        Is.EqualTo(once.PieceArcLength).Within(0.000000000001d));
      Assert.That(repeated.Sample().ContactPoints.Midpoint.X,
        Is.EqualTo(once.Sample().ContactPoints.Midpoint.X));
      Assert.That(repeated.Sample().ContactPoints.Midpoint.Y,
        Is.EqualTo(once.Sample().ContactPoints.Midpoint.Y));
    }
  }

  [Test]
  public void Api_RejectsInvalidPositionsDistancesAndForeignCursors() {
    var traversal = new TrackCircuitTraversal(Circle());
    var foreign = new TrackCircuitTraversal(Circle());
    var defaultCursor = default(TrackCircuitCursor);

    Assert.Throws<ArgumentNullException>(new Action(() =>
      _ = new TrackCircuitTraversal(null!)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.AtPiece(-1, 0d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.AtPiece(traversal.Circuit.Pieces.Count, 0d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.AtPiece(0, -0.1d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.AtPiece(0, double.NaN)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.Start.Advance(double.PositiveInfinity)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      traversal.Start.Advance(double.NaN)));
    Assert.Throws<ArgumentException>(new Action(() =>
      foreign.Advance(traversal.Start, 1d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      defaultCursor.Advance(1d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      defaultCursor.Sample()));
  }

  private static TrackCircuit Circle() {
    var scale = 1.5f;
    return new TrackCircuit([
      new TrackCircuitPiece(
        "north-east",
        Piece(Vector3.UnitX, Vector3.UnitY, Vector3.UnitY * scale, -Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "north-west",
        Piece(Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitX * scale, -Vector3.UnitY * scale)),
      new TrackCircuitPiece(
        "south-west",
        Piece(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitY * scale, Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "south-east",
        Piece(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitX * scale, Vector3.UnitY * scale)),
    ]);
  }

  private static TrackPiece Piece(
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
}
