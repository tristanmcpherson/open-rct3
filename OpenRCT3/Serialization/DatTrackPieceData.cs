// DAT Track Piece Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>
/// Placement and linkage fields retained from one serialized RCT3 <c>TrackPiece</c> entry.
/// </summary>
/// <remarks>
/// The native sampler subtracts <see cref="StartDistance"/> or
/// <see cref="StartDistanceBackwardsSpline"/> from a global wheel-contact distance before sampling
/// the piece-local track spline. These are saved positioning inputs, not reconstructed geometry.
/// </remarks>
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
  /// <summary>The saved global start distance for the piece's forward spline.</summary>
  public float StartDistance { get; }
  /// <summary>The saved global start distance for the piece's backwards spline.</summary>
  public float StartDistanceBackwardsSpline { get; }
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
  ) : this(
    entryId,
    flexiColourField,
    next,
    owner,
    platformPiece,
    prev,
    reversed,
    sidDatabaseEntry,
    symbolName,
    sceneryItem,
    sceneryItemDataField,
    segment,
    0f,
    0f,
    userAngleDegrees) { }

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
    float startDistance,
    float startDistanceBackwardsSpline,
    int userAngleDegrees
  ) {
    ArgumentNullException.ThrowIfNull(symbolName);
    if (!float.IsFinite(startDistance))
      throw new ArgumentOutOfRangeException(
        nameof(startDistance), "Track-piece start distance must be finite.");
    if (!float.IsFinite(startDistanceBackwardsSpline))
      throw new ArgumentOutOfRangeException(
        nameof(startDistanceBackwardsSpline),
        "Track-piece backwards-spline start distance must be finite.");

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
    StartDistance = startDistance;
    StartDistanceBackwardsSpline = startDistanceBackwardsSpline;
    UserAngleDegrees = userAngleDegrees;
  }
}
