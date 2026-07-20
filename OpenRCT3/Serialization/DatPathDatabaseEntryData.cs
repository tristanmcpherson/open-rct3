// DAT Path Database Entry Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>
/// Fields shared by the exact <c>PathTypeDatabaseEntry</c> and
/// <c>QueueTypeDatabaseEntry</c> DAT structures.
/// </summary>
internal abstract class DatPathDatabaseEntryData : DatPathSurfaceEntryData {
  public bool IsAvailable { get; }
  public bool IsHidden { get; }
  public bool IsInvented { get; }
  public string SystemName { get; }

  protected DatPathDatabaseEntryData(
    ulong entryId,
    bool isAvailable,
    bool isHidden,
    bool isInvented,
    string systemName
  ) : base(entryId) {
    ArgumentNullException.ThrowIfNull(systemName);

    IsAvailable = isAvailable;
    IsHidden = isHidden;
    IsInvented = isInvented;
    SystemName = systemName;
  }
}
