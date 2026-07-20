// Ride Car Ordinary Contact Distance Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>Native ordinary-mode front and rear track-contact distances for one ride car.</summary>
internal sealed record RideCarOrdinaryContactDistances(
  float FrontDistance,
  float RearDistance
);

/// <summary>
/// Resolves ordinary-mode ride-car contact distances without advancing train state.
/// </summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> computes these signed offsets at <c>0x00A55DF8</c> through
/// <c>0x00A5640A</c>. Circuit correction at <c>0x00A55E8E</c> and <c>0x00A56412</c>
/// performs at most one subtraction or addition. Inputs requiring more correction are outside the
/// native caller's precondition and fail closed here instead of being silently modulo-wrapped.
/// </remarks>
internal static class RideCarOrdinaryContactDistanceResolver {
  internal static RideCarOrdinaryContactDistances Resolve(
    float baseDistance,
    RideCarLongitudinalGeometry geometry,
    bool reversed,
    bool hasRearGeometry,
    float? circuitLength
  ) {
    ArgumentNullException.ThrowIfNull(geometry);
    if (!float.IsFinite(baseDistance))
      throw new ArgumentOutOfRangeException(
        nameof(baseDistance),
        "Ordinary ride-car base distance must be finite.");

    var frontOffset = reversed
      ? geometry.FrontWheelCenterOffsetFromCarRear
      : geometry.FrontWheelCenterOffsetFromCarFront;
    if (!float.IsFinite(frontOffset))
      throw Invalid("front wheel-center offset is non-finite");

    var frontDistance = reversed
      ? baseDistance - frontOffset
      : baseDistance + frontOffset;
    if (!float.IsFinite(frontDistance))
      throw Invalid("front contact distance is non-finite");

    var rearDistance = frontDistance;
    if (hasRearGeometry) {
      var rearOffset = geometry.RearWheelCenterOffsetFromFrontWheelCenter;
      if (!float.IsFinite(rearOffset))
        throw Invalid("rear-minus-front wheel-center offset is non-finite");
      rearDistance = frontDistance + (rearOffset * (reversed ? -1f : 1f));
      if (!float.IsFinite(rearDistance))
        throw Invalid("rear contact distance is non-finite");
    }

    if (circuitLength.HasValue) {
      ValidateCircuitLength(circuitLength.Value);
      frontDistance = WrapCircuitOnce(frontDistance, circuitLength.Value, "front");
      rearDistance = WrapCircuitOnce(rearDistance, circuitLength.Value, "rear");
    }

    return new(frontDistance, rearDistance);
  }

  private static float WrapCircuitOnce(float distance, float circuitLength, string role) {
    var length = Convert.ToDouble(circuitLength);
    var unwrapped = Convert.ToDouble(distance);
    if (unwrapped < -length || unwrapped >= 2d * length)
      throw Invalid(
        $"{role} contact distance {distance:R} requires more than one circuit correction");

    if (distance >= circuitLength) distance -= circuitLength;
    else if (distance < 0f) distance += circuitLength;
    if (!float.IsFinite(distance) || distance < 0f || distance >= circuitLength)
      throw Invalid($"{role} contact distance is outside the circuit after one correction");
    return distance;
  }

  private static void ValidateCircuitLength(float circuitLength) {
    if (!float.IsFinite(circuitLength) || circuitLength <= 0f)
      throw new ArgumentOutOfRangeException(
        nameof(circuitLength),
        "Ordinary ride-car circuit length must be finite and positive.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot resolve ordinary ride-car contact distances: {message}.");
}
