// Ride Track Graph Adapter
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>
/// Adapts the exact serialized order of one non-circuit DAT ride track into the current dual-rail
/// directed acyclic graph substrate.
/// </summary>
/// <remarks>
/// RCT3 circuit tracks cannot be represented by <see cref="TrackGraph"/>, which deliberately
/// rejects cycles. Expansion layouts that omit the serialized Track piece list are also rejected:
/// their loader-retained placement order is membership evidence, not authoritative traversal order.
/// </remarks>
internal static class RideTrackGraphAdapter {
  private const int MaximumPieceCount = 1_000_000;

  public static TrackGraph Build(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece
  ) {
    ArgumentNullException.ThrowIfNull(track);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(createPiece);

    if (track.IsCircuit != false)
      throw Invalid(track, "only explicitly non-circuit tracks can be adapted to a DAG");
    var orderedPlacements = ResolveOrderedPlacements(track, placements);
    ValidatePieceLinks(track, orderedPlacements, isCircuit: false);

    var nodes = new TrackNode[orderedPlacements.Length + 1];
    foreach (var index in Enumerable.Range(0, nodes.Length))
      nodes[index] = new TrackNode($"track-{track.SourceEntryId}-boundary-{index}");

    var edges = new TrackEdge[orderedPlacements.Length];
    foreach (var index in Enumerable.Range(0, orderedPlacements.Length)) {
      var placement = orderedPlacements[index];
      var piece = createPiece(placement);
      if (piece == null)
        throw Invalid(track, $"TrackPiece {placement.SourceEntryId} has no resolved geometry");
      edges[index] = new TrackEdge(
        $"track-piece-{placement.SourceEntryId}",
        nodes[index],
        nodes[index + 1],
        piece);
    }

    return new TrackGraph(nodes, edges);
  }

  /// <summary>Adapts one explicitly cyclic serialized piece order into a closed track circuit.</summary>
  public static TrackCircuit BuildCircuit(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece
  ) {
    ArgumentNullException.ThrowIfNull(track);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(createPiece);

    if (track.IsCircuit != true)
      throw Invalid(track, "only explicitly circuit tracks can be adapted to a closed circuit");
    var orderedPlacements = ResolveOrderedPlacements(track, placements);
    ValidatePieceLinks(track, orderedPlacements, isCircuit: true);

    var pieces = new TrackCircuitPiece[orderedPlacements.Length];
    foreach (var index in Enumerable.Range(0, orderedPlacements.Length)) {
      var placement = orderedPlacements[index];
      var piece = createPiece(placement);
      if (piece == null)
        throw Invalid(track, $"TrackPiece {placement.SourceEntryId} has no resolved geometry");
      pieces[index] = new TrackCircuitPiece($"track-piece-{placement.SourceEntryId}", piece);
    }
    return new TrackCircuit(pieces);
  }

  private static RideTrackPlacement[] ResolveOrderedPlacements(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    if (!track.HasSerializedTrackPieceOrder)
      throw Invalid(track, "has no authoritative serialized TrackPiece order");
    if (track.TrackPieceSourceEntryIds.Count == 0)
      throw Invalid(track, "contains no TrackPiece references");
    if (track.TrackPieceSourceEntryIds.Count > MaximumPieceCount)
      throw Invalid(track, $"piece count exceeds the limit {MaximumPieceCount}");

    var placementsById = IndexPlacements(placements, track);
    var trackPieceIds = new HashSet<ulong>();
    foreach (var pieceId in track.TrackPieceSourceEntryIds) {
      if (pieceId == 0 || !trackPieceIds.Add(pieceId))
        throw Invalid(track, $"contains a missing or duplicate TrackPiece reference {pieceId}");
    }

    var segmentIds = new HashSet<ulong>(track.SegmentSourceEntryIds);
    if (segmentIds.Count != track.SegmentSourceEntryIds.Count || segmentIds.Contains(0))
      throw Invalid(track, "contains a missing or duplicate TrackSegment reference");

    var orderedPlacements = new RideTrackPlacement[track.TrackPieceSourceEntryIds.Count];
    foreach (var index in Enumerable.Range(0, orderedPlacements.Length)) {
      var pieceId = track.TrackPieceSourceEntryIds[index];
      if (!placementsById.TryGetValue(pieceId, out var placement))
        throw Invalid(track, $"references missing TrackPiece {pieceId}");
      if (!segmentIds.Contains(placement.SegmentReference))
        throw Invalid(track,
          $"TrackPiece {pieceId} references TrackSegment {placement.SegmentReference} " +
          "outside the track");
      orderedPlacements[index] = placement;
    }
    return orderedPlacements;
  }

  private static IReadOnlyDictionary<ulong, RideTrackPlacement> IndexPlacements(
    IReadOnlyList<RideTrackPlacement> placements,
    RideTrack track
  ) {
    if (placements.Count > MaximumPieceCount)
      throw Invalid(track, $"placement count exceeds the limit {MaximumPieceCount}");

    var byId = new Dictionary<ulong, RideTrackPlacement>();
    foreach (var placement in placements) {
      if (placement == null)
        throw Invalid(track, "placement catalog contains null");
      if (placement.SourceEntryId == 0 || !byId.TryAdd(placement.SourceEntryId, placement))
        throw Invalid(track,
          $"placement catalog contains a missing or duplicate TrackPiece ID " +
          $"{placement.SourceEntryId}");
    }
    return byId;
  }

  private static void ValidatePieceLinks(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    bool isCircuit
  ) {
    foreach (var index in Enumerable.Range(0, placements.Count)) {
      var placement = placements[index];
      var previous = index == 0
        ? isCircuit ? placements[^1] : null
        : placements[index - 1];
      var next = index == placements.Count - 1
        ? isCircuit ? placements[0] : null
        : placements[index + 1];

      if (previous != null && placement.PreviousPieceReference != previous.SourceEntryId)
        throw Invalid(track,
          $"TrackPiece {placement.SourceEntryId} does not reference ordered predecessor " +
          $"{previous.SourceEntryId}");
      if (next != null && placement.NextPieceReference != next.SourceEntryId)
        throw Invalid(track,
          $"TrackPiece {placement.SourceEntryId} does not reference ordered successor " +
          $"{next.SourceEntryId}");
      if (previous == null && placements.Any(candidate =>
        candidate.SourceEntryId == placement.PreviousPieceReference))
        throw Invalid(track,
          $"first TrackPiece {placement.SourceEntryId} points inside the track through Prev");
      if (next == null && placements.Any(candidate =>
        candidate.SourceEntryId == placement.NextPieceReference))
        throw Invalid(track,
          $"last TrackPiece {placement.SourceEntryId} points inside the track through Next");
    }
  }

  private static InvalidDataException Invalid(RideTrack track, string message) =>
    new($"Ride track {track.SourceEntryId} cannot be adapted: {message}.");
}
