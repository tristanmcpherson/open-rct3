// Track Circuit Kinematic Train State Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackCircuitKinematicTrainStateTests {
  [Test]
  public void Constructor_CopiesBoundedOffsetsAndRetainsExactOrderedLayout() {
    var traversal = new TrackCircuitTraversal(Circle());
    var lead = traversal.AtPiece(0, 0.75d);
    var offsets = new List<double> { 0d, 0.25d, 0.25d, 1d };
    var expectedOffsets = offsets.ToArray();
    var expectedPoses = TrackConsistLayoutBuilder.FromCircuit(lead, expectedOffsets);

    var state = new TrackCircuitKinematicTrainState(lead, 3d, offsets);
    offsets[0] = 10d;
    offsets.Add(11d);

    using (Assert.EnterMultipleScope()) {
      Assert.That(state.LeadCursor, Is.EqualTo(lead));
      Assert.That(state.Lead.Cursor, Is.EqualTo(lead));
      Assert.That(state.Lead.Pose, Is.EqualTo(Pose(lead)));
      Assert.That(state.Speed, Is.EqualTo(3d));
      Assert.That(state.OffsetsBehindLead, Is.EqualTo(expectedOffsets));
      Assert.That(state.Poses, Is.EqualTo(expectedPoses));
      Assert.That(state.Poses[1].Cursor, Is.EqualTo(state.Poses[2].Cursor));
    }

    var readOnlyOffsets = (IList<double>)state.OffsetsBehindLead;
    var readOnlyPoses = (IList<TrackCircuitConsistPose>)state.Poses;
    Assert.Throws<NotSupportedException>(new Action(() =>
      readOnlyOffsets[0] = 1d));
    Assert.Throws<NotSupportedException>(new Action(() =>
      readOnlyPoses[0] = default));
  }

  [Test]
  public void Advance_MovesSignedLeadThenRebuildsExactConsist() {
    var traversal = new TrackCircuitTraversal(Circle());
    var originalCursor = traversal.AtPiece(0, 0.25d);
    double[] offsets = [0d, 0.125d, 0.5d, 1.25d];
    var state = new TrackCircuitKinematicTrainState(originalCursor, -2d, offsets);
    var elapsed = TimeSpan.FromSeconds(0.25d);
    var expectedLead = state.Lead.Advance(elapsed);
    var expectedPoses = TrackConsistLayoutBuilder.FromCircuit(
      expectedLead.Cursor,
      offsets);

    var advanced = state.Advance(elapsed);
    var repeated = state.Advance(elapsed);
    var zeroStep = state.Advance(TimeSpan.Zero);

    using (Assert.EnterMultipleScope()) {
      Assert.That(state.LeadCursor, Is.EqualTo(originalCursor));
      Assert.That(advanced.Lead, Is.EqualTo(expectedLead));
      Assert.That(advanced.Speed, Is.EqualTo(-2d));
      Assert.That(advanced.OffsetsBehindLead, Is.EqualTo(offsets));
      Assert.That(advanced.Poses, Is.EqualTo(expectedPoses));
      Assert.That(repeated.Lead, Is.EqualTo(advanced.Lead));
      Assert.That(repeated.Poses, Is.EqualTo(advanced.Poses));
      Assert.That(zeroStep.Lead, Is.EqualTo(state.Lead));
      Assert.That(zeroStep.Poses, Is.EqualTo(state.Poses));
      Assert.That(advanced.Poses, Is.Not.SameAs(state.Poses));
    }
    AssertExactPoseSamples(advanced.Poses);
  }

  [Test]
  public void WithSpeed_PreservesExactCursorAndLayoutWithoutMutatingSource() {
    var traversal = new TrackCircuitTraversal(Circle());
    var state = new TrackCircuitKinematicTrainState(
      traversal.AtPiece(2, 0.5d),
      1.25d,
      [0d, 0.5d, 1d]);

    var replacement = state.WithSpeed(-4d);

    using (Assert.EnterMultipleScope()) {
      Assert.That(state.Speed, Is.EqualTo(1.25d));
      Assert.That(replacement.Speed, Is.EqualTo(-4d));
      Assert.That(replacement.LeadCursor, Is.EqualTo(state.LeadCursor));
      Assert.That(replacement.Lead.Pose, Is.EqualTo(state.Lead.Pose));
      Assert.That(replacement.OffsetsBehindLead, Is.EqualTo(state.OffsetsBehindLead));
      Assert.That(replacement.Poses, Is.EqualTo(state.Poses));
    }
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      state.WithSpeed(double.NaN)));
  }

  [Test]
  public void Api_RejectsInvalidUnsortedUnboundedAndOverflowingState() {
    var cursor = new TrackCircuitTraversal(Circle()).Start;

    Assert.Throws<ArgumentNullException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, 0d, null!)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, 0d, [-1d])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, 0d, [double.NaN])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(
        cursor,
        0d,
        [double.PositiveInfinity])));
    Assert.Throws<ArgumentException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, 0d, [0d, 2d, 1d])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, 0d, new double[16_385])));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(cursor, double.NegativeInfinity, [])));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      _ = new TrackCircuitKinematicTrainState(
        default(TrackCircuitKinematicVehicleState),
        [])));

    var normal = new TrackCircuitKinematicTrainState(cursor, 1d, []);
    var overflowing = new TrackCircuitKinematicTrainState(cursor, double.MaxValue, []);
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      normal.Advance(TimeSpan.FromTicks(-1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      overflowing.Advance(TimeSpan.FromSeconds(2d))));
  }

  private static void AssertExactPoseSamples(
    IReadOnlyList<TrackCircuitConsistPose> poses
  ) {
    foreach (var pose in poses)
      Assert.That(pose.Pose, Is.EqualTo(Pose(pose.Cursor)));
  }

  private static TrackContactPose Pose(TrackCircuitCursor cursor) =>
    TrackContactPoseAdapter.Create(cursor.Sample().ContactPoints);

  private static TrackCircuit Circle() {
    const float scale = 1.5f;
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
}
