// Ride Track Geometry Spatial Index
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One resolved DAT track and its exact baked dual-rail bounds.</summary>
internal sealed record RideTrackGeometrySpatialEntry(
  int DatTrackIndex,
  ulong TrackSourceEntryId,
  RideTrackGeometryStatus Status,
  TrackAxisAlignedBounds Bounds
);

/// <summary>One point-query result ordered against an exact track bound.</summary>
internal readonly record struct RideTrackGeometrySpatialHit(
  RideTrackGeometrySpatialEntry Entry,
  double SquaredDistanceToBounds
) {
  public bool ContainsPoint => SquaredDistanceToBounds == 0d;
}

/// <summary>A bounded immutable broad-phase index over resolved DAT ride tracks.</summary>
/// <remarks>
/// Entry bounds come directly from <see cref="TrackBoundsBuilder"/>. They do not add vehicle,
/// support, selection, safety-clearance, or other inferred expansion.
/// </remarks>
internal sealed class RideTrackGeometrySpatialIndex {
  private readonly IReadOnlyList<RideTrackGeometrySpatialEntry> entries;
  private readonly RideTrackGeometrySpatialIndexLimits limits;

  public IReadOnlyList<RideTrackGeometrySpatialEntry> Entries => entries;
  public int TrackCount { get; }
  public int ResolvedTrackCount => entries.Count;
  public int UnresolvedResourceTrackCount { get; }
  public int UnsupportedGeometryTrackCount { get; }
  public int UnsupportedTopologyTrackCount { get; }

  private RideTrackGeometrySpatialIndex(
    RideTrackGeometrySpatialEntry[] entries,
    int trackCount,
    int unresolvedResourceTrackCount,
    int unsupportedGeometryTrackCount,
    int unsupportedTopologyTrackCount,
    RideTrackGeometrySpatialIndexLimits limits
  ) {
    this.entries = Array.AsReadOnly(entries);
    this.limits = limits;
    TrackCount = trackCount;
    UnresolvedResourceTrackCount = unresolvedResourceTrackCount;
    UnsupportedGeometryTrackCount = unsupportedGeometryTrackCount;
    UnsupportedTopologyTrackCount = unsupportedTopologyTrackCount;
  }

  public static RideTrackGeometrySpatialIndex Build(
    RideTrackGeometryResolution resolution
  ) => Build(resolution, RideTrackGeometrySpatialIndexLimits.Default);

  internal static RideTrackGeometrySpatialIndex Build(
    RideTrackGeometryResolution resolution,
    RideTrackGeometrySpatialIndexLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(resolution);
    ValidateLimits(limits);
    if (resolution.Tracks is null)
      throw Invalid("track list is null");
    if (resolution.Tracks.Count > limits.MaximumTrackCount)
      throw Limit("track", Convert.ToUInt64(limits.MaximumTrackCount));
    if (resolution.UnresolvedResourceTrackCount < 0
      || resolution.UnsupportedGeometryTrackCount < 0
      || resolution.UnsupportedTopologyTrackCount < 0)
      throw Invalid("advertised outcome counts cannot be negative");

    var resolved = new List<(int DatTrackIndex, RideTrackGeometryLink Link)>();
    var trackIds = new HashSet<ulong>();
    var unresolvedCount = 0;
    var unsupportedGeometryCount = 0;
    var unsupportedTopologyCount = 0;
    foreach (var index in Enumerable.Range(0, resolution.Tracks.Count)) {
      var link = resolution.Tracks[index];
      if (link?.Track is null || link.Track.SourceEntryId == 0)
        throw Invalid($"track {index} is incomplete");
      if (!trackIds.Add(link.Track.SourceEntryId))
        throw Invalid($"track ID {link.Track.SourceEntryId} is duplicated");

      switch (link.Status) {
        case RideTrackGeometryStatus.OpenTrack:
          if (link.Graph is null || link.Circuit != null ||
              link.SegmentCircuits?.Count != 0)
            throw Invalid(
              $"open track {link.Track.SourceEntryId} has inconsistent geometry");
          resolved.Add((index, link));
          break;
        case RideTrackGeometryStatus.Circuit:
          if (link.Circuit is null || link.Graph != null ||
              link.SegmentCircuits?.Count != 1 ||
              !ReferenceEquals(link.SegmentCircuits[0].Circuit, link.Circuit))
            throw Invalid(
              $"circuit {link.Track.SourceEntryId} has inconsistent geometry");
          resolved.Add((index, link));
          break;
        case RideTrackGeometryStatus.MultiCircuit:
          if (link.Circuit != null || link.Graph != null)
            throw Invalid(
              $"multi-circuit track {link.Track.SourceEntryId} has singular geometry");
          ValidateSegmentCircuits(link, 2);
          resolved.Add((index, link));
          break;
        case RideTrackGeometryStatus.UnresolvedResources:
          ValidateSkipped(link);
          unresolvedCount++;
          break;
        case RideTrackGeometryStatus.UnsupportedGeometry:
          ValidateSkipped(link);
          unsupportedGeometryCount++;
          break;
        case RideTrackGeometryStatus.UnsupportedTopology:
          ValidateSkipped(link);
          unsupportedTopologyCount++;
          break;
        default:
          throw Invalid(
            $"track {link.Track.SourceEntryId} has unknown status {link.Status}");
      }
    }

    var advertisedCount = Convert.ToInt64(resolved.Count)
      + resolution.UnresolvedResourceTrackCount
      + resolution.UnsupportedGeometryTrackCount
      + resolution.UnsupportedTopologyTrackCount;
    if (advertisedCount != resolution.Tracks.Count)
      throw Invalid(
        $"advertised outcome counts do not conserve {resolution.Tracks.Count} tracks");
    ValidateCount(
      "unresolved resource",
      unresolvedCount,
      resolution.UnresolvedResourceTrackCount);
    ValidateCount(
      "unsupported geometry",
      unsupportedGeometryCount,
      resolution.UnsupportedGeometryTrackCount);
    ValidateCount(
      "unsupported topology",
      unsupportedTopologyCount,
      resolution.UnsupportedTopologyTrackCount);

    var pieceCount = 0ul;
    var railSampleCount = 0ul;
    foreach (var item in resolved)
      ReserveGeometry(item.Link, limits, ref pieceCount, ref railSampleCount);

    var entries = new RideTrackGeometrySpatialEntry[resolved.Count];
    var boundsLimits = new TrackBoundsLimits(
      limits.MaximumPieceCount,
      limits.MaximumRailSampleCount);
    foreach (var index in Enumerable.Range(0, resolved.Count)) {
      var item = resolved[index];
      var bounds = BuildBounds(item.Link, boundsLimits);
      entries[index] = new(
        item.DatTrackIndex,
        item.Link.Track.SourceEntryId,
        item.Link.Status,
        bounds);
    }

    return new(
      entries,
      resolution.Tracks.Count,
      unresolvedCount,
      unsupportedGeometryCount,
      unsupportedTopologyCount,
      limits);
  }

  /// <summary>Returns at most the requested nearest bounds with DAT order breaking ties.</summary>
  public IReadOnlyList<RideTrackGeometrySpatialHit> Query(
    Vector3 point,
    int maximumResults
  ) {
    if (!IsFinite(point))
      throw new ArgumentException("Ride-track spatial query point must be finite.", nameof(point));
    if (maximumResults < 0 || maximumResults > limits.MaximumQueryResults)
      throw new ArgumentOutOfRangeException(nameof(maximumResults));
    if (maximumResults == 0 || entries.Count == 0)
      return Array.Empty<RideTrackGeometrySpatialHit>();

    var hits = entries
      .Select(entry => new RideTrackGeometrySpatialHit(
        entry,
        SquaredDistance(point, entry.Bounds)))
      .OrderBy(hit => hit.SquaredDistanceToBounds)
      .ThenBy(hit => hit.Entry.DatTrackIndex)
      .Take(maximumResults)
      .ToArray();
    return Array.AsReadOnly(hits);
  }

  private static void ReserveGeometry(
    RideTrackGeometryLink link,
    RideTrackGeometrySpatialIndexLimits limits,
    ref ulong pieceCount,
    ref ulong railSampleCount
  ) {
    if (link.Status == RideTrackGeometryStatus.OpenTrack) {
      foreach (var edge in link.Graph!.Edges) {
        if (edge?.Piece is null)
          throw Invalid($"open track {link.Track.SourceEntryId} contains an incomplete edge");
        ReservePiece(edge.Piece, limits, ref pieceCount, ref railSampleCount);
      }
      return;
    }

    foreach (var segment in link.SegmentCircuits) {
      foreach (var item in segment.Circuit.Pieces) {
        if (item?.Piece is null)
          throw Invalid($"circuit {link.Track.SourceEntryId} contains an incomplete piece");
        ReservePiece(item.Piece, limits, ref pieceCount, ref railSampleCount);
      }
    }
  }

  private static TrackAxisAlignedBounds BuildBounds(
    RideTrackGeometryLink link,
    TrackBoundsLimits limits
  ) {
    if (link.Status == RideTrackGeometryStatus.OpenTrack)
      return TrackBoundsBuilder.FromGraph(link.Graph!, limits);
    var bounds = link.SegmentCircuits
      .Select(segment => TrackBoundsBuilder.FromCircuit(segment.Circuit, limits))
      .ToArray();
    var minimum = bounds[0].Min;
    var maximum = bounds[0].Max;
    foreach (var index in Enumerable.Range(1, bounds.Length - 1)) {
      minimum = Vector3.Min(minimum, bounds[index].Min);
      maximum = Vector3.Max(maximum, bounds[index].Max);
    }
    return new(minimum, maximum);
  }

  private static void ValidateSegmentCircuits(
    RideTrackGeometryLink link,
    int minimumCount
  ) {
    var segments = link.SegmentCircuits;
    if (segments is null || segments.Count < minimumCount ||
        (minimumCount == 1 && segments.Count != 1) ||
        !segments.Select(segment => segment?.SegmentSourceEntryId ?? 0ul)
          .SequenceEqual(link.Track.SegmentSourceEntryIds))
      throw Invalid(
        $"circuit track {link.Track.SourceEntryId} has inconsistent segment geometry");
    var flattenedIds = new List<ulong>(link.Track.TrackPieceSourceEntryIds.Count);
    foreach (var segment in segments) {
      if (segment?.Circuit is null || segment.PieceSourceEntryIds is null ||
          segment.PieceSourceEntryIds.Count == 0 ||
          segment.PieceSourceEntryIds.Count != segment.Circuit.Pieces.Count)
        throw Invalid(
          $"circuit track {link.Track.SourceEntryId} has incomplete segment geometry");
      flattenedIds.AddRange(segment.PieceSourceEntryIds);
    }
    if (!flattenedIds.SequenceEqual(link.Track.TrackPieceSourceEntryIds) ||
        (link.Circuit != null && !ReferenceEquals(link.Circuit, segments[0].Circuit)))
      throw Invalid(
        $"circuit track {link.Track.SourceEntryId} changed exact piece geometry");
  }

  private static void ReservePiece(
    TrackPiece piece,
    RideTrackGeometrySpatialIndexLimits limits,
    ref ulong pieceCount,
    ref ulong railSampleCount
  ) {
    Reserve(
      ref pieceCount,
      1ul,
      Convert.ToUInt64(limits.MaximumPieceCount),
      "piece");
    var railSamples = checked(Convert.ToUInt64(piece.BakedSampleCount) * 2ul);
    Reserve(
      ref railSampleCount,
      railSamples,
      limits.MaximumRailSampleCount,
      "baked rail sample");
  }

  private static void Reserve(
    ref ulong current,
    ulong addition,
    ulong maximum,
    string resource
  ) {
    if (addition > maximum || current > maximum - addition)
      throw Limit(resource, maximum);
    current += addition;
  }

  private static double SquaredDistance(
    Vector3 point,
    TrackAxisAlignedBounds bounds
  ) {
    var x = AxisDistance(point.X, bounds.Min.X, bounds.Max.X);
    var y = AxisDistance(point.Y, bounds.Min.Y, bounds.Max.Y);
    var z = AxisDistance(point.Z, bounds.Min.Z, bounds.Max.Z);
    return x * x + y * y + z * z;
  }

  private static double AxisDistance(float value, float minimum, float maximum) {
    if (value < minimum) return Convert.ToDouble(minimum) - value;
    if (value > maximum) return Convert.ToDouble(value) - maximum;
    return 0d;
  }

  private static void ValidateSkipped(RideTrackGeometryLink link) {
    if (link.Graph != null || link.Circuit != null ||
        link.SegmentCircuits?.Count != 0 || link.IsResolved)
      throw Invalid(
        $"skipped track {link.Track.SourceEntryId} unexpectedly contains resolved geometry");
  }

  private static void ValidateCount(string outcome, int actual, int advertised) {
    if (actual != advertised)
      throw Invalid(
        $"result advertises {advertised} {outcome} tracks, found {actual}");
  }

  private static void ValidateLimits(RideTrackGeometrySpatialIndexLimits limits) {
    if (limits.MaximumTrackCount <= 0
      || limits.MaximumPieceCount <= 0
      || limits.MaximumRailSampleCount < 2
      || limits.MaximumQueryResults <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-track geometry spatial index input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Ride-track geometry spatial index {resource} count exceeds the limit {maximum}.");
}

internal readonly record struct RideTrackGeometrySpatialIndexLimits(
  int MaximumTrackCount,
  int MaximumPieceCount,
  ulong MaximumRailSampleCount,
  int MaximumQueryResults
) {
  public static RideTrackGeometrySpatialIndexLimits Default { get; } =
    new(100_000, 1_000_000, 16_000_000, 100_000);
}
