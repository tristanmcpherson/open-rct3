// World Data Plumbing Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WorldDataPlumbingTests {
  private const ulong SceneryItemEntryId = 100;

  [Test]
  public void FromData_ReturnsEveryDecodedParkObjectCollection() {
    var data = Data();

    var terrain = Terrain.FromData(
      data,
      out var waterManager,
      out var paths,
      out var sceneryItems,
      out var sceneryItemPlacements,
      out var trackPieces,
      out var rideTracks,
      out var trackSegments,
      out var trackedRideInstances,
      out var rideTrainInstances,
      out var rideCarInstances,
      out var wildAnimalSpeciesDatabaseEntries,
      out var wildAnimalVisuals,
      out var wildAnimalPlacements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Width, Is.EqualTo(data.Width));
      Assert.That(waterManager, Is.SameAs(data.WaterManager));
      Assert.That(paths, Is.SameAs(data.Paths));
      Assert.That(sceneryItems, Is.SameAs(data.SceneryItems));
      Assert.That(sceneryItemPlacements, Is.SameAs(data.SceneryItemPlacements));
      Assert.That(trackPieces, Is.SameAs(data.TrackPieces));
      Assert.That(rideTracks, Is.SameAs(data.RideTracks));
      Assert.That(trackSegments, Is.SameAs(data.TrackSegments));
      Assert.That(trackedRideInstances, Is.SameAs(data.TrackedRideInstances));
      Assert.That(rideTrainInstances, Is.SameAs(data.RideTrainInstances));
      Assert.That(rideCarInstances, Is.SameAs(data.RideCarInstances));
      Assert.That(
        wildAnimalSpeciesDatabaseEntries,
        Is.SameAs(data.WildAnimalSpeciesDatabaseEntries));
      Assert.That(wildAnimalVisuals, Is.SameAs(data.WildAnimalVisuals));
      Assert.That(wildAnimalPlacements, Is.SameAs(data.WildAnimalPlacements));
    }
  }

  [Test]
  public void BuildPark_LoadsPathsAndOrdinarySceneryWithoutDuplicatingWrapper() {
    var data = Data();
    var terrain = Terrain.FromData(
      data,
      out var waterManager,
      out var paths,
      out var sceneryItems,
      out var sceneryItemPlacements,
      out var trackPieces,
      out var rideTracks,
      out var trackSegments,
      out var trackedRideInstances,
      out var rideTrainInstances,
      out var rideCarInstances,
      out var wildAnimalSpeciesDatabaseEntries,
      out var wildAnimalVisuals,
      out var wildAnimalPlacements);

    var park = World.BuildPark(
      terrain,
      waterManager,
      paths,
      sceneryItems,
      sceneryItemPlacements,
      trackPieces,
      rideTracks,
      trackSegments,
      trackedRideInstances,
      rideTrainInstances,
      rideCarInstances,
      wildAnimalSpeciesDatabaseEntries,
      wildAnimalVisuals,
      wildAnimalPlacements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(park.PathPlacements, Has.Count.EqualTo(1));
      Assert.That(park.Paths, Does.ContainKey((1, 1)));
      Assert.That(park.SceneryPlacements, Has.Count.EqualTo(1));
      Assert.That(park.SceneryPlacements[0].ObjectKey, Is.EqualTo("Test_SID"));
      Assert.That(park.RideTrackPlacements, Has.Count.EqualTo(1));
      Assert.That(park.RideTrackPieceRecords, Is.EqualTo(data.TrackPieces));
      Assert.That(park.RideTracks, Has.Count.EqualTo(1));
      Assert.That(park.RideTrackSegments, Has.Count.EqualTo(1));
      Assert.That(park.TrackedRideInstances, Is.EqualTo(data.TrackedRideInstances));
      Assert.That(park.RideTrainInstances, Is.EqualTo(data.RideTrainInstances));
      Assert.That(park.RideCarInstances, Is.EqualTo(data.RideCarInstances));
      Assert.That(
        park.WildAnimalSpeciesDatabaseEntries,
        Is.EqualTo(data.WildAnimalSpeciesDatabaseEntries));
      Assert.That(park.WildAnimalVisuals, Is.EqualTo(data.WildAnimalVisuals));
      Assert.That(park.WildAnimalPlacements, Is.EqualTo(data.WildAnimalPlacements));
      Assert.That(
        park.RideTrackPlacements[0].SceneryPlacementSourceEntryId,
        Is.EqualTo(SceneryItemEntryId));
    }
  }

  private static DatTerrainData Data() {
    var sid = new DatSidDatabaseEntryData(
      entryId: 200,
      isAvailable: true,
      isHidden: false,
      isInvented: null,
      overlayFilename: "Style\\Themed\\Test",
      symbolName: "Test_SID");
    var item = new DatSceneryItemData(
      entryId: SceneryItemEntryId,
      variant: DatSceneryItemVariant.Base,
      adSpend: null,
      animInfoList: [],
      breakFlags: 0,
      breakTime: 0f,
      behaviourArray: [],
      customUvProvider: null,
      databaseEntry: sid.EntryId,
      forceAbsoluteHeight: false,
      frameOffset: 0,
      fireworkSlotTransform: null,
      flexiColourField: new DatSceneryFlexiColour(0, 0, 0),
      heightOffset: 0,
      isHidden: false,
      lightFlexiColourField: null,
      madIndex: null,
      owner: 0,
      particleSourceEntries: [],
      sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1),
      vendor: 0);
    item.ResolveDatabaseEntry(sid);
    var placementSingle = new DatSceneryItemPlacementSingleData(
      entryId: 300,
      flexiColourField: new DatSceneryFlexiColour(0, 0, 0),
      owner: 0,
      sidDatabaseEntry: sid.EntryId,
      sceneryItem: item.EntryId,
      sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1));
    var path = new DatPathGroundData(
      entryId: 400,
      colIndex: 1,
      direction: 0,
      pathType: 0,
      rowIndex: 1,
      surface: 0,
      surfaceType: byte.MaxValue,
      boolValue: 0);
    var trackPiece = new DatTrackPieceData(
      entryId: 500,
      flexiColourField: new DatSceneryFlexiColour(1, 2, 3),
      next: 0,
      owner: 800,
      platformPiece: 0,
      prev: 0,
      reversed: false,
      sidDatabaseEntry: sid.EntryId,
      symbolName: "Test_SID:tks",
      sceneryItem: item.EntryId,
      sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1),
      segment: 800,
      userAngleDegrees: 0);
    var rideTrack = new DatRideTrackData(
      entryId: 700,
      direction: 2,
      firstSegment: 800,
      isCircuit: false,
      lastSegment: 800,
      prototype: false,
      trackPieces: [trackPiece.EntryId],
      trackFlexiColours: new DatSceneryFlexiColour(1, 2, 3),
      trackedRideInstance: 900);
    var trackSegment = new DatTrackSegmentData(
      entryId: 800,
      direction: 2,
      firstPiece: trackPiece.EntryId,
      lastPiece: trackPiece.EntryId,
      nextSegment: 1697,
      prevSegment: 1697,
      prototype: false,
      track: rideTrack.EntryId);
    var trackedRideInstance = new DatTrackedRideInstanceData(
      entryId: 900,
      name: "Synthetic coaster",
      track: rideTrack.EntryId,
      trackedRideOverlayName: "rides\\synthetic",
      trackedRideSymbolName: "Synthetic_Ride",
      nTrains: 1,
      nCarsPerTrain: 4,
      trainSelection: 0,
      trains: [1_000]);
    var rideTrainInstance = new DatRideTrainInstanceData(
      entryId: 1_000,
      rideTrainOverlayName: @"Cars\Synthetic\SyntheticTrain",
      rideTrainSymbolName: "SyntheticTrain:rit",
      trackedRideInstance: trackedRideInstance.EntryId,
      whichTrain: 0,
      length: 12.5f,
      mass: 1_000f,
      cars: [1_001]);
    var rideCarInstance = new DatRideCarInstanceData(
      entryId: 1_001,
      rideTrainInstance: rideTrainInstance.EntryId,
      whichCar: 0,
      whichRideTrainCar: 0,
      trackPiece: trackPiece.EntryId,
      rearTrackPiece: trackPiece.EntryId,
      distance: 1.25f,
      reversed: false,
      speed: 2.5f);
    var wildAnimalSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
      1_100,
      true,
      @"WildAnimals\WildAnimals",
      "Ostrich");
    var wildAnimalVisual = new DatWildAnimalVisualData(
      1_101,
      [],
      true,
      true,
      Matrix4x4.CreateTranslation(12f, 3f, -8f));
    var wildAnimal = new DatWildAnimalData(
      1_102,
      wildAnimalSpecies.EntryId,
      wildAnimalVisual.EntryId,
      true,
      false,
      1);
    var wildAnimalPlacement = new DatWildAnimalPlacementData(
      wildAnimal,
      wildAnimalSpecies,
      wildAnimalVisual,
      DatWildAnimalVariantSelectionStatus.Unsupported);

    return new DatTerrainData(
      width: 12,
      height: 12,
      originX: 0f,
      originY: 0f,
      tileSizeX: 4f,
      tileSizeY: 4f,
      cells: new DatTerrainCell[12 * 12],
      waterManager: new DatWaterManagerData(12, 12, []),
      paths: [path],
      sceneryEntries: [sid, item, placementSingle],
      trackPieces: [trackPiece],
      rideTracks: [rideTrack],
      trackSegments: [trackSegment],
      trackedRideInstances: [trackedRideInstance],
      rideTrainInstances: [rideTrainInstance],
      rideCarInstances: [rideCarInstance],
      wildAnimalSpeciesDatabaseEntries: [wildAnimalSpecies],
      wildAnimalVisuals: [wildAnimalVisual],
      wildAnimals: [wildAnimal],
      wildAnimalPlacements: [wildAnimalPlacement]);
  }
}
