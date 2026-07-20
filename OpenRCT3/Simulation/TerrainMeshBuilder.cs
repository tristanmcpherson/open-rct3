// Terrain Mesh Builder
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>
/// Builds renderable <see cref="Mesh"/> geometry from a <see cref="Terrain"/>'s corner-height grid.
/// </summary>
/// <remarks>
/// Each tile emits a top face (two triangles) from its four corners, plus
/// a vertical cliff face on its South/West edges when <see cref="Terrain.IsEdgeDetached"/> reports a
/// detached edge. Checking only South/West per tile (rather than all four) emits each interior edge's
/// cliff face exactly once, since a tile's South edge is the same world edge as its southern
/// neighbor's North edge. <see cref="BuildBatches"/> separates top and cliff geometry by the decoded
/// <see cref="TerrainCorner.SurfaceIndex"/>/<see cref="TerrainCorner.CliffIndex"/> material keys while
/// retaining the same <paramref name="color"/> tint for every vertex.
/// </remarks>
public static class TerrainMeshBuilder {
  /// <summary>Builds one aggregate mesh for geometry-only callers.</summary>
  public static Mesh Build(Terrain terrain, Vector4 color, string? name = "Terrain") {
    var vertices = new List<Vertex>();
    var indices = new List<uint>();
    var topTextureScale = DefaultTopTextureScale(terrain);

    for (var tileY = 0; tileY < terrain.Height; tileY++) {
      for (var tileX = 0; tileX < terrain.Width; tileX++) {
        AddTopFace(terrain, tileX, tileY, color, topTextureScale, vertices, indices);

        if (terrain.IsEdgeDetached(tileX, tileY, Edge.South))
          AddCliffFace(
            terrain,
            tileX,
            tileY,
            Edge.South,
            color,
            DefaultCliffTextureScale(terrain, Edge.South),
            vertices,
            indices);
        if (terrain.IsEdgeDetached(tileX, tileY, Edge.West))
          AddCliffFace(
            terrain,
            tileX,
            tileY,
            Edge.West,
            color,
            DefaultCliffTextureScale(terrain, Edge.West),
            vertices,
            indices);
      }
    }

    return new Mesh(vertices, indices) { Name = name };
  }

  /// <summary>
  /// Builds deterministic texture batches for the terrain's decoded per-tile surface and cliff
  /// indices.
  /// </summary>
  /// <remarks>
  /// RCT3 DAT cells store one surface and one cliff index per tile. The simulation duplicates those
  /// values onto the tile's four corners so future paint tools can support blended terrain. Until
  /// that blending is implemented, mixed corner indices fail explicitly instead of silently choosing
  /// the wrong texture.
  /// </remarks>
  public static IReadOnlyList<TerrainMeshBatch> BuildBatches(
    Terrain terrain,
    Vector4 color,
    string name = "Terrain"
  ) => BuildBatches(terrain, color, (kind, index) =>
    ResolveCatalogTextureScale(terrain, kind, index), name);

  internal static IReadOnlyList<TerrainMeshBatch> BuildBatches(
    Terrain terrain,
    Vector4 color,
    Func<TerrainMaterialKind, byte, Vector2?> resolveTextureScale,
    string name = "Terrain"
  ) {
    ArgumentNullException.ThrowIfNull(resolveTextureScale);
    var geometry = new Dictionary<(TerrainMaterialKind Kind, byte Index), MeshGeometry>();

    for (var tileY = 0; tileY < terrain.Height; tileY++) {
      for (var tileX = 0; tileX < terrain.Width; tileX++) {
        var surfaceIndex = GetUniformMaterialIndex(
          terrain, tileX, tileY, TerrainMaterialKind.Surface);
        var surface = GetGeometry(geometry, TerrainMaterialKind.Surface, surfaceIndex);
        var surfaceTextureScale = resolveTextureScale(
          TerrainMaterialKind.Surface, surfaceIndex) ?? DefaultTopTextureScale(terrain);
        AddTopFace(
          terrain,
          tileX,
          tileY,
          color,
          surfaceTextureScale,
          surface.Vertices,
          surface.Indices);

        if (terrain.IsEdgeDetached(tileX, tileY, Edge.South)) {
          var cliffIndex = GetUniformMaterialIndex(
            terrain, tileX, tileY, TerrainMaterialKind.Cliff);
          var cliff = GetGeometry(geometry, TerrainMaterialKind.Cliff, cliffIndex);
          var cliffTextureScale = resolveTextureScale(
            TerrainMaterialKind.Cliff, cliffIndex) ??
            DefaultCliffTextureScale(terrain, Edge.South);
          AddCliffFace(
            terrain,
            tileX,
            tileY,
            Edge.South,
            color,
            cliffTextureScale,
            cliff.Vertices,
            cliff.Indices);
        }
        if (terrain.IsEdgeDetached(tileX, tileY, Edge.West)) {
          var cliffIndex = GetUniformMaterialIndex(
            terrain, tileX, tileY, TerrainMaterialKind.Cliff);
          var cliff = GetGeometry(geometry, TerrainMaterialKind.Cliff, cliffIndex);
          var cliffTextureScale = resolveTextureScale(
            TerrainMaterialKind.Cliff, cliffIndex) ??
            DefaultCliffTextureScale(terrain, Edge.West);
          AddCliffFace(
            terrain,
            tileX,
            tileY,
            Edge.West,
            color,
            cliffTextureScale,
            cliff.Vertices,
            cliff.Indices);
        }
      }
    }

    return geometry
      .OrderBy(batch => batch.Key.Kind)
      .ThenBy(batch => batch.Key.Index)
      .Select(batch => new TerrainMeshBatch(
        batch.Key.Kind,
        batch.Key.Index,
        new Mesh(batch.Value.Vertices, batch.Value.Indices) {
          Name = $"{name} {batch.Key.Kind} {batch.Key.Index}"
        }))
      .ToArray();
  }

  private static MeshGeometry GetGeometry(
    Dictionary<(TerrainMaterialKind Kind, byte Index), MeshGeometry> geometry,
    TerrainMaterialKind kind,
    byte index
  ) {
    var key = (kind, index);
    if (!geometry.TryGetValue(key, out var batch)) {
      batch = new MeshGeometry();
      geometry.Add(key, batch);
    }
    return batch;
  }

  private static byte GetUniformMaterialIndex(
    Terrain terrain,
    int tileX,
    int tileY,
    TerrainMaterialKind kind
  ) {
    var corners = terrain.GetCorners(tileX, tileY);
    var index = GetMaterialIndex(corners[0], kind);
    foreach (var corner in corners[1..]) {
      if (GetMaterialIndex(corner, kind) == index) continue;
      throw new InvalidOperationException(
        $"Terrain tile ({tileX}, {tileY}) has mixed {kind.ToString().ToLowerInvariant()} " +
        "indices; blended terrain rendering is not implemented.");
    }
    return index;
  }

  private static byte GetMaterialIndex(TerrainCorner corner, TerrainMaterialKind kind) => kind switch {
    TerrainMaterialKind.Surface => corner.SurfaceIndex,
    TerrainMaterialKind.Cliff => corner.CliffIndex,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  private static Vector3 CornerPosition(Terrain terrain, int tileX, int tileY, TerrainCornerSlot slot) {
    var (dx, dy) = slot switch {
      TerrainCornerSlot.SouthWest => (0, 0),
      TerrainCornerSlot.SouthEast => (1, 0),
      TerrainCornerSlot.NorthWest => (0, 1),
      TerrainCornerSlot.NorthEast => (1, 1),
      _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    var worldX = terrain.Origin.X + ((tileX + dx) * terrain.TileSize.X);
    var worldY = terrain.Origin.Y + ((tileY + dy) * terrain.TileSize.Y);
    var worldZ = Terrain.CornerHeightToWorldZ(terrain.GetCorner(tileX, tileY, slot).Height);
    return new Vector3(worldX, worldY, worldZ);
  }

  private static void AddTopFace(
    Terrain terrain,
    int tileX,
    int tileY,
    Vector4 color,
    Vector2 textureScale,
    List<Vertex> vertices,
    List<uint> indices) {
    var sw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthWest);
    var se = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthEast);
    var nw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthWest);
    var ne = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthEast);

    // RCT3 splits each terrain cell along its SouthEast-to-NorthWest diagonal. These are the same
    // two triangle orders serialized by WaterManager: (SW, SE, NW) and (NE, NW, SE).
    var southWestNormal = Vector3.Normalize(Vector3.Cross(se - sw, nw - sw));
    var northEastNormal = Vector3.Normalize(Vector3.Cross(nw - ne, se - ne));
    var sharedNormal = Vector3.Normalize(southWestNormal + northEastNormal);
    var baseIndex = (uint)vertices.Count;
    vertices.Add(new Vertex {
      Position = sw,
      Normal = southWestNormal,
      TexCoord = TopTextureCoordinate(terrain, sw, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = se,
      Normal = sharedNormal,
      TexCoord = TopTextureCoordinate(terrain, se, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = ne,
      Normal = northEastNormal,
      TexCoord = TopTextureCoordinate(terrain, ne, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = nw,
      Normal = sharedNormal,
      TexCoord = TopTextureCoordinate(terrain, nw, textureScale),
      Color = color
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

  private static void AddCliffFace(
    Terrain terrain,
    int tileX,
    int tileY,
    Edge edge,
    Vector4 color,
    Vector2 textureScale,
    List<Vertex> vertices,
    List<uint> indices) {
    var (nearSlot, farSlot) = edge switch {
      Edge.South => (TerrainCornerSlot.SouthWest, TerrainCornerSlot.SouthEast),
      Edge.West => (TerrainCornerSlot.SouthWest, TerrainCornerSlot.NorthWest),
      _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };
    var (dx, dy) = edge == Edge.South ? (0, -1) : (-1, 0);
    var neighborX = tileX + dx;
    var neighborY = tileY + dy;

    var nearTop = CornerPosition(terrain, tileX, tileY, nearSlot);
    var farTop = CornerPosition(terrain, tileX, tileY, farSlot);
    var nearNeighborSlot = edge == Edge.South ? TerrainCornerSlot.NorthWest : TerrainCornerSlot.SouthEast;
    var farNeighborSlot = edge == Edge.South ? TerrainCornerSlot.NorthEast : TerrainCornerSlot.NorthEast;
    var nearBottom = CornerPosition(terrain, neighborX, neighborY, nearNeighborSlot);
    var farBottom = CornerPosition(terrain, neighborX, neighborY, farNeighborSlot);

    // Wind so the face's outward normal points away from this tile, into the neighbor.
    var normal = Vector3.Normalize(Vector3.Cross(farTop - nearTop, nearBottom - nearTop));
    var baseIndex = (uint)vertices.Count;
    vertices.Add(new Vertex {
      Position = nearTop,
      Normal = normal,
      TexCoord = CliffTextureCoordinate(terrain, edge, nearTop, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = farTop,
      Normal = normal,
      TexCoord = CliffTextureCoordinate(terrain, edge, farTop, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = farBottom,
      Normal = normal,
      TexCoord = CliffTextureCoordinate(terrain, edge, farBottom, textureScale),
      Color = color
    });
    vertices.Add(new Vertex {
      Position = nearBottom,
      Normal = normal,
      TexCoord = CliffTextureCoordinate(terrain, edge, nearBottom, textureScale),
      Color = color
    });
    indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3]);
  }

  private static Vector2? ResolveCatalogTextureScale(
    Terrain terrain,
    TerrainMaterialKind kind,
    byte index
  ) {
    if (terrain.TextureCatalog == null) return null;
    var parameters = kind switch {
      TerrainMaterialKind.Surface => terrain.TextureCatalog.GetSurfaceParameters(index),
      TerrainMaterialKind.Cliff => terrain.TextureCatalog.GetCliffParameters(index),
      _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
    return new Vector2(parameters.InvWidth, parameters.InvHeight);
  }

  private static Vector2 DefaultTopTextureScale(Terrain terrain) => new(
    1f / terrain.TileSize.X,
    1f / terrain.TileSize.Y);

  private static Vector2 DefaultCliffTextureScale(Terrain terrain, Edge edge) {
    var edgeLength = edge switch {
      Edge.South => terrain.TileSize.X,
      Edge.West => terrain.TileSize.Y,
      _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };
    var inverseEdgeLength = 1f / edgeLength;
    return new Vector2(inverseEdgeLength, inverseEdgeLength);
  }

  private static Vector2 TopTextureCoordinate(
    Terrain terrain,
    Vector3 position,
    Vector2 textureScale
  ) => new(
    (position.X - terrain.Origin.X) * textureScale.X,
    (position.Y - terrain.Origin.Y) * textureScale.Y);

  private static Vector2 CliffTextureCoordinate(
    Terrain terrain,
    Edge edge,
    Vector3 position,
    Vector2 textureScale
  ) {
    var horizontalPosition = edge switch {
      Edge.South => position.X - terrain.Origin.X,
      Edge.West => position.Y - terrain.Origin.Y,
      _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };
    return new Vector2(horizontalPosition * textureScale.X, position.Z * textureScale.Y);
  }

  private sealed class MeshGeometry {
    public List<Vertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];
  }
}

/// <summary>Identifies which terrain texture catalog partition a mesh batch uses.</summary>
public enum TerrainMaterialKind {
  Surface,
  Cliff,
}

/// <summary>A terrain mesh whose faces all use one decoded texture catalog entry.</summary>
public sealed record TerrainMeshBatch(TerrainMaterialKind Kind, byte Index, Mesh Mesh);
