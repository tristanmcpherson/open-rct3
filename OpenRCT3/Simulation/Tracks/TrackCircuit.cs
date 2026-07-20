// Track Circuit
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>One ordered piece in a closed tracked-ride circuit.</summary>
public sealed record TrackCircuitPiece(string Id, TrackPiece Piece);

/// <summary>The geometric continuity contract used to construct a closed track circuit.</summary>
public enum TrackCircuitContinuity {
  StrictC1,
  ImportedPiecewise,
}

/// <summary>One imported piece plus its exact reciprocal neighbors in authoritative DAT order.</summary>
internal sealed record ImportedTrackCircuitPiece(
  string Id,
  TrackPiece Piece,
  string PreviousId,
  string NextId
);

/// <summary>A wheel-contact sample located on one exact piece of a circuit.</summary>
public readonly record struct TrackCircuitSample(
  int PieceIndex,
  TrackCircuitPiece CircuitPiece,
  float CircuitArcLength,
  float PieceArcLength,
  TrackContactPoints ContactPoints
);

/// <summary>An immutable closed sequence of dual-rail track pieces.</summary>
/// <remarks>
/// <see cref="TrackGraph"/> intentionally models a DAG and therefore cannot represent the closed
/// circuits used by most complete roller coasters. This type keeps the cyclic case explicit instead
/// of weakening that graph invariant or duplicating the first piece as a fake terminal edge. Public
/// constructors retain strict C1 validation for hand-authored geometry. The bounded internal import
/// path retains native piece-local frames across position-continuous reciprocal DAT seams.
/// </remarks>
public sealed class TrackCircuit {
  private const int MaximumPieceCount = 1_000_000;
  private const float ImportedJoinPositionTolerance = 0.001f;
  private readonly ReadOnlyCollection<TrackCircuitPiece> pieces;
  private readonly float[] pieceStarts;

  public IReadOnlyList<TrackCircuitPiece> Pieces => pieces;
  public float Length => pieceStarts[^1];
  public TrackCircuitContinuity Continuity { get; }

  public TrackCircuit(
    IEnumerable<TrackCircuitPiece> pieces,
    float joinPositionTolerance = 0.001f,
    float joinTangentTolerance = 0.001f,
    float joinBankToleranceRadians = 0.001f
  ) : this(
    pieces,
    new TrackJoinValidationPolicy(
      joinPositionTolerance,
      joinTangentTolerance,
      joinTangentTolerance,
      joinBankToleranceRadians)) {
  }

  /// <summary>Creates a closed circuit with an explicit join-validation policy.</summary>
  public TrackCircuit(
    IEnumerable<TrackCircuitPiece> pieces,
    TrackJoinValidationPolicy joinValidation
  ) : this(PrepareStrict(pieces, joinValidation), TrackCircuitContinuity.StrictC1) {
  }

  private TrackCircuit(
    TrackCircuitPiece[] pieces,
    TrackCircuitContinuity continuity
  ) {
    pieceStarts = BuildPieceStarts(pieces);
    this.pieces = Array.AsReadOnly(pieces);
    Continuity = continuity;
  }

  /// <summary>
  /// Creates a native-style circuit from exact reciprocal DAT links and position-closed seams.
  /// </summary>
  /// <remarks>
  /// RCT3.exe's x86 path at <c>0xC5A570</c> subtracts the selected piece's saved start distance and
  /// calls the piece-local samplers at <c>0xC39310</c>/<c>0xC38A10</c>. It does not merge or compare
  /// neighboring tangents. This path therefore preserves each piece's own endpoint tangent, bank,
  /// and frame while still rejecting reordered links, open seams, and swapped rails.
  /// </remarks>
  internal static TrackCircuit CreateImportedPiecewise(
    IEnumerable<ImportedTrackCircuitPiece> pieces
  ) {
    ArgumentNullException.ThrowIfNull(pieces);

    var imported = CopyImportedBounded(pieces);
    ValidateImportedLinks(imported);
    var copied = imported
      .Select(piece => new TrackCircuitPiece(piece.Id, piece.Piece))
      .ToArray();
    ValidatePieces(copied);
    ValidatePositionJoins(copied, ImportedJoinPositionTolerance);
    return new(copied, TrackCircuitContinuity.ImportedPiecewise);
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

  private static ImportedTrackCircuitPiece[] CopyImportedBounded(
    IEnumerable<ImportedTrackCircuitPiece> source
  ) {
    var copied = new List<ImportedTrackCircuitPiece>();
    foreach (var piece in source) {
      if (copied.Count >= MaximumPieceCount)
        throw new ArgumentException(
          $"Track circuit piece count exceeds the limit {MaximumPieceCount}.",
          nameof(source));
      copied.Add(piece);
    }
    return [.. copied];
  }

  private static TrackCircuitPiece[] PrepareStrict(
    IEnumerable<TrackCircuitPiece> pieces,
    TrackJoinValidationPolicy joinValidation
  ) {
    ArgumentNullException.ThrowIfNull(pieces);
    ArgumentNullException.ThrowIfNull(joinValidation);

    var copied = CopyBounded(pieces);
    ValidatePieces(copied);
    ValidateJoins(copied, joinValidation);
    return copied;
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

  private static void ValidateImportedLinks(
    IReadOnlyList<ImportedTrackCircuitPiece> pieces
  ) {
    if (pieces.Count == 0)
      throw new ArgumentException("An imported track circuit needs at least one piece.",
        nameof(pieces));

    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var piece in pieces) {
      if (piece == null || string.IsNullOrWhiteSpace(piece.Id) || piece.Piece == null ||
          string.IsNullOrWhiteSpace(piece.PreviousId) ||
          string.IsNullOrWhiteSpace(piece.NextId))
        throw new ArgumentException(
          "Imported track circuit pieces need geometry and exact neighbor IDs.",
          nameof(pieces));
      if (!ids.Add(piece.Id))
        throw new ArgumentException(
          $"Duplicate imported track circuit piece ID '{piece.Id}'.",
          nameof(pieces));
    }

    foreach (var index in Enumerable.Range(0, pieces.Count)) {
      var piece = pieces[index];
      var previous = pieces[(index + pieces.Count - 1) % pieces.Count];
      var next = pieces[(index + 1) % pieces.Count];
      if (!string.Equals(piece.PreviousId, previous.Id, StringComparison.Ordinal) ||
          !string.Equals(piece.NextId, next.Id, StringComparison.Ordinal))
        throw new ArgumentException(
          $"Imported track circuit piece '{piece.Id}' does not retain exact reciprocal order.",
          nameof(pieces));
    }
  }

  private static void ValidatePositionJoins(
    IReadOnlyList<TrackCircuitPiece> pieces,
    float positionTolerance
  ) {
    foreach (var index in Enumerable.Range(0, pieces.Count)) {
      var outgoing = pieces[index];
      var incoming = pieces[(index + 1) % pieces.Count];
      ValidateRailPositionJoin(
        outgoing.Piece.Exit.Left,
        incoming.Piece.Entry.Left,
        positionTolerance,
        outgoing.Id,
        incoming.Id,
        RailSide.Left);
      ValidateRailPositionJoin(
        outgoing.Piece.Exit.Right,
        incoming.Piece.Entry.Right,
        positionTolerance,
        outgoing.Id,
        incoming.Id,
        RailSide.Right);
    }
  }

  private static void ValidateRailPositionJoin(
    RailEndpoint expected,
    RailEndpoint actual,
    float positionTolerance,
    string outgoingId,
    string incomingId,
    RailSide side
  ) {
    if (TrackMath.Distance(expected.Position, actual.Position) > positionTolerance)
      throw new ArgumentException(
        $"Imported track circuit pieces '{outgoingId}' and '{incomingId}' do not form a " +
        $"position-continuous {side} rail join.");
  }

  private static void ValidateJoins(
    IReadOnlyList<TrackCircuitPiece> pieces,
    TrackJoinValidationPolicy validation
  ) {
    foreach (var index in Enumerable.Range(0, pieces.Count)) {
      var outgoing = pieces[index];
      var incoming = pieces[(index + 1) % pieces.Count];
      ValidateRailJoin(
        outgoing.Piece.Exit.Left,
        incoming.Piece.Entry.Left,
        validation,
        outgoing.Id,
        incoming.Id,
        RailSide.Left);
      ValidateRailJoin(
        outgoing.Piece.Exit.Right,
        incoming.Piece.Entry.Right,
        validation,
        outgoing.Id,
        incoming.Id,
        RailSide.Right);
    }
  }

  private static void ValidateRailJoin(
    RailEndpoint expected,
    RailEndpoint actual,
    TrackJoinValidationPolicy validation,
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
    var magnitudeMismatch = validation.TangentMagnitudeTolerance is not null
      && magnitudeDifference > validation.TangentMagnitudeTolerance.Value;

    if (TrackMath.Distance(expected.Position, actual.Position) > validation.PositionTolerance
        || directionDifference > validation.TangentDirectionTolerance
        || magnitudeMismatch
        || MathF.Abs(TrackMath.ShortestAngleDelta(
          expected.BankRadians,
          actual.BankRadians)) > validation.BankToleranceRadians)
      throw new ArgumentException(
        $"Track circuit pieces '{outgoingId}' and '{incomingId}' do not form a " +
        $"C1-continuous {side} rail join.");
  }

}
