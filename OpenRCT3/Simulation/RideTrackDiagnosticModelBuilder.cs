// Ride Track Diagnostic Model Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Identifies the fidelity of generated ride-track render geometry.</summary>
public enum RideTrackRenderGeometryStatus {
  /// <summary>
  /// The model visualizes the authoritative left/right contact paths, not the ride's rendered track
  /// surface, supports, ties, or material.
  /// </summary>
  DiagnosticContactRailsOnly,
}

/// <summary>Appearance controls for the diagnostic contact-path markers.</summary>
public sealed record RideTrackDiagnosticStyle(
  float ContactMarkerHalfWidth,
  Vector4 LeftContactColor,
  Vector4 RightContactColor
) {
  public static RideTrackDiagnosticStyle Default { get; } = new(
    0.04f,
    new Vector4(0.95f, 0.25f, 0.20f, 1f),
    new Vector4(0.15f, 0.70f, 1f, 1f));
}

/// <summary>Aggregate safety limits applied before diagnostic mesh allocation.</summary>
public sealed record RideTrackDiagnosticBuildLimits(
  ulong MaximumPieces,
  ulong MaximumBakedCrossSections,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static RideTrackDiagnosticBuildLimits Default { get; } = new(
    1_000_000,
    500_000,
    4_000_000,
    12_000_000);
}

/// <summary>A render-ready model and an explicit statement of its diagnostic fidelity.</summary>
public sealed record RideTrackDiagnosticBuildResult(
  RideTrackRenderGeometryStatus Status,
  string Detail,
  int PieceCount,
  int BakedCrossSectionCount,
  Model Model
) {
  public Mesh Mesh => Model.Mesh;
}

/// <summary>Builds renderable diagnostic geometry from resolved dual-contact-rail pieces.</summary>
/// <remarks>
/// The pinned TKS layout stores a separate SceneryItem reference alongside left/right car splines;
/// it does not define a generated track skin, rail cross-section, support layout, or material. This
/// builder therefore emits explicitly diagnostic square markers around the runtime contact paths.
/// Every ring comes directly from <see cref="TrackPiece.BakedArcLengths"/> and
/// <see cref="TrackPiece.SampleRail(RailSide, float)"/> without inventing another spline sampler.
///
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/tracksection.h">
/// Pinned rct3-importer TKS layout
/// </see>
/// and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/spline.h">
/// pinned SPL control-point layout
/// </see>.
/// </remarks>
public static class RideTrackDiagnosticModelBuilder {
  public const string DefaultMeshName = "Ride Track (diagnostic contact rails)";
  public const string DiagnosticDetail =
    "Generated square markers around resolved left/right contact paths. " +
    "RCT3 track surface, support, tie, and material geometry is not decoded.";

  /// <summary>Builds one diagnostic model for a single resolved track piece.</summary>
  public static RideTrackDiagnosticBuildResult Build(
    TrackPiece piece,
    RideTrackDiagnosticStyle? style = null,
    RideTrackDiagnosticBuildLimits? limits = null,
    string name = DefaultMeshName
  ) {
    ArgumentNullException.ThrowIfNull(piece);
    return Build([piece], style, limits, name);
  }

  /// <summary>Builds one diagnostic model from graph edges in their retained order.</summary>
  public static RideTrackDiagnosticBuildResult Build(
    TrackGraph graph,
    RideTrackDiagnosticStyle? style = null,
    RideTrackDiagnosticBuildLimits? limits = null,
    string name = DefaultMeshName
  ) {
    ArgumentNullException.ThrowIfNull(graph);
    return Build(graph.Edges.Select(edge => edge.Piece), style, limits, name);
  }

  /// <summary>Builds one diagnostic model from closed-circuit pieces in traversal order.</summary>
  public static RideTrackDiagnosticBuildResult Build(
    TrackCircuit circuit,
    RideTrackDiagnosticStyle? style = null,
    RideTrackDiagnosticBuildLimits? limits = null,
    string name = DefaultMeshName
  ) {
    ArgumentNullException.ThrowIfNull(circuit);
    return Build(circuit.Pieces.Select(piece => piece.Piece), style, limits, name);
  }

  /// <summary>Builds one diagnostic model from a bounded sequence of resolved pieces.</summary>
  public static RideTrackDiagnosticBuildResult Build(
    IEnumerable<TrackPiece> pieces,
    RideTrackDiagnosticStyle? style = null,
    RideTrackDiagnosticBuildLimits? limits = null,
    string name = DefaultMeshName
  ) {
    ArgumentNullException.ThrowIfNull(pieces);
    style ??= RideTrackDiagnosticStyle.Default;
    limits ??= RideTrackDiagnosticBuildLimits.Default;
    ValidateStyle(style);
    ValidateLimits(limits);
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("A diagnostic ride-track mesh needs a name.", nameof(name));

    var counted = CopyAndCount(pieces, limits);
    var vertices = new List<Vertex>(Convert.ToInt32(counted.VertexCount));
    var indices = new List<uint>(Convert.ToInt32(counted.IndexCount));
    foreach (var piece in counted.Pieces) {
      AddContactRail(piece, RailSide.Left, style.LeftContactColor, style, vertices, indices);
      AddContactRail(piece, RailSide.Right, style.RightContactColor, style, vertices, indices);
    }

    var mesh = new Mesh(vertices, indices) { Name = name };
    var model = new Model(mesh) {
      Material = new Flat { CullBackFaces = true },
    };
    return new(
      RideTrackRenderGeometryStatus.DiagnosticContactRailsOnly,
      DiagnosticDetail,
      counted.Pieces.Count,
      Convert.ToInt32(counted.CrossSectionCount),
      model);
  }

  private static CountedPieces CopyAndCount(
    IEnumerable<TrackPiece> source,
    RideTrackDiagnosticBuildLimits limits
  ) {
    var pieces = new List<TrackPiece>();
    var crossSectionCount = 0ul;
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var piece in source) {
      if (piece is null)
        throw new ArgumentException(
          "Diagnostic ride-track pieces cannot contain null.",
          nameof(source));
      if (Convert.ToUInt64(pieces.Count) >= limits.MaximumPieces)
        throw Limit("piece", limits.MaximumPieces);
      if (piece.BakedSampleCount < 2 || piece.BakedArcLengths.Count != piece.BakedSampleCount)
        throw new ArgumentException(
          "A diagnostic ride-track piece needs a complete baked contact path.",
          nameof(source));

      var pieceCrossSections = Convert.ToUInt64(piece.BakedSampleCount);
      var pieceVertices = checked(pieceCrossSections * 8ul);
      var pieceIndices = checked((pieceCrossSections - 1ul) * 48ul);
      Reserve(
        ref crossSectionCount,
        pieceCrossSections,
        limits.MaximumBakedCrossSections,
        "baked cross-section");
      Reserve(ref vertexCount, pieceVertices, limits.MaximumVertices, "vertex");
      Reserve(ref indexCount, pieceIndices, limits.MaximumIndices, "index");
      pieces.Add(piece);
    }
    if (pieces.Count == 0)
      throw new ArgumentException(
        "At least one diagnostic ride-track piece is required.",
        nameof(source));
    return new(pieces, crossSectionCount, vertexCount, indexCount);
  }

  private static void AddContactRail(
    TrackPiece piece,
    RailSide side,
    Vector4 color,
    RideTrackDiagnosticStyle style,
    List<Vertex> vertices,
    List<uint> indices
  ) {
    var railBase = vertices.Count;
    foreach (var arcLength in piece.BakedArcLengths) {
      var sample = piece.SampleRail(side, arcLength);
      var lateral = TrackMath.Normalize(Vector3.Transform(Vector3.UnitY, sample.Orientation));
      var up = TrackMath.Normalize(Vector3.Transform(Vector3.UnitZ, sample.Orientation));
      AddRingVertex(sample.Position, lateral + up, color, style, vertices);
      AddRingVertex(sample.Position, -lateral + up, color, style, vertices);
      AddRingVertex(sample.Position, -lateral - up, color, style, vertices);
      AddRingVertex(sample.Position, lateral - up, color, style, vertices);
    }

    foreach (var sampleIndex in Enumerable.Range(0, piece.BakedSampleCount - 1)) {
      var current = railBase + (sampleIndex * 4);
      var next = current + 4;
      foreach (var corner in Enumerable.Range(0, 4)) {
        var followingCorner = (corner + 1) % 4;
        AddQuad(
          current + corner,
          current + followingCorner,
          next + followingCorner,
          next + corner,
          indices);
      }
    }
  }

  private static void AddRingVertex(
    Vector3 center,
    Vector3 offsetDirection,
    Vector4 color,
    RideTrackDiagnosticStyle style,
    List<Vertex> vertices
  ) {
    var normal = TrackMath.Normalize(offsetDirection);
    var position = center + (offsetDirection * style.ContactMarkerHalfWidth);
    if (!TrackMath.IsFinite(position))
      throw new ArgumentOutOfRangeException(
        nameof(style),
        "The diagnostic contact-marker width produced a non-finite vertex.");
    vertices.Add(new Vertex {
      Position = position,
      Normal = normal,
      TexCoord = Vector2.Zero,
      Color = color,
    });
  }

  private static void AddQuad(
    int current,
    int currentFollowing,
    int nextFollowing,
    int next,
    List<uint> indices
  ) => indices.AddRange([
    Convert.ToUInt32(current),
    Convert.ToUInt32(currentFollowing),
    Convert.ToUInt32(nextFollowing),
    Convert.ToUInt32(current),
    Convert.ToUInt32(nextFollowing),
    Convert.ToUInt32(next),
  ]);

  private static void ValidateStyle(RideTrackDiagnosticStyle style) {
    if (!float.IsFinite(style.ContactMarkerHalfWidth) || style.ContactMarkerHalfWidth <= 0f)
      throw new ArgumentOutOfRangeException(
        nameof(style),
        "The diagnostic contact-marker half-width must be finite and positive.");
    ValidateColor(style.LeftContactColor, nameof(style));
    ValidateColor(style.RightContactColor, nameof(style));
  }

  private static void ValidateColor(Vector4 color, string parameterName) {
    if (!float.IsFinite(color.X) || color.X is < 0f or > 1f ||
        !float.IsFinite(color.Y) || color.Y is < 0f or > 1f ||
        !float.IsFinite(color.Z) || color.Z is < 0f or > 1f ||
        !float.IsFinite(color.W) || color.W is < 0f or > 1f)
      throw new ArgumentOutOfRangeException(
        parameterName,
        "Diagnostic contact colors must contain finite normalized components.");
  }

  private static void ValidateLimits(RideTrackDiagnosticBuildLimits limits) {
    if (limits.MaximumPieces is 0 or > int.MaxValue ||
        limits.MaximumBakedCrossSections is 0 or > int.MaxValue ||
        limits.MaximumVertices is 0 or > int.MaxValue ||
        limits.MaximumIndices is 0 or > int.MaxValue)
      throw new ArgumentOutOfRangeException(
        nameof(limits),
        "Diagnostic ride-track limits must be positive signed collection counts.");
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

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Diagnostic ride-track {resource} count exceeds the limit {maximum}.");

  private sealed record CountedPieces(
    IReadOnlyList<TrackPiece> Pieces,
    ulong CrossSectionCount,
    ulong VertexCount,
    ulong IndexCount
  );
}
