// Ride Track Graph Adapter
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One exact closed TrackSegment traversal and its retained runtime circuit.</summary>
internal sealed record RideTrackSegmentCircuit(
  ulong SegmentSourceEntryId,
  IReadOnlyList<ulong> PieceSourceEntryIds,
  TrackCircuit Circuit
);

/// <summary>
/// Adapts authoritative DAT piece order into open, singular-circuit, or per-segment circuit
/// runtime geometry.
/// </summary>
/// <remarks>
/// RCT3 circuits cannot be represented by <see cref="TrackGraph"/>, which deliberately rejects
/// cycles. The topology loader can retain a serialized Track list or reconstruct the same order
/// from bounded reciprocal TrackPiece links.
/// </remarks>
internal static class RideTrackGraphAdapter {
  private const int MaximumPieceCount = 1_000_000;

  // RCT3.exe's paired TKS evaluator at 0x00F62070 samples each car spline by its own arc distance
  // and normalizes the resulting direction. BoxOffice's 432 rail joins have a maximum normalized
  // direction delta of 0.031452168, but 410 raw derivative magnitudes differ because each SPL has
  // an independent parameterization. Keep this evidence-backed exception local to DAT imports.
  internal const float ImportedJoinDirectionTolerance = 0.032f;
  private static readonly TrackJoinValidationPolicy ImportedJoinValidation = new(
    positionTolerance: 0.001f,
    tangentDirectionTolerance: ImportedJoinDirectionTolerance,
    tangentMagnitudeTolerance: null,
    bankToleranceRadians: 0.001f);

  // Wild split rides retain separate, authoritative closed link cycles even when alternate-path
  // geometry is not C1-continuous. ScrubGardens Seizmic segment 6489 proves this at the exact
  // 6528 -> 6527 join. Multi-circuit traversals support saved-piece identity, static placement,
  // diagnostics, and bounds only; vehicle motion remains gated to singular Circuit geometry.
  private static readonly TrackJoinValidationPolicy ImportedMultiCircuitJoinValidation = new(
    positionTolerance: float.MaxValue,
    tangentDirectionTolerance: float.MaxValue,
    tangentMagnitudeTolerance: null,
    bankToleranceRadians: float.MaxValue);

  public static TrackGraph Build(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece
  ) {
    ArgumentNullException.ThrowIfNull(track);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(createPiece);

    if (track.IsCircuit != false)
      throw Invalid(track, "only link-derived open tracks can be adapted to a DAG");
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

    return new TrackGraph(nodes, edges, ImportedJoinValidation);
  }

  /// <summary>Adapts one link-derived cyclic piece order into a closed track circuit.</summary>
  public static TrackCircuit BuildCircuit(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece
  ) {
    ArgumentNullException.ThrowIfNull(track);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(createPiece);

    if (track.IsCircuit != true)
      throw Invalid(track, "only link-derived circuit tracks can be adapted to a closed circuit");
    var orderedPlacements = ResolveOrderedPlacements(
      track,
      placements,
      requireSingleSegment: true);
    return BuildSegmentCircuit(
      track,
      track.SegmentSourceEntryIds[0],
      orderedPlacements,
      createPiece,
      ImportedJoinValidation).Circuit;
  }

  /// <summary>Adapts every separately closed TrackSegment into its own runtime circuit.</summary>
  public static IReadOnlyList<RideTrackSegmentCircuit> BuildCircuits(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece
  ) => BuildCircuits(
    track,
    placements,
    createPiece,
    static placement => placement.SegmentReference);

  internal static IReadOnlyList<RideTrackSegmentCircuit> BuildCircuits(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    Func<RideTrackPlacement, TrackPiece?> createPiece,
    Func<RideTrackPlacement, ulong> readSegmentReference
  ) {
    ArgumentNullException.ThrowIfNull(track);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(createPiece);
    ArgumentNullException.ThrowIfNull(readSegmentReference);

    if (track.IsCircuit is not null)
      throw Invalid(track,
        "only link-derived multi-segment topology can be adapted to multiple circuits");
    if (track.SegmentSourceEntryIds.Count < 2)
      throw Invalid(track, "multiple circuits require at least two TrackSegment traversals");
    var orderedPlacements = ResolveOrderedPlacements(
      track,
      placements,
      requireSingleSegment: false);
    var segmentIndices = new Dictionary<ulong, int>(track.SegmentSourceEntryIds.Count);
    var groupedPlacements = new List<RideTrackPlacement>[track.SegmentSourceEntryIds.Count];
    foreach (var index in Enumerable.Range(0, track.SegmentSourceEntryIds.Count)) {
      var segmentId = track.SegmentSourceEntryIds[index];
      if (!segmentIndices.TryAdd(segmentId, index))
        throw Invalid(track, $"contains duplicate TrackSegment {segmentId}");
      groupedPlacements[index] = new List<RideTrackPlacement>();
    }

    var previousSegmentIndex = -1;
    var retainsContiguousSegmentOrder = true;
    foreach (var placement in orderedPlacements) {
      var segmentId = readSegmentReference(placement);
      if (!segmentIndices.TryGetValue(segmentId, out var segmentIndex))
        throw Invalid(track,
          $"TrackPiece {placement.SourceEntryId} references TrackSegment {segmentId} " +
          "outside the track");
      groupedPlacements[segmentIndex].Add(placement);
      if (previousSegmentIndex < 0) {
        if (segmentIndex != 0) retainsContiguousSegmentOrder = false;
      } else if (segmentIndex != previousSegmentIndex &&
                 segmentIndex != previousSegmentIndex + 1) {
        retainsContiguousSegmentOrder = false;
      }
      previousSegmentIndex = segmentIndex;
    }

    var circuits = new RideTrackSegmentCircuit[track.SegmentSourceEntryIds.Count];
    foreach (var index in Enumerable.Range(0, circuits.Length)) {
      var segmentId = track.SegmentSourceEntryIds[index];
      var segmentPlacements = groupedPlacements[index];
      if (segmentPlacements.Count == 0)
        throw Invalid(track, $"TrackSegment {segmentId} contains no TrackPiece references");
    }
    if (!retainsContiguousSegmentOrder)
      throw Invalid(track,
        "authoritative TrackPiece order does not retain contiguous TrackSegment order");

    foreach (var index in Enumerable.Range(0, circuits.Length)) {
      var segmentId = track.SegmentSourceEntryIds[index];
      circuits[index] = BuildSegmentCircuit(
        track,
        segmentId,
        groupedPlacements[index],
        createPiece,
        ImportedMultiCircuitJoinValidation);
    }
    return Array.AsReadOnly(circuits);
  }

  private static RideTrackSegmentCircuit BuildSegmentCircuit(
    RideTrack track,
    ulong segmentSourceEntryId,
    IReadOnlyList<RideTrackPlacement> orderedPlacements,
    Func<RideTrackPlacement, TrackPiece?> createPiece,
    TrackJoinValidationPolicy joinValidation
  ) {
    ValidatePieceLinks(track, orderedPlacements, isCircuit: true);
    var pieces = new TrackCircuitPiece[orderedPlacements.Count];
    var pieceIds = new ulong[orderedPlacements.Count];
    foreach (var index in Enumerable.Range(0, orderedPlacements.Count)) {
      var placement = orderedPlacements[index];
      if (placement.SegmentReference != segmentSourceEntryId ||
          placement.OwnerReference != segmentSourceEntryId)
        throw Invalid(track,
          $"TrackPiece {placement.SourceEntryId} does not retain exact TrackSegment " +
          $"{segmentSourceEntryId} ownership");
      var piece = createPiece(placement);
      if (piece == null)
        throw Invalid(track, $"TrackPiece {placement.SourceEntryId} has no resolved geometry");
      pieceIds[index] = placement.SourceEntryId;
      pieces[index] = new TrackCircuitPiece($"track-piece-{placement.SourceEntryId}", piece);
    }
    return new(
      segmentSourceEntryId,
      Array.AsReadOnly(pieceIds),
      new TrackCircuit(pieces, joinValidation));
  }

  private static RideTrackPlacement[] ResolveOrderedPlacements(
    RideTrack track,
    IReadOnlyList<RideTrackPlacement> placements,
    bool requireSingleSegment = true
  ) {
    if (!track.HasAuthoritativeTrackPieceOrder)
      throw Invalid(track, "has no authoritative TrackPiece order");
    if (requireSingleSegment && track.SegmentSourceEntryIds.Count != 1)
      throw Invalid(track,
        "the current geometry substrate requires exactly one TrackSegment traversal");
    if (track.SegmentSourceEntryIds.Count == 0 ||
        track.SegmentSourceEntryIds.Count > MaximumPieceCount)
      throw Invalid(track, "contains an invalid TrackSegment count");
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
