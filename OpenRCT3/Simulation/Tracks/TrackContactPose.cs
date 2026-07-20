// Track Contact Pose
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>An immutable, right-handed vehicle-placement frame derived from dual-rail contacts.</summary>
/// <param name="ArcLength">The exact piece-local contact arc length.</param>
/// <param name="Center">The midpoint between the two rail contacts.</param>
/// <param name="Forward">The normalized mean rail-tangent direction.</param>
/// <param name="Right">The normalized left-to-right gauge direction orthogonal to forward.</param>
/// <param name="Up">The normalized right-handed up direction.</param>
/// <param name="Gauge">The double-precision distance between rail contacts.</param>
/// <param name="Orientation">The rotation from local X-forward, Y-right, Z-up coordinates.</param>
/// <param name="Transform">The local-to-world orientation and center translation.</param>
public readonly record struct TrackContactPose(
  float ArcLength,
  Vector3 Center,
  Vector3 Forward,
  Vector3 Right,
  Vector3 Up,
  double Gauge,
  Quaternion Orientation,
  Matrix4x4 Transform
);

/// <summary>Builds a physics-neutral placement pose from one exact dual-rail contact sample.</summary>
/// <remarks>
/// The adapter only establishes the contact center and orthonormal rail frame. It deliberately does
/// not add a vehicle center-of-mass offset, wheelbase, suspension travel, velocity, acceleration, or
/// any other ride-physics assumption.
/// </remarks>
public static class TrackContactPoseAdapter {
  private const double UnitTolerance = 0.001d;
  private const double OrientationForwardTolerance = 0.001d;

  /// <summary>Creates a finite right-handed pose from one exact paired contact sample.</summary>
  public static TrackContactPose Create(TrackContactPoints contacts) {
    ValidateContacts(contacts);

    var center = TrackMath.Midpoint(contacts.Left.Position, contacts.Right.Position);
    var gauge = TrackMath.Distance(contacts.Left.Position, contacts.Right.Position);
    if (!double.IsFinite(gauge) || gauge <= TrackMath.Epsilon)
      throw Invalid("rail contacts do not define a finite, non-zero gauge");

    var leftTangent = TrackMath.Normalize(contacts.Left.Tangent);
    var rightTangent = TrackMath.Normalize(contacts.Right.Tangent);
    if (TrackMath.Dot(leftTangent, rightTangent) <= 0d)
      throw Invalid("rail tangents do not point in the same forward hemisphere");
    var forward = TrackMath.Normalize(TrackMath.Midpoint(leftTangent, rightTangent));

    var gaugeDirection = TrackMath.Direction(
      contacts.Left.Position,
      contacts.Right.Position);
    var projectedRight = gaugeDirection -
      (Convert.ToSingle(TrackMath.Dot(gaugeDirection, forward)) * forward);
    if (!TrackMath.IsFinite(projectedRight)
      || TrackMath.Length(projectedRight) <= TrackMath.Epsilon)
      throw Invalid("rail gauge is parallel to the mean forward tangent");

    var right = TrackMath.Normalize(projectedRight);
    var up = TrackMath.Normalize(Vector3.Cross(forward, right));
    right = TrackMath.Normalize(Vector3.Cross(up, forward));
    if (TrackMath.Dot(right, gaugeDirection) <= 0d)
      throw Invalid("orthonormalization reversed the left-to-right rail direction");

    var rotation = new Matrix4x4(
      forward.X, forward.Y, forward.Z, 0f,
      right.X, right.Y, right.Z, 0f,
      up.X, up.Y, up.Z, 0f,
      0f, 0f, 0f, 1f);
    var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
    var transform = rotation with {
      M41 = center.X,
      M42 = center.Y,
      M43 = center.Z,
    };
    if (!TrackMath.IsFinite(orientation) || !TrackMath.IsFinite(transform))
      throw Invalid("derived orientation or transform is non-finite");

    return new(
      contacts.ArcLength,
      center,
      forward,
      right,
      up,
      gauge,
      orientation,
      transform);
  }

  private static void ValidateContacts(TrackContactPoints contacts) {
    if (!float.IsFinite(contacts.ArcLength) || contacts.ArcLength < 0f)
      throw Invalid("contact arc length is not finite and non-negative");
    ValidateRail(contacts.Left, contacts.ArcLength, "left");
    ValidateRail(contacts.Right, contacts.ArcLength, "right");
  }

  private static void ValidateRail(RailSample sample, float arcLength, string side) {
    if (sample.ArcLength != arcLength)
      throw Invalid($"{side} rail arc length does not match the paired contact");
    if (!TrackMath.IsFinite(sample.Position) || !TrackMath.IsFinite(sample.Tangent))
      throw Invalid($"{side} rail position or tangent is non-finite");
    if (!TrackMath.IsFinite(sample.Orientation) || !float.IsFinite(sample.BankRadians))
      throw Invalid($"{side} rail orientation or bank is non-finite");

    var tangentLength = TrackMath.Length(sample.Tangent);
    if (!double.IsFinite(tangentLength)
      || Math.Abs(tangentLength - 1d) > UnitTolerance)
      throw Invalid($"{side} rail tangent is not a unit direction");
    var orientationLength = QuaternionLength(sample.Orientation);
    if (!double.IsFinite(orientationLength)
      || Math.Abs(orientationLength - 1d) > UnitTolerance)
      throw Invalid($"{side} rail orientation is not a unit quaternion");

    var orientationForward = Vector3.Transform(Vector3.UnitX, sample.Orientation);
    if (!TrackMath.IsFinite(orientationForward)
      || TrackMath.Dot(TrackMath.Normalize(orientationForward), sample.Tangent) <
        1d - OrientationForwardTolerance)
      throw Invalid($"{side} rail orientation does not agree with its tangent");
  }

  private static double QuaternionLength(Quaternion value) => Math.Sqrt(
    (Convert.ToDouble(value.X) * value.X) +
    (Convert.ToDouble(value.Y) * value.Y) +
    (Convert.ToDouble(value.Z) * value.Z) +
    (Convert.ToDouble(value.W) * value.W));

  private static InvalidDataException Invalid(string message) =>
    new($"Track contact pose is malformed or unsupported: {message}.");
}
