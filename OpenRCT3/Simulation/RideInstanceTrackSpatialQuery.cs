// Ride Instance Track Spatial Query
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One proven track-bound hit mapped to its exact DAT ride-instance runtime entry.</summary>
internal readonly record struct RideInstanceTrackSpatialHit(
  RideInstanceTrackRuntimeEntry Runtime,
  RideTrackGeometrySpatialHit Spatial
) {
  public RideTrackGeometrySpatialEntry SpatialEntry => Spatial.Entry;
  public TrackAxisAlignedBounds Bounds => Spatial.Entry.Bounds;
  public double SquaredDistanceToBounds => Spatial.SquaredDistanceToBounds;
  public bool ContainsPoint => Spatial.ContainsPoint;
}

/// <summary>
/// A bounded immutable point-query facade over exact ride-instance and resolved-track identities.
/// </summary>
/// <remarks>
/// Results retain the spatial index's order and exact bound distance. No clearance, vehicle,
/// physics, or analytic curve extent is inferred.
/// </remarks>
internal sealed class RideInstanceTrackSpatialQuery {
  private readonly IReadOnlyDictionary<ulong, RideInstanceTrackRuntimeEntry> runtimeByTrackId;
  private readonly RideTrackGeometrySpatialIndex spatialIndex;
  private readonly RideInstanceTrackSpatialQueryLimits limits;

  public IReadOnlyList<RideInstanceTrackRuntimeEntry> ResolvedEntries { get; }
  public int TrackCount { get; }
  public int ResolvedTrackCount => ResolvedEntries.Count;

  private RideInstanceTrackSpatialQuery(
    RideInstanceTrackRuntimeEntry[] resolvedEntries,
    IReadOnlyDictionary<ulong, RideInstanceTrackRuntimeEntry> runtimeByTrackId,
    RideTrackGeometrySpatialIndex spatialIndex,
    int trackCount,
    RideInstanceTrackSpatialQueryLimits limits
  ) {
    ResolvedEntries = Array.AsReadOnly(resolvedEntries);
    this.runtimeByTrackId = runtimeByTrackId;
    this.spatialIndex = spatialIndex;
    this.limits = limits;
    TrackCount = trackCount;
  }

  public static RideInstanceTrackSpatialQuery Build(
    RideInstanceTrackRuntimeRegistry runtime,
    RideTrackGeometrySpatialIndex spatialIndex
  ) => Build(runtime, spatialIndex, RideInstanceTrackSpatialQueryLimits.Default);

  internal static RideInstanceTrackSpatialQuery Build(
    RideInstanceTrackRuntimeRegistry runtime,
    RideTrackGeometrySpatialIndex spatialIndex,
    RideInstanceTrackSpatialQueryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(spatialIndex);
    ValidateLimits(limits);
    if (runtime.Entries is null)
      throw Invalid("runtime entry list is null");
    if (spatialIndex.Entries is null)
      throw Invalid("spatial entry list is null");
    if (runtime.InstanceCount > limits.MaximumEntryCount)
      throw Limit("runtime entry", limits.MaximumEntryCount);
    if (spatialIndex.ResolvedTrackCount > limits.MaximumEntryCount)
      throw Limit("resolved spatial entry", limits.MaximumEntryCount);

    ValidateCount("total track", runtime.InstanceCount, spatialIndex.TrackCount);
    ValidateCount("resolved track", runtime.ResolvedTrackCount, spatialIndex.ResolvedTrackCount);
    ValidateCount(
      "unresolved resource track",
      runtime.UnresolvedResourceTrackCount,
      spatialIndex.UnresolvedResourceTrackCount);
    ValidateCount(
      "unsupported geometry track",
      runtime.UnsupportedGeometryTrackCount,
      spatialIndex.UnsupportedGeometryTrackCount);
    ValidateCount(
      "unsupported topology track",
      runtime.UnsupportedTopologyTrackCount,
      spatialIndex.UnsupportedTopologyTrackCount);
    ValidateRuntimeConservation(runtime);
    ValidateSpatialConservation(spatialIndex);

    var runtimeByTrackId = IndexRuntimeEntries(runtime);
    var resolvedEntries = new RideInstanceTrackRuntimeEntry[spatialIndex.Entries.Count];
    var indexedTrackIds = new HashSet<ulong>();
    foreach (var index in Enumerable.Range(0, spatialIndex.Entries.Count)) {
      var spatial = spatialIndex.Entries[index];
      if (spatial is null || spatial.TrackSourceEntryId == 0)
        throw Invalid($"spatial entry {index} is incomplete");
      if (spatial.Status is not (
        RideTrackGeometryStatus.OpenTrack or RideTrackGeometryStatus.Circuit))
        throw Invalid(
          $"spatial entry {spatial.TrackSourceEntryId} has skipped status {spatial.Status}");
      if (!indexedTrackIds.Add(spatial.TrackSourceEntryId))
        throw Invalid($"spatial track ID {spatial.TrackSourceEntryId} is duplicated");
      if (!runtimeByTrackId.TryGetValue(spatial.TrackSourceEntryId, out var entry))
        throw Invalid(
          $"spatial track {spatial.TrackSourceEntryId} has no runtime identity");
      if (!entry.IsResolved)
        throw Invalid(
          $"spatial track {spatial.TrackSourceEntryId} maps to a skipped runtime entry");
      if (entry.Status != spatial.Status)
        throw Invalid(
          $"track {spatial.TrackSourceEntryId} status changed from {entry.Status} to " +
          $"{spatial.Status}");
      if (entry.DatTrackIndex != spatial.DatTrackIndex)
        throw Invalid(
          $"track {spatial.TrackSourceEntryId} DAT index changed from " +
          $"{entry.DatTrackIndex} to {spatial.DatTrackIndex}");
      resolvedEntries[index] = entry;
    }

    foreach (var entry in runtime.Entries) {
      if (entry.IsResolved && !indexedTrackIds.Contains(entry.TrackEntryId))
        throw Invalid($"resolved runtime track {entry.TrackEntryId} is not indexed");
    }

    return new(
      resolvedEntries,
      runtimeByTrackId,
      spatialIndex,
      runtime.InstanceCount,
      limits);
  }

  /// <summary>
  /// Returns point-to-bound hits in the spatial index's exact distance and DAT tie order.
  /// </summary>
  public IReadOnlyList<RideInstanceTrackSpatialHit> Query(
    Vector3 point,
    int maximumResults
  ) {
    if (maximumResults < 0 || maximumResults > limits.MaximumQueryResults)
      throw new ArgumentOutOfRangeException(nameof(maximumResults));

    var spatialHits = spatialIndex.Query(point, maximumResults);
    if (spatialHits.Count == 0)
      return Array.Empty<RideInstanceTrackSpatialHit>();

    var hits = new RideInstanceTrackSpatialHit[spatialHits.Count];
    foreach (var index in Enumerable.Range(0, spatialHits.Count)) {
      var spatial = spatialHits[index];
      if (!runtimeByTrackId.TryGetValue(
        spatial.Entry.TrackSourceEntryId,
        out var runtime))
        throw Invalid(
          $"queried track {spatial.Entry.TrackSourceEntryId} lost its runtime identity");
      hits[index] = new(runtime, spatial);
    }
    return Array.AsReadOnly(hits);
  }

  private static IReadOnlyDictionary<ulong, RideInstanceTrackRuntimeEntry> IndexRuntimeEntries(
    RideInstanceTrackRuntimeRegistry runtime
  ) {
    var result = new Dictionary<ulong, RideInstanceTrackRuntimeEntry>(runtime.InstanceCount);
    var instanceIds = new HashSet<ulong>();
    var trackIndices = new HashSet<int>();
    foreach (var index in Enumerable.Range(0, runtime.Entries.Count)) {
      var entry = runtime.Entries[index];
      if (entry?.Track is null || entry.Geometry?.Track is null)
        throw Invalid($"runtime entry {index} is incomplete");
      if (entry.InstanceEntryId == 0 || !instanceIds.Add(entry.InstanceEntryId))
        throw Invalid(
          $"runtime instance ID {entry.InstanceEntryId} is missing or duplicated");
      if (entry.TrackEntryId == 0 || !result.TryAdd(entry.TrackEntryId, entry))
        throw Invalid($"runtime track ID {entry.TrackEntryId} is missing or duplicated");
      if (entry.DatTrackIndex < 0 || entry.DatTrackIndex >= runtime.InstanceCount
        || !trackIndices.Add(entry.DatTrackIndex))
        throw Invalid(
          $"runtime track {entry.TrackEntryId} DAT index {entry.DatTrackIndex} is invalid");
      if (!ReferenceEquals(entry.Track, entry.Geometry.Track)
        || entry.TrackEntryId != entry.Geometry.Track.SourceEntryId)
        throw Invalid(
          $"runtime track {entry.TrackEntryId} changed exact semantic object identity");
      var expectedResolved = entry.Status is (
        RideTrackGeometryStatus.OpenTrack or RideTrackGeometryStatus.Circuit);
      if (entry.IsResolved != expectedResolved)
        throw Invalid(
          $"runtime track {entry.TrackEntryId} has inconsistent status and traversal state");
    }
    return result;
  }

  private static void ValidateRuntimeConservation(
    RideInstanceTrackRuntimeRegistry runtime
  ) {
    var outcomeCount = Convert.ToInt64(runtime.ResolvedTrackCount)
      + runtime.UnresolvedResourceTrackCount
      + runtime.UnsupportedGeometryTrackCount
      + runtime.UnsupportedTopologyTrackCount;
    if (outcomeCount != runtime.InstanceCount)
      throw Invalid(
        $"runtime outcomes do not conserve {runtime.InstanceCount} tracks");
  }

  private static void ValidateSpatialConservation(
    RideTrackGeometrySpatialIndex spatialIndex
  ) {
    var outcomeCount = Convert.ToInt64(spatialIndex.ResolvedTrackCount)
      + spatialIndex.UnresolvedResourceTrackCount
      + spatialIndex.UnsupportedGeometryTrackCount
      + spatialIndex.UnsupportedTopologyTrackCount;
    if (outcomeCount != spatialIndex.TrackCount)
      throw Invalid(
        $"spatial outcomes do not conserve {spatialIndex.TrackCount} tracks");
  }

  private static void ValidateCount(string description, int runtime, int spatial) {
    if (runtime != spatial)
      throw Invalid(
        $"{description} count changed from runtime {runtime} to spatial {spatial}");
  }

  private static void ValidateLimits(RideInstanceTrackSpatialQueryLimits limits) {
    if (limits.MaximumEntryCount <= 0 || limits.MaximumQueryResults <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-instance track spatial query input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, int maximum) =>
    new($"Ride-instance track spatial query {resource} count exceeds the limit {maximum}.");
}

internal readonly record struct RideInstanceTrackSpatialQueryLimits(
  int MaximumEntryCount,
  int MaximumQueryResults
) {
  public static RideInstanceTrackSpatialQueryLimits Default { get; } =
    new(100_000, 100_000);
}
