// Ride Car Static Pose Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarStaticPoseBuilderTests {
  [Test]
  public void Build_StraightTrackCreatesRigidMidpointAnchoredBodyPose() {
    var circuit = VerticalStadium(rollRadians: 0f);
    var rear = Sample(circuit, pieceIndex: 0, fraction: 0.25f);
    var front = Sample(circuit, pieceIndex: 0, fraction: 0.75f);
    var geometry = Geometry(Vector3.UnitX, new Vector3(3f, 4f, 5f));

    var pose = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: false);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Circuit, Is.SameAs(circuit));
      Assert.That(pose.FrontContact, Is.EqualTo(front));
      Assert.That(pose.RearContact, Is.EqualTo(rear));
      Assert.That(pose.Reversed, Is.False);
      AssertVector(pose.Forward, Vector3.UnitX);
      AssertVector(pose.Right, Vector3.UnitY);
      AssertVector(pose.Up, Vector3.UnitZ);
      AssertVector(
        Vector3.Transform(geometry.LongitudinalAxis, pose.Orientation),
        pose.Forward);
      AssertVector(
        Vector3.TransformNormal(geometry.LongitudinalAxis, pose.Transform),
        pose.Forward);
      AssertVector(
        Vector3.Transform(ModelWheelMidpoint(geometry), pose.Transform),
        pose.ContactMidpoint);
      Assert.That(pose.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Build_CurvedTrackUsesContactChordAndOrthonormalizedPairedGauges() {
    var circuit = VerticalStadium(rollRadians: 0f);
    var rear = Sample(circuit, pieceIndex: 1, fraction: 0.15f);
    var front = Sample(circuit, pieceIndex: 1, fraction: 0.85f);
    var modelAxis = Vector3.Normalize(new Vector3(1f, 0f, 1f));
    var geometry = Geometry(modelAxis, new Vector3(-2f, 3f, 7f));
    var expectedForward = Vector3.Normalize(front.ContactPoints.Midpoint -
      rear.ContactPoints.Midpoint);

    var pose = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: false);

    using (Assert.EnterMultipleScope()) {
      AssertVector(pose.Forward, expectedForward);
      Assert.That(pose.Forward.Length(), Is.EqualTo(1f).Within(0.00001f));
      Assert.That(pose.Right.Length(), Is.EqualTo(1f).Within(0.00001f));
      Assert.That(pose.Up.Length(), Is.EqualTo(1f).Within(0.00001f));
      Assert.That(Vector3.Dot(pose.Forward, pose.Right), Is.Zero.Within(0.00001f));
      Assert.That(Vector3.Dot(pose.Forward, pose.Up), Is.Zero.Within(0.00001f));
      Assert.That(Vector3.Dot(pose.Right, pose.Up), Is.Zero.Within(0.00001f));
      AssertVector(Vector3.Cross(pose.Forward, pose.Right), pose.Up);
      AssertVector(
        Vector3.TransformNormal(geometry.LongitudinalAxis, pose.Transform),
        pose.Forward);
      AssertVector(
        Vector3.Transform(ModelWheelMidpoint(geometry), pose.Transform),
        pose.ContactMidpoint);
      Assert.That(pose.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Build_BankedStraightUsesPairedRailGaugeForRightAndUp() {
    var roll = MathF.PI / 6f;
    var circuit = VerticalStadium(roll);
    var rear = Sample(circuit, pieceIndex: 0, fraction: 0.25f);
    var front = Sample(circuit, pieceIndex: 0, fraction: 0.75f);
    var geometry = Geometry(Vector3.UnitX, Vector3.Zero);
    var expectedRight = Vector3.Transform(
      Vector3.UnitY,
      Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll));
    var expectedUp = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, expectedRight));

    var pose = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: false);

    using (Assert.EnterMultipleScope()) {
      AssertVector(pose.Forward, Vector3.UnitX);
      AssertVector(pose.Right, expectedRight);
      AssertVector(pose.Up, expectedUp);
      AssertVector(Vector3.Cross(pose.Forward, pose.Right), pose.Up);
      Assert.That(pose.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Build_ModelForwardAlongRepoYRetainsDecodedZUpConvention() {
    var circuit = VerticalStadium(rollRadians: 0f);
    var rear = Sample(circuit, pieceIndex: 0, fraction: 0.25f);
    var front = Sample(circuit, pieceIndex: 0, fraction: 0.75f);
    var geometry = Geometry(Vector3.UnitY, new Vector3(2f, 4f, 6f));

    var pose = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: false);

    using (Assert.EnterMultipleScope()) {
      AssertVector(
        Vector3.TransformNormal(Vector3.UnitY, pose.Transform),
        Vector3.UnitX);
      AssertVector(
        Vector3.TransformNormal(Vector3.UnitZ, pose.Transform),
        Vector3.UnitZ);
      AssertVector(
        Vector3.Transform(ModelWheelMidpoint(geometry), pose.Transform),
        pose.ContactMidpoint);
      Assert.That(pose.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Build_ReversedAppliesNativePiAboutUpWithoutMovingContacts() {
    var circuit = VerticalStadium(MathF.PI / 8f);
    var rear = Sample(circuit, pieceIndex: 0, fraction: 0.2f);
    var front = Sample(circuit, pieceIndex: 0, fraction: 0.8f);
    var geometry = Geometry(Vector3.UnitX, new Vector3(10f, -3f, 2f));
    var normal = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: false);

    var reversed = RideCarStaticPoseBuilder.Build(
      circuit,
      front,
      rear,
      geometry,
      reversed: true);

    using (Assert.EnterMultipleScope()) {
      AssertVector(reversed.Forward, -normal.Forward);
      AssertVector(reversed.Right, -normal.Right);
      AssertVector(reversed.Up, normal.Up);
      AssertVector(reversed.ContactMidpoint, normal.ContactMidpoint);
      AssertVector(
        Vector3.Transform(ModelWheelMidpoint(geometry), reversed.Transform),
        normal.ContactMidpoint);
      Assert.That(reversed.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Build_RejectsForeignMalformedDegenerateAndNonFiniteEvidence() {
    var circuit = VerticalStadium(rollRadians: 0f);
    var foreignCircuit = VerticalStadium(rollRadians: 0f);
    var rear = Sample(circuit, pieceIndex: 0, fraction: 0.25f);
    var front = Sample(circuit, pieceIndex: 0, fraction: 0.75f);
    var foreignRear = Sample(foreignCircuit, pieceIndex: 0, fraction: 0.25f);
    var geometry = Geometry(Vector3.UnitX, Vector3.Zero);
    var malformedContacts = front with {
      ContactPoints = front.ContactPoints with {
        Right = front.ContactPoints.Right with {
          Position = front.ContactPoints.Left.Position,
        },
      },
    };
    var nonFiniteGeometry = geometry with {
      FrontWheelCenterPosition = new Vector3(float.NaN, 0f, 0f),
    };
    var degenerateModelFrame = Geometry(Vector3.UnitZ, Vector3.Zero);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        circuit,
        front,
        foreignRear,
        geometry,
        reversed: false)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        circuit,
        malformedContacts,
        rear,
        geometry,
        reversed: false)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        circuit,
        rear,
        rear,
        geometry,
        reversed: false)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        circuit,
        front,
        rear,
        nonFiniteGeometry,
        reversed: false)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        circuit,
        front,
        rear,
        degenerateModelFrame,
        reversed: false)));

    var traversal = new TrackCircuitTraversal(circuit);
    var foreignTraversal = new TrackCircuitTraversal(circuit);
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticPoseBuilder.Build(
        traversal.AtPiece(0, 0.25d),
        foreignTraversal.AtPiece(0, 0.75d),
        geometry,
        reversed: false)));
  }

  private static RideCarLongitudinalGeometry Geometry(
    Vector3 axis,
    Vector3 wheelMidpoint
  ) {
    axis = Vector3.Normalize(axis);
    var carRear = wheelMidpoint - (axis * 2f);
    var carFront = wheelMidpoint + (axis * 2f);
    var rearWheel = wheelMidpoint - (axis * 0.75f);
    var frontWheel = wheelMidpoint + (axis * 0.75f);
    return new(
      carFront,
      carRear,
      frontWheel,
      rearWheel,
      axis,
      4f,
      Vector3.Dot(frontWheel, axis),
      Vector3.Dot(rearWheel, axis),
      1.5f,
      2f,
      2f);
  }

  private static Vector3 ModelWheelMidpoint(RideCarLongitudinalGeometry geometry) =>
    (geometry.FrontWheelCenterPosition + geometry.RearWheelCenterPosition) * 0.5f;

  private static TrackCircuitSample Sample(
    TrackCircuit circuit,
    int pieceIndex,
    float fraction
  ) {
    var piece = circuit.Pieces[pieceIndex].Piece;
    return new TrackCircuitTraversal(circuit)
      .AtPiece(pieceIndex, Convert.ToDouble(piece.Length) * fraction)
      .Sample();
  }

  private static TrackCircuit VerticalStadium(float rollRadians) {
    const float tangentScale = 1.5f;
    return new TrackCircuit([
      CircuitPiece("bottom", new(-1f, 0f, 0f), new(1f, 0f, 0f),
        Vector3.UnitX * tangentScale, Vector3.UnitX * tangentScale, rollRadians),
      CircuitPiece("lower-right", new(1f, 0f, 0f), new(2f, 0f, 1f),
        Vector3.UnitX * tangentScale, Vector3.UnitZ * tangentScale, rollRadians),
      CircuitPiece("right", new(2f, 0f, 1f), new(2f, 0f, 3f),
        Vector3.UnitZ * tangentScale, Vector3.UnitZ * tangentScale, rollRadians),
      CircuitPiece("upper-right", new(2f, 0f, 3f), new(1f, 0f, 4f),
        Vector3.UnitZ * tangentScale, -Vector3.UnitX * tangentScale, rollRadians),
      CircuitPiece("top", new(1f, 0f, 4f), new(-1f, 0f, 4f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitX * tangentScale, rollRadians),
      CircuitPiece("upper-left", new(-1f, 0f, 4f), new(-2f, 0f, 3f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitZ * tangentScale, rollRadians),
      CircuitPiece("left", new(-2f, 0f, 3f), new(-2f, 0f, 1f),
        -Vector3.UnitZ * tangentScale, -Vector3.UnitZ * tangentScale, rollRadians),
      CircuitPiece("lower-left", new(-2f, 0f, 1f), new(-1f, 0f, 0f),
        -Vector3.UnitZ * tangentScale, Vector3.UnitX * tangentScale, rollRadians),
    ]);
  }

  private static TrackCircuitPiece CircuitPiece(
    string id,
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent,
    float rollRadians
  ) {
    var startGauge = Gauge(startTangent, rollRadians);
    var endGauge = Gauge(endTangent, rollRadians);
    return new(id, new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - startGauge,
        startTangent,
        start + startGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - endGauge,
        endTangent,
        end + endGauge,
        endTangent,
        0f),
    ])));
  }

  private static Vector3 Gauge(Vector3 tangent, float rollRadians) =>
    Vector3.Transform(
      Vector3.UnitY * 0.5f,
      Quaternion.CreateFromAxisAngle(Vector3.Normalize(tangent), rollRadians));

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
    }
  }
}
