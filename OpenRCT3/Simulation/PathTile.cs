// Path Tile
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
namespace OpenRCT3.Simulation;

/// <summary>
/// Per-tile path data, stored sparsely in <see cref="Park.Paths"/> (most tiles have no path).
/// </summary>
/// <remarks>
/// <para>
/// An at-grade path (<see cref="Raised"/> is <c>false</c>) has no stored slope: its slope is derived
/// live from the underlying <see cref="Terrain"/> tile's corners, the same way <see cref="Terrain"/>
/// derives its own slope classification. Placement is restricted to terrain flatter than a 1m rise
/// across the tile (see <see cref="Park.TryPlacePath"/>), so this case never needs its own slope data.
/// </para>
/// <para>
/// A raised path is fully decoupled from terrain for slope/connectivity purposes and stores its own
/// <see cref="RaisedHeight"/> plus a discrete <see cref="PathRaisedSlope"/>, with
  /// <see cref="RaisedSlopeDirection"/> naming the tile's high edge for <see cref="Gentle"/>/
  /// <see cref="Sloped"/> shapes. The only remaining terrain interaction is support-post placement
/// (post height = raised height minus terrain height at that point), which is a rendering concern.
/// </para>
/// </remarks>
public struct PathTile {
  /// <summary>
  /// The path's serialized facing edge after converting RCT3's direction ordinal, or <c>null</c>
  /// when RCT3 stores its 255 sentinel on an unoriented flat tile.
  /// </summary>
  public Edge? Direction;

  /// <summary>The DAT entry ID of the referenced path-surface object.</summary>
  public ulong SurfaceReference;

  /// <summary>RCT3's legacy surface fallback selector; 255 means the reference is authoritative.</summary>
  public byte SurfaceType;

  /// <summary>Whether the expansion-era path record marks this tile as underground.</summary>
  public bool Underground;

  /// <summary>Whether this tile is a queue path, a distinct subtype used for ride queue lines.</summary>
  public bool IsQueue;

  /// <summary>
  /// The overall flow direction of a queue path tile, toward the named edge. Only meaningful when
  /// <see cref="IsQueue"/> is <c>true</c>; a queue tile has one flow direction, not independent
  /// per-edge state.
  /// </summary>
  public Edge? QueueFlowDirection;

  /// <summary>The directed queue segment's incoming edge, when serialized.</summary>
  public Edge? QueueStartDirection;

  /// <summary>The directed queue segment's outgoing edge, when serialized.</summary>
  public Edge? QueueEndDirection;

  /// <summary>The DAT entry ID of the owning queue network, or zero when unassigned.</summary>
  public ulong QueueLineReference;

  /// <summary>
  /// Whether this tile is raised (elevated on supports) rather than following terrain at-grade.
  /// </summary>
  public bool Raised;

  /// <summary>
  /// The signed height of this tile's low edge, in <see cref="Terrain.HeightStep"/> units. Only
  /// meaningful when <see cref="Raised"/> is <c>true</c>.
  /// </summary>
  public int RaisedHeight;

  /// <summary>The raised tile's discrete slope shape. Only meaningful when <see cref="Raised"/> is <c>true</c>.</summary>
  public PathRaisedSlope RaisedSlope;

  /// <summary>
  /// The edge that is the "high" side of a <see cref="PathRaisedSlope.Sloped"/> or
  /// <see cref="PathRaisedSlope.SteepStair"/> raised tile. Only meaningful when <see cref="Raised"/>
  /// is <c>true</c> and <see cref="RaisedSlope"/> is not <see cref="PathRaisedSlope.Flat"/>.
  /// </summary>
  public Edge RaisedSlopeDirection;

  /// <summary>
  /// Returns this raised tile's height at <paramref name="edge"/>: <see cref="RaisedHeight"/> on the
  /// low edge, <see cref="RaisedHeight"/> plus the slope's rise on <see cref="RaisedSlopeDirection"/>,
  /// and the midpoint on the two side edges.
  /// </summary>
  /// <remarks>Only valid when <see cref="Raised"/> is <c>true</c>; callers must check first.</remarks>
  public readonly int GetRaisedEdgeHeight(Edge edge) {
    if (RaisedSlope == PathRaisedSlope.Flat) return RaisedHeight;

    var rise = RaisedSlope.RiseInHeightStepUnits();
    if (edge == RaisedSlopeDirection) return checked(RaisedHeight + rise);
    if (edge == RaisedSlopeDirection.Opposite()) return RaisedHeight;
    return checked(RaisedHeight + (rise / 2));
  }
}
