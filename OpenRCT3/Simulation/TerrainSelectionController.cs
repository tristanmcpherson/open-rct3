// Terrain Selection Controller
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>A validated terrain selection that preserves the exact raycast evidence.</summary>
internal readonly record struct TerrainSelection(TerrainRaycastHit Hit) {
  public int TileX => Hit.TileX;
  public int TileY => Hit.TileY;
  public TerrainTriangle Triangle => Hit.Triangle;
  public Vector3 Position => Hit.Position;
  public float Distance => Hit.Distance;
}

/// <summary>Tracks the current terrain selection within fixed terrain bounds.</summary>
internal sealed class TerrainSelectionController {
  private readonly int terrainWidth;
  private readonly int terrainHeight;

  public TerrainSelectionController(int terrainWidth, int terrainHeight) {
    if (terrainWidth <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(terrainWidth),
        "Terrain selection width must be positive.");
    if (terrainHeight <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(terrainHeight),
        "Terrain selection height must be positive.");
    this.terrainWidth = terrainWidth;
    this.terrainHeight = terrainHeight;
  }

  public TerrainSelection? Current { get; private set; }
  public bool HasSelection => Current.HasValue;

  /// <summary>Replaces the current selection after validating the complete raycast hit.</summary>
  public TerrainSelection Select(TerrainRaycastHit hit) {
    Validate(hit);
    var selection = new TerrainSelection(hit);
    Current = selection;
    return selection;
  }

  /// <summary>Clears the current selection and reports whether state changed.</summary>
  public bool Clear() {
    if (!Current.HasValue) return false;
    Current = null;
    return true;
  }

  private void Validate(TerrainRaycastHit hit) {
    if (hit.TileX < 0 || hit.TileX >= terrainWidth)
      throw new ArgumentOutOfRangeException(
        nameof(hit),
        $"Terrain selection tile X {hit.TileX} is outside 0-{terrainWidth - 1}.");
    if (hit.TileY < 0 || hit.TileY >= terrainHeight)
      throw new ArgumentOutOfRangeException(
        nameof(hit),
        $"Terrain selection tile Y {hit.TileY} is outside 0-{terrainHeight - 1}.");
    if (!Enum.IsDefined(hit.Triangle))
      throw new ArgumentOutOfRangeException(
        nameof(hit),
        $"Terrain selection triangle {hit.Triangle} is invalid.");
    if (!IsFinite(hit.Position) || !float.IsFinite(hit.Distance) || hit.Distance < 0f)
      throw new ArgumentException(
        "Terrain selection position and distance must be finite and distance nonnegative.",
        nameof(hit));
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
