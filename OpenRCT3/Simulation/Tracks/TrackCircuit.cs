// Track Circuit
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>One ordered piece in a closed tracked-ride circuit.</summary>
public sealed record TrackCircuitPiece(string Id, TrackPiece Piece);

/// <summary>A wheel-contact sample located on one exact piece of a circuit.</summary>
public readonly record struct TrackCircuitSample(
  int PieceIndex,
  TrackCircuitPiece CircuitPiece,
  float CircuitArcLength,
  float PieceArcLength,
  TrackContactPoints ContactPoints
);

/// <summary>An immutable, C1-continuous closed sequence of dual-rail track pieces.</summary>
/// <remarks>
/// <see cref="TrackGraph"/> intentionally models a DAG and therefore cannot represent the closed
/// circuits used by most complete roller coasters. This type keeps the cyclic case explicit instead
/// of weakening that graph invariant or duplicating the first piece as a fake terminal edge.
/// </remarks>
public sealed class TrackCircuit {
  private const int MaximumPieceCount = 1_000_000;
  private readonly ReadOnlyCollection<TrackCircuitPiece> pieces;
  private readonly float[] pieceStarts;

  public IReadOnlyList<TrackCircuitPiece> Pieces => pieces;
  public float Length => pieceStarts[^1];

  public TrackCircuit(
    IEnumerable<TrackCircuitPiece> pieces,
    float joinPositionTolerance = 0.001f,
    float joinTangentTolerance = 0.001f,
    float joinBankToleranceRadians = 0.001f
  ) {
    ArgumentNullException.ThrowIfNull(pieces);
    ValidateTolerance(joinPositionTolerance, nameof(joinPositionTolerance));
    ValidateTolerance(joinTangentTolerance, nameof(joinTangentTolerance));
    ValidateTolerance(joinBankToleranceRadians, nameof(joinBankToleranceRadians));

    var copied = CopyBounded(pieces);
    ValidatePieces(copied);
    ValidateJoins(
      copied,
      joinPositionTolerance,
      joinTangentTolerance,
      joinBankToleranceRadians);

    pieceStarts = BuildPieceStarts(copied);
    this.pieces = Array.AsReadOnly(copied);
  }

  /// <summary>Samples the circuit over its canonical closed interval.</summary>
  /// <remarks>
  /// An arc length exactly equal to <see cref="Length"/> returns the final piece's exit. It is
  /// geometrically identical to arc length zero, but retaining the final-piece identity is useful
  /// when advancing a vehicle across the wrap boundary.
  /// </remarks>
  public TrackCircuitSample Sample(float arcLength) {
    if (!float.IsFinite(arcLength) || arcLength < 0f || arcLength > Length)
      throw new ArgumentOutOfRangeException(nameof(arcLength));

    var pieceIndex = FindPieceIndex(arcLength);
    var pieceArcLength = pieceIndex == pieces.Count - 1 && arcLength == Length
      ? pieces[pieceIndex].Piece.Length
      : arcLength - pieceStarts[pieceIndex];
    var contactPoints = pieces[pieceIndex].Piece.SampleContactPoints(pieceArcLength);
    return new(
      pieceIndex,
      pieces[pieceIndex],
      arcLength,
      pieceArcLength,
      contactPoints);
  }

  private int FindPieceIndex(float arcLength) {
    var exactIndex = Array.BinarySearch(pieceStarts, arcLength);
    if (exactIndex >= 0)
      return exactIndex == pieces.Count ? pieces.Count - 1 : exactIndex;
    return (~exactIndex) - 1;
  }

  private static TrackCircuitPiece[] CopyBounded(IEnumerable<TrackCircuitPiece> source) {
    var copied = new List<TrackCircuitPiece>();
    foreach (var piece in source) {
      if (copied.Count >= MaximumPieceCount)
        throw new ArgumentException(
          $"Track circuit piece count exceeds the limit {MaximumPieceCount}.",
          nameof(source));
      copied.Add(piece);
    }
    return [.. copied];
  }

  private static void ValidatePieces(IReadOnlyList<TrackCircuitPiece> pieces) {
    if (pieces.Count == 0)
      throw new ArgumentException("A track circuit needs at least one piece.", nameof(pieces));

    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var piece in pieces) {
      if (piece == null || string.IsNullOrWhiteSpace(piece.Id) || piece.Piece == null)
        throw new ArgumentException(
          "Track circuit pieces need non-empty IDs and geometry.",
          nameof(pieces));
      if (!ids.Add(piece.Id))
        throw new ArgumentException(
          $"Duplicate track circuit piece ID '{piece.Id}'.",
          nameof(pieces));
      if (!float.IsFinite(piece.Piece.Length) || piece.Piece.Length <= 0f)
        throw new ArgumentException(
          $"Track circuit piece '{piece.Id}' has invalid length.",
          nameof(pieces));
    }
  }

  private static float[] BuildPieceStarts(IReadOnlyList<TrackCircuitPiece> pieces) {
    var starts = new float[pieces.Count + 1];
    var total = 0d;
    foreach (var index in Enumerable.Range(0, pieces.Count)) {
      total += pieces[index].Piece.Length;
      if (!double.IsFinite(total) || total > float.MaxValue)
        throw new ArgumentException(
          "Track circuit length exceeds the finite single-precision range.",
          nameof(pieces));
      starts[index + 1] = Convert.ToSingle(total);
      if (starts[index + 1] <= starts[index])
        throw new ArgumentException(
          "Track circuit cumulative lengths must be strictly increasing.",
          nameof(pieces));
    }
    return starts;
  }

  private static void ValidateJoins(
    IReadOnlyList<TrackCircuitPiece> pieces,
    float positionTolerance,
    float tangentTolerance,
    float bankTolerance
  ) {
    foreach (var index in Enumerable.Range(0, pieces.Count)) {
      var outgoing = pieces[index];
      var incoming = pieces[(index + 1) % pieces.Count];
      ValidateRailJoin(
        outgoing.Piece.Exit.Left,
        incoming.Piece.Entry.Left,
        positionTolerance,
        tangentTolerance,
        bankTolerance,
        outgoing.Id,
        incoming.Id,
        RailSide.Left);
      ValidateRailJoin(
        outgoing.Piece.Exit.Right,
        incoming.Piece.Entry.Right,
        positionTolerance,
        tangentTolerance,
        bankTolerance,
        outgoing.Id,
        incoming.Id,
        RailSide.Right);
    }
  }

  private static void ValidateRailJoin(
    RailEndpoint expected,
    RailEndpoint actual,
    float positionTolerance,
    float tangentTolerance,
    float bankTolerance,
    string outgoingId,
    string incomingId,
    RailSide side
  ) {
    var expectedMagnitude = TrackMath.Length(expected.Tangent);
    var actualMagnitude = TrackMath.Length(actual.Tangent);
    var directionDifference = TrackMath.Distance(
      TrackMath.Normalize(expected.Tangent),
      TrackMath.Normalize(actual.Tangent));
    var magnitudeScale = Math.Max(expectedMagnitude, actualMagnitude);
    var magnitudeDifference = Math.Abs(expectedMagnitude - actualMagnitude) / magnitudeScale;

    if (TrackMath.Distance(expected.Position, actual.Position) > positionTolerance
        || directionDifference > tangentTolerance
        || magnitudeDifference > tangentTolerance
        || MathF.Abs(TrackMath.ShortestAngleDelta(
          expected.BankRadians,
          actual.BankRadians)) > bankTolerance)
      throw new ArgumentException(
        $"Track circuit pieces '{outgoingId}' and '{incomingId}' do not form a " +
        $"C1-continuous {side} rail join.");
  }

  private static void ValidateTolerance(float tolerance, string parameterName) {
    if (!float.IsFinite(tolerance) || tolerance < 0f)
      throw new ArgumentOutOfRangeException(parameterName);
  }
}
