// DAT Path Surface Entry Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Raw identity shared by decoded DAT path-surface database entries.</summary>
internal abstract class DatPathSurfaceEntryData {
  public ulong EntryId { get; }

  protected DatPathSurfaceEntryData(ulong entryId) {
    EntryId = entryId;
  }
}
