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
      Assert.That(track.SerializedIsCircuit, Is.False);
      Assert.That(track.HasAuthoritativeTrackPieceOrder, Is.True);
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
  public void Load_ExpansionTrackReconstructsOpenTraversalFromPieceLinks() {
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
      Assert.That(track.IsCircuit, Is.False);
      Assert.That(track.SerializedIsCircuit, Is.Null);
      Assert.That(track.HasAuthoritativeTrackPieceOrder, Is.True);
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
      Assert.That(track.IsCircuit, Is.False);
      Assert.That(track.SerializedIsCircuit, Is.False);
      Assert.That(track.HasAuthoritativeTrackPieceOrder, Is.True);
      Assert.That(track.HasSerializedTrackPieceOrder, Is.False);
      Assert.That(track.TrackPieceSourceEntryIds, Is.EqualTo(new ulong[] { 500 }));
    }
  }

  [Test]
  public void Load_EmptyLegacyListUsesReciprocalRingInsteadOfAdvisoryCircuitField() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.AddRange([
      Placement(501, previousPieceReference: 500, nextPieceReference: 500),
      Placement(500, previousPieceReference: 501, nextPieceReference: 501),
    ]);
    var source = Track(trackPieces: [], isCircuit: false);
    var segment = Segment(firstPiece: 500, lastPiece: 501);

    RideTrackTopologyLoader.Load(park, [source], [segment]);

    var track = park.RideTracks.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.TrackPieceSourceEntryIds, Is.EqualTo(new ulong[] { 500, 501 }));
      Assert.That(track.IsCircuit, Is.True);
      Assert.That(track.SerializedIsCircuit, Is.False);
      Assert.That(track.HasAuthoritativeTrackPieceOrder, Is.True);
      Assert.That(track.HasSerializedTrackPieceOrder, Is.False);
    }
  }

  [Test]
  public void Load_DerivesOpenPieceAndSegmentOrderIndependentlyOfDatEntryOrder() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.AddRange([
      Placement(
        601,
        segmentReference: 801,
        ownerReference: 801,
        previousPieceReference: 600,
        nextPieceReference: 1697),
      Placement(501, previousPieceReference: 500, nextPieceReference: 1697),
      Placement(
        600,
        segmentReference: 801,
        ownerReference: 801,
        previousPieceReference: 1697,
        nextPieceReference: 601),
      Placement(500, previousPieceReference: 1697, nextPieceReference: 501),
    ]);
    var source = Track(
      isCircuit: null,
      firstSegment: 800,
      lastSegment: 801,
      omitTrackPieces: true);
    var first = Segment(
      entryId: 800,
      firstPiece: 500,
      lastPiece: 501,
      nextSegment: 801,
      prevSegment: 1697);
    var last = Segment(
      entryId: 801,
      firstPiece: 600,
      lastPiece: 601,
      nextSegment: 1697,
      prevSegment: 800);

    RideTrackTopologyLoader.Load(park, [source], [last, first]);

    var track = park.RideTracks.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.SegmentSourceEntryIds, Is.EqualTo(new ulong[] { 800, 801 }));
      Assert.That(track.TrackPieceSourceEntryIds,
        Is.EqualTo(new ulong[] { 500, 501, 600, 601 }));
      Assert.That(track.IsCircuit, Is.False);
      Assert.That(track.SerializedIsCircuit, Is.Null);
      Assert.That(track.HasAuthoritativeTrackPieceOrder, Is.True);
      Assert.That(track.HasSerializedTrackPieceOrder, Is.False);
    }
  }

  [Test]
  public void Load_MissingOrNonReciprocalPieceLinkFailsClosed() {
    var missingPark = new Park(buildableWidth: 1, buildableHeight: 1);
    missingPark.RideTrackPlacements.AddRange([
      Placement(500, previousPieceReference: 1697, nextPieceReference: 999),
      Placement(501, previousPieceReference: 500, nextPieceReference: 1697),
    ]);
    var source = Track(isCircuit: null, omitTrackPieces: true);
    var segment = Segment(firstPiece: 500, lastPiece: 501);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackTopologyLoader.Load(missingPark, [source], [segment])));

    var nonReciprocalPark = new Park(buildableWidth: 1, buildableHeight: 1);
    nonReciprocalPark.RideTrackPlacements.AddRange([
      Placement(500, previousPieceReference: 1697, nextPieceReference: 501),
      Placement(501, previousPieceReference: 999, nextPieceReference: 1697),
    ]);
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackTopologyLoader.Load(nonReciprocalPark, [source], [segment])));
  }

  [Test]
  public void Load_CycleBeforeLastPieceFailsWithoutPartialTopology() {
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.RideTrackPlacements.AddRange([
      Placement(500, previousPieceReference: 501, nextPieceReference: 501),
      Placement(501, previousPieceReference: 500, nextPieceReference: 500),
      Placement(502, previousPieceReference: 1697, nextPieceReference: 1697),
    ]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackTopologyLoader.Load(
        park,
        [Track(isCircuit: null, omitTrackPieces: true)],
        [Segment(firstPiece: 500, lastPiece: 502)])));
    using (Assert.EnterMultipleScope()) {
      Assert.That(park.RideTracks, Is.Empty);
      Assert.That(park.RideTrackSegments, Is.Empty);
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

  private static RideTrackPlacement Placement(
    ulong sourceEntryId = 500,
    ulong ownerReference = 800,
    ulong segmentReference = 800,
    ulong previousPieceReference = 0,
    ulong nextPieceReference = 0
  ) => new(
    sourceEntryId,
    sceneryPlacementSourceEntryId: sourceEntryId + 100,
    sidDatabaseEntryReference: sourceEntryId + 200,
    symbolName: $"Test{sourceEntryId}_SID:tks",
    objectKey: $"Test{sourceEntryId}_SID",
    overlayPath: "Tracks\\Test",
    tileX: 1,
    tileY: 1,
    rotation: Edge.North,
    serializedDirection: 1,
    serializedHeight: 6,
    corner: 3,
    ownerReference,
    segmentReference,
    previousPieceReference,
    nextPieceReference,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 4,
    flexiColour1: 5,
    flexiColour2: 6);

  private static DatRideTrackData Track(
    ulong[]? trackPieces = null,
    bool? isCircuit = false,
    ulong firstSegment = 800,
    ulong lastSegment = 800,
    bool omitTrackPieces = false
  ) => new(
    entryId: 700,
    direction: 2,
    firstSegment,
    isCircuit,
    lastSegment,
    prototype: false,
    trackPieces: omitTrackPieces ? null : trackPieces ?? [500],
    trackFlexiColours: new DatSceneryFlexiColour(4, 5, 6),
    trackedRideInstance: 900);

  private static DatTrackSegmentData Segment(
    ulong entryId = 800,
    ulong firstPiece = 500,
    ulong lastPiece = 500,
    ulong nextSegment = 1697,
    ulong prevSegment = 1697
  ) => new(
    entryId,
    direction: 2,
    firstPiece,
    lastPiece,
    nextSegment,
    prevSegment,
    prototype: false,
    track: 700);
}
