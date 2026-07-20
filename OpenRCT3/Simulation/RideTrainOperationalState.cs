// Ride Train Operational State
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>Descriptive overlay for the native ride-train operational-state values.</summary>
/// <remarks>
/// Runtime and serialized state remains an <see cref="int"/>. This enum only names raw values that
/// the native executable proves, so callers cannot accidentally coerce an unknown value into a
/// valid state.
/// </remarks>
internal enum RideTrainOperationalState {
  WaitingToLoad = 0,
  Loading = 1,
  LoadingReadyToGo = 2,
  RestraintsClosing = 3,
  DoorsClosing = 4,
  WaitingToGoAtStation = 5,
  WaitingToGoAtStationSyncOtherTrains = 6,
  WaitingToGoAtStationSyncAdjacentStations = 7,
  WaitingForCableLiftAtStation = 8,
  XxxxEngagingCableLift = 9,
  AscendingCableLift = 10,
  DisengagingCableLift = 11,
  WaitingToGoAtCableLift = 12,
  TravelToNextStation = 13,
  WaitingToGoAtLiftHill = 14,
  StoppingAtBlockBrake = 15,
  WaitingToGoAtBlockBrake = 16,
  WaitingForCableLiftAtBlockBrake = 17,
  WaitingAtTop = 18,
  StoppingOnHoldingPiece = 19,
  StoppedOnHoldingPiece = 20,
  WaitingOnHoldingPiece = 21,
  StoppingAtStation = 22,
  StoppedAtStation = 23,
  AligningAtStation = 24,
  WaitingToUnloadAtStation = 25,
  WaitingToUnloadAtStationSyncOtherTrains = 26,
  DoorsOpening = 27,
  RestraintsOpening = 28,
  Unloading = 29,
  StoppingAtThrillLift = 30,
  AscendingThrillLift = 31,
  WaitingToGoAtThrillLift = 32,
  CableLiftIdle = 33,
  CableLiftDescending = 34,
  CableLiftEngaging = 35,
  CableLiftAscending = 36,
  CableLiftDisengaging = 37,
  ThrillLiftIdle = 38,
  ThrillLiftEngaging = 39,
  ThrillLiftAscending = 40,
  ThrillLiftWaitingAtTop = 41,
  ThrillLiftDisengaging = 42,
  ThrillLiftTrainLeaving = 43,
  ThrillLiftDescending = 44,
  Crashing = 45,
  OffTrack = 46,
  OffTrackReturning = 47,
  OffTrackReturnedAligning = 48,
  OffTrackReturnedEntering = 49,
  ThrillLiftEntering = 50,
  Crashed = 51,
  SlowingAtBlockBrake = 52,
  WaitingForReverseCableLiftAtStation = 53,
  XxxxEngagingReverseCableLift = 54,
  AscendingReverseCableLift = 55,
  DisengagingReverseCableLift = 56,
  WaitingToGoAtReverseCableLift = 57,
  ReverseCableLiftIdle = 58,
  ReverseCableLiftDescending = 59,
  ReverseCableLiftEngaging = 60,
  ReverseCableLiftAscending = 61,
  ReverseCableLiftDisengaging = 62,
  WaitingToAscendCableLiftSyncAdjacentStations = 63,
  CableLiftWaitingToAscend = 64,
  WaitingToAscendReverseCableLiftSyncAdjacentStations = 65,
  ReverseCableLiftWaitingToAscend = 66,
  FillingWithWater = 67,
  OffTrackHeldInvisible = 68,
  BadState = 69,
  SlowingForObstruction = 70,
}

/// <summary>A raw state plus its optional proven native interpretation.</summary>
internal readonly record struct RideTrainOperationalStateResolution(
  int RawValue,
  RideTrainOperationalState? KnownState,
  string? NativeName
) {
  public bool IsKnown => KnownState.HasValue;
}

/// <summary>Resolves only operational-state values named by the native executable.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> maps raw states
/// <c>0</c> through <c>70</c> to these names in the switch at <c>0x00AAFBB0</c>, using the jump
/// table at <c>0x00AAFD6C</c> and return stubs at <c>0x00AAFBC0</c> through
/// <c>0x00AAFD64</c>. The value <c>69</c> deliberately maps to the native text
/// <c>BAD STATE</c>; values outside the proven range remain unknown.
/// </remarks>
internal static class RideTrainOperationalStateCatalog {
  public const int MinimumKnownRawValue = 0;
  public const int MaximumKnownRawValue = 70;

  private static readonly RideTrainOperationalState[] KnownStates =
    Enum.GetValues<RideTrainOperationalState>();

  private static readonly string[] NativeNames = [
    "StateWaitingToLoad",
    "StateLoading",
    "StateLoadingReadyToGo",
    "StateRestraintsClosing",
    "StateDoorsClosing",
    "StateWaitingToGoAtStation",
    "StateWaitingToGoAtStationSyncOtherTrains",
    "StateWaitingToGoAtStationSyncAdjacentStations",
    "StateWaitingForCableLiftAtStation",
    "XXXXStateEngagingCableLift",
    "StateAscendingCableLift",
    "StateDisengagingCableLift",
    "StateWaitingToGoAtCableLift",
    "StateTravelToNextStation",
    "StateWaitingToGoAtLiftHill",
    "StateStoppingAtBlockBrake",
    "StateWaitingToGoAtBlockBrake",
    "StateWaitingForCableLiftAtBlockBrake",
    "StateWaitingAtTop",
    "StateStoppingOnHoldingPiece",
    "StateStoppedOnHoldingPiece",
    "StateWaitingOnHoldingPiece",
    "StateStoppingAtStation",
    "StateStoppedAtStation",
    "StateAligningAtStation",
    "StateWaitingToUnloadAtStation",
    "StateWaitingToUnloadAtStationSyncOtherTrains",
    "StateDoorsOpening",
    "StateRestraintsOpening",
    "StateUnloading",
    "StateStoppingAtThrillLift",
    "StateAscendingThrillLift",
    "StateWaitingToGoAtThrillLift",
    "StateCableLiftIdle",
    "StateCableLiftDescending",
    "StateCableLiftEngaging",
    "StateCableLiftAscending",
    "StateCableLiftDisengaging",
    "StateThrillLiftIdle",
    "StateThrillLiftEngaging",
    "StateThrillLiftAscending",
    "StateThrillLiftWaitingAtTop",
    "StateThrillLiftDisengaging",
    "StateThrillLiftTrainLeaving",
    "StateThrillLiftDescending",
    "StateCrashing",
    "StateOffTrack",
    "StateOffTrackReturning",
    "StateOffTrackReturnedAligning",
    "StateOffTrackReturnedEntering",
    "StateThrillLiftEntering",
    "StateCrashed",
    "StateSlowingAtBlockBrake",
    "StateWaitingForReverseCableLiftAtStation",
    "XXXXStateEngagingReverseCableLift",
    "StateAscendingReverseCableLift",
    "StateDisengagingReverseCableLift",
    "StateWaitingToGoAtReverseCableLift",
    "StateReverseCableLiftIdle",
    "StateReverseCableLiftDescending",
    "StateReverseCableLiftEngaging",
    "StateReverseCableLiftAscending",
    "StateReverseCableLiftDisengaging",
    "StateWaitingToAscendCableLiftSyncAdjacentStations",
    "StateCableLiftWaitingToAscend",
    "StateWaitingToAscendReverseCableLiftSyncAdjacentStations",
    "StateReverseCableLiftWaitingToAscend",
    "StateFillingWithWater",
    "StateOffTrackHeldInvisible",
    "BAD STATE",
    "StateSlowingForObstruction",
  ];

  public static RideTrainOperationalStateResolution Resolve(int rawValue) {
    if (rawValue < MinimumKnownRawValue || rawValue > MaximumKnownRawValue ||
        rawValue >= KnownStates.Length || rawValue >= NativeNames.Length)
      return new(rawValue, null, null);

    var state = KnownStates[rawValue];
    if (Convert.ToInt32(state) != rawValue)
      return new(rawValue, null, null);
    return new(rawValue, state, NativeNames[rawValue]);
  }
}
