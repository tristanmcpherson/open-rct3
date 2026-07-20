// Track Circuit Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackCircuitTests {
  [Test]
  public void Constructor_AcceptsClosedC1CircuitAndRetainsOrder() {
    var circuit = Circle();

    using (Assert.EnterMultipleScope()) {
      Assert.That(circuit.Pieces.Select(piece => piece.Id),
        Is.EqualTo(new[] { "north-east", "north-west", "south-west", "south-east" }));
      Assert.That(circuit.Length, Is.GreaterThan(0f));
      Assert.That(circuit.Pieces, Has.Count.EqualTo(4));
    }
  }

  [Test]
  public void Sample_LocatesPieceBoundariesAndClosedEndpoint() {
    var circuit = Circle();
    var secondStart = circuit.Pieces[0].Piece.Length;

    var start = circuit.Sample(0f);
    var second = circuit.Sample(secondStart);
    var closed = circuit.Sample(circuit.Length);

    using (Assert.EnterMultipleScope()) {
      Assert.That(start.PieceIndex, Is.Zero);
      Assert.That(start.PieceArcLength, Is.Zero);
      Assert.That(second.PieceIndex, Is.EqualTo(1));
      Assert.That(second.PieceArcLength, Is.Zero.Within(0.000001f));
      Assert.That(closed.PieceIndex, Is.EqualTo(3));
      Assert.That(closed.PieceArcLength,
        Is.EqualTo(circuit.Pieces[3].Piece.Length).Within(0.000001f));
      Assert.That(closed.ContactPoints.Midpoint.X,
        Is.EqualTo(start.ContactPoints.Midpoint.X).Within(0.000001f));
      Assert.That(closed.ContactPoints.Midpoint.Y,
        Is.EqualTo(start.ContactPoints.Midpoint.Y).Within(0.000001f));
    }
  }

  [Test]
  public void Constructor_RejectsDiscontinuousClosingJoin() {
    var pieces = Circle().Pieces.ToArray();
    pieces[^1] = new TrackCircuitPiece(
      pieces[^1].Id,
      Piece(new(0f, -1f, 0f), new(2f, 0f, 0f), Vector3.UnitX, Vector3.UnitY));

    Assert.Throws<ArgumentException>(new Action(() => new TrackCircuit(pieces)));
  }

  [Test]
  public void Constructor_RejectsDuplicateIdsAndOpenSinglePiece() {
    var pieces = Circle().Pieces.ToArray();
    pieces[1] = pieces[1] with { Id = pieces[0].Id };

    Assert.Throws<ArgumentException>(new Action(() => new TrackCircuit(pieces)));
    Assert.Throws<ArgumentException>(new Action(() => new TrackCircuit([
      new TrackCircuitPiece(
        "open",
        Piece(Vector3.Zero, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX)),
    ])));
  }

  [Test]
  public void Sample_RejectsNonFiniteAndOutOfRangeArcLengths() {
    var circuit = Circle();

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() => circuit.Sample(-0.1f)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      circuit.Sample(circuit.Length + 0.1f)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() => circuit.Sample(float.NaN)));
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
