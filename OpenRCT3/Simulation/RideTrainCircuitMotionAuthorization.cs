// Ride Train Circuit Motion Authorization
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
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
  UnresolvedSavedCursorIdentity,
  CrossCircuitSavedConsist,
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
  ) => Authorize(trainRuntime, trackRuntime, renderedCars: null);

  public static RideTrainCircuitMotionAuthorizationResult Authorize(
    RideInstanceTrainRuntimeEntry? trainRuntime,
    RideInstanceTrackRuntimeEntry? trackRuntime,
    IReadOnlyList<RideCarStaticInstanceEntry>? renderedCars
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

    return trackRuntime.Status switch {
      RideTrackGeometryStatus.Circuit => AuthorizeSingularCircuit(trackRuntime),
      RideTrackGeometryStatus.MultiCircuit => renderedCars is null
        ? Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal)
        : AuthorizeSegmentCircuit(trainRuntime, trackRuntime, renderedCars),
      _ => Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal),
    };
  }

  private static RideTrainCircuitMotionAuthorizationResult AuthorizeSingularCircuit(
    RideInstanceTrackRuntimeEntry trackRuntime
  ) {
    if (trackRuntime.Circuit is null || trackRuntime.CircuitTraversal is null ||
        trackRuntime.Graph is not null || trackRuntime.GraphTraversal is not null)
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal);

    if (!ReferenceEquals(trackRuntime.CircuitTraversal.Circuit, trackRuntime.Circuit) ||
        !CircuitMatchesAuthoritativeOrder(trackRuntime.Track, trackRuntime.Circuit))
      return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);

    return Authorized(trackRuntime.CircuitTraversal);
  }

  private static RideTrainCircuitMotionAuthorizationResult AuthorizeSegmentCircuit(
    RideInstanceTrainRuntimeEntry trainRuntime,
    RideInstanceTrackRuntimeEntry trackRuntime,
    IReadOnlyList<RideCarStaticInstanceEntry> renderedCars
  ) {
    if (trackRuntime.Circuit is not null || trackRuntime.CircuitTraversal is not null ||
        trackRuntime.Graph is not null || trackRuntime.GraphTraversal is not null ||
        trackRuntime.SegmentCircuitTraversals is null ||
        trackRuntime.SegmentCircuitTraversals.Count < 2)
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal);

    if (!SegmentCircuitsMatchAuthoritativeOrder(trackRuntime))
      return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);

    var savedCars = trainRuntime.TrainResource.TrainInstance.Cars;
    if (renderedCars.Count == 0 || renderedCars.Count != savedCars.Count)
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity);

    var occupiedOrdinals = new bool[savedCars.Count];
    int? selectedCircuitIndex = null;
    foreach (var entry in renderedCars) {
      if (entry is null || entry.CarRuntime is null || entry.SavedCursor is null ||
          !entry.IsResolved || entry.RegistryIndex != entry.CarRuntime.RegistryIndex ||
          entry.SavedCursor.RegistryIndex != entry.CarRuntime.RegistryIndex ||
          !ReferenceEquals(entry.SavedCursor.CarRuntime, entry.CarRuntime) ||
          !ReferenceEquals(entry.CarRuntime.TrainRuntime, trainRuntime))
        return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity);

      var ordinal = entry.CarRuntime.WhichCar;
      if (ordinal < 0 || ordinal >= savedCars.Count || occupiedOrdinals[ordinal] ||
          savedCars[ordinal] != entry.CarInstanceEntryId)
        return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity);
      occupiedOrdinals[ordinal] = true;

      var front = entry.CarRuntime.TrackPiece;
      var rear = entry.CarRuntime.RearTrackPiece;
      if (!HasCompleteCircuitIdentity(front) || !HasCompleteCircuitIdentity(rear))
        return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity);
      if (front.CircuitIndex != rear.CircuitIndex)
        return Result(RideTrainCircuitMotionAuthorizationStatus.CrossCircuitSavedConsist);

      var circuitIndex = front.CircuitIndex!.Value;
      if (selectedCircuitIndex is { } priorIndex && priorIndex != circuitIndex)
        return Result(RideTrainCircuitMotionAuthorizationStatus.CrossCircuitSavedConsist);
      if (circuitIndex < 0 ||
          circuitIndex >= trackRuntime.SegmentCircuitTraversals.Count)
        return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);

      var segment = trackRuntime.SegmentCircuitTraversals[circuitIndex];
      if (front.SegmentSourceEntryId != segment.SegmentSourceEntryId ||
          rear.SegmentSourceEntryId != segment.SegmentSourceEntryId)
        return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);
      if (!ContactMatchesTraversal(entry.SavedCursor.Front, front, segment.Traversal) ||
          !ContactMatchesTraversal(entry.SavedCursor.Rear, rear, segment.Traversal))
        return Result(RideTrainCircuitMotionAuthorizationStatus.ChangedCircuitTraversalIdentity);

      selectedCircuitIndex = circuitIndex;
    }

    if (selectedCircuitIndex is not { } selected ||
        occupiedOrdinals.Any(occupied => !occupied))
      return Result(RideTrainCircuitMotionAuthorizationStatus.UnresolvedSavedCursorIdentity);
    return Authorized(trackRuntime.SegmentCircuitTraversals[selected].Traversal);
  }

  private static bool HasCompleteCircuitIdentity(RideCarTrackPieceRuntimeLink? link) =>
    link is not null && link.IsResolved && link.PieceIndex is not null &&
    link.Piece is not null && link.CircuitIndex is not null &&
    link.SegmentSourceEntryId is not null;

  private static bool ContactMatchesTraversal(
    RideCarSavedWheelContactCursor? contact,
    RideCarTrackPieceRuntimeLink link,
    TrackCircuitTraversal traversal
  ) {
    if (contact?.IsResolved != true || contact.TrackPieceData is null ||
        contact.Cursor is not { } cursor ||
        contact.SavedTrackPieceEntryId != link.SavedTrackPieceEntryId ||
        contact.TrackPieceData.EntryId != link.SavedTrackPieceEntryId ||
        !ReferenceEquals(cursor.Traversal, traversal) ||
        cursor.PieceIndex != link.PieceIndex ||
        !ReferenceEquals(cursor.CircuitPiece.Piece, link.Piece))
      return false;
    return true;
  }

  private static bool SegmentCircuitsMatchAuthoritativeOrder(
    RideInstanceTrackRuntimeEntry trackRuntime
  ) {
    var track = trackRuntime.Track;
    var geometrySegments = trackRuntime.Geometry.SegmentCircuits;
    var traversals = trackRuntime.SegmentCircuitTraversals;
    if (geometrySegments is null || traversals is null ||
        geometrySegments.Count != traversals.Count ||
        traversals.Count != track.SegmentSourceEntryIds.Count)
      return false;

    var flattenedPieceIndex = 0;
    foreach (var index in Enumerable.Range(0, traversals.Count)) {
      var geometry = geometrySegments[index];
      var segment = traversals[index];
      if (geometry is null || segment is null || segment.Traversal is null ||
          !ReferenceEquals(segment.Geometry, geometry) ||
          !ReferenceEquals(segment.Circuit, geometry.Circuit) ||
          !ReferenceEquals(segment.Traversal.Circuit, geometry.Circuit) ||
          segment.SegmentSourceEntryId != track.SegmentSourceEntryIds[index] ||
          segment.PieceSourceEntryIds.Count != segment.Circuit.Pieces.Count)
        return false;

      foreach (var pieceIndex in Enumerable.Range(0, segment.PieceSourceEntryIds.Count)) {
        if (flattenedPieceIndex >= track.TrackPieceSourceEntryIds.Count ||
            segment.PieceSourceEntryIds[pieceIndex] !=
              track.TrackPieceSourceEntryIds[flattenedPieceIndex] ||
            !string.Equals(
              segment.Circuit.Pieces[pieceIndex].Id,
              $"track-piece-{segment.PieceSourceEntryIds[pieceIndex]}",
              StringComparison.Ordinal))
          return false;
        flattenedPieceIndex++;
      }
    }
    return flattenedPieceIndex == track.TrackPieceSourceEntryIds.Count;
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

  private static RideTrainCircuitMotionAuthorizationResult Authorized(
    TrackCircuitTraversal traversal
  ) => new(
    RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit,
    traversal);
}
