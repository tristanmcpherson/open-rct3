// DAT Path Structure Kind
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>The exact DAT structure name used by a decoded path entry.</summary>
internal enum DatPathStructureKind {
  PathTile,
  PathGround,
  PathFlying,
  PathQueue
}
