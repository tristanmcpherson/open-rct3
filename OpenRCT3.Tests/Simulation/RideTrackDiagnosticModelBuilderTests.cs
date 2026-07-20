// Ride Track Diagnostic Model Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackDiagnosticModelBuilderTests {
  [Test]
  public void BuildPiece_ReturnsExplicitDiagnosticFlatModelForBothContactPaths() {
    var piece = StraightPiece();
    var style = new RideTrackDiagnosticStyle(
      0.1f,
      new Vector4(1f, 0f, 0f, 1f),
      new Vector4(0f, 0f, 1f, 1f));

    var result = RideTrackDiagnosticModelBuilder.Build(piece, style);
    using var model = result.Model;

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status,
        Is.EqualTo(RideTrackRenderGeometryStatus.DiagnosticContactRailsOnly));
      Assert.That(result.Detail, Does.Contain("not decoded"));
      Assert.That(result.PieceCount, Is.EqualTo(1));
      Assert.That(result.BakedCrossSectionCount, Is.EqualTo(piece.BakedSampleCount));
      Assert.That(result.Mesh.Name, Is.EqualTo(RideTrackDiagnosticModelBuilder.DefaultMeshName));
      Assert.That(model.Material, Is.TypeOf<Flat>());
      Assert.That(model.Material!.CullBackFaces, Is.True);
      Assert.That(result.Mesh.Vertices.Count, Is.EqualTo(piece.BakedSampleCount * 8));
      Assert.That(result.Mesh.Indices.Count, Is.EqualTo((piece.BakedSampleCount - 1) * 48));
      Assert.That(result.Mesh.Vertices.Take(piece.BakedSampleCount * 4).Select(v => v.Color),
        Is.All.EqualTo(style.LeftContactColor));
      Assert.That(result.Mesh.Vertices.Skip(piece.BakedSampleCount * 4).Select(v => v.Color),
        Is.All.EqualTo(style.RightContactColor));
      Assert.That(result.Mesh.BoundingBox.Min.Y, Is.EqualTo(-1.1f).Within(0.000001f));
      Assert.That(result.Mesh.BoundingBox.Max.Y, Is.EqualTo(1.1f).Within(0.000001f));
      Assert.That(result.Mesh.BoundingBox.Min.Z, Is.EqualTo(-0.1f).Within(0.000001f));
      Assert.That(result.Mesh.BoundingBox.Max.Z, Is.EqualTo(0.1f).Within(0.000001f));
    }
  }

  [Test]
  public void BuildPiece_UsesEveryAdaptiveBakeCrossSectionWithoutResampling() {
    var piece = QuarterCirclePiece(
      Vector3.UnitX,
      Vector3.UnitY,
      Vector3.UnitY * 1.5f,
      -Vector3.UnitX * 1.5f);
    Assert.That(piece.BakedSampleCount, Is.GreaterThan(2));

    var result = RideTrackDiagnosticModelBuilder.Build(piece);
    using var model = result.Model;

    foreach (var sampleIndex in Enumerable.Range(0, piece.BakedSampleCount)) {
      var vertexOffset = sampleIndex * 4;
      var ringCenter = result.Mesh.Vertices
        .Skip(vertexOffset)
        .Take(4)
        .Aggregate(Vector3.Zero, (total, vertex) => total + vertex.Position) / 4f;
      var expected = piece.SampleRail(
        RailSide.Left,
        piece.BakedArcLengths[sampleIndex]).Position;
      Assert.That(Vector3.Distance(ringCenter, expected), Is.LessThan(0.000001f));
    }
  }

  [Test]
  public void BuildPiece_WindsEveryTubeTriangleTowardItsVertexNormals() {
    var result = RideTrackDiagnosticModelBuilder.Build(StraightPiece());
    using var model = result.Model;

    foreach (var indexOffset in Enumerable.Range(0, result.Mesh.Indices.Count / 3)) {
      var index = indexOffset * 3;
      var a = result.Mesh.Vertices[Convert.ToInt32(result.Mesh.Indices[index])];
      var b = result.Mesh.Vertices[Convert.ToInt32(result.Mesh.Indices[index + 1])];
      var c = result.Mesh.Vertices[Convert.ToInt32(result.Mesh.Indices[index + 2])];
      var faceNormal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
      var vertexNormal = a.Normal + b.Normal + c.Normal;
      using (Assert.EnterMultipleScope()) {
        Assert.That(faceNormal.LengthSquared(), Is.GreaterThan(0f));
        Assert.That(Vector3.Dot(faceNormal, vertexNormal), Is.GreaterThan(0f));
      }
    }
  }

  [Test]
  public void BuildGraphAndCircuit_ComposeEveryRetainedPiece() {
    var first = StraightPiece(2f);
    var second = StraightPiece(2f, Matrix4x4.CreateTranslation(2f, 0f, 0f));
    var start = new TrackNode("start");
    var join = new TrackNode("join");
    var end = new TrackNode("end");
    var graph = new TrackGraph(
      [start, join, end],
      [
        new TrackEdge("first", start, join, first),
        new TrackEdge("second", join, end, second),
      ]);
    var circuit = Circle();

    var graphResult = RideTrackDiagnosticModelBuilder.Build(graph, name: "Diagnostic graph");
    using var graphModel = graphResult.Model;
    var circuitResult = RideTrackDiagnosticModelBuilder.Build(circuit, name: "Diagnostic circuit");
    using var circuitModel = circuitResult.Model;

    using (Assert.EnterMultipleScope()) {
      Assert.That(graphResult.PieceCount, Is.EqualTo(graph.Edges.Count));
      Assert.That(graphResult.BakedCrossSectionCount,
        Is.EqualTo(graph.Edges.Sum(edge => edge.Piece.BakedSampleCount)));
      Assert.That(graphResult.Mesh.Name, Is.EqualTo("Diagnostic graph"));
      Assert.That(circuitResult.PieceCount, Is.EqualTo(circuit.Pieces.Count));
      Assert.That(circuitResult.BakedCrossSectionCount,
        Is.EqualTo(circuit.Pieces.Sum(piece => piece.Piece.BakedSampleCount)));
      Assert.That(circuitResult.Mesh.Name, Is.EqualTo("Diagnostic circuit"));
    }
  }

  [Test]
  public void Build_RejectsInvalidAppearanceInputAndAllocationLimits() {
    var piece = StraightPiece();
    var defaultStyle = RideTrackDiagnosticStyle.Default;
    var tinyLimits = RideTrackDiagnosticBuildLimits.Default with { MaximumVertices = 15 };
    IEnumerable<TrackPiece> empty = [];
    IEnumerable<TrackPiece> withNull = [piece, null!];

    Assert.Throws<ArgumentException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(empty)));
    Assert.Throws<ArgumentException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(withNull)));
    Assert.Throws<ArgumentException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(piece, name: " ")));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(
        piece,
        defaultStyle with { ContactMarkerHalfWidth = 0f })));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(
        piece,
        defaultStyle with { LeftContactColor = new Vector4(float.NaN) })));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrackDiagnosticModelBuilder.Build(piece, limits: tinyLimits)));
  }

  private static TrackPiece StraightPiece(
    float length = 4f,
    Matrix4x4? placement = null
  ) => new(
    TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        new Vector3(0f, -1f, 0f),
        Vector3.UnitX * length,
        new Vector3(0f, 1f, 0f),
        Vector3.UnitX * length,
        0f),
      new RailControlPair(
        1f,
        new Vector3(length, -1f, 0f),
        Vector3.UnitX * length,
        new Vector3(length, 1f, 0f),
        Vector3.UnitX * length,
        0f),
    ]),
    placement ?? Matrix4x4.Identity);

  private static TrackCircuit Circle() {
    var scale = 1.5f;
    return new TrackCircuit([
      new TrackCircuitPiece(
        "north-east",
        QuarterCirclePiece(
          Vector3.UnitX,
          Vector3.UnitY,
          Vector3.UnitY * scale,
          -Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "north-west",
        QuarterCirclePiece(
          Vector3.UnitY,
          -Vector3.UnitX,
          -Vector3.UnitX * scale,
          -Vector3.UnitY * scale)),
      new TrackCircuitPiece(
        "south-west",
        QuarterCirclePiece(
          -Vector3.UnitX,
          -Vector3.UnitY,
          -Vector3.UnitY * scale,
          Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "south-east",
        QuarterCirclePiece(
          -Vector3.UnitY,
          Vector3.UnitX,
          Vector3.UnitX * scale,
          Vector3.UnitY * scale)),
    ]);
  }

  private static TrackPiece QuarterCirclePiece(
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent
  ) {
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        startTangent,
        start + halfGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        endTangent,
        end + halfGauge,
        endTangent,
        0f),
    ]));
  }
}
