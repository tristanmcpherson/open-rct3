// Track Kinematic Vehicle State Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackKinematicVehicleStateTests {
  [Test]
  public void CircuitAdvance_IsDeterministicAndRetainsExactCursorAndSampledPose() {
    var traversal = new TrackCircuitTraversal(Circle());
    var cursor = traversal.AtPiece(0, 0.125d);
    const double speed = 2.5d;
    var elapsed = TimeSpan.FromSeconds(0.4d);
    var state = new TrackCircuitKinematicVehicleState(cursor, speed);
    var expectedCursor = cursor.Advance(speed * elapsed.TotalSeconds);

    var advanced = state.Advance(elapsed);
    var repeated = state.Advance(elapsed);

    using (Assert.EnterMultipleScope()) {
      Assert.That(state.IsInitialized, Is.True);
      Assert.That(state.Cursor, Is.EqualTo(cursor));
      Assert.That(state.Speed, Is.EqualTo(speed));
      Assert.That(advanced.Cursor, Is.EqualTo(expectedCursor));
      Assert.That(advanced.Speed, Is.EqualTo(speed));
      Assert.That(advanced.Pose, Is.EqualTo(Pose(expectedCursor)));
      Assert.That(repeated, Is.EqualTo(advanced));
    }
  }

  [Test]
  public void CircuitAdvance_UsesSignedSpeedAndRetainsExactZeroSteps() {
    var traversal = new TrackCircuitTraversal(Circle());
    var cursor = traversal.AtPiece(1, 0.75d);
    var reverse = new TrackCircuitKinematicVehicleState(cursor, -1.5d);
    var stationary = new TrackCircuitKinematicVehicleState(cursor, 0d);

    var advanced = reverse.Advance(TimeSpan.FromSeconds(0.25d));

    using (Assert.EnterMultipleScope()) {
      Assert.That(advanced.Cursor, Is.EqualTo(cursor.Advance(-0.375d)));
      Assert.That(advanced.Speed, Is.EqualTo(-1.5d));
      Assert.That(advanced.Pose, Is.EqualTo(Pose(advanced.Cursor)));
      Assert.That(reverse.Advance(TimeSpan.Zero), Is.EqualTo(reverse));
      Assert.That(stationary.Advance(TimeSpan.MaxValue), Is.EqualTo(stationary));
    }
  }

  [Test]
  public void GraphAdvance_ForwardsExactBranchSelectorAndSamplesSelectedPose() {
    var fixture = BranchingGraph();
    var cursor = fixture.Traversal.AtEdge(fixture.Incoming, 9d);
    var state = new TrackGraphKinematicVehicleState(cursor, 2d);
    var selectorCalls = 0;
    TrackGraphEdgeSelector chooseRight = (boundary, node, candidates) => {
      selectorCalls++;
      using (Assert.EnterMultipleScope()) {
        Assert.That(boundary.Edge, Is.SameAs(fixture.Incoming));
        Assert.That(boundary.PieceArcLength, Is.EqualTo(10d));
        Assert.That(node, Is.SameAs(fixture.Incoming.To));
        Assert.That(candidates, Has.Count.EqualTo(2));
      }
      return fixture.Right;
    };

    var advanced = state.Advance(TimeSpan.FromSeconds(1d), chooseRight);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selectorCalls, Is.EqualTo(1));
      Assert.That(advanced.Cursor.Edge, Is.SameAs(fixture.Right));
      Assert.That(advanced.Cursor.PieceArcLength, Is.EqualTo(1d));
      Assert.That(advanced.Speed, Is.EqualTo(2d));
      Assert.That(advanced.Pose, Is.EqualTo(Pose(advanced.Cursor)));
    }
    Assert.Throws<InvalidOperationException>(new Action(() =>
      state.Advance(TimeSpan.FromSeconds(1d))));
  }

  [Test]
  public void GraphAdvance_ForwardsExactMergeSelectorForNegativeSpeed() {
    var fixture = BranchingGraph();
    var cursor = fixture.Traversal.AtEdge(fixture.Outgoing, 1d);
    var state = new TrackGraphKinematicVehicleState(cursor, -2d);
    var selectorCalls = 0;
    TrackGraphEdgeSelector chooseLeft = (boundary, node, candidates) => {
      selectorCalls++;
      using (Assert.EnterMultipleScope()) {
        Assert.That(boundary.Edge, Is.SameAs(fixture.Outgoing));
        Assert.That(boundary.PieceArcLength, Is.Zero);
        Assert.That(node, Is.SameAs(fixture.Outgoing.From));
        Assert.That(candidates, Has.Count.EqualTo(2));
      }
      return fixture.Left;
    };

    var advanced = state.Advance(
      TimeSpan.FromSeconds(1d),
      reverseSelector: chooseLeft);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selectorCalls, Is.EqualTo(1));
      Assert.That(advanced.Cursor.Edge, Is.SameAs(fixture.Left));
      Assert.That(advanced.Cursor.PieceArcLength, Is.EqualTo(9d));
      Assert.That(advanced.Speed, Is.EqualTo(-2d));
      Assert.That(advanced.Pose, Is.EqualTo(Pose(advanced.Cursor)));
    }
  }

  [Test]
  public void Api_RejectsInvalidStateStepsAndOverflowBeforeTraversal() {
    var circuitCursor = new TrackCircuitTraversal(Circle()).Start;
    var fixture = BranchingGraph();
    var graphCursor = fixture.Traversal.AtEdge(fixture.Incoming, 0d);
    var circuit = new TrackCircuitKinematicVehicleState(circuitCursor, 1d);
    var graph = new TrackGraphKinematicVehicleState(graphCursor, 1d);
    var fastCircuit = new TrackCircuitKinematicVehicleState(circuitCursor, double.MaxValue);
    var fastGraph = new TrackGraphKinematicVehicleState(graphCursor, double.MaxValue);
    var graphSelectorCalls = 0;
    TrackGraphEdgeSelector unusedSelector = (_, _, candidates) => {
      graphSelectorCalls++;
      return candidates[0];
    };

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackCircuitKinematicVehicleState(circuitCursor, double.NaN)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackGraphKinematicVehicleState(graphCursor, double.PositiveInfinity)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      _ = new TrackCircuitKinematicVehicleState(default, 0d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      _ = new TrackGraphKinematicVehicleState(default, 0d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      default(TrackCircuitKinematicVehicleState).Advance(TimeSpan.Zero)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      default(TrackGraphKinematicVehicleState).Advance(TimeSpan.Zero)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      circuit.Advance(TimeSpan.FromTicks(-1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      graph.Advance(TimeSpan.FromTicks(-1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      fastCircuit.Advance(TimeSpan.FromSeconds(2d))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      fastGraph.Advance(TimeSpan.FromSeconds(2d), unusedSelector, unusedSelector)));

    var unchanged = graph.Advance(TimeSpan.Zero, unusedSelector, unusedSelector);
    using (Assert.EnterMultipleScope()) {
      Assert.That(unchanged, Is.EqualTo(graph));
      Assert.That(graphSelectorCalls, Is.Zero);
    }
  }

  private static TrackContactPose Pose(TrackCircuitCursor cursor) =>
    TrackContactPoseAdapter.Create(cursor.Sample().ContactPoints);

  private static TrackContactPose Pose(TrackGraphCursor cursor) =>
    TrackContactPoseAdapter.Create(cursor.Sample().ContactPoints);

  private static TrackCircuit Circle() {
    const float scale = 1.5f;
    return new TrackCircuit([
      new TrackCircuitPiece(
        "north-east",
        Piece(Vector3.UnitX, Vector3.UnitY, Vector3.UnitY * scale, -Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "north-west",
        Piece(Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitX * scale, -Vector3.UnitY * scale)),
      new TrackCircuitPiece(
        "south-west",
        Piece(-Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitY * scale, Vector3.UnitX * scale)),
      new TrackCircuitPiece(
        "south-east",
        Piece(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitX * scale, Vector3.UnitY * scale)),
    ]);
  }

  private static TrackPiece Piece(
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
    return new(
      new TrackGraphTraversal(graph),
      incoming,
      left,
      right,
      outgoing);
  }

  private static TrackPiece StraightPiece(float startX, float endX) {
    var start = new Vector3(startX, 0f, 0f);
    var end = new Vector3(endX, 0f, 0f);
    var tangent = end - start;
    var halfGauge = Vector3.UnitY * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        tangent,
        start + halfGauge,
        tangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        tangent,
        end + halfGauge,
        tangent,
        0f),
    ]));
  }

  private sealed record GraphFixture(
    TrackGraphTraversal Traversal,
    TrackEdge Incoming,
    TrackEdge Left,
    TrackEdge Right,
    TrackEdge Outgoing
  );
}
