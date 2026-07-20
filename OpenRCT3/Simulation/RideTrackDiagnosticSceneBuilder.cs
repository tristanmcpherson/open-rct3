// Ride Track Diagnostic Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Caller-owned diagnostic models plus explicit resolver outcome counts.</summary>
/// <remarks>
/// Models retain the DAT order of resolved tracks after unresolved and unsupported entries are
/// omitted. The caller owns every returned model and may transfer them to a <see cref="Scene"/>.
/// These are diagnostic contact paths, not final RCT3 track skins.
/// </remarks>
internal sealed record RideTrackDiagnosticSceneBuildResult(
  IReadOnlyList<Model> Models,
  RideTrackRenderGeometryStatus Status,
  string Detail,
  int TrackCount,
  int ResolvedTrackCount,
  int UnresolvedResourceTrackCount,
  int UnsupportedGeometryTrackCount,
  int UnsupportedTopologyTrackCount
);

/// <summary>Builds ordered scene-ready diagnostics from one geometry resolution.</summary>
internal static class RideTrackDiagnosticSceneBuilder {
  private const int MaximumTrackCount = 100_000;
  private const ulong MaximumPieceCount = 1_000_000;
  private const ulong MaximumCrossSectionCount = 500_000;
  private const ulong MaximumVertexCount = 4_000_000;
  private const ulong MaximumIndexCount = 12_000_000;

  public static RideTrackDiagnosticSceneBuildResult Build(
    RideTrackGeometryResolution resolution
  ) {
    ArgumentNullException.ThrowIfNull(resolution);
    if (resolution.Tracks is null)
      throw Invalid("track list is null");
    if (resolution.Tracks.Count > MaximumTrackCount)
      throw Limit("track", MaximumTrackCount);

    var models = new List<Model>();
    var unresolvedCount = 0;
    var unsupportedGeometryCount = 0;
    var unsupportedTopologyCount = 0;
    var totalPieces = 0ul;
    var totalCrossSections = 0ul;
    var totalVertices = 0ul;
    var totalIndices = 0ul;

    try {
      foreach (var link in resolution.Tracks) {
        if (link?.Track is null || link.Track.SourceEntryId == 0)
          throw Invalid("track list contains an incomplete link");
        switch (link.Status) {
          case RideTrackGeometryStatus.OpenTrack:
            if (link.Graph is null || link.Circuit != null)
              throw Invalid(
                $"open track {link.Track.SourceEntryId} has inconsistent geometry");
            BuildResolved(link, link.Graph.Edges.Select(edge => edge.Piece));
            break;
          case RideTrackGeometryStatus.Circuit:
            if (link.Circuit is null || link.Graph != null)
              throw Invalid(
                $"circuit {link.Track.SourceEntryId} has inconsistent geometry");
            BuildResolved(link, link.Circuit.Pieces.Select(piece => piece.Piece));
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

      if (unresolvedCount != resolution.UnresolvedResourceTrackCount)
        throw Invalid(
          $"result advertises {resolution.UnresolvedResourceTrackCount} unresolved tracks, " +
          $"found {unresolvedCount}");
      if (unsupportedGeometryCount != resolution.UnsupportedGeometryTrackCount)
        throw Invalid(
          $"result advertises {resolution.UnsupportedGeometryTrackCount} unsupported geometry " +
          $"tracks, found {unsupportedGeometryCount}");
      if (unsupportedTopologyCount != resolution.UnsupportedTopologyTrackCount)
        throw Invalid(
          $"result advertises {resolution.UnsupportedTopologyTrackCount} unsupported topology " +
          $"tracks, found {unsupportedTopologyCount}");
      return new(
        Array.AsReadOnly(models.ToArray()),
        RideTrackRenderGeometryStatus.DiagnosticContactRailsOnly,
        RideTrackDiagnosticModelBuilder.DiagnosticDetail,
        resolution.Tracks.Count,
        models.Count,
        unresolvedCount,
        unsupportedGeometryCount,
        unsupportedTopologyCount);
    } catch (Exception primaryError) {
      var cleanupErrors = ReleaseModels(models);
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Diagnostic ride-track scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }

    void BuildResolved(
      RideTrackGeometryLink link,
      IEnumerable<TrackPiece> pieces
    ) {
      var pieceCount = 0;
      var crossSectionCount = 0ul;
      var vertexCount = 0ul;
      var indexCount = 0ul;
      foreach (var piece in pieces) {
        if (piece is null || piece.BakedSampleCount < 2)
          throw Invalid(
            $"resolved track {link.Track.SourceEntryId} contains invalid baked geometry");
        var samples = Convert.ToUInt64(piece.BakedSampleCount);
        var vertices = checked(samples * 8ul);
        var indices = checked((samples - 1ul) * 48ul);
        Reserve(ref totalPieces, 1ul, MaximumPieceCount, "piece");
        Reserve(
          ref totalCrossSections,
          samples,
          MaximumCrossSectionCount,
          "baked cross-section");
        Reserve(ref totalVertices, vertices, MaximumVertexCount, "vertex");
        Reserve(ref totalIndices, indices, MaximumIndexCount, "index");
        pieceCount++;
        crossSectionCount += samples;
        vertexCount += vertices;
        indexCount += indices;
      }
      if (pieceCount == 0)
        throw Invalid($"resolved track {link.Track.SourceEntryId} contains no pieces");

      var name = $"Ride Track {link.Track.SourceEntryId} (diagnostic contact rails)";
      var built = link.Status == RideTrackGeometryStatus.OpenTrack
        ? RideTrackDiagnosticModelBuilder.Build(link.Graph!, name: name)
        : RideTrackDiagnosticModelBuilder.Build(link.Circuit!, name: name);
      var model = built.Model
        ?? throw Invalid(
          $"track {link.Track.SourceEntryId} returned a null diagnostic model");
      models.Add(model);
      if (built.Status != RideTrackRenderGeometryStatus.DiagnosticContactRailsOnly ||
          !string.Equals(
            built.Detail,
            RideTrackDiagnosticModelBuilder.DiagnosticDetail,
            StringComparison.Ordinal) ||
          built.PieceCount != pieceCount ||
          built.BakedCrossSectionCount != Convert.ToInt32(crossSectionCount) ||
          model.Material is not Flat ||
          !string.Equals(model.Mesh.Name, name, StringComparison.Ordinal) ||
          model.Mesh.Vertices.Count != Convert.ToInt32(vertexCount) ||
          model.Mesh.Indices.Count != Convert.ToInt32(indexCount))
        throw Invalid(
          $"track {link.Track.SourceEntryId} returned inconsistent diagnostic geometry");
    }
  }

  private static void ValidateSkipped(RideTrackGeometryLink link) {
    if (link.Graph != null || link.Circuit != null || link.IsResolved)
      throw Invalid(
        $"skipped track {link.Track.SourceEntryId} unexpectedly contains resolved geometry");
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

  private static List<Exception> ReleaseModels(List<Model> models) {
    var errors = new List<Exception>();
    while (models.Count > 0) {
      var lastIndex = models.Count - 1;
      var model = models[lastIndex];
      models.RemoveAt(lastIndex);
      try {
        model.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Diagnostic ride-track scene input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Diagnostic ride-track scene {resource} count exceeds the limit {maximum}.");
}
