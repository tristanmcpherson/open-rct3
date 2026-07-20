// DAT Path Flying Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>PathFlying</c> DAT entry.</summary>
internal sealed class DatPathFlyingData : DatPathData {
  public int BaseHeight { get; }
  public int QuantisedHeight { get; }
  public ulong SceneryItem { get; }
  public byte SlopeType { get; }
  public bool? UndergroundFlag { get; }

  public DatPathFlyingData(
    ulong entryId,
    int baseHeight,
    byte colIndex,
    byte direction,
    byte pathType,
    int quantisedHeight,
    byte rowIndex,
    ulong sceneryItem,
    byte slopeType,
    ulong surface,
    byte surfaceType,
    bool? undergroundFlag,
    byte boolValue
  ) : base(
    entryId,
    DatPathStructureKind.PathFlying,
    colIndex,
    direction,
    pathType,
    rowIndex,
    surface,
    surfaceType,
    boolValue) {
    BaseHeight = baseHeight;
    QuantisedHeight = quantisedHeight;
    SceneryItem = sceneryItem;
    SlopeType = slopeType;
    UndergroundFlag = undergroundFlag;
  }
}
