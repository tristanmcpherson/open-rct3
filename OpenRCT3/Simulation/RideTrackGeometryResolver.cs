// Ride Track Geometry Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The exact outcome of adapting one semantic DAT track to runtime rail geometry.</summary>
internal enum RideTrackGeometryStatus {
  OpenTrack,
  Circuit,
  UnresolvedResources,
  UnsupportedGeometry,
  UnsupportedTopology,
}

/// <summary>One DAT ride track plus its resolved open or closed runtime representation.</summary>
internal sealed record RideTrackGeometryLink(
  RideTrack Track,
  RideTrackGeometryStatus Status,
  TrackGraph? Graph,
  TrackCircuit? Circuit
) {
  public bool IsResolved => Graph != null || Circuit != null;
  public string? Detail { get; init; }
}

/// <summary>The bounded, DAT-order result of resolving every track's runtime geometry.</summary>
internal sealed record RideTrackGeometryResolution(
  IReadOnlyList<RideTrackGeometryLink> Tracks,
  int UnresolvedResourceTrackCount,
  int UnsupportedGeometryTrackCount,
  int UnsupportedTopologyTrackCount
);

/// <summary>
/// Composes provenance-backed TKS/SPL resources, DAT placement transforms, and serialized track
/// topology into the runtime dual-rail open-track or circuit substrates.
/// </summary>
internal static class RideTrackGeometryResolver {
  private const int MaximumTrackCount = 100_000;
  private const int MaximumPlacementCount = 1_000_000;

  public static RideTrackGeometryResolution Resolve(
    Terrain terrain,
    IReadOnlyList<RideTrack> tracks,
    RideTrackResourceResolution resources
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(tracks);
    ArgumentNullException.ThrowIfNull(resources);
    if (resources.Placements is null)
      throw Invalid("resource placement list is null");
    ValidateCount(resources.Placements.Count, MaximumPlacementCount, "resource placements");

    var placements = new RideTrackPlacement[resources.Placements.Count];
    var pieces = new Dictionary<ulong, TrackPiece?>();
    var unsupportedPieceIds = new HashSet<ulong>();
    var unresolvedPlacements = 0;
    foreach (var index in Enumerable.Range(0, resources.Placements.Count)) {
      var resource = resources.Placements[index];
      if (resource?.PlacementSection?.Placement is null)
        throw Invalid($"resource placement {index} is incomplete");
      var placement = resource.Placement;
      if (placement.SourceEntryId == 0 || pieces.ContainsKey(placement.SourceEntryId))
        throw Invalid(
          $"resource placements contain a missing or duplicate TrackPiece ID " +
          $"{placement.SourceEntryId}");

      placements[index] = placement;
      if (!resource.IsResolved) {
        pieces.Add(placement.SourceEntryId, null);
        unresolvedPlacements++;
        continue;
      }
      if (resource.Section is null)
        throw Invalid($"resolved TrackPiece {placement.SourceEntryId} has no TKS graph link");

      if (HasUnresolvedGeometryResource(resource.Section)) {
        pieces.Add(placement.SourceEntryId, null);
        continue;
      }

      try {
        var geometry = TrackSectionGeometryAdapter.CreateCarGeometry(
          resource.Section,
          placement.Reversed);
        var sceneryItem = resource.Section.Scenery.Source!.Resource;
        var transform = RideTrackPlacementTransform.Create(
          placement,
          sceneryItem,
          terrain);
        pieces.Add(placement.SourceEntryId, new TrackPiece(geometry, transform));
      } catch (Exception error) when (IsUnsupportedGeometry(error)) {
        pieces.Add(placement.SourceEntryId, null);
        unsupportedPieceIds.Add(placement.SourceEntryId);
      }
    }
    if (unresolvedPlacements != resources.UnresolvedPlacementCount)
      throw Invalid(
        $"resource result advertises {resources.UnresolvedPlacementCount} unresolved placements, " +
        $"found {unresolvedPlacements}");

    return Resolve(tracks, placements, pieces, unsupportedPieceIds);
  }

  internal static RideTrackGeometryResolution Resolve(
    IReadOnlyList<RideTrack> tracks,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyDictionary<ulong, TrackPiece?> pieces
  ) => Resolve(tracks, placements, pieces, new HashSet<ulong>());

  internal static RideTrackGeometryResolution Resolve(
    IReadOnlyList<RideTrack> tracks,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyDictionary<ulong, TrackPiece?> pieces,
    IReadOnlySet<ulong> unsupportedPieceIds
  ) {
    ArgumentNullException.ThrowIfNull(tracks);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(pieces);
    ArgumentNullException.ThrowIfNull(unsupportedPieceIds);
    ValidateCount(tracks.Count, MaximumTrackCount, "tracks");
    ValidateCount(placements.Count, MaximumPlacementCount, "placements");
    ValidateCount(pieces.Count, MaximumPlacementCount, "resolved pieces");

    var placementsById = IndexPlacements(placements);
    if (pieces.Count != placementsById.Count)
      throw Invalid(
        $"piece catalog count {pieces.Count} does not match placement count " +
        $"{placementsById.Count}");
    foreach (var pieceId in pieces.Keys) {
      if (!placementsById.ContainsKey(pieceId))
        throw Invalid($"piece catalog contains unknown TrackPiece {pieceId}");
    }
    foreach (var pieceId in unsupportedPieceIds) {
      if (!pieces.TryGetValue(pieceId, out var piece) || piece != null)
        throw Invalid(
          $"unsupported geometry identifies unknown or resolved TrackPiece {pieceId}");
    }

    var links = new List<RideTrackGeometryLink>(tracks.Count);
    var claimedPieceIds = new HashSet<ulong>();
    var trackIds = new HashSet<ulong>();
    var unresolvedTracks = 0;
    var unsupportedGeometryTracks = 0;
    var unsupportedTracks = 0;
    foreach (var track in tracks) {
      if (track is null)
        throw Invalid("track list contains null");
      if (track.SourceEntryId == 0 || !trackIds.Add(track.SourceEntryId))
        throw Invalid(
          $"track list contains a missing or duplicate Track ID {track.SourceEntryId}");

      var trackPlacements = ResolveTrackPlacements(
        track,
        placementsById,
        claimedPieceIds);
      var hasUnresolvedResources = trackPlacements.Any(placement =>
        !unsupportedPieceIds.Contains(placement.SourceEntryId) &&
        (!pieces.TryGetValue(placement.SourceEntryId, out var piece) || piece == null));
      if (hasUnresolvedResources) {
        links.Add(new(
          track,
          RideTrackGeometryStatus.UnresolvedResources,
          Graph: null,
          Circuit: null) {
          Detail = "One or more track pieces have unresolved TKS, SID, or car-spline resources.",
        });
        unresolvedTracks++;
        continue;
      }

      if (trackPlacements.Any(placement =>
        unsupportedPieceIds.Contains(placement.SourceEntryId))) {
        links.Add(new(
          track,
          RideTrackGeometryStatus.UnsupportedGeometry,
          Graph: null,
          Circuit: null) {
          Detail = "One or more track pieces have malformed or unsupported rail geometry.",
        });
        unsupportedGeometryTracks++;
        continue;
      }

      if (!track.HasAuthoritativeTrackPieceOrder || track.IsCircuit is null) {
        links.Add(new(
          track,
          RideTrackGeometryStatus.UnsupportedTopology,
          Graph: null,
          Circuit: null) {
          Detail = "The DAT links do not define one authoritative open/circuit traversal.",
        });
        unsupportedTracks++;
        continue;
      }

      try {
        TrackPiece? CreatePiece(RideTrackPlacement placement) => pieces[placement.SourceEntryId];
        if (track.IsCircuit == true) {
          links.Add(new(
            track,
            RideTrackGeometryStatus.Circuit,
            Graph: null,
            Circuit: RideTrackGraphAdapter.BuildCircuit(
              track,
              trackPlacements,
              CreatePiece)));
        } else {
          links.Add(new(
            track,
            RideTrackGeometryStatus.OpenTrack,
            Graph: RideTrackGraphAdapter.Build(
              track,
              trackPlacements,
              CreatePiece),
            Circuit: null));
        }
      } catch (InvalidDataException error) {
        links.Add(new(
          track,
          RideTrackGeometryStatus.UnsupportedTopology,
          Graph: null,
          Circuit: null) {
          Detail = error.Message,
        });
        unsupportedTracks++;
      } catch (Exception error) when (IsUnsupportedGeometry(error)) {
        links.Add(new(
          track,
          RideTrackGeometryStatus.UnsupportedGeometry,
          Graph: null,
          Circuit: null) {
          Detail = error.Message,
        });
        unsupportedGeometryTracks++;
      }
    }

    if (claimedPieceIds.Count != placementsById.Count)
      throw Invalid(
        $"track topology claims {claimedPieceIds.Count} of {placementsById.Count} placements");
    return new(
      Array.AsReadOnly(links.ToArray()),
      unresolvedTracks,
      unsupportedGeometryTracks,
      unsupportedTracks);
  }

  private static IReadOnlyDictionary<ulong, RideTrackPlacement> IndexPlacements(
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    var byId = new Dictionary<ulong, RideTrackPlacement>();
    foreach (var placement in placements) {
      if (placement is null)
        throw Invalid("placement list contains null");
      if (placement.SourceEntryId == 0 || !byId.TryAdd(placement.SourceEntryId, placement))
        throw Invalid(
          $"placement list contains a missing or duplicate TrackPiece ID " +
          $"{placement.SourceEntryId}");
    }
    return byId;
  }

  private static bool HasUnresolvedGeometryResource(TrackSectionResourceLink section) {
    if (section.Scenery?.Source is null) return true;
    if (section.Splines is null) return false;
    foreach (var role in new[] {
      TrackSectionSplineRole.CarLeft,
      TrackSectionSplineRole.CarRight,
    }) {
      var matches = section.Splines.Where(link => link?.Role == role).ToArray();
      if (matches.Length == 1 && matches[0].Source is null) return true;
    }
    return false;
  }

  private static RideTrackPlacement[] ResolveTrackPlacements(
    RideTrack track,
    IReadOnlyDictionary<ulong, RideTrackPlacement> placements,
    ISet<ulong> claimedPieceIds
  ) {
    if (track.TrackPieceSourceEntryIds is null || track.TrackPieceSourceEntryIds.Count == 0)
      throw Invalid($"Track {track.SourceEntryId} has no TrackPiece membership");
    if (track.TrackPieceSourceEntryIds.Count > MaximumPlacementCount)
      throw Invalid(
        $"Track {track.SourceEntryId} piece count exceeds the limit {MaximumPlacementCount}");

    var ordered = new RideTrackPlacement[track.TrackPieceSourceEntryIds.Count];
    foreach (var index in Enumerable.Range(0, ordered.Length)) {
      var pieceId = track.TrackPieceSourceEntryIds[index];
      if (pieceId == 0 || !claimedPieceIds.Add(pieceId))
        throw Invalid(
          $"Track {track.SourceEntryId} contains a missing or multiply claimed TrackPiece " +
          $"{pieceId}");
      if (!placements.TryGetValue(pieceId, out var placement))
        throw Invalid($"Track {track.SourceEntryId} references missing TrackPiece {pieceId}");
      ordered[index] = placement;
    }
    return ordered;
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count < 0 || count > maximum)
      throw Invalid($"{description} count {count} exceeds the limit {maximum}");
  }

  private static bool IsUnsupportedGeometry(Exception error) =>
    error is InvalidDataException or ArgumentException or InvalidOperationException;

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-track geometry resolution is invalid: {message}.");
}
