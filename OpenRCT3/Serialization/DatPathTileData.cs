// DAT Path Tile Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>PathTile</c> DAT entry.</summary>
internal sealed class DatPathTileData : DatPathData {
  public DatPathTileData(
    ulong entryId,
    byte colIndex,
    byte direction,
    byte pathType,
    byte rowIndex,
    ulong surface,
    byte surfaceType,
    byte boolValue
  ) : base(
    entryId,
    DatPathStructureKind.PathTile,
    colIndex,
    direction,
    pathType,
    rowIndex,
    surface,
    surfaceType,
    boolValue) { }
}
