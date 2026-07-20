// Ride Car Geometry Adapter
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Authoritative model-space longitudinal geometry for one ride-car body.</summary>
public sealed record RideCarLongitudinalGeometry(
  Vector3 CarFrontPosition,
  Vector3 CarRearPosition,
  Vector3 FrontWheelCenterPosition,
  Vector3 RearWheelCenterPosition,
  Vector3 LongitudinalAxis,
  float CarLength,
  float FrontWheelCenterLongitudinalPosition,
  float RearWheelCenterLongitudinalPosition,
  float Wheelbase,
  float FrontWheelSpan,
  float RearWheelSpan
);

/// <summary>Derives ride-car longitudinal geometry from the body visual's named BSH bones.</summary>
/// <remarks>
/// The pinned
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/boneshape.h">
/// BoneShape layout</see> stores two matrices parallel to the named bone array, and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerBSH.cpp">
/// ManagerBSH</see> writes those arrays independently. RCT3's ride-car setup reads the absolute
/// model-space translation from the second matrix. Complete Edition <c>RCT3.exe</c> resolves bone
/// names through its shape lookup at <c>0x00A62EE0</c>; installed stock bodies prove that lookup is
/// case-insensitive. The direct-body path averages <c>WheelFR</c>/<c>WheelFL</c> and
/// <c>WheelRR</c>/<c>WheelRL</c> at <c>0x00A525BB</c> and <c>0x00A52811</c>. Separate axle visuals
/// instead add their <c>AxleF</c>/<c>AxleR</c> (or <c>AxelF</c>/<c>AxelR</c>) parent transforms to
/// wheel geometry owned by another shape, so those anchors cannot substitute for a missing body
/// wheel pair here. This adapter exposes model-space visual geometry only; it does not infer train
/// spacing or runtime track-contact distances.
/// </remarks>
public static class RideCarGeometryAdapter {
  private const int MaximumBoneCount = 64 * 1024;
  private const float MinimumLongitudinalLength = 0.000001f;
  private const string CarFrontName = "CarFront";
  private const string CarRearName = "CarRear";
  private const string FrontRightWheelName = "WheelFR";
  private const string FrontLeftWheelName = "WheelFL";
  private const string RearRightWheelName = "WheelRR";
  private const string RearLeftWheelName = "WheelRL";

  /// <summary>
  /// Resolves native body and paired wheel marker bones, then projects the wheel centers onto the
  /// rear-to-front model axis. Projections retain the BSH model origin.
  /// </summary>
  public static RideCarLongitudinalGeometry CreateLongitudinalGeometry(BoneShape bodyShape) {
    ArgumentNullException.ThrowIfNull(bodyShape);
    if (bodyShape.Bones == null)
      throw Invalid(bodyShape, "has a null bone list");

    var boneCount = bodyShape.Bones.Count;
    if (boneCount is < 0 or > MaximumBoneCount)
      throw Invalid(
        bodyShape,
        $"bone count {boneCount} is outside the supported range 0 through {MaximumBoneCount}");

    BoneShapeBone? carFront = null;
    BoneShapeBone? carRear = null;
    BoneShapeBone? frontRightWheel = null;
    BoneShapeBone? frontLeftWheel = null;
    BoneShapeBone? rearRightWheel = null;
    BoneShapeBone? rearLeftWheel = null;

    for (var index = 0; index < boneCount; index++) {
      var bone = bodyShape.Bones[index];
      if (bone == null)
        throw Invalid(bodyShape, $"bone {index} is null");
      if (string.IsNullOrWhiteSpace(bone.Name))
        throw Invalid(bodyShape, $"bone {index} has no name");

      // Native lookup returns the first case-insensitive match; preserve that deterministic order.
      if (Matches(bone.Name, CarFrontName)) carFront ??= bone;
      else if (Matches(bone.Name, CarRearName)) carRear ??= bone;
      else if (Matches(bone.Name, FrontRightWheelName)) frontRightWheel ??= bone;
      else if (Matches(bone.Name, FrontLeftWheelName)) frontLeftWheel ??= bone;
      else if (Matches(bone.Name, RearRightWheelName)) rearRightWheel ??= bone;
      else if (Matches(bone.Name, RearLeftWheelName)) rearLeftWheel ??= bone;
    }

    carFront = Require(bodyShape, carFront, CarFrontName);
    carRear = Require(bodyShape, carRear, CarRearName);
    var frontWheels = ResolveWheelPair(
      bodyShape,
      "front",
      FrontRightWheelName,
      frontRightWheel,
      FrontLeftWheelName,
      frontLeftWheel);
    var rearWheels = ResolveWheelPair(
      bodyShape,
      "rear",
      RearRightWheelName,
      rearRightWheel,
      RearLeftWheelName,
      rearLeftWheel);

    var carFrontPosition = ReadPosition(bodyShape, carFront);
    var carRearPosition = ReadPosition(bodyShape, carRear);
    var (axis, carLength) = CreateAxis(bodyShape, carRearPosition, carFrontPosition);
    var frontProjection = Dot(frontWheels.Center, axis);
    var rearProjection = Dot(rearWheels.Center, axis);
    var wheelbase = Math.Abs(frontProjection - rearProjection);
    if (!double.IsFinite(wheelbase) || wheelbase <= MinimumLongitudinalLength)
      throw Invalid(bodyShape, "front and rear wheel centers have a degenerate wheelbase");

    return new RideCarLongitudinalGeometry(
      carFrontPosition,
      carRearPosition,
      frontWheels.Center,
      rearWheels.Center,
      axis,
      carLength,
      ToFiniteSingle(bodyShape, frontProjection, "front wheel-center projection"),
      ToFiniteSingle(bodyShape, rearProjection, "rear wheel-center projection"),
      ToFiniteSingle(bodyShape, wheelbase, "wheelbase"),
      frontWheels.Span,
      rearWheels.Span);
  }

  private static (Vector3 Center, float Span) ResolveWheelPair(
    BoneShape shape,
    string role,
    string rightName,
    BoneShapeBone? right,
    string leftName,
    BoneShapeBone? left
  ) {
    right = Require(shape, right, rightName);
    left = Require(shape, left, leftName);
    var rightPosition = ReadPosition(shape, right);
    var leftPosition = ReadPosition(shape, left);
    var center = Average(shape, rightPosition, leftPosition, $"{role} wheel center");
    var span = Distance(shape, rightPosition, leftPosition, $"{role} wheel span");
    if (span <= MinimumLongitudinalLength)
      throw Invalid(shape, $"{role} wheel markers define a degenerate span");
    return (center, span);
  }

  private static BoneShapeBone Require(
    BoneShape shape,
    BoneShapeBone? bone,
    string name
  ) => bone ?? throw Invalid(shape, $"is missing required '{name}' bone");

  private static Vector3 Average(
    BoneShape shape,
    Vector3 first,
    Vector3 second,
    string description
  ) => new(
    ToFiniteSingle(shape, (Convert.ToDouble(first.X) + second.X) * 0.5, description),
    ToFiniteSingle(shape, (Convert.ToDouble(first.Y) + second.Y) * 0.5, description),
    ToFiniteSingle(shape, (Convert.ToDouble(first.Z) + second.Z) * 0.5, description));

  private static float Distance(
    BoneShape shape,
    Vector3 first,
    Vector3 second,
    string description
  ) {
    var x = Convert.ToDouble(first.X) - second.X;
    var y = Convert.ToDouble(first.Y) - second.Y;
    var z = Convert.ToDouble(first.Z) - second.Z;
    return ToFiniteSingle(shape, Math.Sqrt((x * x) + (y * y) + (z * z)), description);
  }

  private static Vector3 ReadPosition(BoneShape shape, BoneShapeBone bone) {
    if (!IsFinite(bone.Position2))
      throw Invalid(shape, $"bone '{bone.Name}' position2 matrix contains a non-finite value");
    var native = bone.Position2.Translation;
    var position = new Vector3(native.X, native.Z, native.Y);
    if (!IsFinite(position))
      throw Invalid(shape, $"bone '{bone.Name}' position is non-finite after axis conversion");
    return position;
  }

  private static (Vector3 Axis, float Length) CreateAxis(
    BoneShape shape,
    Vector3 rear,
    Vector3 front
  ) {
    var x = Convert.ToDouble(front.X) - rear.X;
    var y = Convert.ToDouble(front.Y) - rear.Y;
    var z = Convert.ToDouble(front.Z) - rear.Z;
    var length = Math.Sqrt((x * x) + (y * y) + (z * z));
    if (!double.IsFinite(length) || length <= MinimumLongitudinalLength)
      throw Invalid(shape, "'CarFront' and 'CarRear' bones define a degenerate longitudinal axis");

    var axis = new Vector3(
      Convert.ToSingle(x / length),
      Convert.ToSingle(y / length),
      Convert.ToSingle(z / length));
    if (!IsFinite(axis))
      throw Invalid(shape, "longitudinal axis is non-finite after normalization");
    return (axis, ToFiniteSingle(shape, length, "car length"));
  }

  private static double Dot(Vector3 left, Vector3 right) =>
    (Convert.ToDouble(left.X) * right.X) +
    (Convert.ToDouble(left.Y) * right.Y) +
    (Convert.ToDouble(left.Z) * right.Z);

  private static float ToFiniteSingle(BoneShape shape, double value, string description) {
    if (!double.IsFinite(value) || value > float.MaxValue || value < float.MinValue)
      throw Invalid(shape, $"{description} is outside the finite single-precision range");
    return Convert.ToSingle(value);
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static bool Matches(string actual, string expected) =>
    string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

  private static InvalidDataException Invalid(BoneShape shape, string message) =>
    new($"Ride-car body bone shape '{shape.Name}' {message}.");
}
