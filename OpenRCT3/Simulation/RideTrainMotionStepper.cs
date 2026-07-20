// Ride Train Motion Stepper
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>The native persisted motion fields for one ride train.</summary>
internal readonly record struct RideTrainMotionState(
  float Distance,
  float Speed,
  bool Reversed
);

/// <summary>Advances persisted ride-train distance using the native scalar-float operation order.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> at <c>0x00AC7A78</c> first multiplies speed by the
/// caller's time step and then adds that rounded product to distance. The persisted reversed flag
/// does not negate speed. Native circuit correction follows behind a separate runtime predicate at
/// <c>0x00AC7B24</c>; this pure step intentionally returns the raw, unwrapped distance until that
/// predicate's contract is pinned.
/// </remarks>
internal static class RideTrainMotionStepper {
  internal static RideTrainMotionState Advance(
    RideTrainMotionState state,
    float elapsedSeconds
  ) {
    if (!float.IsFinite(state.Distance))
      throw new ArgumentOutOfRangeException(
        nameof(state),
        "Ride-train distance must be finite.");
    if (!float.IsFinite(state.Speed))
      throw new ArgumentOutOfRangeException(
        nameof(state),
        "Ride-train speed must be finite.");
    if (!float.IsFinite(elapsedSeconds))
      throw new ArgumentOutOfRangeException(
        nameof(elapsedSeconds),
        "Ride-train time step must be finite.");

    var travelDistance = state.Speed * elapsedSeconds;
    if (!float.IsFinite(travelDistance))
      throw new ArgumentOutOfRangeException(
        nameof(elapsedSeconds),
        "Ride-train time step overflows the finite travel distance.");

    var distance = state.Distance + travelDistance;
    if (!float.IsFinite(distance))
      throw new ArgumentOutOfRangeException(
        nameof(state),
        "Ride-train motion overflows the finite saved distance.");

    return new(distance, state.Speed, state.Reversed);
  }
}
