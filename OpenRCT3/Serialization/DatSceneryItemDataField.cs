// DAT Scenery Item Data Field
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Raw placement coordinates embedded in a DAT scenery record.</summary>
internal readonly record struct DatSceneryItemDataField(
  int Corner,
  int Direction,
  int Height,
  float? HeightAdjust,
  int PosX,
  int PosZ);
