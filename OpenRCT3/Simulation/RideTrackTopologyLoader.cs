// Ride Track Topology Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>Validates and exposes the serialized <c>Track</c>/<c>TrackSegment</c> topology.</summary>
internal static class RideTrackTopologyLoader {
  public static void Load(
    Park park,
    IReadOnlyList<DatRideTrackData> tracks,
    IReadOnlyList<DatTrackSegmentData> segments
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(tracks);
    ArgumentNullException.ThrowIfNull(segments);

    var placementsById = IndexTrackPlacements(park.RideTrackPlacements);
    var tracksById = IndexTracks(tracks);
    var segmentsById = IndexSegments(segments);
    var segmentsByTrack = GroupSegmentsByTrack(segments, tracksById);
    ValidateSegmentReferences(segments, segmentsById, placementsById);
    ValidatePlacementSegments(park.RideTrackPlacements, segmentsById);

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

      var hasSerializedTrackPieceOrder = source.TrackPieces is { Count: > 0 };
      var orderedPieceIds = hasSerializedTrackPieceOrder
        ? ValidateSerializedTrackPieceMembership(
          source.EntryId,
          source.TrackPieces!,
          placementsById,
          segmentsById,
          claimedTrackPieces)
        : DeriveTrackPieceMembership(
          source.EntryId,
          park.RideTrackPlacements,
          segmentsById,
          claimedTrackPieces);
      var pieceIds = new HashSet<ulong>(orderedPieceIds);
      foreach (var segment in trackSegments) {
        if (!pieceIds.Contains(segment.FirstPiece) || !pieceIds.Contains(segment.LastPiece))
          throw new InvalidDataException(
            $"Decoded TrackSegment entry {segment.EntryId} has boundary pieces outside Track " +
            $"{source.EntryId}: FirstPiece={segment.FirstPiece}, LastPiece={segment.LastPiece}, " +
            $"serialized membership count={orderedPieceIds.Length}.");
      }

      var segmentIds = new ulong[trackSegments.Count];
      for (var index = 0; index < segmentIds.Length; index++)
        segmentIds[index] = trackSegments[index].EntryId;
      var colours = source.TrackFlexiColours;
      convertedTracks.Add(new RideTrack(
        source.EntryId,
        source.Direction,
        source.FirstSegment,
        source.LastSegment,
        source.IsCircuit,
        source.Prototype,
        hasSerializedTrackPieceOrder,
        orderedPieceIds,
        segmentIds,
        colours.Col0,
        colours.Col1,
        colours.Col2,
        source.TrackedRideInstance,
        source.FlippedTrackSections,
        source.TunnelLightColour));
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

  private static ulong[] ValidateSerializedTrackPieceMembership(
    ulong trackEntryId,
    IReadOnlyList<ulong> sourcePieceIds,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placementsById,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById,
    ISet<ulong> claimedTrackPieces
  ) {
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
    return orderedPieceIds;
  }

  private static ulong[] DeriveTrackPieceMembership(
    ulong trackEntryId,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyDictionary<ulong, DatTrackSegmentData> segmentsById,
    ISet<ulong> claimedTrackPieces
  ) {
    var pieceIds = new List<ulong>();
    foreach (var placement in placements) {
      if (!segmentsById.TryGetValue(placement.SegmentReference, out var segment) ||
          segment.Track != trackEntryId)
        continue;
      if (!claimedTrackPieces.Add(placement.SourceEntryId))
        throw new InvalidDataException(
          $"Decoded Track entry {trackEntryId} derives duplicate TrackPiece reference " +
          $"{placement.SourceEntryId}.");
      pieceIds.Add(placement.SourceEntryId);
    }
    return [.. pieceIds];
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
}
