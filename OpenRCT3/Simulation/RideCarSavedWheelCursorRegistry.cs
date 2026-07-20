// Ride Car Saved Wheel Cursor Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed outcome of locating one saved wheel contact on a closed circuit.</summary>
internal enum RideCarSavedWheelCursorStatus {
  Resolved,
  MissingSavedReference,
  MissingTrackPieceData,
  UnresolvedTrack,
  UnsupportedOpenTrack,
  DistanceOutsideCircuit,
  AmbiguousSplineStart,
  DistanceOutsideSavedPiece,
}

/// <summary>The saved TrackPiece start-distance field that uniquely located a wheel contact.</summary>
internal enum RideCarSavedWheelSplineStart {
  Forward,
  Backwards,
  Equivalent,
}

/// <summary>One saved global wheel distance resolved against its exact cached TrackPiece.</summary>
internal sealed record RideCarSavedWheelContactCursor(
  RideCarSavedWheelCursorStatus Status,
  float SavedGlobalDistance,
  double? NormalizedCircuitDistance,
  ulong SavedTrackPieceEntryId,
  DatTrackPieceData? TrackPieceData,
  RideCarSavedWheelSplineStart? SplineStart,
  TrackCircuitCursor? Cursor,
  TrackCircuitSample? Sample
) {
  public bool IsResolved => Status == RideCarSavedWheelCursorStatus.Resolved;
}

/// <summary>The two exact saved wheel contacts for one linked DAT car instance.</summary>
internal sealed record RideCarSavedWheelCursorEntry(
  int RegistryIndex,
  RideCarInstanceRuntimeEntry CarRuntime,
  RideCarSavedWheelContactCursor Front,
  RideCarSavedWheelContactCursor Rear
) {
  public ulong CarInstanceEntryId => CarRuntime.CarInstanceEntryId;
  public bool IsResolved => Front.IsResolved && Rear.IsResolved;

  /// <summary>
  /// Whether native placement rotates both sampled contact frames by pi after locating them.
  /// </summary>
  public bool RequiresPiContactFrameRotation => CarRuntime.SavedReversed;
}

/// <summary>
/// Immutable static-load placement cursors created only from explicit saved wheel distances and
/// their cached TrackPiece identities.
/// </summary>
/// <remarks>
/// This is not a next-tick vehicle simulation. Native ordinary-mode updates recompute wheel
/// distances from a caller distance and RIC offsets. The saved values are used here only as a
/// validated load-time placement shortcut. <c>RideCarInstance.Reversed</c> rotates the resulting
/// frames; it does not select a TrackPiece start-distance field.
/// The runtime traversal length is the current stand-in for the native ride's saved total-distance
/// field until that field is retained by the DAT decoder.
/// </remarks>
internal sealed class RideCarSavedWheelCursorRegistry {
  public IReadOnlyList<RideCarSavedWheelCursorEntry> Entries { get; }
  public int CarCount => Entries.Count;
  public int ResolvedCarCount { get; }
  public int ResolvedContactCount { get; }

  private RideCarSavedWheelCursorRegistry(RideCarSavedWheelCursorEntry[] entries) {
    Entries = Array.AsReadOnly(entries);
    ResolvedCarCount = entries.Count(entry => entry.IsResolved);
    ResolvedContactCount = entries.Sum(entry =>
      Convert.ToInt32(entry.Front.IsResolved) + Convert.ToInt32(entry.Rear.IsResolved));
  }

  public static RideCarSavedWheelCursorRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    IReadOnlyList<DatTrackPieceData> trackPieces
  ) => Build(carRuntime, trackPieces, RideCarSavedWheelCursorRegistryLimits.Default);

  internal static RideCarSavedWheelCursorRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    IReadOnlyList<DatTrackPieceData> trackPieces,
    RideCarSavedWheelCursorRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(carRuntime);
    ArgumentNullException.ThrowIfNull(trackPieces);
    ValidateLimits(limits);
    ValidateCount(carRuntime.Entries.Count, limits.MaximumCarCount, "car");
    ValidateCount(trackPieces.Count, limits.MaximumTrackPieceCount, "TrackPiece");
    ValidateCarRegistryCounts(carRuntime);

    var trackPiecesById = IndexTrackPieces(trackPieces);
    var carIds = new HashSet<ulong>();
    var entries = new RideCarSavedWheelCursorEntry[carRuntime.Entries.Count];
    foreach (var index in Enumerable.Range(0, carRuntime.Entries.Count)) {
      var car = carRuntime.Entries[index];
      ValidateCarEntry(car, index, carIds);
      var trackRuntime = car.TrainRuntime.TrackRuntime;
      var traversal = ResolveCircuitTraversal(trackRuntime);
      var front = ResolveContact(
        car,
        car.CarInstance.FrontWheelDistance,
        car.TrackPiece,
        traversal,
        trackPiecesById);
      var rear = ResolveContact(
        car,
        car.CarInstance.RearWheelDistance,
        car.RearTrackPiece,
        traversal,
        trackPiecesById);
      entries[index] = new(index, car, front, rear);
    }

    return new(entries);
  }

  private static IReadOnlyDictionary<ulong, DatTrackPieceData> IndexTrackPieces(
    IReadOnlyList<DatTrackPieceData> trackPieces
  ) {
    var result = new Dictionary<ulong, DatTrackPieceData>(trackPieces.Count);
    foreach (var piece in trackPieces) {
      if (piece is null)
        throw new ArgumentException(
          "Saved TrackPiece data cannot contain null.",
          nameof(trackPieces));
      if (piece.EntryId == 0 || !result.TryAdd(piece.EntryId, piece))
        throw Invalid($"saved TrackPiece ID {piece.EntryId} is missing or duplicated");
      if (!float.IsFinite(piece.StartDistance) ||
          !float.IsFinite(piece.StartDistanceBackwardsSpline))
        throw Invalid($"saved TrackPiece {piece.EntryId} has a non-finite start distance");
    }
    return result;
  }

  private static void ValidateCarRegistryCounts(RideCarInstanceRuntimeRegistry carRuntime) {
    if (carRuntime.LinkedCarCount != carRuntime.Entries.Count)
      throw Invalid("linked-car count does not match the entry list");
    if (carRuntime.SavedCarCount !=
        carRuntime.LinkedCarCount + carRuntime.UnreferencedCarCount)
      throw Invalid("saved-car counts are not conserved");
    var resolvedPieceCars = carRuntime.Entries.Count(entry => entry.HasResolvedTrackPieces);
    if (resolvedPieceCars != carRuntime.ResolvedTrackPieceCarCount)
      throw Invalid("resolved TrackPiece car count does not match the entry list");
  }

  private static void ValidateCarEntry(
    RideCarInstanceRuntimeEntry car,
    int index,
    ISet<ulong> carIds
  ) {
    if (car is null || car.CarInstance is null || car.TrainRuntime is null ||
        car.TrainRuntime.TrackRuntime is null)
      throw Invalid($"car entry {index} is incomplete");
    if (car.RegistryIndex != index)
      throw Invalid($"car entry {index} advertises registry index {car.RegistryIndex}");
    if (car.CarInstanceEntryId == 0 || !carIds.Add(car.CarInstanceEntryId))
      throw Invalid($"car entry ID {car.CarInstanceEntryId} is missing or duplicated");
    var savedTrain = car.TrainRuntime.TrainResource.TrainInstance;
    if (car.WhichCar < 0 || car.WhichCar >= savedTrain.Cars.Count ||
        savedTrain.Cars[car.WhichCar] != car.CarInstanceEntryId)
      throw Invalid($"car {car.CarInstanceEntryId} changed exact saved train order");
    if (car.TrainInstanceEntryId != car.CarInstance.RideTrainInstance)
      throw Invalid($"car {car.CarInstanceEntryId} changed exact train identity");
    if (!float.IsFinite(car.CarInstance.FrontWheelDistance) ||
        !float.IsFinite(car.CarInstance.RearWheelDistance))
      throw Invalid($"car {car.CarInstanceEntryId} has a non-finite wheel distance");
    ValidateSavedPieceLink(car, car.TrackPiece, car.CarInstance.TrackPiece, "front");
    ValidateSavedPieceLink(car, car.RearTrackPiece, car.CarInstance.RearTrackPiece, "rear");
  }

  private static void ValidateSavedPieceLink(
    RideCarInstanceRuntimeEntry car,
    RideCarTrackPieceRuntimeLink link,
    ulong savedEntryId,
    string contact
  ) {
    if (link is null)
      throw Invalid($"car {car.CarInstanceEntryId} has no {contact} TrackPiece link");
    if (link.SavedTrackPieceEntryId != savedEntryId)
      throw Invalid(
        $"car {car.CarInstanceEntryId} changed its exact saved {contact} TrackPiece identity");
    if (link.IsResolved != (link.PieceIndex != null && link.Piece != null))
      throw Invalid($"car {car.CarInstanceEntryId} has an inconsistent {contact} TrackPiece link");
    switch (link.Status) {
      case RideCarTrackPieceRuntimeStatus.MissingSavedReference:
        if (savedEntryId != 0)
          throw Invalid(
            $"car {car.CarInstanceEntryId} marks nonzero {contact} TrackPiece " +
            $"{savedEntryId} as missing");
        break;
      case RideCarTrackPieceRuntimeStatus.UnresolvedTrack:
      case RideCarTrackPieceRuntimeStatus.Resolved:
        if (savedEntryId == 0)
          throw Invalid(
            $"car {car.CarInstanceEntryId} has zero {contact} TrackPiece with status " +
            $"{link.Status}");
        break;
      default:
        throw Invalid(
          $"car {car.CarInstanceEntryId} has unknown {contact} TrackPiece status {link.Status}");
    }
  }

  private static TrackCircuitTraversal? ResolveCircuitTraversal(
    RideInstanceTrackRuntimeEntry trackRuntime
  ) {
    if (trackRuntime.CircuitTraversal != null) {
      if (trackRuntime.GraphTraversal != null || trackRuntime.Circuit == null ||
          trackRuntime.Graph != null ||
          !ReferenceEquals(trackRuntime.CircuitTraversal.Circuit, trackRuntime.Circuit))
        throw Invalid(
          $"ride {trackRuntime.InstanceEntryId} has inconsistent circuit traversal identity");
      return trackRuntime.CircuitTraversal;
    }
    if (trackRuntime.Circuit != null)
      throw Invalid($"ride {trackRuntime.InstanceEntryId} lost its circuit traversal");
    return null;
  }

  private static RideCarSavedWheelContactCursor ResolveContact(
    RideCarInstanceRuntimeEntry car,
    float savedGlobalDistance,
    RideCarTrackPieceRuntimeLink savedPieceLink,
    TrackCircuitTraversal? traversal,
    IReadOnlyDictionary<ulong, DatTrackPieceData> trackPiecesById
  ) {
    var savedPieceId = savedPieceLink.SavedTrackPieceEntryId;
    if (savedPieceId == 0 ||
        savedPieceLink.Status == RideCarTrackPieceRuntimeStatus.MissingSavedReference)
      return Unresolved(
        RideCarSavedWheelCursorStatus.MissingSavedReference,
        savedGlobalDistance,
        savedPieceId);

    if (traversal == null) {
      var openTrack = car.TrainRuntime.TrackRuntime.GraphTraversal != null;
      if (openTrack != savedPieceLink.IsResolved)
        throw Invalid(
          $"car {car.CarInstanceEntryId} cached TrackPiece {savedPieceId} disagrees with its " +
          "non-circuit track outcome");
      var status = openTrack
        ? RideCarSavedWheelCursorStatus.UnsupportedOpenTrack
        : RideCarSavedWheelCursorStatus.UnresolvedTrack;
      return Unresolved(status, savedGlobalDistance, savedPieceId);
    }

    if (!savedPieceLink.IsResolved)
      throw Invalid(
        $"car {car.CarInstanceEntryId} has a resolved circuit but unresolved cached " +
        $"TrackPiece {savedPieceId}");
    if (!trackPiecesById.TryGetValue(savedPieceId, out var trackPieceData))
      return Unresolved(
        RideCarSavedWheelCursorStatus.MissingTrackPieceData,
        savedGlobalDistance,
        savedPieceId);

    var pieceIndex = ValidateExactPieceIdentity(
      car,
      savedPieceLink,
      trackPieceData,
      traversal);
    if (!TryNormalizeCircuitDistance(
      savedGlobalDistance,
      traversal.Length,
      out var normalizedDistance))
      return Unresolved(
        RideCarSavedWheelCursorStatus.DistanceOutsideCircuit,
        savedGlobalDistance,
        savedPieceId,
        normalizedCircuitDistance: null,
        trackPieceData);
    var pieceLength = Convert.ToDouble(savedPieceLink.Piece!.Length);
    var forwardLocal = normalizedDistance - trackPieceData.StartDistance;
    var backwardsLocal = normalizedDistance - trackPieceData.StartDistanceBackwardsSpline;
    var forwardValid = IsInPieceRange(forwardLocal, pieceLength);
    var backwardsValid = IsInPieceRange(backwardsLocal, pieceLength);

    if (!forwardValid && !backwardsValid)
      return Unresolved(
        RideCarSavedWheelCursorStatus.DistanceOutsideSavedPiece,
        savedGlobalDistance,
        savedPieceId,
        normalizedDistance,
        trackPieceData);

    double pieceArcLength;
    RideCarSavedWheelSplineStart splineStart;
    if (forwardValid && backwardsValid) {
      if (forwardLocal != backwardsLocal)
        return Unresolved(
          RideCarSavedWheelCursorStatus.AmbiguousSplineStart,
          savedGlobalDistance,
          savedPieceId,
          normalizedDistance,
          trackPieceData);
      pieceArcLength = forwardLocal;
      splineStart = RideCarSavedWheelSplineStart.Equivalent;
    } else if (forwardValid) {
      pieceArcLength = forwardLocal;
      splineStart = RideCarSavedWheelSplineStart.Forward;
    } else {
      pieceArcLength = backwardsLocal;
      splineStart = RideCarSavedWheelSplineStart.Backwards;
    }

    var cursor = traversal.AtPiece(pieceIndex, pieceArcLength);
    var sample = cursor.Sample();
    ValidateSample(car, savedPieceId, cursor, sample);
    return new(
      RideCarSavedWheelCursorStatus.Resolved,
      savedGlobalDistance,
      normalizedDistance,
      savedPieceId,
      trackPieceData,
      splineStart,
      cursor,
      sample);
  }

  private static int ValidateExactPieceIdentity(
    RideCarInstanceRuntimeEntry car,
    RideCarTrackPieceRuntimeLink savedPieceLink,
    DatTrackPieceData trackPieceData,
    TrackCircuitTraversal traversal
  ) {
    var trackRuntime = car.TrainRuntime.TrackRuntime;
    var ids = trackRuntime.Track.TrackPieceSourceEntryIds;
    var pieceIndex = savedPieceLink.PieceIndex!.Value;
    if (pieceIndex < 0 || pieceIndex >= ids.Count ||
        ids.Count != traversal.Circuit.Pieces.Count)
      throw Invalid(
        $"car {car.CarInstanceEntryId} cached TrackPiece index {pieceIndex} is outside the " +
        "exact circuit order");
    if (ids[pieceIndex] != savedPieceLink.SavedTrackPieceEntryId ||
        trackPieceData.EntryId != savedPieceLink.SavedTrackPieceEntryId)
      throw Invalid(
        $"car {car.CarInstanceEntryId} cached TrackPiece changed exact ordered identity");
    if (!ReferenceEquals(
      traversal.Circuit.Pieces[pieceIndex].Piece,
      savedPieceLink.Piece))
      throw Invalid(
        $"car {car.CarInstanceEntryId} cached TrackPiece changed runtime object identity");
    if (trackPieceData.Owner != trackPieceData.Segment ||
        !trackRuntime.Track.SegmentSourceEntryIds.Contains(trackPieceData.Segment))
      throw Invalid(
        $"saved TrackPiece {trackPieceData.EntryId} does not retain exact TrackSegment " +
        "ownership for its ride track");

    var previousIndex = pieceIndex == 0 ? ids.Count - 1 : pieceIndex - 1;
    var nextIndex = pieceIndex == ids.Count - 1 ? 0 : pieceIndex + 1;
    if (trackPieceData.Prev != ids[previousIndex] || trackPieceData.Next != ids[nextIndex])
      throw Invalid(
        $"saved TrackPiece {trackPieceData.EntryId} does not match the exact circuit order");
    return pieceIndex;
  }

  private static bool TryNormalizeCircuitDistance(
    float savedDistance,
    double circuitLength,
    out double normalized
  ) {
    if (!float.IsFinite(savedDistance) ||
        !double.IsFinite(circuitLength) ||
        circuitLength <= 0d)
      throw Invalid("cannot normalize a non-finite wheel distance or circuit length");
    normalized = Convert.ToDouble(savedDistance);
    if (normalized < 0d) normalized += circuitLength;
    else if (normalized >= circuitLength) normalized -= circuitLength;
    return double.IsFinite(normalized) && normalized >= 0d && normalized < circuitLength;
  }

  private static bool IsInPieceRange(double localDistance, double pieceLength) =>
    double.IsFinite(localDistance) &&
    double.IsFinite(pieceLength) &&
    localDistance >= 0d &&
    localDistance <= pieceLength;

  private static void ValidateSample(
    RideCarInstanceRuntimeEntry car,
    ulong savedPieceId,
    TrackCircuitCursor cursor,
    TrackCircuitSample sample
  ) {
    if (!cursor.IsInitialized || !double.IsFinite(cursor.PieceArcLength) ||
        !float.IsFinite(sample.CircuitArcLength) ||
        !float.IsFinite(sample.PieceArcLength) ||
        !TrackMath.IsFinite(sample.ContactPoints.Left.Position) ||
        !TrackMath.IsFinite(sample.ContactPoints.Left.Tangent) ||
        !TrackMath.IsFinite(sample.ContactPoints.Left.Orientation) ||
        !float.IsFinite(sample.ContactPoints.Left.BankRadians) ||
        !TrackMath.IsFinite(sample.ContactPoints.Right.Position) ||
        !TrackMath.IsFinite(sample.ContactPoints.Right.Tangent) ||
        !TrackMath.IsFinite(sample.ContactPoints.Right.Orientation) ||
        !float.IsFinite(sample.ContactPoints.Right.BankRadians))
      throw Invalid(
        $"car {car.CarInstanceEntryId} TrackPiece {savedPieceId} produced a non-finite sample");
  }

  private static RideCarSavedWheelContactCursor Unresolved(
    RideCarSavedWheelCursorStatus status,
    float savedGlobalDistance,
    ulong savedPieceId,
    double? normalizedCircuitDistance = null,
    DatTrackPieceData? trackPieceData = null
  ) => new(
    status,
    savedGlobalDistance,
    normalizedCircuitDistance,
    savedPieceId,
    trackPieceData,
    SplineStart: null,
    Cursor: null,
    Sample: null);

  private static void ValidateLimits(RideCarSavedWheelCursorRegistryLimits limits) {
    if (limits.MaximumCarCount < 0 || limits.MaximumTrackPieceCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-car saved wheel cursor {description} count exceeds the limit {maximum}.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car saved wheel cursor input is invalid: {message}.");
}

internal readonly record struct RideCarSavedWheelCursorRegistryLimits(
  int MaximumCarCount,
  int MaximumTrackPieceCount
) {
  public static RideCarSavedWheelCursorRegistryLimits Default { get; } =
    new(1_000_000, 1_000_000);
}
