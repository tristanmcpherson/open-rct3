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
  public IReadOnlyList<DatWildAnimalSpeciesDatabaseEntryData>
    WildAnimalSpeciesDatabaseEntries { get; }
  public IReadOnlyList<DatWildAnimalVisualData> WildAnimalVisuals { get; }
  public IReadOnlyList<DatWildAnimalData> WildAnimals { get; }
  public IReadOnlyList<DatWildAnimalPlacementData> WildAnimalPlacements { get; }
  public DatGameTimeData? GameTime { get; }
  public IReadOnlyList<DatGenericStructureInventoryData> GenericStructureInventory { get; }

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
    DatRideCarInstanceData[]? rideCarInstances = null,
    DatWildAnimalSpeciesDatabaseEntryData[]? wildAnimalSpeciesDatabaseEntries = null,
    DatWildAnimalVisualData[]? wildAnimalVisuals = null,
    DatWildAnimalData[]? wildAnimals = null,
    DatWildAnimalPlacementData[]? wildAnimalPlacements = null,
    DatGenericStructureInventoryData[]? genericStructureInventory = null,
    DatGameTimeData? gameTime = null) {
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
    var rawWildAnimalSpeciesDatabaseEntries =
      (DatWildAnimalSpeciesDatabaseEntryData[])(
        wildAnimalSpeciesDatabaseEntries?.Clone() ??
        Array.Empty<DatWildAnimalSpeciesDatabaseEntryData>());
    WildAnimalSpeciesDatabaseEntries = Array.AsReadOnly(
      rawWildAnimalSpeciesDatabaseEntries);
    var rawWildAnimalVisuals = (DatWildAnimalVisualData[])(
      wildAnimalVisuals?.Clone() ?? Array.Empty<DatWildAnimalVisualData>());
    WildAnimalVisuals = Array.AsReadOnly(rawWildAnimalVisuals);
    var rawWildAnimals = (DatWildAnimalData[])(
      wildAnimals?.Clone() ?? Array.Empty<DatWildAnimalData>());
    WildAnimals = Array.AsReadOnly(rawWildAnimals);
    var rawWildAnimalPlacements = (DatWildAnimalPlacementData[])(
      wildAnimalPlacements?.Clone() ?? Array.Empty<DatWildAnimalPlacementData>());
    WildAnimalPlacements = Array.AsReadOnly(rawWildAnimalPlacements);
    GameTime = gameTime;
    var rawGenericStructureInventory = (DatGenericStructureInventoryData[])(
      genericStructureInventory?.Clone() ?? Array.Empty<DatGenericStructureInventoryData>());
    GenericStructureInventory = Array.AsReadOnly(rawGenericStructureInventory);
  }
}

/// <summary>
/// Bounded structural evidence for entries consumed by the generic DAT field walker.
/// </summary>
internal sealed class DatGenericStructureInventoryData {
  public int StructureIndex { get; }
  public string Name { get; }
  public IReadOnlyList<DatGenericStructureFieldData> Fields { get; }
  public IReadOnlyList<ulong> EntryIds { get; }
  public int EntryCount => EntryIds.Count;

  public DatGenericStructureInventoryData(
    int structureIndex,
    string name,
    DatGenericStructureFieldData[] fields,
    ulong[] entryIds
  ) {
    if (structureIndex < 0) throw new ArgumentOutOfRangeException(nameof(structureIndex));
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(fields);
    ArgumentNullException.ThrowIfNull(entryIds);

    StructureIndex = structureIndex;
    Name = name;
    Fields = Array.AsReadOnly((DatGenericStructureFieldData[])fields.Clone());
    EntryIds = Array.AsReadOnly((ulong[])entryIds.Clone());
  }
}

/// <summary>One exact field definition from a generically consumed DAT structure.</summary>
internal sealed class DatGenericStructureFieldData {
  public string Name { get; }
  public string Kind { get; }
  public uint FixedSize { get; }
  public IReadOnlyList<DatGenericStructureFieldData> Children { get; }

  public DatGenericStructureFieldData(
    string name,
    string kind,
    uint fixedSize,
    DatGenericStructureFieldData[] children
  ) {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentException.ThrowIfNullOrWhiteSpace(kind);
    ArgumentNullException.ThrowIfNull(children);

    Name = name;
    Kind = kind;
    FixedSize = fixedSize;
    Children = Array.AsReadOnly((DatGenericStructureFieldData[])children.Clone());
  }
}
