// DAT Queue Type Database Entry Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One exact <c>QueueTypeDatabaseEntry</c> DAT entry.</summary>
internal sealed class DatQueueTypeDatabaseEntryData : DatPathDatabaseEntryData {
  public DatQueueTypeDatabaseEntryData(
    ulong entryId,
    bool isAvailable,
    bool isHidden,
    bool isInvented,
    string systemName
  ) : base(entryId, isAvailable, isHidden, isInvented, systemName) { }
}
