// Path Mesh Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds the visible walking surface for the paths loaded into a park.</summary>
public static class PathMeshBuilder {
  private const float EdgeInset = 0.28f;
  private const float TerrainClearance = 0.04f;

  public static Mesh Build(
    Park park,
    Terrain terrain,
    Vector4 pathColor,
    Vector4 queueColor,
    string? name = "Paths"
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);

    var vertices = new List<Vertex>();
    var indices = new List<uint>();
    foreach (var placement in OrderedPlacements(park)) {
      var (tileX, tileY, tile) = placement;
      ValidatePlacement(terrain, tileX, tileY);

      AddTile(
        terrain,
        tileX,
        tileY,
        tile,
        tile.IsQueue ? queueColor : pathColor,
        vertices,
        indices);
    }

    return new Mesh(vertices, indices) { Name = name };
  }

  /// <summary>
  /// Builds deterministic surface-aware batches without interpreting RCT3's flexi-colour indices.
  /// </summary>
  /// <remarks>
  /// Geometry is keyed only by ordinary/queue kind and the exact decoded surface system name.
  /// <see cref="PathMeshBatch.SurfaceColours"/> retains one raw value per emitted tile, in the same
  /// row-major order as that tile's four vertices. Colour-index-to-palette conversion belongs in a
  /// future path material implementation, not this geometry builder.
  /// </remarks>
  public static IReadOnlyList<PathMeshBatch> BuildBatches(
    Park park,
    Terrain terrain,
    Vector4 pathColor,
    Vector4 queueColor,
    string name = "Paths"
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);

    var geometry = new Dictionary<(
      PathMaterialKind Kind,
      string? SurfaceSystemName,
      PathSurfaceColours? MaterialColours), MeshGeometry>();
    foreach (var placement in OrderedPlacements(park)) {
      var (tileX, tileY, tile) = placement;
      ValidatePlacement(terrain, tileX, tileY);

      var kind = tile.IsQueue ? PathMaterialKind.Queue : PathMaterialKind.Ordinary;
      var materialColours = tile.IsQueue ? tile.SurfaceColours : null;
      var key = (kind, tile.SurfaceSystemName, materialColours);
      if (!geometry.TryGetValue(key, out var batch)) {
        batch = new MeshGeometry();
        geometry.Add(key, batch);
      }
      AddTile(
        terrain,
        tileX,
        tileY,
        tile,
        tile.IsQueue ? queueColor : pathColor,
        batch.Vertices,
        batch.Indices);
      batch.SurfaceColours.Add(tile.SurfaceColours);
    }

    return geometry
      .OrderBy(batch => batch.Key.Kind)
      .ThenBy(batch => batch.Key.SurfaceSystemName, StringComparer.Ordinal)
      .ThenBy(batch => batch.Key.MaterialColours.HasValue ? 1 : 0)
      .ThenBy(batch => batch.Key.MaterialColours?.First ?? 0)
      .ThenBy(batch => batch.Key.MaterialColours?.Second ?? 0)
      .ThenBy(batch => batch.Key.MaterialColours?.Third ?? 0)
      .Select(batch => new PathMeshBatch(
        batch.Key.Kind,
        batch.Key.SurfaceSystemName,
        batch.Value.SurfaceColours.ToArray(),
        new Mesh(batch.Value.Vertices, batch.Value.Indices) {
          Name = $"{name} {batch.Key.Kind} {batch.Key.SurfaceSystemName ?? "Unresolved"}"
        }) {
          MaterialColours = batch.Key.MaterialColours
        })
      .ToArray();
  }

  private static IEnumerable<PathPlacement> OrderedPlacements(Park park) {
    var placements = park.PathPlacements.Count > 0
      ? park.PathPlacements
      : park.Paths.Select(path => new PathPlacement(path.Key.X, path.Key.Y, path.Value)).ToList();
    return placements
      .OrderBy(path => path.TileY)
      .ThenBy(path => path.TileX)
      .ThenBy(path => path.Tile.Raised);
  }

  private static void ValidatePlacement(Terrain terrain, int tileX, int tileY) {
    if (!terrain.HasTile(tileX, tileY))
      throw new InvalidDataException($"Path tile ({tileX}, {tileY}) is outside the terrain grid.");
  }

  private static void AddTile(
    Terrain terrain,
    int tileX,
    int tileY,
    PathTile tile,
    Vector4 color,
    List<Vertex> vertices,
    List<uint> indices
  ) {
    var insetU = Math.Clamp(EdgeInset / terrain.TileSize.X, 0f, 0.49f);
    var insetV = Math.Clamp(EdgeInset / terrain.TileSize.Y, 0f, 0.49f);
    var sw = Position(terrain, tileX, tileY, tile, insetU, insetV);
    var se = Position(terrain, tileX, tileY, tile, 1f - insetU, insetV);
    var ne = Position(terrain, tileX, tileY, tile, 1f - insetU, 1f - insetV);
    var nw = Position(terrain, tileX, tileY, tile, insetU, 1f - insetV);

    var southWestNormal = Vector3.Normalize(Vector3.Cross(se - sw, nw - sw));
    var northEastNormal = Vector3.Normalize(Vector3.Cross(nw - ne, se - ne));
    var sharedNormal = Vector3.Normalize(southWestNormal + northEastNormal);
    var baseIndex = Convert.ToUInt32(vertices.Count);
    vertices.Add(new Vertex {
      Position = sw, Normal = southWestNormal, TexCoord = new Vector2(0, 0), Color = color
    });
    vertices.Add(new Vertex {
      Position = se, Normal = sharedNormal, TexCoord = new Vector2(1, 0), Color = color
    });
    vertices.Add(new Vertex {
      Position = ne, Normal = northEastNormal, TexCoord = new Vector2(1, 1), Color = color
    });
    vertices.Add(new Vertex {
      Position = nw, Normal = sharedNormal, TexCoord = new Vector2(0, 1), Color = color
    });
    indices.AddRange([
      baseIndex,
      baseIndex + 1,
      baseIndex + 3,
      baseIndex + 1,
      baseIndex + 2,
      baseIndex + 3
    ]);
  }

  private static Vector3 Position(
    Terrain terrain,
    int tileX,
    int tileY,
    PathTile tile,
    float u,
    float v
  ) {
    var worldX = terrain.Origin.X + ((tileX + u) * terrain.TileSize.X);
    var worldY = terrain.Origin.Y + ((tileY + v) * terrain.TileSize.Y);
    var worldZ = tile.Raised
      ? RaisedHeight(tile, u, v)
      : TerrainHeight(terrain, tileX, tileY, u, v) + TerrainClearance;
    return new Vector3(worldX, worldY, worldZ);
  }

  private static float TerrainHeight(
    Terrain terrain,
    int tileX,
    int tileY,
    float u,
    float v
  ) {
    var southWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthWest).Height);
    var southEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthEast).Height);
    var northWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthWest).Height);
    var northEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthEast).Height);
    return u + v <= 1f
      ? (southWest * (1f - u - v)) + (southEast * u) + (northWest * v)
      : (northEast * (u + v - 1f)) +
        (northWest * (1f - u)) +
        (southEast * (1f - v));
  }

  private static float RaisedHeight(PathTile tile, float u, float v) {
    var baseHeight = Terrain.CornerHeightToWorldZ(tile.RaisedHeight);
    var rise = Terrain.CornerHeightToWorldZ(tile.RaisedSlope.RiseInHeightStepUnits());
    var factor = tile.RaisedSlopeDirection switch {
      Edge.South => 1f - v,
      Edge.West => 1f - u,
      Edge.East => u,
      Edge.North => v,
      _ => throw new ArgumentOutOfRangeException(
        nameof(tile), tile.RaisedSlopeDirection, "Unsupported raised-path direction."),
    };
    return baseHeight + (rise * factor);
  }

  private sealed class MeshGeometry {
    public List<Vertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];
    public List<PathSurfaceColours?> SurfaceColours { get; } = [];
  }
}

/// <summary>Identifies whether one path mesh batch contains ordinary or queue tiles.</summary>
public enum PathMaterialKind {
  Ordinary,
  Queue,
}

/// <summary>A path mesh whose tiles share one decoded surface resource.</summary>
public sealed record PathMeshBatch(
  PathMaterialKind Kind,
  string? SurfaceSystemName,
  IReadOnlyList<PathSurfaceColours?> SurfaceColours,
  Mesh Mesh
) {
  /// <summary>
  /// The one queue flexi-colour triplet shared by this material batch, or <c>null</c> for ordinary
  /// paths and queues that do not serialize a ground-surface colour wrapper.
  /// </summary>
  public PathSurfaceColours? MaterialColours { get; init; }
}
