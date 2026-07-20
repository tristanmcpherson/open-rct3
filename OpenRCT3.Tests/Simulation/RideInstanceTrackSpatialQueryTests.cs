// Ride Instance Track Spatial Query Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceTrackSpatialQueryTests {
  [Test]
  public void Query_MapsExactRuntimeEntriesWithoutChangingSpatialOrderOrDistance() {
    var skippedTrack = Track(700, 900);
    var distantTrack = Track(701, 901, isCircuit: false);
    var containingTrack = Track(702, 902, isCircuit: false);
    var instances = new[] {
      Instance(902, 702),
      Instance(900, 700),
      Instance(901, 701),
    };
    var identity = RideInstanceTrackGraph.Build(
      instances,
      [distantTrack, skippedTrack, containingTrack]);
    var geometry = new RideTrackGeometryResolution(
      [
        Link(distantTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
        Link(skippedTrack, RideTrackGeometryStatus.UnresolvedResources),
        Link(containingTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(5f, 7f)),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var runtime = RideInstanceTrackRuntimeRegistry.Build(identity, geometry);
    var spatial = RideTrackGeometrySpatialIndex.Build(geometry);
    var query = RideInstanceTrackSpatialQuery.Build(runtime, spatial);

    var hits = query.Query(new Vector3(6f, 0f, 0f), maximumResults: 3);

    using (Assert.EnterMultipleScope()) {
      Assert.That(query.TrackCount, Is.EqualTo(3));
      Assert.That(query.ResolvedTrackCount, Is.EqualTo(2));
      Assert.That(query.ResolvedEntries.Select(entry => entry.TrackEntryId),
        Is.EqualTo(new ulong[] { 701, 702 }));
      Assert.That(hits.Select(hit => hit.Runtime.TrackEntryId),
        Is.EqualTo(new ulong[] { 702, 701 }));
      Assert.That(hits[0].Runtime, Is.SameAs(runtime.Entries[0]));
      Assert.That(hits[1].Runtime, Is.SameAs(runtime.Entries[2]));
      Assert.That(hits.Select(hit => hit.SpatialEntry.TrackSourceEntryId),
        Is.EqualTo(new ulong[] { 702, 701 }));
      Assert.That(hits.Select(hit => hit.SquaredDistanceToBounds),
        Is.EqualTo(new[] { 0d, 16d }));
      Assert.That(hits.Select(hit => hit.ContainsPoint),
        Is.EqualTo(new[] { true, false }));
      Assert.That(hits[0].Bounds, Is.EqualTo(spatial.Entries[1].Bounds));
      Assert.That(hits.Any(hit => hit.Runtime.TrackEntryId == 700), Is.False);
    }
  }

  [Test]
  public void Build_RejectsCountIdentityAndStatusMismatches() {
    var firstTrack = Track(700, 900, isCircuit: false);
    var secondTrack = Track(701, 901);
    var identity = RideInstanceTrackGraph.Build(
      [Instance(900, 700), Instance(901, 701)],
      [firstTrack, secondTrack]);
    var runtimeGeometry = new RideTrackGeometryResolution(
      [
        Link(firstTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
        Link(secondTrack, RideTrackGeometryStatus.UnresolvedResources),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var runtime = RideInstanceTrackRuntimeRegistry.Build(identity, runtimeGeometry);
    var countMismatch = RideTrackGeometrySpatialIndex.Build(new(
      [Link(firstTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0));
    var statusMismatch = RideTrackGeometrySpatialIndex.Build(new(
      [
        Link(firstTrack, RideTrackGeometryStatus.UnresolvedResources),
        Link(secondTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(5f, 7f)),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0));
    var indexMismatch = RideTrackGeometrySpatialIndex.Build(new(
      [
        Link(secondTrack, RideTrackGeometryStatus.UnresolvedResources),
        Link(firstTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0));

    var countError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackSpatialQuery.Build(runtime, countMismatch)));
    var statusError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackSpatialQuery.Build(runtime, statusMismatch)));
    var indexError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackSpatialQuery.Build(runtime, indexMismatch)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(countError!.Message, Does.Contain("total track count changed"));
      Assert.That(statusError!.Message, Does.Contain("maps to a skipped runtime entry"));
      Assert.That(indexError!.Message, Does.Contain("DAT index changed"));
    }
  }

  [Test]
  public void BuildAndQuery_EnforceEntryRequestAndFinitePointBounds() {
    var track = Track(700, 900, isCircuit: false);
    var identity = RideInstanceTrackGraph.Build([Instance(900, 700)], [track]);
    var geometry = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var runtime = RideInstanceTrackRuntimeRegistry.Build(identity, geometry);
    var spatial = RideTrackGeometrySpatialIndex.Build(geometry);
    var query = RideInstanceTrackSpatialQuery.Build(
      runtime,
      spatial,
      new(MaximumEntryCount: 1, MaximumQueryResults: 1));
    var secondTrack = Track(701, 901, isCircuit: false);
    var twoIdentity = RideInstanceTrackGraph.Build(
      [Instance(900, 700), Instance(901, 701)],
      [track, secondTrack]);
    var twoGeometry = new RideTrackGeometryResolution(
      [
        Link(track, RideTrackGeometryStatus.OpenTrack, graph: Graph(0f, 2f)),
        Link(secondTrack, RideTrackGeometryStatus.OpenTrack, graph: Graph(5f, 7f)),
      ],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var twoRuntime = RideInstanceTrackRuntimeRegistry.Build(twoIdentity, twoGeometry);
    var twoSpatial = RideTrackGeometrySpatialIndex.Build(twoGeometry);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideInstanceTrackSpatialQuery.Build(
        twoRuntime,
        twoSpatial,
        new(MaximumEntryCount: 1, MaximumQueryResults: 1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      query.Query(Vector3.Zero, 2)));
    Assert.Throws<ArgumentException>(new Action(() =>
      query.Query(new Vector3(float.NaN, 0f, 0f), 1)));
  }

  private static RideTrackGeometryLink Link(
    RideTrack track,
    RideTrackGeometryStatus status,
    TrackGraph? graph = null
  ) => new(track, status, graph, Circuit: null);

  private static DatTrackedRideInstanceData Instance(ulong entryId, ulong trackId) => new(
    entryId,
    $"Ride {entryId}",
    trackId,
    "Tracks\\Synthetic",
    "Synthetic:trr",
    nTrains: 1,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains: [entryId + 100]);

  private static RideTrack Track(
    ulong entryId,
    ulong instanceReference,
    bool? isCircuit = null
  ) => new(
    entryId,
    direction: 0,
    firstSegmentSourceEntryId: entryId + 100,
    lastSegmentSourceEntryId: entryId + 100,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: isCircuit != null,
    trackPieceSourceEntryIds: [entryId + 200],
    segmentSourceEntryIds: [entryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: instanceReference,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static TrackGraph Graph(float startX, float endX) {
    var start = new TrackNode($"start-{startX}");
    var end = new TrackNode($"end-{endX}");
    return new(
      [start, end],
      [new TrackEdge("edge", start, end, StraightPiece(startX, endX))]);
  }

  private static TrackPiece StraightPiece(float startX, float endX) =>
    new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, new Vector3(startX, 0f, 0f)),
      Pair(1f, new Vector3(endX, 0f, 0f)),
    ]));

  private static RailControlPair Pair(float parameter, Vector3 center) => new(
    parameter,
    center - Vector3.UnitY,
    Vector3.UnitX,
    center + Vector3.UnitY,
    Vector3.UnitX,
    0f);
}
