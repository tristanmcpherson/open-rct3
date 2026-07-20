// DAT Path Queue Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>PathQueue</c> DAT entry.</summary>
internal sealed class DatPathQueueData : DatPathData {
  public int BaseHeight { get; }
  public byte EndDirection { get; }
  public ulong FenceEntry { get; }
  public DatFenceFlexiColours FenceFlexiColours { get; }
  public int QuantisedHeight { get; }
  public ulong QueueLine { get; }
  public ulong SceneryItem { get; }
  public byte SlopeType { get; }
  public byte StartDirection { get; }
  public bool? UndergroundFlag { get; }

  public DatPathQueueData(
    ulong entryId,
    int baseHeight,
    byte colIndex,
    byte direction,
    byte endDirection,
    ulong fenceEntry,
    DatFenceFlexiColours fenceFlexiColours,
    byte pathType,
    int quantisedHeight,
    ulong queueLine,
    byte rowIndex,
    ulong sceneryItem,
    byte slopeType,
    byte startDirection,
    ulong surface,
    byte surfaceType,
    bool? undergroundFlag,
    byte boolValue
  ) : base(
    entryId,
    DatPathStructureKind.PathQueue,
    colIndex,
    direction,
    pathType,
    rowIndex,
    surface,
    surfaceType,
    boolValue) {
    BaseHeight = baseHeight;
    EndDirection = endDirection;
    FenceEntry = fenceEntry;
    FenceFlexiColours = fenceFlexiColours;
    QuantisedHeight = quantisedHeight;
    QueueLine = queueLine;
    SceneryItem = sceneryItem;
    SlopeType = slopeType;
    StartDirection = startDirection;
    UndergroundFlag = undergroundFlag;
  }
}
