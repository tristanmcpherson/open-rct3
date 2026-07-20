// Ride Track Topology Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackTopologyLoaderTests {
  [Test]
  public void Load_ExposesExactTopologyAndRetainsUnresolvedSentinelNeighbours() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.Add(Placement());

    RideTrackTopologyLoader.Load(park, [Track()], [Segment()]);

    var track = park.RideTracks.Single();
    var segment = park.RideTrackSegments.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.SourceEntryId, Is.EqualTo(700));
      Assert.That(track.Direction, Is.EqualTo(2));
      Assert.That(track.FirstSegmentSourceEntryId, Is.EqualTo(800));
      Assert.That(track.LastSegmentSourceEntryId, Is.EqualTo(800));
      Assert.That(track.IsCircuit, Is.False);
      Assert.That(track.HasSerializedTrackPieceOrder, Is.True);
      Assert.That(track.TrackPieceSourceEntryIds, Is.EqualTo(new ulong[] { 500 }));
      Assert.That(track.SegmentSourceEntryIds, Is.EqualTo(new ulong[] { 800 }));
      Assert.That(track.TrackedRideInstanceReference, Is.EqualTo(900));
      Assert.That(track.FlippedTrackSections, Is.Null);
      Assert.That(track.TunnelLightColour, Is.Null);
      Assert.That(
        (track.FlexiColour0, track.FlexiColour1, track.FlexiColour2),
        Is.EqualTo((4, 5, 6)));
      Assert.That(segment.RideTrackSourceEntryId, Is.EqualTo(track.SourceEntryId));
      Assert.That(segment.FirstPieceSourceEntryId, Is.EqualTo(500));
      Assert.That(segment.LastPieceSourceEntryId, Is.EqualTo(500));
      Assert.That(segment.NextSegmentReference, Is.EqualTo(1697));
      Assert.That(segment.PreviousSegmentReference, Is.EqualTo(1697));
    }
  }

  [Test]
  public void Load_ExpansionTrackDerivesMembershipWithoutInventingAbsentFields() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.Add(Placement());
    var expansionTrack = new DatRideTrackData(
      entryId: 700,
      direction: 2,
      firstSegment: 800,
      isCircuit: null,
      lastSegment: 800,
      prototype: false,
      trackPieces: null,
      trackFlexiColours: new DatSceneryFlexiColour(4, 5, 6),
      trackedRideInstance: 900,
      flippedTrackSections: true,
      tunnelLightColour: 12);

    RideTrackTopologyLoader.Load(park, [expansionTrack], [Segment()]);

    var track = park.RideTracks.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.IsCircuit, Is.Null);
      Assert.That(track.HasSerializedTrackPieceOrder, Is.False);
      Assert.That(track.TrackPieceSourceEntryIds, Is.EqualTo(new ulong[] { 500 }));
      Assert.That(track.FlippedTrackSections, Is.True);
      Assert.That(track.TunnelLightColour, Is.EqualTo(12));
    }
  }

  [Test]
  public void Load_EmptyBaseTrackListDerivesMembershipFromSegmentOwnership() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.Add(Placement());
    var trackWithEmptySerializedList = new DatRideTrackData(
      entryId: 700,
      direction: 2,
      firstSegment: 800,
      isCircuit: false,
      lastSegment: 800,
      prototype: false,
      trackPieces: [],
      trackFlexiColours: new DatSceneryFlexiColour(4, 5, 6),
      trackedRideInstance: 900);

    RideTrackTopologyLoader.Load(park, [trackWithEmptySerializedList], [Segment()]);

    var track = park.RideTracks.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.HasSerializedTrackPieceOrder, Is.False);
      Assert.That(track.TrackPieceSourceEntryIds, Is.EqualTo(new ulong[] { 500 }));
    }
  }

  [Test]
  public void Load_InconsistentPieceOwnerFailsWithoutPartiallyAddingTopology() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.Add(Placement(ownerReference: 801));

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackTopologyLoader.Load(park, [Track()], [Segment()])));
    using (Assert.EnterMultipleScope()) {
      Assert.That(park.RideTracks, Is.Empty);
      Assert.That(park.RideTrackSegments, Is.Empty);
    }
  }

  private static RideTrackPlacement Placement(ulong ownerReference = 800) => new(
    sourceEntryId: 500,
    sceneryPlacementSourceEntryId: 100,
    sidDatabaseEntryReference: 200,
    symbolName: "Test_SID:tks",
    objectKey: "Test_SID",
    overlayPath: "Tracks\\Test",
    tileX: 1,
    tileY: 1,
    rotation: Edge.North,
    serializedDirection: 1,
    serializedHeight: 6,
    corner: 3,
    ownerReference,
    segmentReference: 800,
    previousPieceReference: 0,
    nextPieceReference: 0,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 4,
    flexiColour1: 5,
    flexiColour2: 6);

  private static DatRideTrackData Track() => new(
    entryId: 700,
    direction: 2,
    firstSegment: 800,
    isCircuit: false,
    lastSegment: 800,
    prototype: false,
    trackPieces: [500],
    trackFlexiColours: new DatSceneryFlexiColour(4, 5, 6),
    trackedRideInstance: 900);

  private static DatTrackSegmentData Segment() => new(
    entryId: 800,
    direction: 2,
    firstPiece: 500,
    lastPiece: 500,
    nextSegment: 1697,
    prevSegment: 1697,
    prototype: false,
    track: 700);
}
