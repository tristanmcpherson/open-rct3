// Ride Train Operational State Clock
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>Finite, fail-closed primitives for the native ride-train state timer.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> adds the frame
/// delta to <c>StateTime</c> before state-specific update handling at <c>0x00AC7944</c> through
/// <c>0x00AC794E</c>. The concrete state setter at <c>0x00ABE360</c> compares the old and requested
/// states at <c>0x00ABE37B</c>, then clears <c>StateTime</c> and writes the new state at
/// <c>0x00ABE383</c> through <c>0x00ABE38A</c> only when the value changes.
/// </remarks>
internal static class RideTrainOperationalStateClock {
  public static bool TryAdvance(
    int rawState,
    float stateTime,
    float deltaTime,
    out float advancedStateTime
  ) {
    advancedStateTime = stateTime;
    if (!RideTrainOperationalStateCatalog.Resolve(rawState).IsKnown ||
        !float.IsFinite(stateTime) || !float.IsFinite(deltaTime))
      return false;

    // Preserve the native movss/addss operand order and single-precision result.
    var candidate = deltaTime + stateTime;
    if (!float.IsFinite(candidate)) return false;
    advancedStateTime = candidate;
    return true;
  }

  public static bool TryTransition(
    int currentRawState,
    float stateTime,
    int requestedRawState,
    out int resultingRawState,
    out float resultingStateTime
  ) {
    resultingRawState = currentRawState;
    resultingStateTime = stateTime;
    if (!RideTrainOperationalStateCatalog.Resolve(currentRawState).IsKnown ||
        !RideTrainOperationalStateCatalog.Resolve(requestedRawState).IsKnown ||
        !float.IsFinite(stateTime))
      return false;

    if (currentRawState == requestedRawState) return true;
    resultingRawState = requestedRawState;
    resultingStateTime = 0f;
    return true;
  }
}
