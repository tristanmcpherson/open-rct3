// Path Visual Scene Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>Scene-owned native path models and explicit build outcomes.</summary>
internal sealed record PathVisualSceneLoadResult(
  IReadOnlyList<Model> Models,
  int RenderedPlacementCount,
  int FallbackPlacementCount,
  int UnsupportedTopologyPlacementCount,
  int ShapeModelCount,
  IReadOnlyDictionary<string, int> ShapeKinds
);

/// <summary>Builds PTD/QTD-selected native path shapes and their exact SHS materials.</summary>
internal static class PathVisualSceneLoader {
  private const float TerrainClearance = 0.04f;

  public static PathVisualSceneLoadResult Load(
    Park park,
    Terrain terrain,
    IPathVisualResourceResolver resources
  ) => Load(park, terrain, resources, StaticShapeMeshBuilder.BuildBatches);

  internal static PathVisualSceneLoadResult Load(
    Park park,
    Terrain terrain,
    IPathVisualResourceResolver resources,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> buildShapeBatches
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(buildShapeBatches);

    var placementsByTile = park.PathPlacements
      .GroupBy(placement => (placement.TileX, placement.TileY))
      .ToDictionary(group => group.Key, group => group.ToArray());
    var models = new List<Model>();
    var fallbackPlacements = new List<PathPlacement>();
    var shapeKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var renderedPlacements = 0;
    var unsupportedTopologyPlacements = 0;
    var shapeModelCount = 0;
    try {
      foreach (var placement in park.PathPlacements) {
        if (!resources.TryResolve(placement.Tile, out var resource)) {
          fallbackPlacements.Add(placement);
          continue;
        }

        bool Connected(Edge edge) => IsConnected(
          placement,
          edge,
          terrain,
          placementsByTile);
        bool Diagonal(PathCornerMask corner) => HasDiagonal(
          placement,
          corner,
          terrain,
          placementsByTile);
        if (resource is ResolvedPathTypeResource &&
            (!placement.Tile.Raised || placement.Tile.RaisedSlope == PathRaisedSlope.Flat) &&
            !Enum.GetValues<Edge>().Any(Connected)) {
          fallbackPlacements.Add(placement);
          unsupportedTopologyPlacements++;
          continue;
        }
        var selection = resource switch {
          ResolvedPathTypeResource path => PathVisualSelector.SelectOrdinary(
            path.Resource,
            placement.Tile,
            Connected,
            Diagonal),
          ResolvedQueueTypeResource queue => PathVisualSelector.SelectQueue(
            queue.Resource,
            placement.Tile),
          _ => throw new InvalidDataException(
            $"Path surface '{resource!.SystemName}' has an unsupported resource kind."),
        };
        if (!resources.TryResolveShape(
              placement.Tile,
              selection.OwnerName,
              out var resolvedShape))
          throw new InvalidDataException(
            $"Path surface '{resource!.SystemName}' cannot resolve owner " +
            $"'{selection.OwnerName}'.");

        var batches = buildShapeBatches(resolvedShape!.Shape)
          ?? throw new InvalidDataException(
            $"Path shape '{resolvedShape.Shape.Name}' produced null mesh batches.");
        if (batches.Any(batch => batch == null || batch.Mesh == null))
          throw new InvalidDataException(
            $"Path shape '{resolvedShape.Shape.Name}' produced a null batch or mesh.");
        var unowned = new HashSet<OpenCobra.GDK.Meshes.Mesh>(
          batches.Select(batch => batch.Mesh),
          ReferenceEqualityComparer.Instance);
        try {
          foreach (var batch in batches) {
            var material = CreateMaterial(
              placement,
              resolvedShape.ShapeSource,
              batch,
              resources);
            Model? model = null;
            try {
              model = new Model(batch.Mesh) {
                Material = material,
                Transform = new Transform {
                  Matrix = CreateTransform(placement, terrain, selection.QuarterTurns)
                }
              };
              models.Add(model);
              unowned.Remove(batch.Mesh);
              shapeModelCount++;
            } catch {
              if (model == null) material.Dispose();
              else model.Dispose();
              throw;
            }
          }
        } catch {
          foreach (var mesh in unowned) mesh.Dispose();
          throw;
        }
        renderedPlacements++;
        shapeKinds[selection.ShapeKind] =
          shapeKinds.GetValueOrDefault(selection.ShapeKind) + 1;
      }

      if (fallbackPlacements.Count > 0) {
        var fallbackPark = new Park(terrain);
        fallbackPark.PathPlacements.AddRange(fallbackPlacements);
        var fallbackBatches = PathMeshBuilder.BuildBatches(
          fallbackPark,
          terrain,
          new Vector4(0.72f, 0.64f, 0.50f, 1f),
          new Vector4(0.28f, 0.48f, 0.70f, 1f),
          "Unresolved Paths");
        var unownedFallbackMeshes = new HashSet<OpenCobra.GDK.Meshes.Mesh>(
          fallbackBatches.Select(batch => batch.Mesh),
          ReferenceEqualityComparer.Instance);
        try {
          foreach (var batch in fallbackBatches) {
            var material = new Flat();
            Model? model = null;
            try {
              model = new Model(batch.Mesh) { Material = material };
              models.Add(model);
              unownedFallbackMeshes.Remove(batch.Mesh);
            } catch {
              if (model == null) material.Dispose();
              else model.Dispose();
              throw;
            }
          }
        } catch {
          foreach (var mesh in unownedFallbackMeshes) mesh.Dispose();
          throw;
        }
      }

      return new PathVisualSceneLoadResult(
        models.ToArray(),
        renderedPlacements,
        fallbackPlacements.Count,
        unsupportedTopologyPlacements,
        shapeModelCount,
        new Dictionary<string, int>(shapeKinds, StringComparer.Ordinal));
    } catch {
      foreach (var model in models.AsEnumerable().Reverse()) model.Dispose();
      throw;
    }
  }

  internal static Material CreateMaterial(
    PathPlacement placement,
    SceneryResourceEntry shapeSource,
    StaticShapeMeshBatch batch,
    IPathVisualResourceResolver resources
  ) {
    var cullBackFaces = batch.Sides switch {
      1 => false,
      3 => true,
      _ => throw new InvalidDataException(
        $"Path shape '{batch.SourceMeshName}' has invalid sides {batch.Sides}."),
    };
    var engineGlobalMaterial = CreateEngineGlobalMaterial(batch);
    if (engineGlobalMaterial != null) {
      engineGlobalMaterial.CullBackFaces = cullBackFaces;
      return engineGlobalMaterial;
    }

    var blendMode = batch.Transparency switch {
      0 => MaterialBlendMode.Opaque,
      1 => MaterialBlendMode.AlphaMask,
      2 => MaterialBlendMode.Alpha,
      _ => throw new InvalidDataException(
        $"Path shape '{batch.SourceMeshName}' has invalid transparency {batch.Transparency}."),
    };
    var tile = placement.Tile;
    Texture? texture;
    var foundTexture = string.IsNullOrWhiteSpace(batch.FtxRef)
      ? resources.TryResolveTexture(tile, out texture)
      : resources.TryResolveShapeTexture(
        tile,
        shapeSource,
        batch.FtxRef,
        out texture);
    if (!foundTexture || texture == null)
      throw new InvalidDataException(
        $"Path shape '{batch.SourceMeshName}' material " +
        $"'{batch.FtxRef ?? "PTD/QTD surface"}' was not found.");

    var alphaReference = ResolveAlphaReference(batch.TxsRef);
    var material = alphaReference.HasValue
      ? new Textured(blendMode, alphaReference.Value)
      : new Textured(blendMode);
    try {
      material.AlbedoTexture = texture;
      material.CullBackFaces = cullBackFaces;
      return material;
    } catch {
      material.Dispose();
      throw;
    }
  }

  private static Material? CreateEngineGlobalMaterial(StaticShapeMeshBatch batch) {
    if (string.Equals(batch.TxsRef, "SIWater:txs", StringComparison.OrdinalIgnoreCase)) {
      if (batch.Transparency != 2)
        throw new InvalidDataException(
          $"SIWater path batch '{batch.FtxRef}' must use alpha transparency 2, " +
          $"got {batch.Transparency}.");
      return new Water();
    }

    if (string.Equals(
          batch.TxsRef,
          "SIOpaqueChrome:txs",
          StringComparison.OrdinalIgnoreCase)) {
      if (batch.Transparency != 0)
        throw new InvalidDataException(
          $"SIOpaqueChrome path batch '{batch.FtxRef}' must be opaque, " +
          $"got transparency {batch.Transparency}.");
      return new Chrome();
    }

    return null;
  }

  private static byte? ResolveAlphaReference(string? taggedReference) {
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMaskLow")) return 100;
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMask"))
      return Textured.DefaultAlphaMaskReference;
    if (IsTextureStyleFamily(taggedReference, "SIAlpha")) return 8;
    return null;
  }

  private static bool IsTextureStyleFamily(
    string? taggedReference,
    string familyName
  ) {
    const string tag = ":txs";
    if (string.IsNullOrEmpty(taggedReference) ||
        !taggedReference.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
      return false;
    var resourceName = taggedReference[..^tag.Length];
    return resourceName.StartsWith(familyName, StringComparison.OrdinalIgnoreCase);
  }

  private static Matrix4x4 CreateTransform(
    PathPlacement placement,
    Terrain terrain,
    int quarterTurns
  ) {
    var center = new Vector2(
      terrain.Origin.X + ((placement.TileX + 0.5f) * terrain.TileSize.X),
      terrain.Origin.Y + ((placement.TileY + 0.5f) * terrain.TileSize.Y));
    var height = placement.Tile.Raised
      ? Terrain.CornerHeightToWorldZ(placement.Tile.RaisedHeight)
      : AverageTerrainHeight(terrain, placement.TileX, placement.TileY) + TerrainClearance;
    return QuarterTurn(quarterTurns) *
      Matrix4x4.CreateTranslation(new Vector3(center, height));
  }

  private static Matrix4x4 QuarterTurn(int quarterTurns) =>
    (((quarterTurns % 4) + 4) % 4) switch {
      0 => Matrix4x4.Identity,
      1 => new Matrix4x4(
        0f, 1f, 0f, 0f,
        -1f, 0f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f),
      2 => new Matrix4x4(
        -1f, 0f, 0f, 0f,
        0f, -1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f),
      3 => new Matrix4x4(
        0f, -1f, 0f, 0f,
        1f, 0f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f),
      _ => throw new ArgumentOutOfRangeException(nameof(quarterTurns)),
    };

  private static float AverageTerrainHeight(Terrain terrain, int tileX, int tileY) {
    var total = 0f;
    foreach (var corner in terrain.GetCorners(tileX, tileY))
      total += Terrain.CornerHeightToWorldZ(corner.Height);
    return total / Terrain.CornersPerTile;
  }

  private static bool IsConnected(
    PathPlacement placement,
    Edge edge,
    Terrain terrain,
    IReadOnlyDictionary<(int X, int Y), PathPlacement[]> placementsByTile
  ) {
    var (dx, dy) = edge.Offset();
    if (!placementsByTile.TryGetValue(
          (placement.TileX + dx, placement.TileY + dy),
          out var neighbors)) return false;
    foreach (var neighbor in neighbors) {
      if (placement.Tile.Raised != neighbor.Tile.Raised) continue;
      if (placement.Tile.Raised) {
        if (placement.Tile.GetRaisedEdgeHeight(edge) ==
            neighbor.Tile.GetRaisedEdgeHeight(edge.Opposite())) return true;
        continue;
      }
      var (firstA, firstB) = terrain.GetEdgeCornerHeights(
        placement.TileX,
        placement.TileY,
        edge);
      var (secondA, secondB) = terrain.GetEdgeCornerHeights(
        neighbor.TileX,
        neighbor.TileY,
        edge.Opposite());
      var difference = Math.Max(
        Math.Abs(Convert.ToInt64(firstA) - secondA),
        Math.Abs(Convert.ToInt64(firstB) - secondB));
      if (difference <= Park.AtGradePathMaxRise / 2) return true;
    }
    return false;
  }

  internal static bool HasDiagonal(
    PathPlacement placement,
    PathCornerMask corner,
    Terrain terrain,
    IReadOnlyDictionary<(int X, int Y), PathPlacement[]> placementsByTile
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(placementsByTile);
    var (dx, dy) = corner switch {
      PathCornerMask.SouthWest => (-1, -1),
      PathCornerMask.SouthEast => (1, -1),
      PathCornerMask.NorthWest => (-1, 1),
      PathCornerMask.NorthEast => (1, 1),
      _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };
    if (!placementsByTile.TryGetValue(
          (placement.TileX + dx, placement.TileY + dy),
          out var diagonals)) return false;
    var placementCorner = ToTerrainCorner(corner);
    var candidateCorner = OppositeCorner(placementCorner);
    foreach (var candidate in diagonals) {
      if (candidate.Tile.Raised != placement.Tile.Raised) continue;
      if (placement.Tile.Raised) {
        if (GetRaisedCornerHeight(placement.Tile, placementCorner) ==
            GetRaisedCornerHeight(candidate.Tile, candidateCorner)) return true;
        continue;
      }

      var placementHeight = terrain.GetCorner(
        placement.TileX,
        placement.TileY,
        placementCorner).Height;
      var candidateHeight = terrain.GetCorner(
        candidate.TileX,
        candidate.TileY,
        candidateCorner).Height;
      if (Math.Abs(Convert.ToInt64(placementHeight) - candidateHeight) <=
          Park.AtGradePathMaxRise / 2) return true;
    }
    return false;
  }

  private static TerrainCornerSlot ToTerrainCorner(PathCornerMask corner) => corner switch {
    PathCornerMask.SouthWest => TerrainCornerSlot.SouthWest,
    PathCornerMask.SouthEast => TerrainCornerSlot.SouthEast,
    PathCornerMask.NorthWest => TerrainCornerSlot.NorthWest,
    PathCornerMask.NorthEast => TerrainCornerSlot.NorthEast,
    _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
  };

  private static TerrainCornerSlot OppositeCorner(TerrainCornerSlot corner) => corner switch {
    TerrainCornerSlot.SouthWest => TerrainCornerSlot.NorthEast,
    TerrainCornerSlot.SouthEast => TerrainCornerSlot.NorthWest,
    TerrainCornerSlot.NorthWest => TerrainCornerSlot.SouthEast,
    TerrainCornerSlot.NorthEast => TerrainCornerSlot.SouthWest,
    _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
  };

  private static int GetRaisedCornerHeight(PathTile tile, TerrainCornerSlot corner) {
    if (!tile.Raised)
      throw new ArgumentException("Raised corner height requires a raised path tile.", nameof(tile));
    if (tile.RaisedSlope == PathRaisedSlope.Flat) return tile.RaisedHeight;
    if (!Enum.IsDefined(tile.RaisedSlope) || !Enum.IsDefined(tile.RaisedSlopeDirection))
      throw new InvalidDataException("Raised path tile has an invalid slope shape or direction.");
    var highCorner = (corner, tile.RaisedSlopeDirection) switch {
      (TerrainCornerSlot.SouthWest, Edge.South or Edge.West) => true,
      (TerrainCornerSlot.SouthEast, Edge.South or Edge.East) => true,
      (TerrainCornerSlot.NorthWest, Edge.North or Edge.West) => true,
      (TerrainCornerSlot.NorthEast, Edge.North or Edge.East) => true,
      _ => false,
    };
    return highCorner
      ? checked(tile.RaisedHeight + tile.RaisedSlope.RiseInHeightStepUnits())
      : tile.RaisedHeight;
  }
}
