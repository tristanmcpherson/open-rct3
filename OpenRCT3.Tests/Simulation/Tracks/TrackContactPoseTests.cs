// Track Contact Pose Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackContactPoseTests {
  [Test]
  public void Create_BuildsExactCenterAndRightHandedLocalToWorldFrame() {
    var piece = SlopedPiece();
    var contacts = piece.SampleContactPoints(piece.Length * 0.5f);

    var pose = TrackContactPoseAdapter.Create(contacts);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.ArcLength, Is.EqualTo(contacts.ArcLength));
      AssertVector(pose.Center, contacts.Midpoint);
      Assert.That(pose.Gauge, Is.EqualTo(2d).Within(0.000001d));
      Assert.That(pose.Forward.Length(), Is.EqualTo(1f).Within(0.000001f));
      Assert.That(pose.Right.Length(), Is.EqualTo(1f).Within(0.000001f));
      Assert.That(pose.Up.Length(), Is.EqualTo(1f).Within(0.000001f));
      Assert.That(Vector3.Dot(pose.Forward, pose.Right), Is.Zero.Within(0.000001f));
      Assert.That(Vector3.Dot(pose.Forward, pose.Up), Is.Zero.Within(0.000001f));
      Assert.That(Vector3.Dot(pose.Right, pose.Up), Is.Zero.Within(0.000001f));
      AssertVector(Vector3.Cross(pose.Forward, pose.Right), pose.Up);
      AssertVector(Vector3.Transform(Vector3.Zero, pose.Transform), pose.Center);
      AssertVector(Vector3.TransformNormal(Vector3.UnitX, pose.Transform), pose.Forward);
      AssertVector(Vector3.TransformNormal(Vector3.UnitY, pose.Transform), pose.Right);
      AssertVector(Vector3.TransformNormal(Vector3.UnitZ, pose.Transform), pose.Up);
      AssertVector(Vector3.Transform(Vector3.UnitX, pose.Orientation), pose.Forward);
      AssertVector(Vector3.Transform(Vector3.UnitY, pose.Orientation), pose.Right);
      AssertVector(Vector3.Transform(Vector3.UnitZ, pose.Orientation), pose.Up);
      Assert.That(pose.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
    }
  }

  [Test]
  public void Create_UsesMeanRailTangentsAndOrthogonalizedGauge() {
    var leftTangent = Vector3.UnitX;
    var rightTangent = Vector3.Normalize(new Vector3(1f, 0.2f, 0f));
    var contacts = Contacts(
      new Vector3(10f, 19f, 30f),
      new Vector3(10f, 21f, 30f),
      leftTangent,
      rightTangent);
    var expectedForward = Vector3.Normalize((leftTangent + rightTangent) * 0.5f);

    var pose = TrackContactPoseAdapter.Create(contacts);

    using (Assert.EnterMultipleScope()) {
      AssertVector(pose.Center, new Vector3(10f, 20f, 30f));
      AssertVector(pose.Forward, expectedForward);
      Assert.That(Vector3.Dot(pose.Right, Vector3.UnitY), Is.GreaterThan(0f));
      Assert.That(Vector3.Dot(pose.Right, pose.Forward), Is.Zero.Within(0.000001f));
      AssertVector(Vector3.Cross(pose.Forward, pose.Right), pose.Up);
    }
  }

  [Test]
  public void Create_UsesDoublePrecisionForExtremeFiniteGauge() {
    var contacts = Contacts(
      new Vector3(0f, -float.MaxValue, 0f),
      new Vector3(0f, float.MaxValue, 0f),
      Vector3.UnitX,
      Vector3.UnitX);

    var pose = TrackContactPoseAdapter.Create(contacts);

    using (Assert.EnterMultipleScope()) {
      AssertVector(pose.Center, Vector3.Zero);
      Assert.That(double.IsFinite(pose.Gauge), Is.True);
      Assert.That(pose.Gauge, Is.GreaterThan(float.MaxValue));
      AssertVector(pose.Forward, Vector3.UnitX);
      AssertVector(pose.Right, Vector3.UnitY);
      AssertVector(pose.Up, Vector3.UnitZ);
    }
  }

  [Test]
  public void Create_IsDeterministicWhenResamplingTheSamePieceContact() {
    var piece = SlopedPiece();
    var arcLength = piece.Length * 0.375f;

    var first = TrackContactPoseAdapter.Create(piece.SampleContactPoints(arcLength));
    var second = TrackContactPoseAdapter.Create(piece.SampleContactPoints(arcLength));

    Assert.That(second, Is.EqualTo(first));
  }

  [Test]
  public void Create_RejectsDegenerateGaugeAndTangentGeometry() {
    var coincident = Contacts(
      Vector3.Zero,
      Vector3.Zero,
      Vector3.UnitX,
      Vector3.UnitX);
    var parallelGauge = Contacts(
      Vector3.Zero,
      Vector3.UnitX,
      Vector3.UnitX,
      Vector3.UnitX);
    var oppositeTangents = Contacts(
      -Vector3.UnitY,
      Vector3.UnitY,
      Vector3.UnitX,
      -Vector3.UnitX);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(coincident)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(parallelGauge)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(oppositeTangents)));
  }

  [Test]
  public void Create_RejectsNonFiniteAndInconsistentRailSamples() {
    var valid = Contacts(
      -Vector3.UnitY,
      Vector3.UnitY,
      Vector3.UnitX,
      Vector3.UnitX);
    var mismatchedArc = valid with {
      Left = valid.Left with { ArcLength = valid.ArcLength + 1f },
    };
    var nonFinitePosition = valid with {
      Left = valid.Left with {
        Position = new Vector3(float.NaN, 0f, 0f),
      },
    };
    var nonUnitTangent = valid with {
      Left = valid.Left with { Tangent = Vector3.UnitX * 2f },
    };
    var nonUnitOrientation = valid with {
      Left = valid.Left with { Orientation = default },
    };
    var mismatchedOrientation = valid with {
      Left = valid.Left with {
        Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f),
      },
    };
    var nonFiniteBank = valid with {
      Left = valid.Left with { BankRadians = float.PositiveInfinity },
    };

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(valid with { ArcLength = float.NaN })));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(mismatchedArc)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(nonFinitePosition)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(nonUnitTangent)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(nonUnitOrientation)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(mismatchedOrientation)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackContactPoseAdapter.Create(nonFiniteBank)));
  }

  private static TrackPiece SlopedPiece() {
    var start = Vector3.Zero;
    var end = new Vector3(10f, 0f, 5f);
    var tangent = end - start;
    var halfGauge = Vector3.UnitY;
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

  private static TrackContactPoints Contacts(
    Vector3 leftPosition,
    Vector3 rightPosition,
    Vector3 leftTangent,
    Vector3 rightTangent,
    float arcLength = 5f
  ) => new(
    arcLength,
    Sample(arcLength, leftPosition, leftTangent),
    Sample(arcLength, rightPosition, rightTangent));

  private static RailSample Sample(
    float arcLength,
    Vector3 position,
    Vector3 tangent
  ) {
    var normalizedTangent = Vector3.Normalize(tangent);
    var referenceUp = MathF.Abs(Vector3.Dot(normalizedTangent, Vector3.UnitZ)) < 0.9f
      ? Vector3.UnitZ
      : Vector3.UnitY;
    var right = Vector3.Normalize(Vector3.Cross(referenceUp, normalizedTangent));
    var up = Vector3.Normalize(Vector3.Cross(normalizedTangent, right));
    var matrix = new Matrix4x4(
      normalizedTangent.X, normalizedTangent.Y, normalizedTangent.Z, 0f,
      right.X, right.Y, right.Z, 0f,
      up.X, up.Y, up.Z, 0f,
      0f, 0f, 0f, 1f);
    return new(
      arcLength,
      position,
      normalizedTangent,
      Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix)),
      0f);
  }

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
    }
  }
}
