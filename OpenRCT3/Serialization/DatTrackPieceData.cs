// DAT Track Piece Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>
/// Placement and linkage fields retained from one serialized RCT3 <c>TrackPiece</c> entry.
/// </summary>
internal sealed class DatTrackPieceData {
  public ulong EntryId { get; }
  public DatSceneryFlexiColour FlexiColourField { get; }
  public ulong Next { get; }
  public ulong Owner { get; }
  public ulong PlatformPiece { get; }
  public ulong Prev { get; }
  public bool Reversed { get; }
  public ulong SidDatabaseEntry { get; }
  public string SymbolName { get; }
  public ulong SceneryItem { get; }
  public DatSceneryItemDataField SceneryItemDataField { get; }
  public ulong Segment { get; }
  public int UserAngleDegrees { get; }

  public DatTrackPieceData(
    ulong entryId,
    DatSceneryFlexiColour flexiColourField,
    ulong next,
    ulong owner,
    ulong platformPiece,
    ulong prev,
    bool reversed,
    ulong sidDatabaseEntry,
    string symbolName,
    ulong sceneryItem,
    DatSceneryItemDataField sceneryItemDataField,
    ulong segment,
    int userAngleDegrees
  ) {
    ArgumentNullException.ThrowIfNull(symbolName);

    EntryId = entryId;
    FlexiColourField = flexiColourField;
    Next = next;
    Owner = owner;
    PlatformPiece = platformPiece;
    Prev = prev;
    Reversed = reversed;
    SidDatabaseEntry = sidDatabaseEntry;
    SymbolName = symbolName;
    SceneryItem = sceneryItem;
    SceneryItemDataField = sceneryItemDataField;
    Segment = segment;
    UserAngleDegrees = userAngleDegrees;
  }
}
