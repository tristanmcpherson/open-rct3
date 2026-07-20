// Ride Instance Track Graph
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>One exact reciprocal DAT ride-instance to semantic track edge.</summary>
internal sealed record RideInstanceTrackLink(
  DatTrackedRideInstanceData Instance,
  RideTrack Track
) {
  public ulong InstanceEntryId => Instance.EntryId;
  public ulong TrackEntryId => Track.SourceEntryId;
}

/// <summary>
/// A bounded, validated one-to-one graph between DAT <c>TrackedRideInstance</c> entries and
/// semantic <see cref="RideTrack"/> roots.
/// </summary>
internal sealed class RideInstanceTrackGraph {
  private const int MaximumNodeCount = 100_000;

  public IReadOnlyList<RideInstanceTrackLink> Links { get; }

  private RideInstanceTrackGraph(RideInstanceTrackLink[] links) {
    Links = Array.AsReadOnly((RideInstanceTrackLink[])links.Clone());
  }

  /// <summary>
  /// Builds the graph only after every forward and reciprocal reference is unique and complete.
  /// </summary>
  public static RideInstanceTrackGraph Build(
    IReadOnlyList<DatTrackedRideInstanceData> instances,
    IReadOnlyList<RideTrack> tracks
  ) {
    ArgumentNullException.ThrowIfNull(instances);
    ArgumentNullException.ThrowIfNull(tracks);
    ValidateCount(instances.Count, "ride-instance");
    ValidateCount(tracks.Count, "ride-track");

    var instancesById = IndexInstances(instances);
    var tracksById = IndexTracks(tracks);
    ValidateForwardReferences(instances, tracksById);
    ValidateReciprocalReferences(tracks, instancesById);

    var links = new RideInstanceTrackLink[instances.Count];
    for (var index = 0; index < instances.Count; index++) {
      var instance = instances[index];
      links[index] = new RideInstanceTrackLink(instance, tracksById[instance.Track]);
    }
    return new RideInstanceTrackGraph(links);
  }

  private static IReadOnlyDictionary<ulong, DatTrackedRideInstanceData> IndexInstances(
    IReadOnlyList<DatTrackedRideInstanceData> instances
  ) {
    var byId = new Dictionary<ulong, DatTrackedRideInstanceData>(instances.Count);
    foreach (var instance in instances) {
      if (instance is null)
        throw new ArgumentException(
          "Ride instances cannot contain null.",
          nameof(instances));
      if (instance.EntryId == 0 || !byId.TryAdd(instance.EntryId, instance))
        throw new InvalidDataException(
          $"Ride-instance track graph has a missing or duplicate instance entry ID " +
          $"{instance.EntryId}.");
    }
    return byId;
  }

  private static IReadOnlyDictionary<ulong, RideTrack> IndexTracks(
    IReadOnlyList<RideTrack> tracks
  ) {
    var byId = new Dictionary<ulong, RideTrack>(tracks.Count);
    foreach (var track in tracks) {
      if (track is null)
        throw new ArgumentException(
          "Ride tracks cannot contain null.",
          nameof(tracks));
      if (track.SourceEntryId == 0 || !byId.TryAdd(track.SourceEntryId, track))
        throw new InvalidDataException(
          $"Ride-instance track graph has a missing or duplicate Track entry ID " +
          $"{track.SourceEntryId}.");
    }
    return byId;
  }

  private static void ValidateForwardReferences(
    IReadOnlyList<DatTrackedRideInstanceData> instances,
    IReadOnlyDictionary<ulong, RideTrack> tracksById
  ) {
    foreach (var instance in instances) {
      if (instance.Track == 0 || !tracksById.TryGetValue(instance.Track, out var track))
        throw new InvalidDataException(
          $"TrackedRideInstance entry {instance.EntryId} references missing Track " +
          $"{instance.Track}.");
      if (track.TrackedRideInstanceReference != instance.EntryId)
        throw new InvalidDataException(
          $"TrackedRideInstance entry {instance.EntryId} references Track {instance.Track}, " +
          $"but that Track references instance {track.TrackedRideInstanceReference}.");
    }
  }

  private static void ValidateReciprocalReferences(
    IReadOnlyList<RideTrack> tracks,
    IReadOnlyDictionary<ulong, DatTrackedRideInstanceData> instancesById
  ) {
    foreach (var track in tracks) {
      var reference = track.TrackedRideInstanceReference;
      if (reference == 0 || !instancesById.TryGetValue(reference, out var instance))
        throw new InvalidDataException(
          $"Track entry {track.SourceEntryId} references missing TrackedRideInstance " +
          $"{reference}.");
      if (instance.Track != track.SourceEntryId)
        throw new InvalidDataException(
          $"Track entry {track.SourceEntryId} references TrackedRideInstance {reference}, " +
          $"but that instance references Track {instance.Track}.");
    }
  }

  private static void ValidateCount(int count, string description) {
    if (count > MaximumNodeCount)
      throw new InvalidDataException(
        $"Ride-instance track graph {description} count exceeds " +
        $"the limit {MaximumNodeCount}.");
  }
}
