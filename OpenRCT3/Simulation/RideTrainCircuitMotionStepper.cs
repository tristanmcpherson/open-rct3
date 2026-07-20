// Ride Train Circuit Motion Stepper
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>Advances one native ride-train motion state on an authorized circuit.</summary>
/// <remarks>
/// <see cref="RideTrainMotionStepper"/> preserves the scalar-float multiply and add at
/// <c>0x00AC7A78</c> through <c>0x00AC7A88</c>. Once the caller has established the native track
/// predicate at <c>0x00AC7B24</c>, Complete Edition corrects the resulting distance at
/// <c>0x00AC7B36</c> through <c>0x00AC7B57</c> with at most one upper subtraction or lower
/// addition. This pure layer does not infer that predicate or any operational state.
/// </remarks>
internal static class RideTrainCircuitMotionStepper {
  internal static RideTrainMotionState Advance(
    RideTrainMotionState state,
    float elapsedSeconds,
    float circuitLength
  ) {
    if (!float.IsFinite(circuitLength) || circuitLength <= 0f)
      throw new ArgumentOutOfRangeException(
        nameof(circuitLength),
        "Ride-train circuit length must be finite and positive.");

    var advanced = RideTrainMotionStepper.Advance(state, elapsedSeconds);
    var distance = advanced.Distance;
    if (distance >= circuitLength) distance -= circuitLength;
    else if (distance < 0f) distance += circuitLength;

    if (!float.IsFinite(distance) || distance < 0f || distance >= circuitLength)
      throw new InvalidDataException(
        "Ride-train distance is outside the circuit after one native correction.");
    return advanced with { Distance = distance };
  }
}
