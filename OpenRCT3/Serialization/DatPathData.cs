// DAT Path Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Raw fields shared by every decoded ordinary DAT path entry.</summary>
internal abstract class DatPathData {
  public ulong EntryId { get; }
  public DatPathStructureKind StructureKind { get; }
  public byte ColIndex { get; }
  public byte Direction { get; }
  public byte PathType { get; }
  public byte RowIndex { get; }
  public ulong Surface { get; }
  public byte SurfaceType { get; }
  public byte BoolValue { get; }

  protected DatPathData(
    ulong entryId,
    DatPathStructureKind structureKind,
    byte colIndex,
    byte direction,
    byte pathType,
    byte rowIndex,
    ulong surface,
    byte surfaceType,
    byte boolValue
  ) {
    EntryId = entryId;
    StructureKind = structureKind;
    ColIndex = colIndex;
    Direction = direction;
    PathType = pathType;
    RowIndex = rowIndex;
    Surface = surface;
    SurfaceType = surfaceType;
    BoolValue = boolValue;
  }
}
