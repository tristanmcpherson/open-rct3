// Ride Track Diagnostic Scene Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Collections;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackDiagnosticSceneBuilderTests {
  [Test]
  public void Build_ReturnsResolvedModelsInDatOrderWithExplicitOutcomeCounts() {
    var resolution = new RideTrackGeometryResolution(
      [
        Link(Track(700), RideTrackGeometryStatus.UnresolvedResources),
        Link(Track(701, isCircuit: false), RideTrackGeometryStatus.OpenTrack, graph: Graph(0f)),
        Link(Track(702), RideTrackGeometryStatus.UnsupportedGeometry),
        Link(Track(703), RideTrackGeometryStatus.UnsupportedTopology),
        Link(Track(704, isCircuit: true), RideTrackGeometryStatus.Circuit, circuit: Circuit()),
      ],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 1,
      UnsupportedTopologyTrackCount: 1);

    var result = RideTrackDiagnosticSceneBuilder.Build(resolution);
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Status,
          Is.EqualTo(RideTrackRenderGeometryStatus.DiagnosticContactRailsOnly));
        Assert.That(result.Detail, Is.EqualTo(RideTrackDiagnosticModelBuilder.DiagnosticDetail));
        Assert.That(result.TrackCount, Is.EqualTo(5));
        Assert.That(result.ResolvedTrackCount, Is.EqualTo(2));
        Assert.That(result.UnresolvedResourceTrackCount, Is.EqualTo(1));
        Assert.That(result.UnsupportedGeometryTrackCount, Is.EqualTo(1));
        Assert.That(result.UnsupportedTopologyTrackCount, Is.EqualTo(1));
        Assert.That(result.Models.Select(model => model.Mesh.Name), Is.EqualTo(new[] {
          "Ride Track 701 (diagnostic contact rails)",
          "Ride Track 704 (diagnostic contact rails)",
        }));
        Assert.That(result.Models.Select(model => model.Material), Is.All.TypeOf<Flat>());
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [Test]
  public void Build_RejectsInconsistentCountsAndStatusGeometry() {
    var track = Track(700);
    var unresolvedWithGraph = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.UnresolvedResources, graph: Graph(0f))],
      UnresolvedResourceTrackCount: 1,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var countMismatch = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.UnresolvedResources)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var unsupportedGeometryWithGraph = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.UnsupportedGeometry, graph: Graph(0f))],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 1,
      UnsupportedTopologyTrackCount: 0);
    var unsupportedGeometryCountMismatch = new RideTrackGeometryResolution(
      [Link(track, RideTrackGeometryStatus.UnsupportedGeometry)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);
    var openWithoutGraph = new RideTrackGeometryResolution(
      [Link(Track(701, false), RideTrackGeometryStatus.OpenTrack)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(unresolvedWithGraph)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(countMismatch)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(unsupportedGeometryWithGraph)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(unsupportedGeometryCountMismatch)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(openWithoutGraph)));
  }

  [Test]
  public void Build_RejectsTrackListsOverTheHardBound() {
    var resolution = new RideTrackGeometryResolution(
      new OversizedTrackList(),
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 0,
      UnsupportedTopologyTrackCount: 0);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrackDiagnosticSceneBuilder.Build(resolution)));
  }

  private static RideTrackGeometryLink Link(
    RideTrack track,
    RideTrackGeometryStatus status,
    TrackGraph? graph = null,
    TrackCircuit? circuit = null
  ) => new(track, status, graph, circuit);

  private static TrackGraph Graph(float offset) {
    var start = new TrackNode($"start-{offset}");
    var end = new TrackNode($"end-{offset}");
    return new(
      [start, end],
      [new TrackEdge($"edge-{offset}", start, end, StraightPiece(offset))]);
  }

  private static TrackCircuit Circuit() =>
    new([
      new TrackCircuitPiece("first", HalfCircuit(first: true)),
      new TrackCircuitPiece("second", HalfCircuit(first: false)),
    ]);

  private static RideTrack Track(ulong sourceEntryId, bool? isCircuit = null) => new(
    sourceEntryId,
    direction: 0,
    firstSegmentSourceEntryId: sourceEntryId + 100,
    lastSegmentSourceEntryId: sourceEntryId + 100,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: isCircuit != null,
    trackPieceSourceEntryIds: [sourceEntryId + 200],
    segmentSourceEntryIds: [sourceEntryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: sourceEntryId + 300,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static TrackPiece StraightPiece(float offset) =>
    new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, new Vector3(offset, 0f, 0f), Vector3.UnitX),
      Pair(1f, new Vector3(offset + 1f, 0f, 0f), Vector3.UnitX),
    ]));

  private static TrackPiece HalfCircuit(bool first) {
    var start = first ? Vector3.UnitX : -Vector3.UnitX;
    var end = -start;
    var startTangent = first ? Vector3.UnitY * 3f : -Vector3.UnitY * 3f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, start, startTangent),
      Pair(1f, end, -startTangent),
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

  private sealed class OversizedTrackList : IReadOnlyList<RideTrackGeometryLink> {
    public int Count => 100_001;
    public RideTrackGeometryLink this[int index] => throw new NotSupportedException();
    public IEnumerator<RideTrackGeometryLink> GetEnumerator() =>
      throw new NotSupportedException();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
