// DAT Scenery Item Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>One raw ordinary <c>SceneryItem</c> DAT entry.</summary>
internal sealed class DatSceneryItemData : DatSceneryEntryData {
  public DatSceneryItemVariant Variant { get; }
  public float? AdSpend { get; }
  public IReadOnlyList<DatSceneryAnimationInfo> AnimInfoList { get; }
  public int BreakFlags { get; }
  public float BreakTime { get; }
  public IReadOnlyList<ulong> BehaviourArray { get; }
  public ulong? CustomUvProvider { get; }
  public ulong DatabaseEntry { get; }
  public DatSidDatabaseEntryData? ResolvedDatabaseEntry { get; private set; }
  public bool ForceAbsoluteHeight { get; }
  public int FrameOffset { get; }
  public DatFireworkSlotTransform? FireworkSlotTransform { get; }
  public DatSceneryFlexiColour FlexiColourField { get; }
  public int HeightOffset { get; }
  public bool IsHidden { get; }
  public DatSceneryFlexiColour? LightFlexiColourField { get; }
  public int? MadIndex { get; }
  public ulong Owner { get; }
  public IReadOnlyList<ulong> ParticleSourceEntries { get; }
  public DatSceneryItemDataField SceneryItemDataField { get; }
  public ulong Vendor { get; }

  public DatSceneryItemData(
    ulong entryId,
    DatSceneryItemVariant variant,
    float? adSpend,
    DatSceneryAnimationInfo[] animInfoList,
    int breakFlags,
    float breakTime,
    ulong[] behaviourArray,
    ulong? customUvProvider,
    ulong databaseEntry,
    bool forceAbsoluteHeight,
    int frameOffset,
    DatFireworkSlotTransform? fireworkSlotTransform,
    DatSceneryFlexiColour flexiColourField,
    int heightOffset,
    bool isHidden,
    DatSceneryFlexiColour? lightFlexiColourField,
    int? madIndex,
    ulong owner,
    ulong[] particleSourceEntries,
    DatSceneryItemDataField sceneryItemDataField,
    ulong vendor
  ) : base(entryId) {
    ArgumentNullException.ThrowIfNull(animInfoList);
    ArgumentNullException.ThrowIfNull(behaviourArray);
    ArgumentNullException.ThrowIfNull(particleSourceEntries);

    Variant = variant;
    AdSpend = adSpend;
    AnimInfoList = Array.AsReadOnly((DatSceneryAnimationInfo[])animInfoList.Clone());
    BreakFlags = breakFlags;
    BreakTime = breakTime;
    BehaviourArray = Array.AsReadOnly((ulong[])behaviourArray.Clone());
    CustomUvProvider = customUvProvider;
    DatabaseEntry = databaseEntry;
    ForceAbsoluteHeight = forceAbsoluteHeight;
    FrameOffset = frameOffset;
    FireworkSlotTransform = fireworkSlotTransform;
    FlexiColourField = flexiColourField;
    HeightOffset = heightOffset;
    IsHidden = isHidden;
    LightFlexiColourField = lightFlexiColourField;
    MadIndex = madIndex;
    Owner = owner;
    ParticleSourceEntries = Array.AsReadOnly((ulong[])particleSourceEntries.Clone());
    SceneryItemDataField = sceneryItemDataField;
    Vendor = vendor;
  }

  internal void ResolveDatabaseEntry(DatSidDatabaseEntryData databaseEntry) {
    ArgumentNullException.ThrowIfNull(databaseEntry);
    if (databaseEntry.EntryId != DatabaseEntry)
      throw new ArgumentException(
        "The SID database entry does not match this scenery reference.",
        nameof(databaseEntry));
    ResolvedDatabaseEntry = databaseEntry;
  }
}
