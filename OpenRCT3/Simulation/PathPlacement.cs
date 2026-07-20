// Path Placement
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>One path layer placed at an OOB-inclusive terrain-grid coordinate.</summary>
public readonly record struct PathPlacement(int TileX, int TileY, PathTile Tile);
