// Ride Track Geometry Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackGeometryResolverTests {
  [Test]
  public void Resolve_BuildsOpenTrackAndCircuitInDatOrder() {
    var openPlacements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };
    var circuitPlacements = new[] {
      Placement(600, previous: 601, next: 601),
      Placement(601, previous: 600, next: 600),
    };
    var placements = openPlacements.Concat(circuitPlacements).ToArray();
    var pieces = new Dictionary<ulong, TrackPiece?> {
      [500] = StraightPiece(0f),
      [501] = StraightPiece(1f),
      [600] = HalfCircuit(first: true),
      [601] = HalfCircuit(first: false),
    };

    var result = RideTrackGeometryResolver.Resolve(
      [Track(700, [500, 501], isCircuit: false), Track(701, [600, 601], isCircuit: true)],
      placements,
      pieces);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Select(link => link.Track.SourceEntryId),
        Is.EqualTo(new ulong[] { 700, 701 }));
      Assert.That(result.Tracks.Select(link => link.Status), Is.EqualTo(new[] {
        RideTrackGeometryStatus.OpenTrack,
        RideTrackGeometryStatus.Circuit,
      }));
      Assert.That(result.Tracks[0].Graph, Is.Not.Null);
      Assert.That(result.Tracks[0].Circuit, Is.Null);
      Assert.That(result.Tracks[1].Graph, Is.Null);
      Assert.That(result.Tracks[1].Circuit, Is.Not.Null);
      Assert.That(result.UnresolvedResourceTrackCount, Is.Zero);
      Assert.That(result.UnsupportedGeometryTrackCount, Is.Zero);
      Assert.That(result.UnsupportedTopologyTrackCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_SeparatesUnresolvedResourcesFromUnsupportedTopology() {
    var placements = new[] {
      Placement(500, previous: 1697, next: 1697),
      Placement(600, previous: 1697, next: 1697),
    };
    var pieces = new Dictionary<ulong, TrackPiece?> {
      [500] = null,
      [600] = StraightPiece(0f),
    };

    var result = RideTrackGeometryResolver.Resolve(
      [
        Track(700, [500], isCircuit: false),
        Track(701, [600], isCircuit: null, hasSerializedOrder: false),
      ],
      placements,
      pieces);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks[0].Status,
        Is.EqualTo(RideTrackGeometryStatus.UnresolvedResources));
      Assert.That(result.Tracks[1].Status,
        Is.EqualTo(RideTrackGeometryStatus.UnsupportedTopology));
      Assert.That(result.UnresolvedResourceTrackCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedGeometryTrackCount, Is.Zero);
      Assert.That(result.UnsupportedTopologyTrackCount, Is.EqualTo(1));
      Assert.That(result.Tracks.All(link => !link.IsResolved), Is.True);
    }
  }

  [Test]
  public void Resolve_ClassifiesUnprovenPlacementGeometrySeparately() {
    var placement = Placement(500, previous: 1697, next: 1697);
    var pieces = new Dictionary<ulong, TrackPiece?> { [500] = null };

    var result = RideTrackGeometryResolver.Resolve(
      [Track(700, [500], isCircuit: false)],
      [placement],
      pieces,
      new HashSet<ulong> { 500 });

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Single().Status,
        Is.EqualTo(RideTrackGeometryStatus.UnsupportedGeometry));
      Assert.That(result.Tracks.Single().IsResolved, Is.False);
      Assert.That(result.UnresolvedResourceTrackCount, Is.Zero);
      Assert.That(result.UnsupportedGeometryTrackCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedTopologyTrackCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_ClassifiesResolvedTksWithUnresolvedCarSplineAsUnresolvedResources() {
    var placement = Placement(500, previous: 1697, next: 1697);
    var resources = ResourceResolution(
      placement,
      resolveScenery: true,
      resolveCarLeft: false,
      resolveCarRight: true);

    var result = RideTrackGeometryResolver.Resolve(
      new Terrain(1, 1, 0),
      [Track(700, [500], isCircuit: false)],
      resources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Single().Status,
        Is.EqualTo(RideTrackGeometryStatus.UnresolvedResources));
      Assert.That(result.Tracks.Single().IsResolved, Is.False);
      Assert.That(result.UnresolvedResourceTrackCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedGeometryTrackCount, Is.Zero);
      Assert.That(result.UnsupportedTopologyTrackCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_ClassifiesResolvedCarSplinesWithUnresolvedSidAsUnresolvedResources() {
    var placement = Placement(500, previous: 1697, next: 1697);
    var resources = ResourceResolution(
      placement,
      resolveScenery: false,
      resolveCarLeft: true,
      resolveCarRight: true);

    var result = RideTrackGeometryResolver.Resolve(
      new Terrain(1, 1, 0),
      [Track(700, [500], isCircuit: false)],
      resources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Single().Status,
        Is.EqualTo(RideTrackGeometryStatus.UnresolvedResources));
      Assert.That(result.Tracks.Single().Detail, Does.Contain("SID"));
      Assert.That(result.UnresolvedResourceTrackCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedGeometryTrackCount, Is.Zero);
      Assert.That(result.UnsupportedTopologyTrackCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_RejectsUnsupportedGeometryThatIsUnknownOrResolved() {
    var placement = Placement(500, previous: 1697, next: 1697);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometryResolver.Resolve(
        [Track(700, [500], isCircuit: false)],
        [placement],
        new Dictionary<ulong, TrackPiece?> { [500] = StraightPiece(0f) },
        new HashSet<ulong> { 500 })));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometryResolver.Resolve(
        [Track(700, [500], isCircuit: false)],
        [placement],
        new Dictionary<ulong, TrackPiece?> { [500] = StraightPiece(0f) },
        new HashSet<ulong> { 501 })));
  }

  [Test]
  public void Resolve_ClassifiesDiscontinuousResolvedPiecesAsUnsupportedGeometry() {
    var placements = new[] {
      Placement(500, previous: 1697, next: 501),
      Placement(501, previous: 500, next: 1697),
    };

    var result = RideTrackGeometryResolver.Resolve(
      [Track(700, [500, 501], isCircuit: false)],
      placements,
      new Dictionary<ulong, TrackPiece?> {
        [500] = StraightPiece(0f),
        [501] = StraightPiece(3f),
      });

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Single().Status,
        Is.EqualTo(RideTrackGeometryStatus.UnsupportedGeometry));
      Assert.That(result.UnsupportedGeometryTrackCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedTopologyTrackCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_ClassifiesInconsistentSerializedLinksAsUnsupportedTopology() {
    var placements = new[] {
      Placement(500, previous: 1697, next: 999),
      Placement(501, previous: 500, next: 1697),
    };

    var result = RideTrackGeometryResolver.Resolve(
      [Track(700, [500, 501], isCircuit: false)],
      placements,
      new Dictionary<ulong, TrackPiece?> {
        [500] = StraightPiece(0f),
        [501] = StraightPiece(1f),
      });

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Tracks.Single().Status,
        Is.EqualTo(RideTrackGeometryStatus.UnsupportedTopology));
      Assert.That(result.UnsupportedGeometryTrackCount, Is.Zero);
      Assert.That(result.UnsupportedTopologyTrackCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_RejectsMissingAndUnclaimedPlacements() {
    var track = Track(700, [500], isCircuit: false);
    var placement = Placement(500, previous: 1697, next: 1697);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometryResolver.Resolve(
        [track],
        [placement],
        new Dictionary<ulong, TrackPiece?>())));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometryResolver.Resolve(
        [track],
        [placement, Placement(501, previous: 1697, next: 1697)],
        new Dictionary<ulong, TrackPiece?> {
          [500] = StraightPiece(0f),
          [501] = StraightPiece(1f),
        })));
  }

  [Test]
  public void Resolve_RejectsPieceClaimedByMultipleTracks() {
    var placement = Placement(500, previous: 1697, next: 1697);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackGeometryResolver.Resolve(
        [Track(700, [500], false), Track(701, [500], false)],
        [placement],
        new Dictionary<ulong, TrackPiece?> { [500] = StraightPiece(0f) })));
  }

  private static RideTrack Track(
    ulong sourceEntryId,
    ulong[] pieceIds,
    bool? isCircuit,
    bool hasSerializedOrder = true
  ) => new(
    sourceEntryId,
    direction: 0,
    firstSegmentSourceEntryId: sourceEntryId + 100,
    lastSegmentSourceEntryId: sourceEntryId + 100,
    isCircuit,
    prototype: false,
    hasSerializedOrder,
    pieceIds,
    segmentSourceEntryIds: [sourceEntryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: sourceEntryId + 200,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static RideTrackPlacement Placement(
    ulong sourceEntryId,
    ulong previous,
    ulong next
  ) => new(
    sourceEntryId,
    sceneryPlacementSourceEntryId: sourceEntryId + 1000,
    sidDatabaseEntryReference: sourceEntryId + 2000,
    symbolName: $"Piece{sourceEntryId}:tks",
    objectKey: $"Piece{sourceEntryId}",
    overlayPath: "Tracks\\Test",
    tileX: 0,
    tileY: 0,
    rotation: Edge.West,
    serializedDirection: 0,
    serializedHeight: 0,
    corner: 0,
    ownerReference: sourceEntryId < 600 ? 800UL : 801UL,
    segmentReference: sourceEntryId < 600 ? 800UL : 801UL,
    previousPieceReference: previous,
    nextPieceReference: next,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0);

  private static TrackSection Section() => new(
    "section",
    TrackSectionVersion.Vanilla,
    "section",
    "scenery:sid",
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    SpecialCurves: 0,
    Direction: 0,
    new TrackSectionSplinePair("left:spl", "right:spl"),
    new TrackSectionSplinePair("join-left:spl", "join-right:spl"),
    ExtraSplines: null,
    WaterSplines: null,
    Speeds: [],
    new TrackSectionAnimations(-1, -1, -1, -1, -1, -1, -1, -1, -1, null, null),
    new TrackSectionOptions(0, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    Expansion: null,
    Wild: null);

  private static RideTrackResourceResolution ResourceResolution(
    RideTrackPlacement placement,
    bool resolveScenery,
    bool resolveCarLeft,
    bool resolveCarRight
  ) {
    var section = Section();
    var file = new OvlFile(section.Name, FileType.TrackSection, "fixture.unique.ovl");
    var sectionSource = new TrackSectionResourceSource(file, section);
    var scenery = Scenery(placement.ObjectKey);
    var scenerySource = new SceneryItemResourceSource(
      new OvlFile(scenery.Name, FileType.SceneryItem, "fixture.unique.ovl"),
      scenery);
    var left = Spline("left", -0.5f);
    var right = Spline("right", 0.5f);
    var leftSource = new SplineResourceSource(
      new OvlFile(left.Name, FileType.Spline, "fixture.common.ovl"),
      left);
    var rightSource = new SplineResourceSource(
      new OvlFile(right.Name, FileType.Spline, "fixture.common.ovl"),
      right);
    var resource = new TrackSectionResourceLink(
      sectionSource,
      new TrackSectionSceneryLink(
        section.SceneryItem,
        resolveScenery ? scenerySource : null),
      [
        new(
          TrackSectionSplineRole.CarLeft,
          null,
          section.CarSplines.Left,
          resolveCarLeft ? leftSource : null),
        new(
          TrackSectionSplineRole.CarRight,
          null,
          section.CarSplines.Right,
          resolveCarRight ? rightSource : null),
      ]);
    return new RideTrackResourceResolution(
      [new(
        new RideTrackSectionResourceLink(
          placement,
          new RideTrackSectionResourceSource(placement.OverlayPath, file, section)),
        resource,
        Array.Empty<RideTrackResourceDeclaration>())],
      UnresolvedPlacementCount: 0);
  }

  private static SceneryItem Scenery(string name) => new(
    name,
    SidFlags.GroundChange,
    SidPosition.TileFull,
    StructureVersion: 0,
    SquaresX: 1,
    SquaresZ: 1,
    PositionX: 0f,
    PositionY: 0f,
    PositionZ: 0f,
    SizeX: 4f,
    SizeY: 4f,
    SizeZ: 4f,
    SidType.SceneryMisc,
    VisualRefs: []);

  private static Spline Spline(string name, float lateral) => new(
    name,
    Cyclic: false,
    TotalLength: 1f,
    InverseTotalLength: 1f,
    MaximumY: 0f,
    Nodes: [
      new(new Vector3(0f, 0f, lateral), Vector3.Zero, new Vector3(1f / 3f, 0f, 0f)),
      new(new Vector3(1f, 0f, lateral), new Vector3(-1f / 3f, 0f, 0f), Vector3.Zero),
    ],
    Segments: [new(1f, new byte[14])]);

  private static TrackPiece StraightPiece(float offset) {
    var geometry = TrackPieceGeometry.FromHandAuthored([
      Pair(0f, new(offset, 0f, 0f), Vector3.UnitX),
      Pair(1f, new(offset + 1f, 0f, 0f), Vector3.UnitX),
    ]);
    return new TrackPiece(geometry);
  }

  private static TrackPiece HalfCircuit(bool first) {
    var start = first ? Vector3.UnitX : -Vector3.UnitX;
    var end = -start;
    var startTangent = first ? Vector3.UnitY * 3f : -Vector3.UnitY * 3f;
    var endTangent = -startTangent;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, start, startTangent),
      Pair(1f, end, endTangent),
    ]));
  }

  private static RailControlPair Pair(float parameter, Vector3 center, Vector3 tangent) {
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new(
      parameter,
      center - halfGauge,
      tangent,
      center + halfGauge,
      tangent,
      0f);
  }
}
