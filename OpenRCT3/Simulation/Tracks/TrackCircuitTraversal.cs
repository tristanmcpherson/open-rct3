// Track Circuit Traversal
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>An immutable vehicle position bound to one <see cref="TrackCircuitTraversal"/>.</summary>
/// <remarks>
/// Piece identity is authoritative at a shared boundary. A piece exit and the following piece entry
/// are geometrically coincident but remain distinct cursor values so direction changes do not lose
/// the side of the seam from which the vehicle arrived.
/// </remarks>
public readonly struct TrackCircuitCursor : IEquatable<TrackCircuitCursor> {
  private readonly TrackCircuitTraversal? traversal;

  /// <summary>The exact ordered circuit-piece index retained by this cursor.</summary>
  public int PieceIndex { get; }

  /// <summary>The piece-local centerline arc length retained in double precision.</summary>
  public double PieceArcLength { get; }

  /// <summary>Whether this value was created by a traversal rather than default initialization.</summary>
  public bool IsInitialized => traversal != null;

  /// <summary>The circuit to which this cursor is bound.</summary>
  public TrackCircuit Circuit => Owner.Circuit;

  /// <summary>The exact circuit piece retained at this position.</summary>
  public TrackCircuitPiece CircuitPiece => Circuit.Pieces[PieceIndex];

  /// <summary>The double-precision circuit arc implied by the exact piece-local identity.</summary>
  public double CircuitArcLength => Owner.GetCircuitArcLength(this);

  internal TrackCircuitTraversal? Traversal => traversal;

  internal TrackCircuitCursor(
    TrackCircuitTraversal traversal,
    int pieceIndex,
    double pieceArcLength
  ) {
    this.traversal = traversal;
    PieceIndex = pieceIndex;
    PieceArcLength = pieceArcLength;
  }

  /// <summary>Returns a new cursor advanced by a signed distance around the closed circuit.</summary>
  public TrackCircuitCursor Advance(double distance) => Owner.Advance(this, distance);

  /// <summary>Samples both contact rails from this cursor's exact piece-local identity.</summary>
  public TrackCircuitSample Sample() => Owner.Sample(this);

  public bool Equals(TrackCircuitCursor other) =>
    ReferenceEquals(traversal, other.traversal) &&
    PieceIndex == other.PieceIndex &&
    PieceArcLength.Equals(other.PieceArcLength);

  public override bool Equals(object? obj) =>
    obj is TrackCircuitCursor other && Equals(other);

  public override int GetHashCode() => HashCode.Combine(
    traversal,
    PieceIndex,
    PieceArcLength);

  public static bool operator ==(TrackCircuitCursor left, TrackCircuitCursor right) =>
    left.Equals(right);

  public static bool operator !=(TrackCircuitCursor left, TrackCircuitCursor right) =>
    !left.Equals(right);

  private TrackCircuitTraversal Owner => traversal
    ?? throw new InvalidOperationException("The track-circuit cursor is not initialized.");
}

/// <summary>Creates and advances immutable vehicle cursors over one closed track circuit.</summary>
/// <remarks>
/// Advancement operates only on scalar arc lengths. Contact positions are always resampled from the
/// owning <see cref="TrackPiece"/> bake, so repeated sampling never integrates world-space error.
/// A move that ends exactly on a seam retains the departing piece's exit when moving forward and the
/// current piece's entry when moving backward. Only distance beyond the seam changes piece identity.
/// </remarks>
public sealed class TrackCircuitTraversal {
  private readonly double[] pieceStarts;

  public TrackCircuit Circuit { get; }

  /// <summary>The exact double-precision sum of the circuit's piece lengths.</summary>
  public double Length => pieceStarts[^1];

  /// <summary>The first piece's entry identity.</summary>
  public TrackCircuitCursor Start => new(this, 0, 0d);

  /// <summary>The final piece's exit identity, geometrically coincident with <see cref="Start"/>.</summary>
  public TrackCircuitCursor ClosedEndpoint => new(
    this,
    Circuit.Pieces.Count - 1,
    Circuit.Pieces[^1].Piece.Length);

  public TrackCircuitTraversal(TrackCircuit circuit) {
    ArgumentNullException.ThrowIfNull(circuit);
    Circuit = circuit;
    pieceStarts = BuildPieceStarts(circuit);
  }

  /// <summary>Creates a cursor at one exact piece-local arc identity.</summary>
  public TrackCircuitCursor AtPiece(int pieceIndex, double pieceArcLength) {
    ValidatePosition(pieceIndex, pieceArcLength);
    return new(this, pieceIndex, pieceArcLength);
  }

  /// <summary>
  /// Creates a cursor using <see cref="TrackCircuit.Sample(float)"/> boundary identity.
  /// </summary>
  public TrackCircuitCursor AtCircuitArcLength(float circuitArcLength) {
    var sample = Circuit.Sample(circuitArcLength);
    return new(this, sample.PieceIndex, sample.PieceArcLength);
  }

  /// <summary>Advances a cursor by a signed finite distance and wraps over complete laps.</summary>
  public TrackCircuitCursor Advance(TrackCircuitCursor cursor, double distance) {
    ValidateCursor(cursor);
    if (!double.IsFinite(distance))
      throw new ArgumentOutOfRangeException(nameof(distance));
    if (distance == 0d) return cursor;

    var remaining = Math.Abs(distance % Length);
    if (remaining == 0d) return cursor;
    return distance > 0d
      ? AdvanceForward(cursor, remaining)
      : AdvanceBackward(cursor, remaining);
  }

  /// <summary>
  /// Samples the cursor's exact piece directly instead of locating it again from a rounded global
  /// arc length.
  /// </summary>
  public TrackCircuitSample Sample(TrackCircuitCursor cursor) {
    ValidateCursor(cursor);
    var circuitPiece = Circuit.Pieces[cursor.PieceIndex];
    var pieceLength = Convert.ToDouble(circuitPiece.Piece.Length);
    var pieceArcLength = cursor.PieceArcLength == pieceLength
      ? circuitPiece.Piece.Length
      : Convert.ToSingle(cursor.PieceArcLength);
    pieceArcLength = Math.Clamp(pieceArcLength, 0f, circuitPiece.Piece.Length);
    var circuitArcLength = cursor.PieceIndex == Circuit.Pieces.Count - 1 &&
      cursor.PieceArcLength == pieceLength
        ? Circuit.Length
        : Math.Clamp(Convert.ToSingle(GetCircuitArcLength(cursor)), 0f, Circuit.Length);
    return new(
      cursor.PieceIndex,
      circuitPiece,
      circuitArcLength,
      pieceArcLength,
      circuitPiece.Piece.SampleContactPoints(pieceArcLength));
  }

  internal double GetCircuitArcLength(TrackCircuitCursor cursor) {
    ValidateCursor(cursor);
    var pieceLength = Convert.ToDouble(Circuit.Pieces[cursor.PieceIndex].Piece.Length);
    return cursor.PieceArcLength == pieceLength
      ? pieceStarts[cursor.PieceIndex + 1]
      : pieceStarts[cursor.PieceIndex] + cursor.PieceArcLength;
  }

  private TrackCircuitCursor AdvanceForward(TrackCircuitCursor cursor, double remaining) {
    var pieceIndex = cursor.PieceIndex;
    var pieceArcLength = cursor.PieceArcLength;
    for (var transition = 0; transition <= Circuit.Pieces.Count; transition++) {
      var pieceLength = Convert.ToDouble(Circuit.Pieces[pieceIndex].Piece.Length);
      var distanceToExit = pieceLength - pieceArcLength;
      if (remaining <= distanceToExit) {
        var resultArcLength = remaining == distanceToExit
          ? pieceLength
          : Math.Min(pieceLength, pieceArcLength + remaining);
        return new(this, pieceIndex, resultArcLength);
      }

      remaining -= distanceToExit;
      pieceIndex = pieceIndex == Circuit.Pieces.Count - 1 ? 0 : pieceIndex + 1;
      pieceArcLength = 0d;
    }

    throw new InvalidOperationException(
      "Forward track traversal exceeded one bounded circuit lap.");
  }

  private TrackCircuitCursor AdvanceBackward(TrackCircuitCursor cursor, double remaining) {
    var pieceIndex = cursor.PieceIndex;
    var pieceArcLength = cursor.PieceArcLength;
    for (var transition = 0; transition <= Circuit.Pieces.Count; transition++) {
      if (remaining <= pieceArcLength) {
        var resultArcLength = remaining == pieceArcLength
          ? 0d
          : Math.Max(0d, pieceArcLength - remaining);
        return new(this, pieceIndex, resultArcLength);
      }

      remaining -= pieceArcLength;
      pieceIndex = pieceIndex == 0 ? Circuit.Pieces.Count - 1 : pieceIndex - 1;
      pieceArcLength = Circuit.Pieces[pieceIndex].Piece.Length;
    }

    throw new InvalidOperationException(
      "Backward track traversal exceeded one bounded circuit lap.");
  }

  private void ValidateCursor(TrackCircuitCursor cursor) {
    if (!ReferenceEquals(cursor.Traversal, this))
      throw new ArgumentException(
        "The track-circuit cursor belongs to a different traversal.",
        nameof(cursor));
    ValidatePosition(cursor.PieceIndex, cursor.PieceArcLength);
  }

  private void ValidatePosition(int pieceIndex, double pieceArcLength) {
    if (pieceIndex < 0 || pieceIndex >= Circuit.Pieces.Count)
      throw new ArgumentOutOfRangeException(nameof(pieceIndex));
    var pieceLength = Convert.ToDouble(Circuit.Pieces[pieceIndex].Piece.Length);
    if (!double.IsFinite(pieceArcLength) ||
        pieceArcLength < 0d ||
        pieceArcLength > pieceLength)
      throw new ArgumentOutOfRangeException(nameof(pieceArcLength));
  }

  private static double[] BuildPieceStarts(TrackCircuit circuit) {
    var starts = new double[circuit.Pieces.Count + 1];
    foreach (var index in Enumerable.Range(0, circuit.Pieces.Count)) {
      starts[index + 1] = starts[index] + circuit.Pieces[index].Piece.Length;
      if (!double.IsFinite(starts[index + 1]) || starts[index + 1] <= starts[index])
        throw new ArgumentException(
          "Track circuit traversal requires finite, strictly increasing piece lengths.",
          nameof(circuit));
    }
    return starts;
  }
}
