// Ride Train Circuit Motion Authorization
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The evidence-backed outcome of authorizing one train for closed-circuit motion.</summary>
internal enum RideTrainCircuitMotionAuthorizationStatus {
  AuthorizedByReciprocalCircuit,
  UnprovenEndpointFallback,
  MalformedRuntimeEntry,
  ForeignTrackRuntime,
  ForeignTrackIdentity,
  MalformedReciprocalCircuit,
  UnresolvedCircuitTraversal,
  ChangedCircuitTraversalIdentity,
}

/// <summary>One authorization result plus the exact traversal authorized for use.</summary>
internal sealed record RideTrainCircuitMotionAuthorizationResult(
  RideTrainCircuitMotionAuthorizationStatus Status,
  TrackCircuitTraversal? Traversal
) {
  public bool IsAuthorized =>
    Status == RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit;
}

/// <summary>
/// Authorizes only the proven reciprocal-link branch of Complete Edition's circuit predicate.
/// </summary>
/// <remarks>
/// Native <c>C51830</c> first calls <c>C517B0</c>, whose proven branch requires a first track
/// piece with a reciprocal previous link. <see cref="RideTrack.IsCircuit"/> is the corresponding
/// link-derived semantic fact in this runtime; the serialized circuit flag is not evidence.
///
/// The native OR fallback remains deliberately unimplemented. The meanings of
/// <c>[TrackedRideInstance+0x6F4]-&gt;+0xBC</c> and endpoint
/// <c>[SIDDatabaseEntry+0x64] == 7</c> have not been decoded. When the proven branch is false or
/// unknown, this layer returns <see
/// cref="RideTrainCircuitMotionAuthorizationStatus.UnprovenEndpointFallback"/> rather than
/// rejecting motion or guessing at those fields.
/// </remarks>
internal static class RideTrainCircuitMotionAuthorization {
  public static RideTrainCircuitMotionAuthorizationResult Authorize(
    RideInstanceTrainRuntimeEntry? trainRuntime,
    RideInstanceTrackRuntimeEntry? trackRuntime
  ) {
    if (!HasCompleteRuntime(trainRuntime, trackRuntime))
      return Result(RideTrainCircuitMotionAuthorizationStatus.MalformedRuntimeEntry);

    if (!ReferenceEquals(trainRuntime!.TrackRuntime, trackRuntime))
      return Result(RideTrainCircuitMotionAuthorizationStatus.ForeignTrackRuntime);

    if (!HasExactSavedIdentity(trainRuntime, trackRuntime!))
      return Result(RideTrainCircuitMotionAuthorizationStatus.ForeignTrackIdentity);

    if (trackRuntime!.Track.IsCircuit != true)
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnprovenEndpointFallback);

    if (!HasAuthoritativeCircuitTopology(trackRuntime.Track))
      return Result(RideTrainCircuitMotionAuthorizationStatus.MalformedReciprocalCircuit);

    if (trackRuntime.Status != RideTrackGeometryStatus.Circuit ||
        trackRuntime.Circuit is null ||
        trackRuntime.CircuitTraversal is null ||
        trackRuntime.Graph is not null ||
        trackRuntime.GraphTraversal is not null)
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal);

    if (!ReferenceEquals(trackRuntime.CircuitTraversal.Circuit, trackRuntime.Circuit) ||
        !CircuitMatchesAuthoritativeOrder(trackRuntime.Track, trackRuntime.Circuit))
      return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);

    return new(
      RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit,
      trackRuntime.CircuitTraversal);
  }

  private static bool HasCompleteRuntime(
    RideInstanceTrainRuntimeEntry? trainRuntime,
    RideInstanceTrackRuntimeEntry? trackRuntime
  ) => trainRuntime is not null &&
    trackRuntime is not null &&
    trainRuntime.TrackRuntime is not null &&
    trainRuntime.TrainResource is not null &&
    trainRuntime.TrainResource.RideInstance is not null &&
    trainRuntime.TrainResource.TrainInstance is not null &&
    trackRuntime.Identity is not null &&
    trackRuntime.Identity.Instance is not null &&
    trackRuntime.Identity.Track is not null &&
    trackRuntime.Geometry is not null &&
    trackRuntime.Geometry.Track is not null;

  private static bool HasExactSavedIdentity(
    RideInstanceTrainRuntimeEntry trainRuntime,
    RideInstanceTrackRuntimeEntry trackRuntime
  ) {
    var link = trainRuntime.TrainResource;
    var ride = link.RideInstance;
    var train = link.TrainInstance;
    return ReferenceEquals(trackRuntime.Identity.Track, trackRuntime.Geometry.Track) &&
      ReferenceEquals(ride, trackRuntime.Instance) &&
      trainRuntime.RideInstanceEntryId != 0 &&
      trainRuntime.TrackEntryId != 0 &&
      trainRuntime.TrainInstanceEntryId != 0 &&
      link.Ordinal >= 0 && link.Ordinal < ride.Trains.Count &&
      link.Ordinal == train.WhichTrain &&
      ride.NTrains == ride.Trains.Count &&
      ride.Trains[link.Ordinal] == train.EntryId &&
      trackRuntime.Instance.Track == trackRuntime.TrackEntryId &&
      trackRuntime.Track.SourceEntryId == trackRuntime.TrackEntryId &&
      trackRuntime.Track.TrackedRideInstanceReference == trackRuntime.InstanceEntryId &&
      train.TrackedRideInstance == trackRuntime.InstanceEntryId;
  }

  private static bool HasAuthoritativeCircuitTopology(RideTrack track) =>
    track.HasAuthoritativeTrackPieceOrder &&
    track.FirstSegmentSourceEntryId != 0 &&
    track.LastSegmentSourceEntryId != 0 &&
    track.SegmentSourceEntryIds.Count > 0 &&
    track.TrackPieceSourceEntryIds.Count > 0 &&
    !track.SegmentSourceEntryIds.Any(id => id == 0) &&
    !track.TrackPieceSourceEntryIds.Any(id => id == 0) &&
    track.SegmentSourceEntryIds.Contains(track.FirstSegmentSourceEntryId) &&
    track.SegmentSourceEntryIds.Contains(track.LastSegmentSourceEntryId);

  private static bool CircuitMatchesAuthoritativeOrder(
    RideTrack track,
    TrackCircuit circuit
  ) {
    if (circuit.Pieces.Count != track.TrackPieceSourceEntryIds.Count) return false;
    foreach (var index in Enumerable.Range(0, circuit.Pieces.Count)) {
      var expectedId = $"track-piece-{track.TrackPieceSourceEntryIds[index]}";
      if (!string.Equals(circuit.Pieces[index].Id, expectedId, StringComparison.Ordinal))
        return false;
    }
    return true;
  }

  private static RideTrainCircuitMotionAuthorizationResult Result(
    RideTrainCircuitMotionAuthorizationStatus status
  ) => new(status, null);
}
