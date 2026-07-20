// Path Raised Slope
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
namespace OpenRCT3.Simulation;

/// <summary>
/// The discrete slope of a raised <see cref="PathTile"/>, independent of the terrain underneath it.
/// </summary>
/// <remarks>
/// Unlike at-grade paths (which derive slope live from <see cref="Terrain"/> corners) and unlike
/// <see cref="Terrain"/> itself (a free-form per-corner height), a raised path only ever takes one of
/// these three fixed shapes.
/// </remarks>
public enum PathRaisedSlope {
  /// <summary>No rise; every edge of the tile sits at the same height.</summary>
  Flat = 0,
  /// <summary>A 1-meter rise from the low edge to the high edge.</summary>
  Gentle = 1,
  /// <summary>A 2-meter rise from the low edge to the high edge.</summary>
  Sloped = 2,
  /// <summary>Compatibility name for RCT3's 2-meter path slope.</summary>
  SteepStair = Sloped,
}

/// <summary>Helper methods for <see cref="PathRaisedSlope"/>.</summary>
public static class PathRaisedSlopeExtensions {
  /// <summary>The rise of this slope, in <see cref="Terrain.HeightStep"/> units.</summary>
  public static int RiseInHeightStepUnits(this PathRaisedSlope slope) => slope switch {
    PathRaisedSlope.Flat => 0,
    PathRaisedSlope.Gentle => 100, // 1m
    PathRaisedSlope.Sloped => 200, // 2m
    _ => throw new ArgumentOutOfRangeException(nameof(slope), slope, null),
  };
}
