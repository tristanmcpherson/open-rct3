// Track Bounds
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>An immutable finite axis-aligned bound over exact baked dual-rail positions.</summary>
public readonly record struct TrackAxisAlignedBounds {
  public Vector3 Min { get; }
  public Vector3 Max { get; }

  /// <summary>The overflow-safe midpoint of the minimum and maximum corners.</summary>
  public Vector3 Center => TrackMath.Midpoint(Min, Max);

  internal TrackAxisAlignedBounds(Vector3 min, Vector3 max) {
    if (!TrackMath.IsFinite(min) || !TrackMath.IsFinite(max))
      throw new ArgumentException("Track bounds must contain only finite values.");
    if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
      throw new ArgumentException("Track bounds minimum cannot exceed its maximum.");
    Min = min;
    Max = max;
  }
}

/// <summary>Computes exact finite AABBs over existing baked track samples.</summary>
/// <remarks>
/// Bounds include both rail positions at every retained baked arc length. They do not add analytic
/// curve extrema between bake points, support geometry, vehicle dimensions, safety clearance, or
/// any other inferred expansion.
/// </remarks>
public static class TrackBoundsBuilder {
  /// <summary>Bounds one piece's exact retained dual-rail bake.</summary>
  public static TrackAxisAlignedBounds FromPiece(TrackPiece piece) =>
    FromPiece(piece, TrackBoundsLimits.Default);

  /// <summary>Unions the exact baked bounds of every edge in one graph.</summary>
  public static TrackAxisAlignedBounds FromGraph(TrackGraph graph) =>
    FromGraph(graph, TrackBoundsLimits.Default);

  /// <summary>Unions the exact baked bounds of every piece in one circuit.</summary>
  public static TrackAxisAlignedBounds FromCircuit(TrackCircuit circuit) =>
    FromCircuit(circuit, TrackBoundsLimits.Default);

  internal static TrackAxisAlignedBounds FromPiece(
    TrackPiece piece,
    TrackBoundsLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(piece);
    ValidateLimits(limits);
    ValidatePiece(piece, "track piece");
    ReserveRailSamples(0, piece, limits, "track piece");
    return BuildPieceBounds(piece, "track piece");
  }

  internal static TrackAxisAlignedBounds FromGraph(
    TrackGraph graph,
    TrackBoundsLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(graph);
    return BuildComposite(
      graph.Edges.Count,
      index => graph.Edges[index].Piece,
      limits,
      "track graph");
  }

  internal static TrackAxisAlignedBounds FromCircuit(
    TrackCircuit circuit,
    TrackBoundsLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(circuit);
    return BuildComposite(
      circuit.Pieces.Count,
      index => circuit.Pieces[index].Piece,
      limits,
      "track circuit");
  }

  private static TrackAxisAlignedBounds BuildComposite(
    int pieceCount,
    Func<int, TrackPiece?> getPiece,
    TrackBoundsLimits limits,
    string description
  ) {
    ValidateLimits(limits);
    if (pieceCount <= 0)
      throw new InvalidDataException($"{description} contains no pieces to bound.");
    if (pieceCount > limits.MaximumPieces)
      throw new InvalidDataException(
        $"{description} piece count {pieceCount} exceeds the bound " +
        $"{limits.MaximumPieces}.");

    var railSamples = 0UL;
    foreach (var index in Enumerable.Range(0, pieceCount)) {
      var piece = getPiece(index)
        ?? throw new InvalidDataException($"{description} piece {index} is null.");
      ValidatePiece(piece, $"{description} piece {index}");
      railSamples = ReserveRailSamples(
        railSamples,
        piece,
        limits,
        $"{description} piece {index}");
    }

    TrackAxisAlignedBounds? result = null;
    foreach (var index in Enumerable.Range(0, pieceCount)) {
      var bounds = BuildPieceBounds(
        getPiece(index)!,
        $"{description} piece {index}");
      result = result is { } existing ? Union(existing, bounds) : bounds;
    }
    return result!.Value;
  }

  private static void ValidatePiece(TrackPiece piece, string description) {
    if (!float.IsFinite(piece.Length) || piece.Length <= 0f)
      throw new InvalidDataException(
        $"{description} length is not finite and positive.");
    if (piece.BakedArcLengths is null
      || piece.BakedArcLengths.Count == 0
      || piece.BakedArcLengths.Count != piece.BakedSampleCount)
      throw new InvalidDataException(
        $"{description} has an empty or inconsistent baked arc-length list.");

    var previous = -1f;
    foreach (var index in Enumerable.Range(0, piece.BakedArcLengths.Count)) {
      var arcLength = piece.BakedArcLengths[index];
      if (!float.IsFinite(arcLength) || arcLength <= previous)
        throw new InvalidDataException(
          $"{description} baked arc length {index} is not finite and strictly increasing.");
      if (index == 0 && arcLength != 0f)
        throw new InvalidDataException(
          $"{description} baked arc lengths do not start exactly at zero.");
      previous = arcLength;
    }
    if (piece.BakedArcLengths[^1] != piece.Length)
      throw new InvalidDataException(
        $"{description} baked arc lengths do not end exactly at the piece length.");
  }

  private static ulong ReserveRailSamples(
    ulong used,
    TrackPiece piece,
    TrackBoundsLimits limits,
    string description
  ) {
    var count = Convert.ToUInt64(piece.BakedArcLengths.Count) * 2UL;
    if (count > limits.MaximumRailSamples
      || used > limits.MaximumRailSamples - count)
      throw new InvalidDataException(
        $"{description} exceeds the aggregate baked rail-sample bound " +
        $"{limits.MaximumRailSamples}.");
    return used + count;
  }

  private static TrackAxisAlignedBounds BuildPieceBounds(
    TrackPiece piece,
    string description
  ) {
    var first = Sample(piece, piece.BakedArcLengths[0], 0, description);
    var min = Vector3.Min(first.Left.Position, first.Right.Position);
    var max = Vector3.Max(first.Left.Position, first.Right.Position);
    foreach (var index in Enumerable.Range(1, piece.BakedArcLengths.Count - 1)) {
      var contacts = Sample(
        piece,
        piece.BakedArcLengths[index],
        index,
        description);
      min = Vector3.Min(min, Vector3.Min(
        contacts.Left.Position,
        contacts.Right.Position));
      max = Vector3.Max(max, Vector3.Max(
        contacts.Left.Position,
        contacts.Right.Position));
    }
    return new(min, max);
  }

  private static TrackContactPoints Sample(
    TrackPiece piece,
    float arcLength,
    int index,
    string description
  ) {
    var contacts = piece.SampleContactPoints(arcLength);
    if (contacts.ArcLength != arcLength
      || contacts.Left.ArcLength != arcLength
      || contacts.Right.ArcLength != arcLength)
      throw new InvalidDataException(
        $"{description} baked sample {index} does not retain its exact arc identity.");
    if (!TrackMath.IsFinite(contacts.Left.Position)
      || !TrackMath.IsFinite(contacts.Right.Position))
      throw new InvalidDataException(
        $"{description} baked sample {index} contains a non-finite rail position.");
    return contacts;
  }

  private static TrackAxisAlignedBounds Union(
    TrackAxisAlignedBounds left,
    TrackAxisAlignedBounds right
  ) => new(
    Vector3.Min(left.Min, right.Min),
    Vector3.Max(left.Max, right.Max));

  private static void ValidateLimits(TrackBoundsLimits limits) {
    if (limits.MaximumPieces <= 0 || limits.MaximumRailSamples < 2)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }
}

internal readonly record struct TrackBoundsLimits(
  int MaximumPieces,
  ulong MaximumRailSamples
) {
  public static TrackBoundsLimits Default { get; } =
    new(1_000_000, 16_000_000);
}
