// DAT Queue Type Ground Surface Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One exact <c>QueueTypeGroundSurface</c> DAT entry.</summary>
internal sealed class DatQueueTypeGroundSurfaceData : DatPathSurfaceEntryData {
  public DatPathSurfaceColours Colours { get; }
  public ulong QueueType { get; }
  public DatQueueTypeDatabaseEntryData? ResolvedQueueType { get; private set; }

  public DatQueueTypeGroundSurfaceData(
    ulong entryId,
    DatPathSurfaceColours colours,
    ulong queueType
  ) : base(entryId) {
    Colours = colours;
    QueueType = queueType;
  }

  internal void ResolveQueueType(DatQueueTypeDatabaseEntryData queueType) {
    ArgumentNullException.ThrowIfNull(queueType);
    if (queueType.EntryId != QueueType)
      throw new ArgumentException(
        "The queue type database entry does not match this ground-surface reference.",
        nameof(queueType));
    ResolvedQueueType = queueType;
  }
}
