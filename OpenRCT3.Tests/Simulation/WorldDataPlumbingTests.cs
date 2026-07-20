// World Data Plumbing Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WorldDataPlumbingTests {
  [Test]
  public void FromData_ReturnsEveryDecodedParkObjectCollection() {
    var data = Data();

    var terrain = Terrain.FromData(
      data,
      out var waterManager,
      out var paths,
      out var sceneryItems,
      out var sceneryItemPlacements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Width, Is.EqualTo(data.Width));
      Assert.That(waterManager, Is.SameAs(data.WaterManager));
      Assert.That(paths, Is.SameAs(data.Paths));
      Assert.That(sceneryItems, Is.SameAs(data.SceneryItems));
      Assert.That(sceneryItemPlacements, Is.SameAs(data.SceneryItemPlacements));
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
      out var sceneryItemPlacements);

    var park = World.BuildPark(
      terrain,
      waterManager,
      paths,
      sceneryItems,
      sceneryItemPlacements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(park.PathPlacements, Has.Count.EqualTo(1));
      Assert.That(park.Paths, Does.ContainKey((1, 1)));
      Assert.That(park.SceneryPlacements, Has.Count.EqualTo(1));
      Assert.That(park.SceneryPlacements[0].ObjectKey, Is.EqualTo("Test_SID"));
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
      entryId: 100,
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
      sceneryEntries: [sid, item, placementSingle]);
  }
}
