// Ride Track Topology Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Validates and exposes the serialized <c>Track</c>/<c>TrackSegment</c> topology.</summary>
internal static class RideTrackTopologyLoader {
  private const int MaximumTrackCount = 100_000;
  private const int MaximumSegmentCount = 1_000_000;
  private const int MaximumPieceCount = 1_000_000;

  public static void Load(
    Park park,
    IReadOnlyList<DatRideTrackData> tracks,
    IReadOnlyList<DatTrackSegmentData> segments
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(tracks);
    ArgumentNullException.ThrowIfNull(segments);

    if (tracks.Count > MaximumTrackCount)
      throw new InvalidDataException(
        $"Decoded ride-track count exceeds the limit {MaximumTrackCount}.");
    if (segments.Count > MaximumSegmentCount)
      throw new InvalidDataException(
        $"Decoded TrackSegment count exceeds the limit {MaximumSegmentCount}.");
    if (park.RideTrackPlacements.Count > MaximumPieceCount)
      throw new InvalidDataException(
        $"Decoded TrackPiece count exceeds the limit {MaximumPieceCount}.");

    var placementsById = IndexTrackPlacements(park.RideTrackPlacements);
    var tracksById = IndexTracks(tracks);
    var segmentsById = IndexSegments(segments);
    var segmentsByTrack = GroupSegmentsByTrack(segments, tracksById);
    ValidateSegmentReferences(segments, segmentsById, placementsById);
    ValidatePlacementSegments(park.RideTrackPlacements, segmentsById);
    var placementsBySegment = GroupPlacementsBySegment(park.RideTrackPlacements);

    var convertedTracks = new List<RideTrack>(tracks.Count);
    var convertedSegments = new List<RideTrackSegment>(segments.Count);
    var claimedTrackPieces = new HashSet<ulong>();
    foreach (var source in tracks) {
      ValidateDirection(source.Direction, "Track", source.EntryId);
      if (!segmentsById.TryGetValue(source.FirstSegment, out var firstSegment) ||
          firstSegment.Track != source.EntryId)
        throw new InvalidDataException(
          $"Decoded Track entry {source.EntryId} has an invalid FirstSegment reference " +
          $"{source.FirstSegment}.");
      if (!segmentsById.TryGetValue(source.LastSegment, out var lastSegment) ||
          lastSegment.Track != source.EntryId)
        throw new InvalidDataException(
          $"Decoded Track entry {source.EntryId} has an invalid LastSegment reference " +
          $"{source.LastSegment}.");

      if (!segmentsByTrack.TryGetValue(source.EntryId, out var trackSegments) ||
          trackSegments.Count == 0)
        throw new InvalidDataException(
          $"Decoded Track entry {source.EntryId} has no TrackSegment entries.");

      var orderedSegments = OrderTrackSegments(
        source,
        trackSegments,
        segmentsById);
      var hasSerializedTrackPieceOrder = source.TrackPieces is { Count: > 0 };
      var topology = hasSerializedTrackPieceOrder
        ? ValidateSerializedTrackPieceMembership(
          source.EntryId,
          source.TrackPieces!,
          orderedSegments,
          placementsById,
          placementsBySegment,
          segmentsById,
          claimedTrackPieces)
        : DeriveTrackPieceTopology(
          source.EntryId,
          orderedSegments,
          placementsById,
          placementsBySegment,
          claimedTrackPieces);
      var orderedPieceIds = topology.OrderedPieceIds;
      var pieceIds = new HashSet<ulong>(orderedPieceIds);
      foreach (var segment in trackSegments) {
        if (!pieceIds.Contains(segment.FirstPiece) || !pieceIds.Contains(segment.LastPiece))
          throw new InvalidDataException(
            $"Decoded TrackSegment entry {segment.EntryId} has boundary pieces outside Track " +
            $"{source.EntryId}: FirstPiece={segment.FirstPiece}, LastPiece={segment.LastPiece}, " +
            $"authoritative membership count={orderedPieceIds.Length}.");
      }

      var segmentIds = new ulong[orderedSegments.Length];
      for (var index = 0; index < segmentIds.Length; index++)
        segmentIds[index] = orderedSegments[index].EntryId;
      var colours = source.TrackFlexiColours;
      convertedTracks.Add(new RideTrack(
        source.EntryId,
        source.Direction,
        source.FirstSegment,
        source.LastSegment,
        topology.IsCircuit,
        source.Prototype,
        hasSerializedTrackPieceOrder,
        orderedPieceIds,
        segmentIds,
        colours.Col0,
        colours.Col1,
        colours.Col2,
        source.TrackedRideInstance,
        source.FlippedTrackSections,
        source.TunnelLightColour,
        serializedIsCircuit: source.IsCircuit,
        hasAuthoritativeTrackPieceOrder: true));
    }

    if (claimedTrackPieces.Count != placementsById.Count)
      throw new InvalidDataException(
        "Decoded ride-track topology does not claim every TrackPiece placement exactly once.");

    foreach (var source in segments)
      convertedSegments.Add(new RideTrackSegment(
        source.EntryId,
        source.Track,
        source.Direction,
        source.FirstPiece,
        source.LastPiece,
        source.NextSegment,
        source.PrevSegment,
        source.Prototype));

    park.RideTracks.AddRange(convertedTracks);
    park.RideTrackSegments.AddRange(convertedSegments);
  }

  private static TrackPieceTopology ValidateSerializedTrackPieceMembership(
    ulong trackEntryId,
    IReadOnlyList<ulong> sourcePieceIds,
    IReadOnlyList<DatTrackSegmentData> orderedSegments,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById,
    IReadOnlyDictionary<ulong, List<RideTrackPlacement>> placementsBySegment,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById,
    ISet<ulong> claimedTrackPieces
  ) {
    if (sourcePieceIds.Count > MaximumPieceCount)
      throw new InvalidDataException(
        $"Decoded Track entry {trackEntryId} piece count exceeds the limit " +
        $"{MaximumPieceCount}.");

    var pieceIds = new HashSet<ulong>();
    var orderedPieceIds = new ulong[sourcePieceIds.Count];
    for (var index = 0; index < sourcePieceIds.Count; index++) {
      var pieceId = sourcePieceIds[index];
      if (pieceId == 0 || !pieceIds.Add(pieceId) || !claimedTrackPieces.Add(pieceId))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} contains a missing or duplicate TrackPiece " +
          $"reference {pieceId}.");
      if (!placementsById.TryGetValue(pieceId, out var placement))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} references missing TrackPiece {pieceId}.");
      if (!segmentsById.TryGetValue(placement.SegmentReference, out var segment) ||
          segment.Track != trackEntryId)
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {pieceId} has inconsistent TrackSegment ownership.");
      orderedPieceIds[index] = pieceId;
    }

    var segmentTopologies = new List<SegmentPieceTopology>(orderedSegments.Count);
    foreach (var segment in orderedSegments) {
      if (!placementsBySegment.TryGetValue(segment.EntryId, out var segmentPlacements))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} has no TrackPiece entries.");
      var topology = DeriveSegmentPieceTopology(
        trackEntryId,
        segment,
        segmentPlacements,
        placementsById);
      var serializedSegmentOrder = orderedPieceIds.Where(pieceId =>
        placementsById[pieceId].SegmentReference == segment.EntryId);
      if (!serializedSegmentOrder.SequenceEqual(topology.OrderedPieceIds))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} serialized TrackPiece order disagrees with " +
          $"TrackSegment {segment.EntryId} Next/Prev links.");
      segmentTopologies.Add(topology);
    }
    return new(orderedPieceIds, ClassifyTrackClosure(segmentTopologies));
  }

  private static TrackPieceTopology DeriveTrackPieceTopology(
    ulong trackEntryId,
    IReadOnlyList<DatTrackSegmentData> orderedSegments,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById,
    IReadOnlyDictionary<ulong, List<RideTrackPlacement>> placementsBySegment,
    ISet<ulong> claimedTrackPieces
  ) {
    var pieceIds = new List<ulong>();
    var segmentTopologies = new List<SegmentPieceTopology>(orderedSegments.Count);
    foreach (var segment in orderedSegments) {
      if (!placementsBySegment.TryGetValue(segment.EntryId, out var segmentPlacements))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} has no TrackPiece entries.");
      var topology = DeriveSegmentPieceTopology(
        trackEntryId,
        segment,
        segmentPlacements,
        placementsById);
      segmentTopologies.Add(topology);
      foreach (var pieceId in topology.OrderedPieceIds) {
        if (!claimedTrackPieces.Add(pieceId))
          throw new InvalidDataException(
            $"Decoded Track entry {trackEntryId} derives duplicate TrackPiece reference " +
            $"{pieceId}.");
        pieceIds.Add(pieceId);
      }
      if (pieceIds.Count > MaximumPieceCount)
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} piece count exceeds the limit " +
          $"{MaximumPieceCount}.");
    }

    return new([.. pieceIds], ClassifyTrackClosure(segmentTopologies));
  }

  private static bool? ClassifyTrackClosure(
    IReadOnlyList<SegmentPieceTopology> segmentTopologies
  ) => segmentTopologies.Count == 1
    ? segmentTopologies[0].IsClosed
    : segmentTopologies.All(topology => !topology.IsClosed)
      ? false
      : null;

  private static DatTrackSegmentData[] OrderTrackSegments(
    DatRideTrackData track,
    IReadOnlyList<DatTrackSegmentData> trackSegments,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById
  ) {
    if (trackSegments.Count > MaximumSegmentCount)
      throw new InvalidDataException(
        $"Decoded Track entry {track.EntryId} segment count exceeds the limit " +
        $"{MaximumSegmentCount}.");

    var ordered = new List<DatTrackSegmentData>(trackSegments.Count);
    var visited = new HashSet<ulong>();
    var currentId = track.FirstSegment;
    var reachedLast = false;
    foreach (var _ in Enumerable.Range(0, trackSegments.Count)) {
      if (!segmentsById.TryGetValue(currentId, out var current) ||
          current.Track != track.EntryId)
        throw new InvalidDataException(
          $"Decoded Track entry {track.EntryId} reaches missing or foreign TrackSegment " +
          $"{currentId} before LastSegment {track.LastSegment}.");
      if (!visited.Add(currentId))
        throw new InvalidDataException(
          $"Decoded Track entry {track.EntryId} contains a TrackSegment cycle before " +
          $"LastSegment {track.LastSegment}.");

      ordered.Add(current);
      if (currentId == track.LastSegment) {
        reachedLast = true;
        break;
      }

      if (!segmentsById.TryGetValue(current.NextSegment, out var next) ||
          next.Track != track.EntryId)
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {current.EntryId} reaches missing or foreign " +
          $"NextSegment {current.NextSegment} before Track {track.EntryId} LastSegment.");
      if (next.PrevSegment != current.EntryId)
        throw new InvalidDataException(
          $"Decoded TrackSegment entries {current.EntryId} and {next.EntryId} have " +
          "non-reciprocal NextSegment/PrevSegment links.");
      currentId = next.EntryId;
    }

    if (!reachedLast)
      throw new InvalidDataException(
        $"Decoded Track entry {track.EntryId} does not reach LastSegment " +
        $"{track.LastSegment} within {trackSegments.Count} segments.");
    if (ordered.Count != trackSegments.Count)
      throw new InvalidDataException(
        $"Decoded Track entry {track.EntryId} reaches {ordered.Count} of " +
        $"{trackSegments.Count} owned TrackSegment entries.");

    var first = ordered[0];
    var last = ordered[^1];
    var nextCloses = last.NextSegment == first.EntryId;
    var previousCloses = first.PrevSegment == last.EntryId;
    if (nextCloses != previousCloses)
      throw new InvalidDataException(
        $"Decoded Track entry {track.EntryId} has a one-sided TrackSegment boundary cycle.");
    if (!nextCloses) {
      if (segmentsById.TryGetValue(last.NextSegment, out var next) &&
          next.Track == track.EntryId)
        throw new InvalidDataException(
          $"Decoded Track entry {track.EntryId} LastSegment {last.EntryId} points to " +
          $"owned TrackSegment {next.EntryId} outside its authoritative order.");
      if (segmentsById.TryGetValue(first.PrevSegment, out var previous) &&
          previous.Track == track.EntryId)
        throw new InvalidDataException(
          $"Decoded Track entry {track.EntryId} FirstSegment {first.EntryId} points to " +
          $"owned TrackSegment {previous.EntryId} outside its authoritative order.");
    }
    return [.. ordered];
  }

  private static SegmentPieceTopology DeriveSegmentPieceTopology(
    ulong trackEntryId,
    DatTrackSegmentData segment,
    IReadOnlyList<RideTrackPlacement> segmentPlacements,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById
  ) {
    if (segmentPlacements.Count > MaximumPieceCount)
      throw new InvalidDataException(
        $"Decoded TrackSegment entry {segment.EntryId} piece count exceeds the limit " +
        $"{MaximumPieceCount}.");

    var ordered = new List<ulong>(segmentPlacements.Count);
    var visited = new HashSet<ulong>();
    var currentId = segment.FirstPiece;
    var reachedLast = false;
    foreach (var _ in Enumerable.Range(0, segmentPlacements.Count)) {
      if (!placementsById.TryGetValue(currentId, out var current))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} reaches missing TrackPiece " +
          $"{currentId} before LastPiece {segment.LastPiece}.");
      if (current.SegmentReference != segment.EntryId ||
          current.OwnerReference != segment.EntryId)
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {currentId} has inconsistent ownership while walking " +
          $"TrackSegment {segment.EntryId} of Track {trackEntryId}.");
      if (!visited.Add(currentId))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} contains a TrackPiece cycle before " +
          $"LastPiece {segment.LastPiece}.");

      ordered.Add(currentId);
      if (currentId == segment.LastPiece) {
        reachedLast = true;
        break;
      }

      if (!placementsById.TryGetValue(current.NextPieceReference, out var next))
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {currentId} reaches missing Next TrackPiece " +
          $"{current.NextPieceReference} before TrackSegment {segment.EntryId} LastPiece.");
      if (next.SegmentReference != segment.EntryId)
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {currentId} crosses from TrackSegment {segment.EntryId} " +
          $"to TrackSegment {next.SegmentReference} before LastPiece.");
      if (next.PreviousPieceReference != currentId)
        throw new InvalidDataException(
          $"Decoded TrackPiece entries {currentId} and {next.SourceEntryId} have " +
          "non-reciprocal Next/Prev links.");
      currentId = next.SourceEntryId;
    }

    if (!reachedLast)
      throw new InvalidDataException(
        $"Decoded TrackSegment entry {segment.EntryId} does not reach LastPiece " +
        $"{segment.LastPiece} within {segmentPlacements.Count} pieces.");
    if (ordered.Count != segmentPlacements.Count)
      throw new InvalidDataException(
        $"Decoded TrackSegment entry {segment.EntryId} reaches {ordered.Count} of " +
        $"{segmentPlacements.Count} owned TrackPiece entries.");

    var isClosed = ClassifyOrderedPieceLinks(trackEntryId, [.. ordered], placementsById);
    return new([.. ordered], isClosed);
  }

  private static bool ClassifyOrderedPieceLinks(
    ulong trackEntryId,
    IReadOnlyList<ulong> orderedPieceIds,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById
  ) {
    if (orderedPieceIds.Count == 0)
      throw new InvalidDataException(
        $"Decoded Track entry {trackEntryId} contains no TrackPiece entries.");

    var first = placementsById[orderedPieceIds[0]];
    var last = placementsById[orderedPieceIds[^1]];
    var nextCloses = last.NextPieceReference == first.SourceEntryId;
    var previousCloses = first.PreviousPieceReference == last.SourceEntryId;
    if (nextCloses != previousCloses)
      throw new InvalidDataException(
        $"Decoded Track entry {trackEntryId} has a one-sided TrackPiece boundary cycle.");

    foreach (var index in Enumerable.Range(0, orderedPieceIds.Count)) {
      var current = placementsById[orderedPieceIds[index]];
      if (index > 0) {
        var previousId = orderedPieceIds[index - 1];
        if (current.PreviousPieceReference != previousId)
          throw new InvalidDataException(
            $"Decoded TrackPiece entry {current.SourceEntryId} does not reciprocally " +
            $"reference ordered predecessor {previousId}.");
      }
      if (index < orderedPieceIds.Count - 1) {
        var nextId = orderedPieceIds[index + 1];
        if (current.NextPieceReference != nextId)
          throw new InvalidDataException(
            $"Decoded TrackPiece entry {current.SourceEntryId} does not reference ordered " +
            $"successor {nextId}.");
      }
    }

    if (!nextCloses) {
      if (placementsById.ContainsKey(first.PreviousPieceReference))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} FirstPiece {first.SourceEntryId} points " +
          "to a decoded TrackPiece outside its authoritative order through Prev.");
      if (placementsById.ContainsKey(last.NextPieceReference))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} LastPiece {last.SourceEntryId} points " +
          "to a decoded TrackPiece outside its authoritative order through Next.");
    }
    return nextCloses;
  }

  private static IReadOnlyDictionary<ulong, List<RideTrackPlacement>>
    GroupPlacementsBySegment(IReadOnlyList<RideTrackPlacement> placements) {
    var bySegment = new Dictionary<ulong, List<RideTrackPlacement>>();
    foreach (var placement in placements) {
      if (!bySegment.TryGetValue(placement.SegmentReference, out var values)) {
        values = [];
        bySegment.Add(placement.SegmentReference, values);
      }
      values.Add(placement);
    }
    return bySegment;
  }

  private static IReadOnlyDictionary<ulong, RideTrackPlacement> IndexTrackPlacements(
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    var byId = new Dictionary<ulong, RideTrackPlacement>();
    foreach (var placement in placements) {
      if (placement.SourceEntryId == 0 || !byId.TryAdd(placement.SourceEntryId, placement))
        throw new InvalidDataException(
          $"Loaded ride track contains a missing or duplicate TrackPiece source entry ID " +
          $"{placement.SourceEntryId}.");
    }
    return byId;
  }

  private static IReadOnlyDictionary<ulong, DatRideTrackData> IndexTracks(
    IReadOnlyList<DatRideTrackData> tracks
  ) {
    var byId = new Dictionary<ulong, DatRideTrackData>();
    foreach (var track in tracks) {
      if (track.EntryId == 0 || !byId.TryAdd(track.EntryId, track))
        throw new InvalidDataException(
          $"Decoded ride tracks contain a missing or duplicate Track entry ID {track.EntryId}.");
    }
    return byId;
  }

  private static IReadOnlyDictionary<ulong, DatTrackSegmentData> IndexSegments(
    IReadOnlyList<DatTrackSegmentData> segments
  ) {
    var byId = new Dictionary<ulong, DatTrackSegmentData>();
    foreach (var segment in segments) {
      if (segment.EntryId == 0 || !byId.TryAdd(segment.EntryId, segment))
        throw new InvalidDataException(
          $"Decoded ride tracks contain a missing or duplicate TrackSegment entry ID " +
          $"{segment.EntryId}.");
    }
    return byId;
  }

  private static IReadOnlyDictionary<ulong, List<DatTrackSegmentData>> GroupSegmentsByTrack(
    IReadOnlyList<DatTrackSegmentData> segments,
    IReadOnlyDictionary<ulong, DatRideTrackData> tracksById
  ) {
    var byTrack = new Dictionary<ulong, List<DatTrackSegmentData>>();
    foreach (var segment in segments) {
      ValidateDirection(segment.Direction, "TrackSegment", segment.EntryId);
      if (segment.Track == 0 || !tracksById.ContainsKey(segment.Track))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} references missing Track " +
          $"{segment.Track}.");
      if (!byTrack.TryGetValue(segment.Track, out var values)) {
        values = [];
        byTrack.Add(segment.Track, values);
      }
      values.Add(segment);
    }
    return byTrack;
  }

  private static void ValidateSegmentReferences(
    IReadOnlyList<DatTrackSegmentData> segments,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById
  ) {
    foreach (var segment in segments) {
      if (segment.FirstPiece == 0 || !placementsById.ContainsKey(segment.FirstPiece) ||
          segment.LastPiece == 0 || !placementsById.ContainsKey(segment.LastPiece))
        throw new InvalidDataException(
          $"Decoded TrackSegment entry {segment.EntryId} references a missing boundary " +
          "TrackPiece.");
      ValidateOptionalNeighbour(segment, segment.NextSegment, "NextSegment", segmentsById);
      ValidateOptionalNeighbour(segment, segment.PrevSegment, "PrevSegment", segmentsById);
    }
  }

  private static void ValidatePlacementSegments(
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById
  ) {
    foreach (var placement in placements) {
      if (!segmentsById.ContainsKey(placement.SegmentReference) ||
          placement.OwnerReference != placement.SegmentReference)
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {placement.SourceEntryId} has inconsistent " +
          "TrackSegment ownership.");
    }
  }

  private static void ValidateOptionalNeighbour(
    DatTrackSegmentData source,
    ulong reference,
    string fieldName,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById
  ) {
    if (reference == 0 || !segmentsById.TryGetValue(reference, out var neighbour)) return;
    if (neighbour.Track != source.Track)
      throw new InvalidDataException(
        $"Decoded TrackSegment entry {source.EntryId} has a cross-track {fieldName} reference " +
        $"{reference}.");
  }

  private static void ValidateDirection(int value, string structureName, ulong entryId) {
    if (value is < 0 or > 3)
      throw new InvalidDataException(
        $"Decoded {structureName} entry {entryId} has invalid Direction value {value}.");
  }

  private sealed record TrackPieceTopology(
    ulong[] OrderedPieceIds,
    bool? IsCircuit);

  private sealed record SegmentPieceTopology(
    ulong[] OrderedPieceIds,
    bool IsClosed);
}
