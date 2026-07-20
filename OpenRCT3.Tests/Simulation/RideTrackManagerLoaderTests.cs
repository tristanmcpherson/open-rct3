// Ride Track Manager Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackManagerLoaderTests {
  [Test]
  public void Load_LinksSemanticTrackToExistingSceneryWithoutDuplicatingGeometry() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.SceneryPlacements.Add(Scenery());

    RideTrackManagerLoader.Load(park, terrain, [TrackPiece()]);

    var track = park.RideTrackPlacements.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(park.SceneryPlacements, Has.Count.EqualTo(1));
      Assert.That(track.SourceEntryId, Is.EqualTo(500));
      Assert.That(track.SceneryPlacementSourceEntryId, Is.EqualTo(100));
      Assert.That(track.SidDatabaseEntryReference, Is.EqualTo(200));
      Assert.That(track.SymbolName, Is.EqualTo("Test_SID:tks"));
      Assert.That(track.ObjectKey, Is.EqualTo("Test_SID"));
      Assert.That(track.OverlayPath, Is.EqualTo("Tracks\\Test"));
      Assert.That(track.Rotation, Is.EqualTo(Edge.North));
      Assert.That(track.OwnerReference, Is.EqualTo(600));
      Assert.That(track.SegmentReference, Is.EqualTo(700));
      Assert.That(track.PreviousPieceReference, Is.EqualTo(499));
      Assert.That(track.NextPieceReference, Is.EqualTo(501));
      Assert.That(track.PlatformPieceReference, Is.EqualTo(800));
      Assert.That(track.Reversed, Is.True);
      Assert.That(track.UserAngleDegrees, Is.EqualTo(45));
      Assert.That(
        (track.FlexiColour0, track.FlexiColour1, track.FlexiColour2),
        Is.EqualTo((4, 5, 6)));
    }
  }

  [Test]
  public void Load_MissingSceneryLinkFailsWithoutPartiallyAddingTracks() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.SceneryPlacements.Add(Scenery());
    var missingLink = TrackPiece(entryId: 501, sceneryItem: 999);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackManagerLoader.Load(park, terrain, [TrackPiece(), missingLink])));
    Assert.That(park.RideTrackPlacements, Is.Empty);
  }

  private static SceneryPlacement Scenery() => new(
    "Test_SID",
    tileX: 1,
    tileY: 1,
    rotation: Edge.North,
    serializedHeight: 6
  ) {
    SourceEntryId = 100,
    DatabaseEntryReference = 200,
    OverlayPath = "Tracks\\Test",
    Corner = 3,
    SerializedDirection = 1,
  };

  private static DatTrackPieceData TrackPiece(
    ulong entryId = 500,
    ulong sceneryItem = 100
  ) => new(
    entryId,
    flexiColourField: new DatSceneryFlexiColour(4, 5, 6),
    next: 501,
    owner: 600,
    platformPiece: 800,
    prev: 499,
    reversed: true,
    sidDatabaseEntry: 200,
    symbolName: "Test_SID:tks",
    sceneryItem,
    sceneryItemDataField: new DatSceneryItemDataField(3, 1, 6, null, 1, 1),
    segment: 700,
    userAngleDegrees: 45);
}
