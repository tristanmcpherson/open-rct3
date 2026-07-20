// Ride Instance Track Runtime Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One exact DAT ride instance composed with its typed runtime track outcome.</summary>
internal sealed record RideInstanceTrackRuntimeEntry(
  int DatInstanceIndex,
  int DatTrackIndex,
  RideInstanceTrackLink Identity,
  RideTrackGeometryLink Geometry,
  TrackGraphTraversal? GraphTraversal,
  TrackCircuitTraversal? CircuitTraversal
) {
  public DatTrackedRideInstanceData Instance => Identity.Instance;
  public RideTrack Track => Identity.Track;
  public ulong InstanceEntryId => Identity.InstanceEntryId;
  public ulong TrackEntryId => Identity.TrackEntryId;
  public RideTrackGeometryStatus Status => Geometry.Status;
  public TrackGraph? Graph => Geometry.Graph;
  public TrackCircuit? Circuit => Geometry.Circuit;
  public bool IsResolved => GraphTraversal != null || CircuitTraversal != null;
}

/// <summary>
/// A bounded immutable registry from exact DAT ride-instance identities to runtime track geometry.
/// </summary>
/// <remarks>
/// This composes track identity and traversal only. It does not resolve vehicles, trains, physics,
/// operating state, or clearance.
/// </remarks>
internal sealed class RideInstanceTrackRuntimeRegistry {
  public IReadOnlyList<RideInstanceTrackRuntimeEntry> Entries { get; }
  public int InstanceCount => Entries.Count;
  public int ResolvedTrackCount { get; }
  public int OpenTrackCount { get; }
  public int CircuitTrackCount { get; }
  public int UnresolvedResourceTrackCount { get; }
  public int UnsupportedGeometryTrackCount { get; }
  public int UnsupportedTopologyTrackCount { get; }

  private RideInstanceTrackRuntimeRegistry(
    RideInstanceTrackRuntimeEntry[] entries,
    int openTrackCount,
    int circuitTrackCount,
    int unresolvedResourceTrackCount,
    int unsupportedGeometryTrackCount,
    int unsupportedTopologyTrackCount
  ) {
    Entries = Array.AsReadOnly(entries);
    OpenTrackCount = openTrackCount;
    CircuitTrackCount = circuitTrackCount;
    ResolvedTrackCount = openTrackCount + circuitTrackCount;
    UnresolvedResourceTrackCount = unresolvedResourceTrackCount;
    UnsupportedGeometryTrackCount = unsupportedGeometryTrackCount;
    UnsupportedTopologyTrackCount = unsupportedTopologyTrackCount;
  }

  public static RideInstanceTrackRuntimeRegistry Build(
    RideInstanceTrackGraph instanceTracks,
    RideTrackGeometryResolution geometry
  ) => Build(instanceTracks, geometry, RideInstanceTrackRuntimeRegistryLimits.Default);

  internal static RideInstanceTrackRuntimeRegistry Build(
    RideInstanceTrackGraph instanceTracks,
    RideTrackGeometryResolution geometry,
    RideInstanceTrackRuntimeRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(instanceTracks);
    ArgumentNullException.ThrowIfNull(geometry);
    ValidateLimits(limits);
    if (instanceTracks.Links is null)
      throw Invalid("instance-track link list is null");
    if (geometry.Tracks is null)
      throw Invalid("geometry track list is null");
    ValidateCount(instanceTracks.Links.Count, limits.MaximumEntryCount, "instance");
    ValidateCount(geometry.Tracks.Count, limits.MaximumEntryCount, "geometry track");
    if (instanceTracks.Links.Count != geometry.Tracks.Count)
      throw Invalid(
        $"instance count {instanceTracks.Links.Count} does not conserve geometry track count " +
        $"{geometry.Tracks.Count}");
    if (geometry.UnresolvedResourceTrackCount < 0
      || geometry.UnsupportedGeometryTrackCount < 0
      || geometry.UnsupportedTopologyTrackCount < 0)
      throw Invalid("advertised geometry outcome counts cannot be negative");

    var geometryByTrackId = new Dictionary<
      ulong,
      (int DatTrackIndex, RideTrackGeometryLink Link)>(geometry.Tracks.Count);
    var openCount = 0;
    var circuitCount = 0;
    var unresolvedCount = 0;
    var unsupportedGeometryCount = 0;
    var unsupportedTopologyCount = 0;
    foreach (var index in Enumerable.Range(0, geometry.Tracks.Count)) {
      var link = geometry.Tracks[index];
      ValidateGeometryLink(
        link,
        index,
        ref openCount,
        ref circuitCount,
        ref unresolvedCount,
        ref unsupportedGeometryCount,
        ref unsupportedTopologyCount);
      if (!geometryByTrackId.TryAdd(link.Track.SourceEntryId, (index, link)))
        throw Invalid($"geometry track ID {link.Track.SourceEntryId} is duplicated");
    }

    var advertisedCount = Convert.ToInt64(openCount + circuitCount)
      + geometry.UnresolvedResourceTrackCount
      + geometry.UnsupportedGeometryTrackCount
      + geometry.UnsupportedTopologyTrackCount;
    if (advertisedCount != geometry.Tracks.Count)
      throw Invalid(
        $"advertised geometry outcomes do not conserve {geometry.Tracks.Count} tracks");
    ValidateOutcomeCount(
      "unresolved resource",
      unresolvedCount,
      geometry.UnresolvedResourceTrackCount);
    ValidateOutcomeCount(
      "unsupported geometry",
      unsupportedGeometryCount,
      geometry.UnsupportedGeometryTrackCount);
    ValidateOutcomeCount(
      "unsupported topology",
      unsupportedTopologyCount,
      geometry.UnsupportedTopologyTrackCount);

    var composed = new (
      int DatInstanceIndex,
      RideInstanceTrackLink Identity,
      int DatTrackIndex,
      RideTrackGeometryLink Geometry)[instanceTracks.Links.Count];
    var instanceIds = new HashSet<ulong>();
    var linkedTrackIds = new HashSet<ulong>();
    var pieceCount = 0ul;
    foreach (var index in Enumerable.Range(0, instanceTracks.Links.Count)) {
      var identity = instanceTracks.Links[index];
      ValidateIdentity(identity, index, instanceIds, linkedTrackIds);
      if (!geometryByTrackId.TryGetValue(identity.TrackEntryId, out var match))
        throw Invalid(
          $"instance {identity.InstanceEntryId} references missing geometry track " +
          $"{identity.TrackEntryId}");
      if (!ReferenceEquals(identity.Track, match.Link.Track))
        throw Invalid(
          $"track {identity.TrackEntryId} changed exact semantic object identity");
      ReservePieces(match.Link, limits, ref pieceCount);
      composed[index] = (index, identity, match.DatTrackIndex, match.Link);
    }
    if (linkedTrackIds.Count != geometryByTrackId.Count)
      throw Invalid(
        $"instance links conserve {linkedTrackIds.Count} of {geometryByTrackId.Count} tracks");

    var entries = new RideInstanceTrackRuntimeEntry[composed.Length];
    foreach (var index in Enumerable.Range(0, composed.Length)) {
      var item = composed[index];
      var graphTraversal = item.Geometry.Status == RideTrackGeometryStatus.OpenTrack
        ? new TrackGraphTraversal(item.Geometry.Graph!)
        : null;
      var circuitTraversal = item.Geometry.Status == RideTrackGeometryStatus.Circuit
        ? new TrackCircuitTraversal(item.Geometry.Circuit!)
        : null;
      entries[index] = new(
        item.DatInstanceIndex,
        item.DatTrackIndex,
        item.Identity,
        item.Geometry,
        graphTraversal,
        circuitTraversal);
    }

    return new(
      entries,
      openCount,
      circuitCount,
      unresolvedCount,
      unsupportedGeometryCount,
      unsupportedTopologyCount);
  }

  private static void ValidateGeometryLink(
    RideTrackGeometryLink link,
    int index,
    ref int openCount,
    ref int circuitCount,
    ref int unresolvedCount,
    ref int unsupportedGeometryCount,
    ref int unsupportedTopologyCount
  ) {
    if (link?.Track is null || link.Track.SourceEntryId == 0)
      throw Invalid($"geometry track {index} is incomplete");
    switch (link.Status) {
      case RideTrackGeometryStatus.OpenTrack:
        if (link.Graph is null || link.Circuit != null)
          throw Invalid(
            $"open track {link.Track.SourceEntryId} has inconsistent geometry");
        openCount++;
        break;
      case RideTrackGeometryStatus.Circuit:
        if (link.Circuit is null || link.Graph != null)
          throw Invalid(
            $"circuit {link.Track.SourceEntryId} has inconsistent geometry");
        circuitCount++;
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

  private static void ValidateIdentity(
    RideInstanceTrackLink identity,
    int index,
    ISet<ulong> instanceIds,
    ISet<ulong> trackIds
  ) {
    if (identity?.Instance is null || identity.Track is null)
      throw Invalid($"instance-track link {index} is incomplete");
    if (identity.InstanceEntryId == 0 || !instanceIds.Add(identity.InstanceEntryId))
      throw Invalid(
        $"instance entry ID {identity.InstanceEntryId} is missing or duplicated");
    if (identity.TrackEntryId == 0 || !trackIds.Add(identity.TrackEntryId))
      throw Invalid($"track entry ID {identity.TrackEntryId} is missing or duplicated");
    if (identity.Instance.Track != identity.TrackEntryId
      || identity.Track.TrackedRideInstanceReference != identity.InstanceEntryId)
      throw Invalid(
        $"instance {identity.InstanceEntryId} and track {identity.TrackEntryId} are not " +
        "reciprocal");
  }

  private static void ReservePieces(
    RideTrackGeometryLink geometry,
    RideInstanceTrackRuntimeRegistryLimits limits,
    ref ulong pieceCount
  ) {
    var addition = geometry.Status switch {
      RideTrackGeometryStatus.OpenTrack => geometry.Graph!.Edges.Count,
      RideTrackGeometryStatus.Circuit => geometry.Circuit!.Pieces.Count,
      _ => 0,
    };
    var count = Convert.ToUInt64(addition);
    var maximum = Convert.ToUInt64(limits.MaximumPieceCount);
    if (count > maximum || pieceCount > maximum - count)
      throw Limit("traversal piece", limits.MaximumPieceCount);
    pieceCount += count;
  }

  private static void ValidateSkipped(RideTrackGeometryLink link) {
    if (link.Graph != null || link.Circuit != null || link.IsResolved)
      throw Invalid(
        $"skipped track {link.Track.SourceEntryId} unexpectedly contains resolved geometry");
  }

  private static void ValidateOutcomeCount(string outcome, int actual, int advertised) {
    if (actual != advertised)
      throw Invalid(
        $"geometry advertises {advertised} {outcome} tracks, found {actual}");
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw Limit(description, maximum);
  }

  private static void ValidateLimits(RideInstanceTrackRuntimeRegistryLimits limits) {
    if (limits.MaximumEntryCount <= 0 || limits.MaximumPieceCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-instance track runtime registry input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, int maximum) =>
    new($"Ride-instance track runtime registry {resource} count exceeds the limit {maximum}.");
}

internal readonly record struct RideInstanceTrackRuntimeRegistryLimits(
  int MaximumEntryCount,
  int MaximumPieceCount
) {
  public static RideInstanceTrackRuntimeRegistryLimits Default { get; } =
    new(100_000, 1_000_000);
}
