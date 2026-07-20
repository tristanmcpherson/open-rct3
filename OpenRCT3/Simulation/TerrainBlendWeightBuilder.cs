// Terrain Blend Weight Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>Quantized corner weights for one terrain surface layer on one tile.</summary>
public readonly record struct TerrainBlendLayer(
  byte SurfaceIndex,
  byte SouthWestWeight,
  byte SouthEastWeight,
  byte NorthWestWeight,
  byte NorthEastWeight
) {
  /// <summary>Returns the quantized weight for one corner.</summary>
  public byte GetWeight(TerrainCornerSlot slot) => slot switch {
    TerrainCornerSlot.SouthWest => SouthWestWeight,
    TerrainCornerSlot.SouthEast => SouthEastWeight,
    TerrainCornerSlot.NorthWest => NorthWestWeight,
    TerrainCornerSlot.NorthEast => NorthEastWeight,
    _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
  };
}

/// <summary>Builds RCT3-compatible GroundBlended surface weights from terrain geometry.</summary>
public static class TerrainBlendWeightBuilder {
  private const float QuantizationScale = 255.5f;

  /// <summary>Builds the layers for one tile using its installed terrain catalog.</summary>
  public static IReadOnlyList<TerrainBlendLayer> Build(
    Terrain terrain,
    int tileX,
    int tileY
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    var catalog = terrain.TextureCatalog
      ?? throw new InvalidOperationException("Terrain has no texture catalog.");
    return Build(terrain, tileX, tileY, catalog.GetSurfaceKind);
  }

  /// <summary>Builds the layers for one tile using a caller-supplied surface-kind lookup.</summary>
  /// <remarks>
  /// The original executable seeds the current surface at every corner. A GroundBlended current
  /// surface then admits continuous cardinal neighbors at both corners of their shared edge and
  /// continuous diagonal neighbors at their shared corner. A diagonal is continuous only when all
  /// four cardinal links around that two-by-two tile block are continuous. Only GroundBlended
  /// neighbors contribute. Duplicate surface IDs are collapsed before each corner is normalized.
  /// </remarks>
  public static IReadOnlyList<TerrainBlendLayer> Build(
    Terrain terrain,
    int tileX,
    int tileY,
    Func<byte, TerrainTypeKind> getSurfaceKind
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(getSurfaceKind);
    ValidateTile(terrain, tileX, tileY);

    var counts = new SortedDictionary<byte, int[]>();
    var kinds = new Dictionary<byte, TerrainTypeKind>();
    var currentSurface = GetUniformSurface(terrain, tileX, tileY);
    Add(counts, currentSurface, TerrainCornerSlot.SouthWest);
    Add(counts, currentSurface, TerrainCornerSlot.SouthEast);
    Add(counts, currentSurface, TerrainCornerSlot.NorthWest);
    Add(counts, currentSurface, TerrainCornerSlot.NorthEast);

    if (!IsGroundBlended(currentSurface, getSurfaceKind, kinds))
      return Quantize(counts);

    var west = IsEdgeContinuous(terrain, tileX, tileY, Edge.West);
    var east = IsEdgeContinuous(terrain, tileX, tileY, Edge.East);
    var south = IsEdgeContinuous(terrain, tileX, tileY, Edge.South);
    var north = IsEdgeContinuous(terrain, tileX, tileY, Edge.North);

    if (west)
      AddEligibleNeighbor(
        terrain, tileX - 1, tileY,
        TerrainCornerSlot.SouthWest, TerrainCornerSlot.NorthWest,
        getSurfaceKind, kinds, counts);
    if (east)
      AddEligibleNeighbor(
        terrain, tileX + 1, tileY,
        TerrainCornerSlot.SouthEast, TerrainCornerSlot.NorthEast,
        getSurfaceKind, kinds, counts);
    if (south)
      AddEligibleNeighbor(
        terrain, tileX, tileY - 1,
        TerrainCornerSlot.SouthWest, TerrainCornerSlot.SouthEast,
        getSurfaceKind, kinds, counts);
    if (north)
      AddEligibleNeighbor(
        terrain, tileX, tileY + 1,
        TerrainCornerSlot.NorthWest, TerrainCornerSlot.NorthEast,
        getSurfaceKind, kinds, counts);

    if (west && south && IsDiagonalContinuous(
          terrain, tileX - 1, tileY - 1, Edge.East, Edge.North))
      AddEligibleNeighbor(
        terrain, tileX - 1, tileY - 1,
        TerrainCornerSlot.SouthWest, null,
        getSurfaceKind, kinds, counts);
    if (east && south && IsDiagonalContinuous(
          terrain, tileX + 1, tileY - 1, Edge.West, Edge.North))
      AddEligibleNeighbor(
        terrain, tileX + 1, tileY - 1,
        TerrainCornerSlot.SouthEast, null,
        getSurfaceKind, kinds, counts);
    if (west && north && IsDiagonalContinuous(
          terrain, tileX - 1, tileY + 1, Edge.East, Edge.South))
      AddEligibleNeighbor(
        terrain, tileX - 1, tileY + 1,
        TerrainCornerSlot.NorthWest, null,
        getSurfaceKind, kinds, counts);
    if (east && north && IsDiagonalContinuous(
          terrain, tileX + 1, tileY + 1, Edge.West, Edge.South))
      AddEligibleNeighbor(
        terrain, tileX + 1, tileY + 1,
        TerrainCornerSlot.NorthEast, null,
        getSurfaceKind, kinds, counts);

    return Quantize(counts);
  }

  private static void ValidateTile(Terrain terrain, int tileX, int tileY) {
    if (tileX < 0 || tileX >= terrain.Width)
      throw new ArgumentOutOfRangeException(nameof(tileX), tileX, "Tile X is outside the terrain.");
    if (tileY < 0 || tileY >= terrain.Height)
      throw new ArgumentOutOfRangeException(nameof(tileY), tileY, "Tile Y is outside the terrain.");
  }

  private static byte GetUniformSurface(Terrain terrain, int tileX, int tileY) {
    ValidateTile(terrain, tileX, tileY);
    var surface = terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthWest).SurfaceIndex;
    if (terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthEast).SurfaceIndex != surface ||
        terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthWest).SurfaceIndex != surface ||
        terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthEast).SurfaceIndex != surface)
      throw new InvalidDataException(
        $"Terrain tile ({tileX}, {tileY}) has mixed surface indices.");
    return surface;
  }

  private static bool IsGroundBlended(
    byte surface,
    Func<byte, TerrainTypeKind> getSurfaceKind,
    IDictionary<byte, TerrainTypeKind> kinds
  ) {
    if (!kinds.TryGetValue(surface, out var kind)) {
      kind = getSurfaceKind(surface);
      if (kind is not TerrainTypeKind.GroundUnblended and not TerrainTypeKind.GroundBlended)
        throw new InvalidDataException(
          $"Surface index {surface} resolves to non-surface terrain kind {kind}.");
      kinds.Add(surface, kind);
    }
    return kind == TerrainTypeKind.GroundBlended;
  }

  private static bool IsEdgeContinuous(Terrain terrain, int tileX, int tileY, Edge edge) {
    if (!terrain.HasTile(tileX, tileY)) return false;
    var (dx, dy) = edge.Offset();
    if (!terrain.HasTile(tileX + dx, tileY + dy)) return false;
    return !terrain.IsEdgeDetached(tileX, tileY, edge);
  }

  private static bool IsDiagonalContinuous(
    Terrain terrain,
    int diagonalX,
    int diagonalY,
    Edge horizontalTowardCurrent,
    Edge verticalTowardCurrent
  ) => IsEdgeContinuous(terrain, diagonalX, diagonalY, horizontalTowardCurrent)
    && IsEdgeContinuous(terrain, diagonalX, diagonalY, verticalTowardCurrent);

  private static void AddEligibleNeighbor(
    Terrain terrain,
    int tileX,
    int tileY,
    TerrainCornerSlot firstCorner,
    TerrainCornerSlot? secondCorner,
    Func<byte, TerrainTypeKind> getSurfaceKind,
    IDictionary<byte, TerrainTypeKind> kinds,
    IDictionary<byte, int[]> counts
  ) {
    var surface = GetUniformSurface(terrain, tileX, tileY);
    if (!IsGroundBlended(surface, getSurfaceKind, kinds)) return;
    Add(counts, surface, firstCorner);
    if (secondCorner.HasValue) Add(counts, surface, secondCorner.Value);
  }

  private static void Add(
    IDictionary<byte, int[]> counts,
    byte surface,
    TerrainCornerSlot corner
  ) {
    if (!counts.TryGetValue(surface, out var cornerCounts)) {
      cornerCounts = new int[Terrain.CornersPerTile];
      counts.Add(surface, cornerCounts);
    }
    cornerCounts[Convert.ToInt32(corner)]++;
  }

  private static IReadOnlyList<TerrainBlendLayer> Quantize(
    SortedDictionary<byte, int[]> counts
  ) {
    var totals = new int[Terrain.CornersPerTile];
    foreach (var cornerCounts in counts.Values) {
      for (var corner = 0; corner < Terrain.CornersPerTile; corner++)
        totals[corner] += cornerCounts[corner];
    }

    var layers = new List<TerrainBlendLayer>(counts.Count);
    foreach (var (surface, cornerCounts) in counts) {
      layers.Add(new TerrainBlendLayer(
        surface,
        Quantize(cornerCounts[Convert.ToInt32(TerrainCornerSlot.SouthWest)],
          totals[Convert.ToInt32(TerrainCornerSlot.SouthWest)]),
        Quantize(cornerCounts[Convert.ToInt32(TerrainCornerSlot.SouthEast)],
          totals[Convert.ToInt32(TerrainCornerSlot.SouthEast)]),
        Quantize(cornerCounts[Convert.ToInt32(TerrainCornerSlot.NorthWest)],
          totals[Convert.ToInt32(TerrainCornerSlot.NorthWest)]),
        Quantize(cornerCounts[Convert.ToInt32(TerrainCornerSlot.NorthEast)],
          totals[Convert.ToInt32(TerrainCornerSlot.NorthEast)])));
    }
    return layers;
  }

  private static byte Quantize(int count, int total) {
    if (count == 0) return 0;
    if (total <= 0)
      throw new InvalidDataException("Terrain blend corner has no contributing surface.");
    var normalized = Convert.ToSingle(count) / Convert.ToSingle(total);
    // The executable multiplies by 255.5, then uses an x87 round-down sequence.
    return Convert.ToByte(MathF.Floor(normalized * QuantizationScale));
  }
}
