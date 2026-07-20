// Track Graph Traversal Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackGraphTraversalTests {
  [Test]
  public void AtEdgeAndSample_PreserveExactEdgeLocalIdentity() {
    var fixture = BranchingGraph();
    var arcLength = Convert.ToDouble(fixture.Incoming.Piece.Length) * 0.25d;

    var cursor = fixture.Traversal.AtEdge(fixture.Incoming, arcLength);
    var sample = cursor.Sample();

    using (Assert.EnterMultipleScope()) {
      Assert.That(cursor.IsInitialized, Is.True);
      Assert.That(cursor.Graph, Is.SameAs(fixture.Traversal.Graph));
      Assert.That(cursor.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(cursor.EdgeIndex, Is.Zero);
      Assert.That(cursor.PieceArcLength, Is.EqualTo(arcLength));
      Assert.That(sample.EdgeIndex, Is.Zero);
      Assert.That(sample.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(sample.PieceArcLength, Is.EqualTo(Convert.ToSingle(arcLength)));
      Assert.That(
        sample.ContactPoints,
        Is.EqualTo(fixture.Incoming.Piece.SampleContactPoints(
          Convert.ToSingle(arcLength))));
      Assert.That(cursor.Sample(), Is.EqualTo(sample));
    }
  }

  [Test]
  public void Advance_PreservesDirectionalIdentityAtExactBoundaries() {
    var fixture = BranchingGraph();
    var incomingLength = Convert.ToDouble(fixture.Incoming.Piece.Length);
    var branchLength = Convert.ToDouble(fixture.Left.Piece.Length);

    var incomingExit = fixture.Traversal.AtEdge(fixture.Incoming, 0d)
      .Advance(incomingLength);
    var leftEntry = fixture.Traversal.AtEdge(fixture.Left, branchLength * 0.25d)
      .Advance(-(branchLength * 0.25d));

    using (Assert.EnterMultipleScope()) {
      Assert.That(incomingExit.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(incomingExit.PieceArcLength, Is.EqualTo(incomingLength));
      Assert.That(leftEntry.Edge, Is.SameAs(fixture.Left));
      Assert.That(leftEntry.PieceArcLength, Is.Zero);
      Assert.That(incomingExit.Advance(0d), Is.EqualTo(incomingExit));
      Assert.That(leftEntry.Advance(0d), Is.EqualTo(leftEntry));
    }
  }

  [Test]
  public void AdvanceForward_RequiresAndValidatesExactBranchSelection() {
    var fixture = BranchingGraph();
    var incomingExit = fixture.Traversal.AtEdge(
      fixture.Incoming,
      fixture.Incoming.Piece.Length);
    var selectorCalls = 0;
    TrackGraphEdgeSelector chooseRight = (boundary, node, candidates) => {
      selectorCalls++;
      using (Assert.EnterMultipleScope()) {
        Assert.That(boundary.Edge, Is.SameAs(fixture.Incoming));
        Assert.That(boundary.PieceArcLength,
          Is.EqualTo(Convert.ToDouble(fixture.Incoming.Piece.Length)));
        Assert.That(node, Is.SameAs(fixture.Incoming.To));
        Assert.That(candidates, Has.Count.EqualTo(2));
      }
      return fixture.Right;
    };

    var selected = incomingExit.Advance(1d, chooseRight);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selectorCalls, Is.EqualTo(1));
      Assert.That(selected.Edge, Is.SameAs(fixture.Right));
      Assert.That(selected.PieceArcLength, Is.EqualTo(1d));
    }
    Assert.Throws<InvalidOperationException>(new Action(() =>
      incomingExit.Advance(1d)));
    Assert.Throws<ArgumentException>(new Action(() =>
      incomingExit.Advance(1d, (_, _, _) => fixture.Outgoing)));
    Assert.Throws<ArgumentException>(new Action(() =>
      incomingExit.Advance(1d, (_, _, _) => fixture.Right with { })));
    Assert.Throws<ArgumentException>(new Action(() =>
      incomingExit.Advance(1d, (_, _, _) => null!)));
  }

  [Test]
  public void AdvanceBackward_UsesUniqueIncomingOrRequiresMergeSelection() {
    var fixture = BranchingGraph();
    var outgoingEntry = fixture.Traversal.AtEdge(fixture.Outgoing, 0d);
    TrackGraphEdgeSelector chooseLeft = (_, node, candidates) => {
      using (Assert.EnterMultipleScope()) {
        Assert.That(node, Is.SameAs(fixture.Outgoing.From));
        Assert.That(candidates, Has.Count.EqualTo(2));
      }
      return fixture.Left;
    };

    var selected = outgoingEntry.Advance(-1d, reverseSelector: chooseLeft);
    var unique = fixture.Traversal.AtEdge(fixture.Left, 0d).Advance(-1d);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selected.Edge, Is.SameAs(fixture.Left));
      Assert.That(selected.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(fixture.Left.Piece.Length) - 1d));
      Assert.That(unique.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(unique.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(fixture.Incoming.Piece.Length) - 1d));
    }
    Assert.Throws<InvalidOperationException>(new Action(() =>
      outgoingEntry.Advance(-1d)));
  }

  [Test]
  public void Advance_CrossesMultipleEdgesWithoutGlobalPathAccumulation() {
    var fixture = BranchingGraph();
    TrackGraphEdgeSelector chooseRight = (_, _, _) => fixture.Right;
    var forwardDistance =
      Convert.ToDouble(fixture.Incoming.Piece.Length) +
      Convert.ToDouble(fixture.Right.Piece.Length) +
      2d;

    var forward = fixture.Traversal.AtEdge(fixture.Incoming, 0d)
      .Advance(forwardDistance, chooseRight);
    var backward = forward.Advance(
      -(2d + fixture.Right.Piece.Length + 3d),
      reverseSelector: chooseRight);

    using (Assert.EnterMultipleScope()) {
      Assert.That(forward.Edge, Is.SameAs(fixture.Outgoing));
      Assert.That(forward.PieceArcLength, Is.EqualTo(2d));
      Assert.That(backward.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(backward.PieceArcLength,
        Is.EqualTo(Convert.ToDouble(fixture.Incoming.Piece.Length) - 3d));
    }
  }

  [Test]
  public void Sample_ResamplesExactPieceIdentityAndRetainsSubFloatMovement() {
    var fixture = BranchingGraph();
    const double step = 0.000000001d;
    var repeated = fixture.Traversal.AtEdge(fixture.Incoming, 0d);
    foreach (var _ in Enumerable.Range(0, 1_000))
      repeated = repeated.Advance(step);
    var once = fixture.Traversal.AtEdge(fixture.Incoming, 0d)
      .Advance(step * 1_000d);
    var incomingExit = fixture.Traversal.AtEdge(
      fixture.Incoming,
      fixture.Incoming.Piece.Length).Sample();
    var rightEntry = fixture.Traversal.AtEdge(fixture.Right, 0d).Sample();

    using (Assert.EnterMultipleScope()) {
      Assert.That(repeated.Edge, Is.SameAs(once.Edge));
      Assert.That(repeated.PieceArcLength,
        Is.EqualTo(once.PieceArcLength).Within(0.000000000001d));
      Assert.That(repeated.Sample().ContactPoints, Is.EqualTo(once.Sample().ContactPoints));
      Assert.That(incomingExit.Edge, Is.SameAs(fixture.Incoming));
      Assert.That(rightEntry.Edge, Is.SameAs(fixture.Right));
      Assert.That(rightEntry.ContactPoints.Midpoint.X,
        Is.EqualTo(incomingExit.ContactPoints.Midpoint.X).Within(0.000001f));
      Assert.That(rightEntry.ContactPoints.Midpoint.Y,
        Is.EqualTo(incomingExit.ContactPoints.Midpoint.Y).Within(0.000001f));
      Assert.That(rightEntry.ContactPoints.Midpoint.Z,
        Is.EqualTo(incomingExit.ContactPoints.Midpoint.Z).Within(0.000001f));
    }
  }

  [Test]
  public void Advance_RejectsMovementPastOpenTerminals() {
    var fixture = BranchingGraph();
    var end = fixture.Traversal.AtEdge(
      fixture.Outgoing,
      fixture.Outgoing.Piece.Length);
    var start = fixture.Traversal.AtEdge(fixture.Incoming, 0d);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      end.Advance(0.1d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      start.Advance(-0.1d)));
  }

  [Test]
  public void Api_RejectsInvalidPositionsDistancesAndForeignIdentities() {
    var fixture = BranchingGraph();
    var foreignTraversal = new TrackGraphTraversal(fixture.Traversal.Graph);
    var cursor = fixture.Traversal.AtEdge(fixture.Incoming, 0d);
    var defaultCursor = default(TrackGraphCursor);

    Assert.Throws<ArgumentNullException>(new Action(() =>
      _ = new TrackGraphTraversal(null!)));
    Assert.Throws<ArgumentNullException>(new Action(() =>
      fixture.Traversal.AtEdge(null!, 0d)));
    Assert.Throws<ArgumentException>(new Action(() =>
      fixture.Traversal.AtEdge(fixture.Incoming with { }, 0d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      fixture.Traversal.AtEdge(fixture.Incoming, -0.1d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      fixture.Traversal.AtEdge(fixture.Incoming, double.NaN)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      fixture.Traversal.AtEdge(
        fixture.Incoming,
        Convert.ToDouble(fixture.Incoming.Piece.Length) + 0.1d)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      cursor.Advance(double.NaN)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      cursor.Advance(double.PositiveInfinity)));
    Assert.Throws<ArgumentException>(new Action(() =>
      foreignTraversal.Advance(cursor, 1d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      defaultCursor.Advance(1d)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      defaultCursor.Sample()));
  }

  private static TraversalFixture BranchingGraph() {
    var root = new TrackNode("root");
    var split = new TrackNode("split");
    var merge = new TrackNode("merge");
    var end = new TrackNode("end");
    var incoming = new TrackEdge("incoming", root, split, Piece(0f, 10f));
    var left = new TrackEdge("left", split, merge, Piece(10f, 20f));
    var right = new TrackEdge("right", split, merge, Piece(10f, 20f));
    var outgoing = new TrackEdge("outgoing", merge, end, Piece(20f, 30f));
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

  private static TrackPiece Piece(float startX, float endX) {
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

  private sealed record TraversalFixture(
    TrackGraphTraversal Traversal,
    TrackEdge Incoming,
    TrackEdge Left,
    TrackEdge Right,
    TrackEdge Outgoing
  );
}
