// Scenery Manager Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryManagerLoaderTests {
  [Test]
  public void Load_SoakedItemPreservesResolvedPlacementMetadata() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(
      entryId: 101,
      variant: DatSceneryItemVariant.Soaked,
      field: new DatSceneryItemDataField(
        Corner: 3,
        Direction: 3,
        Height: 6,
        HeightAdjust: 0.25f,
        PosX: 2,
        PosZ: 3),
      forceAbsoluteHeight: true,
      frameOffset: 12,
      colours: new DatSceneryFlexiColour(4, 5, 6),
      heightOffset: -7,
      isHidden: true,
      owner: 444);

    SceneryManagerLoader.Load(park, terrain, [source], []);

    var placement = park.SceneryPlacements.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(placement.ObjectKey, Is.EqualTo("Test_SID"));
      Assert.That(placement.OverlayPath, Is.EqualTo("Style\\Themed\\Test"));
      Assert.That(placement.TileX, Is.EqualTo(2));
      Assert.That(placement.TileY, Is.EqualTo(3));
      Assert.That(placement.Rotation, Is.EqualTo(Edge.South));
      Assert.That(placement.SourceEntryId, Is.EqualTo(101));
      Assert.That(placement.DatabaseEntryReference, Is.EqualTo(200));
      Assert.That(placement.Corner, Is.EqualTo(3));
      Assert.That(placement.SerializedDirection, Is.EqualTo(3));
      Assert.That(placement.SerializedHeight, Is.EqualTo(6));
      Assert.That(placement.HeightAdjust, Is.EqualTo(0.25f));
      Assert.That(placement.HeightOffset, Is.EqualTo(-7));
      Assert.That(placement.ForceAbsoluteHeight, Is.True);
      Assert.That(placement.IsHidden, Is.True);
      Assert.That(placement.OwnerReference, Is.EqualTo(444));
      Assert.That(placement.FlexiColour0, Is.EqualTo(4));
      Assert.That(placement.FlexiColour1, Is.EqualTo(5));
      Assert.That(placement.FlexiColour2, Is.EqualTo(6));
      Assert.That(placement.FrameOffset, Is.EqualTo(12));
    }
  }

  [TestCase(0, Edge.West)]
  [TestCase(1, Edge.North)]
  [TestCase(2, Edge.East)]
  [TestCase(3, Edge.South)]
  public void Load_ConvertsDatDirectionOrdinal(int direction, Edge expected) {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(field: Field(direction: direction));

    SceneryManagerLoader.Load(park, terrain, [source], []);

    Assert.That(park.SceneryPlacements.Single().Rotation, Is.EqualTo(expected));
  }

  [Test]
  public void Load_PreservesPersistedAnimationStatesInSerializedOrder() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(animationStates: [
      new DatSceneryAnimationInfo(true, 2, 1.25f, false),
      new DatSceneryAnimationInfo(false, -1, 0f, true)
    ]);

    SceneryManagerLoader.Load(park, terrain, [source], []);

    Assert.That(park.SceneryPlacements.Single().AnimationStates, Is.EqualTo(new[] {
      new SceneryAnimationState(true, 2, 1.25f, false),
      new SceneryAnimationState(false, -1, 0f, true)
    }));
  }

  [Test]
  public void Load_ResolvedPlacementSingleDoesNotDuplicateAndAllowsDifferentPlacementMetadata() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(entryId: 101, field: Field(direction: 0));
    var wrapper = Wrapper(
      sceneryItem: source.EntryId,
      sidDatabaseEntry: source.DatabaseEntry,
      field: new DatSceneryItemDataField(9, 2, 99, null, 8, 8));

    SceneryManagerLoader.Load(park, terrain, [source], [wrapper]);

    Assert.That(park.SceneryPlacements, Has.Count.EqualTo(1));
    Assert.That(park.SceneryPlacements[0].SourceEntryId, Is.EqualTo(source.EntryId));
  }

  [Test]
  public void Load_UnresolvedNonzeroPlacementSingleReferenceIsTolerated() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var wrapper = Wrapper(sceneryItem: 999, sidDatabaseEntry: 200);

    SceneryManagerLoader.Load(park, terrain, [], [wrapper]);

    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_ResolvedPlacementSingleWithContradictorySidFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(entryId: 101);
    var wrapper = Wrapper(sceneryItem: source.EntryId, sidDatabaseEntry: 201);

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [wrapper])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_UnresolvedSceneryDatabaseReferenceFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(resolveDatabaseEntry: false);

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [TestCase(-1, 1)]
  [TestCase(1, -1)]
  [TestCase(11, 1)]
  [TestCase(1, 11)]
  public void Load_OffGridCoordinateFailsClosed(int posX, int posZ) {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(field: Field(posX: posX, posZ: posZ));

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_InvalidDirectionFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(field: Field(direction: 4));

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_NonFiniteHeightAdjustFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(field: new DatSceneryItemDataField(0, 0, 0, float.NaN, 1, 1));

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_NonFiniteAnimationTimeFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Item(animationStates: [
      new DatSceneryAnimationInfo(true, 0, float.NaN, false)
    ]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [source], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  [Test]
  public void Load_DuplicateSceneryItemEntryIdFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var first = Item(entryId: 101);
    var duplicate = Item(entryId: 101, field: Field(posX: 2));

    Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryManagerLoader.Load(park, terrain, [first, duplicate], [])));
    Assert.That(park.SceneryPlacements, Is.Empty);
  }

  private static DatSceneryItemData Item(
    ulong entryId = 100,
    ulong databaseEntry = 200,
    DatSceneryItemVariant variant = DatSceneryItemVariant.Base,
    DatSceneryItemDataField? field = null,
    bool resolveDatabaseEntry = true,
    bool forceAbsoluteHeight = false,
    int frameOffset = 0,
    DatSceneryFlexiColour? colours = null,
    int heightOffset = 0,
    bool isHidden = false,
    ulong owner = 0,
    DatSceneryAnimationInfo[]? animationStates = null
  ) {
    var item = new DatSceneryItemData(
      entryId,
      variant,
      adSpend: null,
      animInfoList: animationStates ?? [],
      breakFlags: 0,
      breakTime: 0f,
      behaviourArray: [],
      customUvProvider: null,
      databaseEntry,
      forceAbsoluteHeight,
      frameOffset,
      fireworkSlotTransform: null,
      flexiColourField: colours ?? new DatSceneryFlexiColour(0, 0, 0),
      heightOffset,
      isHidden,
      lightFlexiColourField: null,
      madIndex: null,
      owner,
      particleSourceEntries: [],
      sceneryItemDataField: field ?? Field(),
      vendor: 0);
    if (resolveDatabaseEntry)
      item.ResolveDatabaseEntry(new DatSidDatabaseEntryData(
        databaseEntry,
        isAvailable: true,
        isHidden: false,
        isInvented: null,
        overlayFilename: "Style\\Themed\\Test",
        symbolName: "Test_SID"));
    return item;
  }

  private static DatSceneryItemPlacementSingleData Wrapper(
    ulong sceneryItem,
    ulong sidDatabaseEntry,
    DatSceneryItemDataField? field = null
  ) => new(
    entryId: 300,
    flexiColourField: new DatSceneryFlexiColour(7, 8, 9),
    owner: 500,
    sidDatabaseEntry,
    sceneryItem,
    sceneryItemDataField: field ?? Field());

  private static DatSceneryItemDataField Field(
    int direction = 0,
    int posX = 1,
    int posZ = 1
  ) => new(
    Corner: 0,
    Direction: direction,
    Height: 0,
    HeightAdjust: null,
    PosX: posX,
    PosZ: posZ);
}
