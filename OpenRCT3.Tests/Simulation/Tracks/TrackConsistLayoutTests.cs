// Track Consist Layout Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackConsistLayoutTests {
  [Test]
  public void FromCircuit_PlacesOrderedContactsBackwardAcrossTheCircuitSeam() {
    var traversal = new TrackCircuitTraversal(Circle());
    var leadArc = Convert.ToDouble(traversal.Circuit.Pieces[0].Piece.Length) * 0.25d;
    var previousPieceOffset =
      Convert.ToDouble(traversal.Circuit.Pieces[^1].Piece.Length) * 0.25d;
    var lead = traversal.AtPiece(0, leadArc);
    var offsets = new[] {
      0d,
      leadArc,
      leadArc,
      leadArc + previousPieceOffset,
    };

    var poses = TrackConsistLayoutBuilder.FromCircuit(lead, offsets);

    using (Assert.EnterMultipleScope()) {
      Assert.That(poses, Has.Count.EqualTo(offsets.Length));
      Assert.That(poses[0].Cursor, Is.EqualTo(lead));
      Assert.That(poses[1].Cursor.PieceIndex, Is.Zero);
      Assert.That(poses[1].Cursor.PieceArcLength, Is.Zero);
      Assert.That(poses[2].Cursor, Is.EqualTo(poses[1].Cursor));
      Assert.That(poses[3].Cursor.PieceIndex, Is.EqualTo(3));
      Assert.That(
        poses[3].Cursor.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(
          traversal.Circuit.Pieces[3].Piece.Length) - previousPieceOffset));
    }
    AssertExactPoses(poses, offsets);
  }

  [Test]
  public void FromGraph_UsesOneExplicitReverseMergeSelectionForAConsistentPath() {
    var fixture = BranchingGraph();
    var lead = fixture.Traversal.AtEdge(fixture.Outgoing, 2d);
    var offsets = new[] { 0d, 2d, 5d, 6d };
    var selectorCalls = 0;
    TrackGraphEdgeSelector chooseRight = (boundary, node, candidates) => {
      selectorCalls++;
      using (Assert.EnterMultipleScope()) {
        Assert.That(boundary.Edge, Is.SameAs(fixture.Outgoing));
        Assert.That(boundary.PieceArcLength, Is.Zero);
        Assert.That(node, Is.SameAs(fixture.Outgoing.From));
        Assert.That(candidates, Has.Count.EqualTo(2));
      }
      return fixture.Right;
    };

    var poses = TrackConsistLayoutBuilder.FromGraph(lead, offsets, chooseRight);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selectorCalls, Is.EqualTo(1));
      Assert.That(poses, Has.Count.EqualTo(offsets.Length));
      Assert.That(poses[0].Cursor, Is.EqualTo(lead));
      Assert.That(poses[1].Cursor.Edge, Is.SameAs(fixture.Outgoing));
      Assert.That(poses[1].Cursor.PieceArcLength, Is.Zero);
      Assert.That(poses[2].Cursor.Edge, Is.SameAs(fixture.Right));
      Assert.That(
        poses[2].Cursor.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(fixture.Right.Piece.Length) - 3d));
      Assert.That(poses[3].Cursor.Edge, Is.SameAs(fixture.Right));
      Assert.That(
        poses[3].Cursor.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(fixture.Right.Piece.Length) - 4d));
    }
    AssertExactPoses(poses, offsets);
  }

  [Test]
  public void Api_RejectsInvalidUnsortedAndUnboundedOffsetsBeforeMovement() {
    var traversal = new TrackCircuitTraversal(Circle());
    var lead = traversal.Start;

    Assert.Throws<ArgumentNullException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(lead, null!)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(lead, [-1d])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(lead, [double.NaN])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(lead, [double.PositiveInfinity])));
    Assert.Throws<ArgumentException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(lead, [0d, 2d, 1d])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(
        lead,
        [0d, 1d],
        new TrackConsistLayoutLimits(1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(
        lead,
        [],
        new TrackConsistLayoutLimits(-1))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      TrackConsistLayoutBuilder.FromCircuit(default, [])));
  }

  [Test]
  public void FromGraph_RequiresSelectorAndValidatesOffsetsBeforeCallingIt() {
    var fixture = BranchingGraph();
    var lead = fixture.Traversal.AtEdge(fixture.Outgoing, 2d);
    var selectorCalls = 0;
    TrackGraphEdgeSelector selector = (_, _, _) => {
      selectorCalls++;
      return fixture.Right;
    };

    Assert.Throws<ArgumentNullException>(new Action(() =>
      TrackConsistLayoutBuilder.FromGraph(lead, [], null!)));
    Assert.Throws<ArgumentException>(new Action(() =>
      TrackConsistLayoutBuilder.FromGraph(lead, [0d, 4d, 3d], selector)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      TrackConsistLayoutBuilder.FromGraph(default, [], selector)));
    Assert.That(selectorCalls, Is.Zero);
  }

  private static void AssertExactPoses(
    IReadOnlyList<TrackCircuitConsistPose> poses,
    IReadOnlyList<double> offsets
  ) {
    foreach (var index in Enumerable.Range(0, poses.Count)) {
      var expected = TrackContactPoseAdapter.Create(
        poses[index].Cursor.Sample().ContactPoints);
      using (Assert.EnterMultipleScope()) {
        Assert.That(poses[index].OffsetBehindLead, Is.EqualTo(offsets[index]));
        Assert.That(poses[index].Pose, Is.EqualTo(expected));
        Assert.That(
          poses[index].Pose.ArcLength,
          Is.EqualTo(poses[index].Cursor.Sample().PieceArcLength));
      }
    }
  }

  private static void AssertExactPoses(
    IReadOnlyList<TrackGraphConsistPose> poses,
    IReadOnlyList<double> offsets
  ) {
    foreach (var index in Enumerable.Range(0, poses.Count)) {
      var expected = TrackContactPoseAdapter.Create(
        poses[index].Cursor.Sample().ContactPoints);
      using (Assert.EnterMultipleScope()) {
        Assert.That(poses[index].OffsetBehindLead, Is.EqualTo(offsets[index]));
        Assert.That(poses[index].Pose, Is.EqualTo(expected));
        Assert.That(
          poses[index].Pose.ArcLength,
          Is.EqualTo(poses[index].Cursor.Sample().PieceArcLength));
      }
    }
  }

  private static TrackCircuit Circle() {
    var scale = 1.5f;
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new TrackCircuit([
      new TrackCircuitPiece(
        "north-east",
        Piece(
          Vector3.UnitX,
          Vector3.UnitY,
          Vector3.UnitY * scale,
          -Vector3.UnitX * scale,
          halfGauge)),
      new TrackCircuitPiece(
        "north-west",
        Piece(
          Vector3.UnitY,
          -Vector3.UnitX,
          -Vector3.UnitX * scale,
          -Vector3.UnitY * scale,
          halfGauge)),
      new TrackCircuitPiece(
        "south-west",
        Piece(
          -Vector3.UnitX,
          -Vector3.UnitY,
          -Vector3.UnitY * scale,
          Vector3.UnitX * scale,
          halfGauge)),
      new TrackCircuitPiece(
        "south-east",
        Piece(
          -Vector3.UnitY,
          Vector3.UnitX,
          Vector3.UnitX * scale,
          Vector3.UnitY * scale,
          halfGauge)),
    ]);
  }

  private static GraphFixture BranchingGraph() {
    var root = new TrackNode("root");
    var split = new TrackNode("split");
    var merge = new TrackNode("merge");
    var end = new TrackNode("end");
    var incoming = new TrackEdge("incoming", root, split, StraightPiece(0f, 10f));
    var left = new TrackEdge("left", split, merge, StraightPiece(10f, 20f));
    var right = new TrackEdge("right", split, merge, StraightPiece(10f, 20f));
    var outgoing = new TrackEdge("outgoing", merge, end, StraightPiece(20f, 30f));
    var graph = new TrackGraph(
      [root, split, merge, end],
      [incoming, left, right, outgoing]);
    return new(new TrackGraphTraversal(graph), right, outgoing);
  }

  private static TrackPiece StraightPiece(float startX, float endX) {
    var start = new Vector3(startX, 0f, 0f);
    var end = new Vector3(endX, 0f, 0f);
    return Piece(start, end, end - start, end - start, Vector3.UnitY * 0.5f);
  }

  private static TrackPiece Piece(
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent,
    Vector3 halfGauge
  ) => new(TrackPieceGeometry.FromHandAuthored([
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

  private sealed record GraphFixture(
    TrackGraphTraversal Traversal,
    TrackEdge Right,
    TrackEdge Outgoing
  );
}
