// DAT Scenery Entry Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Raw identity shared by decoded ordinary DAT scenery entries.</summary>
internal abstract class DatSceneryEntryData {
  public ulong EntryId { get; }

  protected DatSceneryEntryData(ulong entryId) {
    EntryId = entryId;
  }
}
