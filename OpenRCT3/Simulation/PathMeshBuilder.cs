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
    var placements = park.PathPlacements.Count > 0
      ? park.PathPlacements
      : park.Paths.Select(path => new PathPlacement(path.Key.X, path.Key.Y, path.Value)).ToList();
    foreach (var placement in placements
      .OrderBy(path => path.TileY)
      .ThenBy(path => path.TileX)
      .ThenBy(path => path.Tile.Raised)) {
      var (tileX, tileY, tile) = placement;
      if (!terrain.HasTile(tileX, tileY))
        throw new InvalidDataException($"Path tile ({tileX}, {tileY}) is outside the terrain grid.");

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
}
