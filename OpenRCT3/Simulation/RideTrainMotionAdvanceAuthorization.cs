// Ride Train Motion Advance Authorization
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>The typed outcome of checking one saved operational state for motion advance.</summary>
internal enum RideTrainMotionAdvanceAuthorizationStatus {
  AuthorizedByNativeOperationalState,
  MissingSavedOperationalState,
  NonFiniteSavedOperationalStateTime,
  UnsupportedSavedOperationalState,
  MalformedRuntimeEntry,
}

/// <summary>Authorization plus the exact runtime and saved state/timer evidence checked.</summary>
internal sealed record RideTrainMotionAdvanceAuthorizationResult(
  RideTrainMotionAdvanceAuthorizationStatus Status,
  RideInstanceTrainRuntimeEntry? TrainRuntime,
  int? SavedOperationalState,
  float? SavedOperationalStateTime
) {
  public bool IsAuthorized =>
    Status == RideTrainMotionAdvanceAuthorizationStatus.AuthorizedByNativeOperationalState;
}

/// <summary>Authorizes only native operational states proven to execute the distance step.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> implements
/// <c>RideTrainSimulation::Update(float)</c> at <c>0x00AC7900</c>. Its exact state comparison chain
/// at <c>0x00AC7A23</c> through <c>0x00AC7A76</c> reaches the distance multiply/add at
/// <c>0x00AC7A78</c> through <c>0x00AC7A88</c> only for the values retained below. State
/// <c>0x33</c> (decimal 51) instead skips to <c>0x00AC9432</c>. Native StateTime accumulation at
/// <c>0x00AC7940</c> through <c>0x00AC794E</c> happens before this gate; this authorization layer
/// preserves the saved timer unchanged and advances neither timer nor motion.
/// </remarks>
internal static class RideTrainMotionAdvanceAuthorization {
  public static RideTrainMotionAdvanceAuthorizationResult Authorize(
    RideInstanceTrainRuntimeEntry? trainRuntime
  ) {
    var train = trainRuntime?.TrainResource?.TrainInstance;
    var hasState = train?.HasSavedOperationalState == true;
    int? state = hasState ? train!.State : null;
    float? stateTime = hasState ? train!.StateTime : null;

    if (!HasExactRuntimeIdentity(trainRuntime))
      return new(
        RideTrainMotionAdvanceAuthorizationStatus.MalformedRuntimeEntry,
        trainRuntime,
        state,
        stateTime);
    if (!hasState)
      return new(
        RideTrainMotionAdvanceAuthorizationStatus.MissingSavedOperationalState,
        trainRuntime,
        null,
        null);
    if (!float.IsFinite(stateTime!.Value))
      return new(
        RideTrainMotionAdvanceAuthorizationStatus.NonFiniteSavedOperationalStateTime,
        trainRuntime,
        state,
        stateTime);
    if (!IsAuthorizedState(state!.Value))
      return new(
        RideTrainMotionAdvanceAuthorizationStatus.UnsupportedSavedOperationalState,
        trainRuntime,
        state,
        stateTime);
    return new(
      RideTrainMotionAdvanceAuthorizationStatus.AuthorizedByNativeOperationalState,
      trainRuntime,
      state,
      stateTime);
  }

  private static bool HasExactRuntimeIdentity(
    RideInstanceTrainRuntimeEntry? runtime
  ) {
    if (runtime == null || runtime.SavedTrainIndex < 0 || runtime.TrackRuntime == null ||
        runtime.TrainResource == null || runtime.TrainResource.RideInstance == null ||
        runtime.TrainResource.TrainInstance == null ||
        runtime.TrackRuntime.Identity == null || runtime.TrackRuntime.Geometry == null ||
        runtime.TrackRuntime.Identity.Instance == null ||
        runtime.TrackRuntime.Identity.Track == null ||
        runtime.TrackRuntime.Geometry.Track == null)
      return false;

    var link = runtime.TrainResource;
    var train = link.TrainInstance;
    var ride = link.RideInstance;
    var track = runtime.TrackRuntime;
    if (!ReferenceEquals(track.Identity.Track, track.Geometry.Track) ||
        !ReferenceEquals(track.Instance, ride) || runtime.RideInstanceEntryId == 0 ||
        runtime.TrackEntryId == 0 || runtime.TrainInstanceEntryId == 0 ||
        link.Ordinal < 0 || link.Ordinal >= ride.Trains.Count ||
        link.Ordinal != train.WhichTrain || ride.NTrains != ride.Trains.Count ||
        ride.Trains[link.Ordinal] != train.EntryId ||
        train.TrackedRideInstance != ride.EntryId ||
        track.InstanceEntryId != ride.EntryId ||
        ride.Track != track.TrackEntryId ||
        track.TrackEntryId != track.Track.SourceEntryId ||
        track.Track.TrackedRideInstanceReference != ride.EntryId)
      return false;
    return true;
  }

  private static bool IsAuthorizedState(int state) => state is
    10 or 13 or 15 or 19 or 22 or 30 or 34 or 36 or 40 or
    44 or 45 or 52 or 55 or 59 or 61 or 69 or 70;
}
