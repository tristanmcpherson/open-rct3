// DAT Scenery Item Placement Single Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>SceneryItemPlacementSingle</c> DAT wrapper entry.</summary>
internal sealed class DatSceneryItemPlacementSingleData : DatSceneryEntryData {
  public DatSceneryFlexiColour FlexiColourField { get; }
  public ulong Owner { get; }
  public ulong SidDatabaseEntry { get; }
  public ulong SceneryItem { get; }
  public DatSceneryItemDataField SceneryItemDataField { get; }

  public DatSceneryItemPlacementSingleData(
    ulong entryId,
    DatSceneryFlexiColour flexiColourField,
    ulong owner,
    ulong sidDatabaseEntry,
    ulong sceneryItem,
    DatSceneryItemDataField sceneryItemDataField
  ) : base(entryId) {
    FlexiColourField = flexiColourField;
    Owner = owner;
    SidDatabaseEntry = sidDatabaseEntry;
    SceneryItem = sceneryItem;
    SceneryItemDataField = sceneryItemDataField;
  }
}
