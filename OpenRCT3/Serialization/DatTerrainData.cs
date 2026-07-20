// DatTerrainData
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>
/// Terrain metadata and row-major tile data decoded from an RCT3 DAT file.
/// </summary>
internal sealed class DatTerrainData {
  public int Width { get; }
  public int Height { get; }
  public float OriginX { get; }
  public float OriginY { get; }
  public float TileSizeX { get; }
  public float TileSizeY { get; }
  public IReadOnlyList<DatTerrainCell> Cells { get; }
  public DatWaterManagerData? WaterManager { get; }
  public IReadOnlyList<DatPathData> Paths { get; }
  public IReadOnlyList<DatPathSurfaceEntryData> PathSurfaceEntries { get; }
  public IReadOnlyList<DatPathTypeDatabaseEntryData> PathTypeDatabaseEntries { get; }
  public IReadOnlyList<DatQueueTypeDatabaseEntryData> QueueTypeDatabaseEntries { get; }
  public IReadOnlyList<DatQueueTypeGroundSurfaceData> QueueTypeGroundSurfaces { get; }
  public IReadOnlyList<DatSceneryEntryData> SceneryEntries { get; }
  public IReadOnlyList<DatSidDatabaseEntryData> SidDatabaseEntries { get; }
  public IReadOnlyList<DatSceneryItemData> SceneryItems { get; }
  public IReadOnlyList<DatSceneryItemPlacementSingleData> SceneryItemPlacements { get; }
  public IReadOnlyList<DatTrackPieceData> TrackPieces { get; }
  public IReadOnlyList<DatRideTrackData> RideTracks { get; }
  public IReadOnlyList<DatTrackSegmentData> TrackSegments { get; }
  public IReadOnlyList<DatTrackedRideInstanceData> TrackedRideInstances { get; }
  public IReadOnlyList<DatRideTrainInstanceData> RideTrainInstances { get; }
  public IReadOnlyList<DatRideCarInstanceData> RideCarInstances { get; }

  public DatTerrainData(
    int width,
    int height,
    float originX,
    float originY,
    float tileSizeX,
    float tileSizeY,
    DatTerrainCell[] cells,
    DatWaterManagerData? waterManager = null,
    DatPathData[]? paths = null,
    DatSceneryEntryData[]? sceneryEntries = null,
    DatTrackPieceData[]? trackPieces = null,
    DatRideTrackData[]? rideTracks = null,
    DatTrackSegmentData[]? trackSegments = null,
    DatPathSurfaceEntryData[]? pathSurfaceEntries = null,
    DatTrackedRideInstanceData[]? trackedRideInstances = null,
    DatRideTrainInstanceData[]? rideTrainInstances = null,
    DatRideCarInstanceData[]? rideCarInstances = null) {
    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    ArgumentNullException.ThrowIfNull(cells);
    if (cells.Length != checked(width * height))
      throw new ArgumentException("Cell count does not match the terrain dimensions.", nameof(cells));

    Width = width;
    Height = height;
    OriginX = originX;
    OriginY = originY;
    TileSizeX = tileSizeX;
    TileSizeY = tileSizeY;
    Cells = Array.AsReadOnly((DatTerrainCell[])cells.Clone());
    WaterManager = waterManager;
    var pathData = (DatPathData[])(paths?.Clone() ?? Array.Empty<DatPathData>());
    Paths = Array.AsReadOnly(pathData);
    var rawPathSurfaceEntries = (DatPathSurfaceEntryData[])(
      pathSurfaceEntries?.Clone() ?? Array.Empty<DatPathSurfaceEntryData>());
    var pathTypeDatabaseEntries = new List<DatPathTypeDatabaseEntryData>();
    var queueTypeDatabaseEntries = new List<DatQueueTypeDatabaseEntryData>();
    var queueTypeGroundSurfaces = new List<DatQueueTypeGroundSurfaceData>();
    foreach (var entry in rawPathSurfaceEntries) {
      switch (entry) {
        case DatPathTypeDatabaseEntryData pathType:
          pathTypeDatabaseEntries.Add(pathType);
          break;
        case DatQueueTypeDatabaseEntryData queueType:
          queueTypeDatabaseEntries.Add(queueType);
          break;
        case DatQueueTypeGroundSurfaceData queueGround:
          queueTypeGroundSurfaces.Add(queueGround);
          break;
      }
    }

    PathSurfaceEntries = Array.AsReadOnly(rawPathSurfaceEntries);
    PathTypeDatabaseEntries = pathTypeDatabaseEntries.AsReadOnly();
    QueueTypeDatabaseEntries = queueTypeDatabaseEntries.AsReadOnly();
    QueueTypeGroundSurfaces = queueTypeGroundSurfaces.AsReadOnly();
    var rawSceneryEntries = (DatSceneryEntryData[])(
      sceneryEntries?.Clone() ?? Array.Empty<DatSceneryEntryData>());
    var sidDatabaseEntries = new List<DatSidDatabaseEntryData>();
    var sceneryItems = new List<DatSceneryItemData>();
    var sceneryItemPlacements = new List<DatSceneryItemPlacementSingleData>();
    foreach (var entry in rawSceneryEntries) {
      switch (entry) {
        case DatSidDatabaseEntryData sidDatabaseEntry:
          sidDatabaseEntries.Add(sidDatabaseEntry);
          break;
        case DatSceneryItemData sceneryItem:
          sceneryItems.Add(sceneryItem);
          break;
        case DatSceneryItemPlacementSingleData sceneryItemPlacement:
          sceneryItemPlacements.Add(sceneryItemPlacement);
          break;
      }
    }

    SceneryEntries = Array.AsReadOnly(rawSceneryEntries);
    SidDatabaseEntries = sidDatabaseEntries.AsReadOnly();
    SceneryItems = sceneryItems.AsReadOnly();
    SceneryItemPlacements = sceneryItemPlacements.AsReadOnly();
    var rawTrackPieces = (DatTrackPieceData[])(
      trackPieces?.Clone() ?? Array.Empty<DatTrackPieceData>());
    TrackPieces = Array.AsReadOnly(rawTrackPieces);
    var rawRideTracks = (DatRideTrackData[])(
      rideTracks?.Clone() ?? Array.Empty<DatRideTrackData>());
    RideTracks = Array.AsReadOnly(rawRideTracks);
    var rawTrackSegments = (DatTrackSegmentData[])(
      trackSegments?.Clone() ?? Array.Empty<DatTrackSegmentData>());
    TrackSegments = Array.AsReadOnly(rawTrackSegments);
    var rawTrackedRideInstances = (DatTrackedRideInstanceData[])(
      trackedRideInstances?.Clone() ?? Array.Empty<DatTrackedRideInstanceData>());
    TrackedRideInstances = Array.AsReadOnly(rawTrackedRideInstances);
    var rawRideTrainInstances = (DatRideTrainInstanceData[])(
      rideTrainInstances?.Clone() ?? Array.Empty<DatRideTrainInstanceData>());
    RideTrainInstances = Array.AsReadOnly(rawRideTrainInstances);
    var rawRideCarInstances = (DatRideCarInstanceData[])(
      rideCarInstances?.Clone() ?? Array.Empty<DatRideCarInstanceData>());
    RideCarInstances = Array.AsReadOnly(rawRideCarInstances);
  }
}
