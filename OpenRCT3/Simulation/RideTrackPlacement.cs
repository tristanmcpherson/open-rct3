// Ride Track Placement
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>
/// One semantic ride-track placement decoded from a DAT <c>TrackPiece</c>. Rendering remains attached
/// to the linked <see cref="SceneryPlacement"/> until native track geometry is implemented.
/// </summary>
public sealed class RideTrackPlacement {
  public ulong SourceEntryId { get; }
  public ulong SceneryPlacementSourceEntryId { get; }
  public ulong SidDatabaseEntryReference { get; }
  public string SymbolName { get; }
  public string ObjectKey { get; }
  public string OverlayPath { get; }
  public int TileX { get; }
  public int TileY { get; }
  public Edge Rotation { get; }
  public int SerializedDirection { get; }
  public int SerializedHeight { get; }
  public int Corner { get; }
  public ulong OwnerReference { get; }
  public ulong SegmentReference { get; }
  public ulong PreviousPieceReference { get; }
  public ulong NextPieceReference { get; }
  public ulong PlatformPieceReference { get; }
  public bool Reversed { get; }
  public int UserAngleDegrees { get; }
  public int FlexiColour0 { get; }
  public int FlexiColour1 { get; }
  public int FlexiColour2 { get; }

  internal RideTrackPlacement(
    ulong sourceEntryId,
    ulong sceneryPlacementSourceEntryId,
    ulong sidDatabaseEntryReference,
    string symbolName,
    string objectKey,
    string overlayPath,
    int tileX,
    int tileY,
    Edge rotation,
    int serializedDirection,
    int serializedHeight,
    int corner,
    ulong ownerReference,
    ulong segmentReference,
    ulong previousPieceReference,
    ulong nextPieceReference,
    ulong platformPieceReference,
    bool reversed,
    int userAngleDegrees,
    int flexiColour0,
    int flexiColour1,
    int flexiColour2
  ) {
    ArgumentNullException.ThrowIfNull(symbolName);
    ArgumentNullException.ThrowIfNull(objectKey);
    ArgumentNullException.ThrowIfNull(overlayPath);

    SourceEntryId = sourceEntryId;
    SceneryPlacementSourceEntryId = sceneryPlacementSourceEntryId;
    SidDatabaseEntryReference = sidDatabaseEntryReference;
    SymbolName = symbolName;
    ObjectKey = objectKey;
    OverlayPath = overlayPath;
    TileX = tileX;
    TileY = tileY;
    Rotation = rotation;
    SerializedDirection = serializedDirection;
    SerializedHeight = serializedHeight;
    Corner = corner;
    OwnerReference = ownerReference;
    SegmentReference = segmentReference;
    PreviousPieceReference = previousPieceReference;
    NextPieceReference = nextPieceReference;
    PlatformPieceReference = platformPieceReference;
    Reversed = reversed;
    UserAngleDegrees = userAngleDegrees;
    FlexiColour0 = flexiColour0;
    FlexiColour1 = flexiColour1;
    FlexiColour2 = flexiColour2;
  }
}
