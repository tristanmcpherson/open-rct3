// DAT Path Ground Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>PathGround</c> DAT entry.</summary>
internal sealed class DatPathGroundData : DatPathData {
  public DatPathGroundData(
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
    DatPathStructureKind.PathGround,
    colIndex,
    direction,
    pathType,
    rowIndex,
    surface,
    surfaceType,
    boolValue) { }
}
