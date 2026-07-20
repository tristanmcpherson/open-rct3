// Ride Car Instance Runtime Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed outcome of resolving one saved car wheel-contact piece.</summary>
internal enum RideCarTrackPieceRuntimeStatus {
  MissingSavedReference,
  UnresolvedTrack,
  Resolved,
}

/// <summary>One saved track-piece identity mapped to its exact owning runtime track.</summary>
internal sealed record RideCarTrackPieceRuntimeLink(
  ulong SavedTrackPieceEntryId,
  RideCarTrackPieceRuntimeStatus Status,
  int? PieceIndex,
  TrackPiece? Piece
) {
  public bool IsResolved => Status == RideCarTrackPieceRuntimeStatus.Resolved;
}

/// <summary>The typed outcome of resolving one saved car role to an exact RIC resource.</summary>
internal enum RideCarResourceRuntimeStatus {
  UnresolvedTrainResource,
  UnresolvedConsist,
  UnresolvedCarResource,
  Resolved,
}

/// <summary>
/// One exact saved car identity composed with its owning train, saved role, track pieces, and
/// optional provenance-resolved RIC resource.
/// </summary>
internal sealed record RideCarInstanceRuntimeEntry(
  int SavedCarIndex,
  int RegistryIndex,
  RideInstanceTrainRuntimeEntry TrainRuntime,
  DatRideCarInstanceData CarInstance,
  RideTrainCarRole SavedRole,
  RideCarTrackPieceRuntimeLink TrackPiece,
  RideCarTrackPieceRuntimeLink RearTrackPiece,
  RideCarResourceRuntimeStatus ResourceStatus,
  RideCarLink? CarResource,
  RideInstanceTrainConsistRuntimeEntry? TrainConsist,
  RideInstanceTrainConsistCarRuntimeEntry? ConsistCar
) {
  public ulong CarInstanceEntryId => CarInstance.EntryId;
  public ulong TrainInstanceEntryId => TrainRuntime.TrainInstanceEntryId;
  public int WhichCar => CarInstance.WhichCar;
  public float SavedDistance => CarInstance.Distance;
  public bool SavedReversed => CarInstance.Reversed;
  public float SavedSpeed => CarInstance.Speed;
  public RideTrackGeometryStatus TrackStatus => TrainRuntime.TrackRuntime.Status;
  public RideInstanceTrainConsistRuntimeStatus? ConsistStatus => TrainConsist?.Status;
  public bool HasResolvedTrackPieces => TrackPiece.IsResolved && RearTrackPiece.IsResolved;
  public bool HasResolvedResource => ResourceStatus == RideCarResourceRuntimeStatus.Resolved;
}

/// <summary>
/// Bounded immutable composition of exact saved DAT car instances with their owning train runtime.
/// </summary>
/// <remarks>
/// Entries follow the authoritative train-to-car reference order. Saved track-piece references,
/// distance, direction, and speed remain resume evidence only; this registry does not create a
/// cursor, seed motion, sample geometry, or infer vehicle state.
/// </remarks>
internal sealed class RideCarInstanceRuntimeRegistry {
  public IReadOnlyList<RideCarInstanceRuntimeEntry> Entries { get; }
  public IReadOnlyList<DatRideCarInstanceData> UnreferencedInstances { get; }
  public int SavedCarCount { get; }
  public int LinkedCarCount => Entries.Count;
  public int UnreferencedCarCount => UnreferencedInstances.Count;
  public int ResolvedTrackPieceCarCount { get; }
  public int ResolvedResourceCarCount { get; }

  private RideCarInstanceRuntimeRegistry(
    RideCarInstanceRuntimeEntry[] entries,
    DatRideCarInstanceData[] unreferencedInstances,
    int savedCarCount
  ) {
    Entries = Array.AsReadOnly(entries);
    UnreferencedInstances = Array.AsReadOnly(unreferencedInstances);
    SavedCarCount = savedCarCount;
    ResolvedTrackPieceCarCount = entries.Count(entry => entry.HasResolvedTrackPieces);
    ResolvedResourceCarCount = entries.Count(entry => entry.HasResolvedResource);
  }

  public static RideCarInstanceRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    IReadOnlyList<DatRideCarInstanceData> carInstances
  ) => Build(
    trainRuntime,
    carInstances,
    consistRuntime: null,
    RideCarInstanceRuntimeRegistryLimits.Default);

  public static RideCarInstanceRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    IReadOnlyList<DatRideCarInstanceData> carInstances,
    RideInstanceTrainConsistRuntimeRegistry consistRuntime
  ) {
    ArgumentNullException.ThrowIfNull(consistRuntime);
    return Build(
      trainRuntime,
      carInstances,
      consistRuntime,
      RideCarInstanceRuntimeRegistryLimits.Default);
  }

  internal static RideCarInstanceRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    IReadOnlyList<DatRideCarInstanceData> carInstances,
    RideCarInstanceRuntimeRegistryLimits limits
  ) => Build(trainRuntime, carInstances, consistRuntime: null, limits);

  internal static RideCarInstanceRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    IReadOnlyList<DatRideCarInstanceData> carInstances,
    RideInstanceTrainConsistRuntimeRegistry? consistRuntime,
    RideCarInstanceRuntimeRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(trainRuntime);
    ArgumentNullException.ThrowIfNull(carInstances);
    ValidateLimits(limits);
    ValidateCount(carInstances.Count, limits.MaximumCarCount, "saved car");
    ValidateCount(trainRuntime.Entries.Count, limits.MaximumTrainCount, "linked train");

    var carsById = IndexCars(carInstances);
    var runtimeTrainIds = new HashSet<ulong>();
    var linkedCarIds = new HashSet<ulong>();
    var entries = new List<RideCarInstanceRuntimeEntry>(carInstances.Count);
    var trackPiecesByRide = new Dictionary<ulong, TrackPieceIndex>();
    var consistsByTrainId = consistRuntime == null
      ? null
      : IndexConsists(trainRuntime, consistRuntime);
    foreach (var trainRuntimeEntry in trainRuntime.Entries) {
      ValidateTrainRuntimeEntry(trainRuntimeEntry, runtimeTrainIds);
      var savedTrain = trainRuntimeEntry.TrainResource.TrainInstance;
      var trackPieceIndex = GetTrackPieceIndex(trainRuntimeEntry, trackPiecesByRide);
      RideInstanceTrainConsistRuntimeEntry? trainConsist = null;
      if (consistsByTrainId != null &&
          !consistsByTrainId.TryGetValue(savedTrain.EntryId, out trainConsist))
        throw Invalid($"linked train {savedTrain.EntryId} has no consist-runtime entry");

      foreach (var ordinal in Enumerable.Range(0, savedTrain.Cars.Count)) {
        var carId = savedTrain.Cars[ordinal];
        if (carId == 0 || !linkedCarIds.Add(carId))
          throw Invalid($"saved car instance ID {carId} is missing or listed more than once");
        if (!carsById.TryGetValue(carId, out var indexedCar))
          throw Invalid(
            $"saved train {savedTrain.EntryId} references missing car instance {carId}");

        var car = indexedCar.Instance;
        if (car.RideTrainInstance != savedTrain.EntryId)
          throw Invalid(
            $"saved train {savedTrain.EntryId} references car {carId}, whose reciprocal owner " +
            $"is {car.RideTrainInstance}");
        if (car.WhichCar != ordinal)
          throw Invalid(
            $"saved train {savedTrain.EntryId} lists car {carId} at index {ordinal}, but its " +
            $"WhichCar value is {car.WhichCar}");

        var role = SavedRole(car);
        var consistCar = ResolveConsistCar(trainConsist, car, role, savedTrain.Cars.Count);
        var resourceStatus = ResolveResourceStatus(
          trainRuntimeEntry,
          trainConsist,
          consistCar);
        entries.Add(new RideCarInstanceRuntimeEntry(
          indexedCar.SavedIndex,
          entries.Count,
          trainRuntimeEntry,
          car,
          role,
          ResolveTrackPiece(car.TrackPiece, trackPieceIndex, savedTrain.EntryId, carId),
          ResolveTrackPiece(car.RearTrackPiece, trackPieceIndex, savedTrain.EntryId, carId),
          resourceStatus,
          consistCar?.CarResource,
          trainConsist,
          consistCar));
      }
    }

    foreach (var car in carInstances) {
      if (runtimeTrainIds.Contains(car.RideTrainInstance) && !linkedCarIds.Contains(car.EntryId))
        throw Invalid(
          $"saved car {car.EntryId} names linked train {car.RideTrainInstance} but is not listed " +
          "by that train");
    }

    var unreferenced = carInstances
      .Where(car => !linkedCarIds.Contains(car.EntryId))
      .ToArray();
    return new(entries.ToArray(), unreferenced, carInstances.Count);
  }

  private static Dictionary<ulong, IndexedCar> IndexCars(
    IReadOnlyList<DatRideCarInstanceData> carInstances
  ) {
    var result = new Dictionary<ulong, IndexedCar>(carInstances.Count);
    foreach (var index in Enumerable.Range(0, carInstances.Count)) {
      var car = carInstances[index];
      if (car is null)
        throw new ArgumentException(
          "Saved car instances cannot contain null.",
          nameof(carInstances));
      if (car.EntryId == 0 || !result.TryAdd(car.EntryId, new(index, car)))
        throw Invalid($"saved car instance ID {car.EntryId} is missing or duplicated");
      if (car.RideTrainInstance == 0)
        throw Invalid($"saved car {car.EntryId} has no owning train");
      if (car.WhichCar < 0)
        throw Invalid($"saved car {car.EntryId} has negative WhichCar {car.WhichCar}");
      _ = SavedRole(car);
      if (!float.IsFinite(car.Distance) || !float.IsFinite(car.Speed))
        throw Invalid($"saved car {car.EntryId} has non-finite resume state");
    }
    return result;
  }

  private static void ValidateTrainRuntimeEntry(
    RideInstanceTrainRuntimeEntry entry,
    ISet<ulong> trainIds
  ) {
    if (entry is null || entry.TrainResource is null || entry.TrackRuntime is null
        || entry.TrainResource.TrainInstance is null)
      throw Invalid("linked train runtime contains an incomplete entry");
    if (entry.TrainInstanceEntryId == 0 || !trainIds.Add(entry.TrainInstanceEntryId))
      throw Invalid(
        $"linked train runtime ID {entry.TrainInstanceEntryId} is missing or duplicated");
    if (!ReferenceEquals(entry.TrainResource.RideInstance, entry.TrackRuntime.Instance))
      throw Invalid(
        $"linked train {entry.TrainInstanceEntryId} changed exact ride-instance identity");
  }

  private static IReadOnlyDictionary<ulong, RideInstanceTrainConsistRuntimeEntry> IndexConsists(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    RideInstanceTrainConsistRuntimeRegistry consists
  ) {
    ArgumentNullException.ThrowIfNull(consists);
    if (consists.Entries.Count != trainRuntime.Entries.Count)
      throw Invalid(
        $"consist runtime conserves {consists.Entries.Count} of " +
        $"{trainRuntime.Entries.Count} linked trains");

    var result = new Dictionary<ulong, RideInstanceTrainConsistRuntimeEntry>(
      consists.Entries.Count);
    foreach (var consist in consists.Entries) {
      if (consist is null || consist.TrainRuntime is null)
        throw Invalid("consist runtime contains an incomplete entry");
      var trainId = consist.TrainRuntime.TrainInstanceEntryId;
      if (trainId == 0 || !result.TryAdd(trainId, consist))
        throw Invalid($"consist runtime train ID {trainId} is missing or duplicated");
    }
    foreach (var runtime in trainRuntime.Entries) {
      if (!result.TryGetValue(runtime.TrainInstanceEntryId, out var consist) ||
          !ReferenceEquals(consist.TrainRuntime, runtime))
        throw Invalid(
          $"consist runtime train {runtime.TrainInstanceEntryId} changed exact runtime identity");
    }
    return result;
  }

  private static RideInstanceTrainConsistCarRuntimeEntry? ResolveConsistCar(
    RideInstanceTrainConsistRuntimeEntry? consist,
    DatRideCarInstanceData car,
    RideTrainCarRole savedRole,
    int savedCarCount
  ) {
    if (consist == null) return null;
    if (consist.Cars == null)
      throw Invalid($"consist for train {car.RideTrainInstance} has a null car list");
    if (!consist.IsResolved) {
      if (consist.Roles != null || consist.Cars.Count != 0)
        throw Invalid(
          $"unresolved consist for train {car.RideTrainInstance} exposes resolved cars");
      return null;
    }
    if (consist.Roles == null)
      throw Invalid($"resolved consist for train {car.RideTrainInstance} is incomplete");
    if (consist.Roles.Entries.Count != savedCarCount || consist.Cars.Count != savedCarCount)
      throw Invalid(
        $"resolved consist for train {car.RideTrainInstance} has {consist.Cars.Count} cars, " +
        $"but its saved car list has {savedCarCount}");

    var consistCar = consist.Cars[car.WhichCar];
    if (consistCar == null || consistCar.CarResource == null)
      throw Invalid(
        $"resolved consist car {car.WhichCar} on train {car.RideTrainInstance} is incomplete");
    if (consistCar.Role.RuntimeIndex != car.WhichCar)
      throw Invalid(
        $"resolved consist car {consistCar.Role.RuntimeIndex} does not match saved WhichCar " +
        $"{car.WhichCar} on train {car.RideTrainInstance}");
    if (consistCar.Role.Role != savedRole || consistCar.CarResource.Role != savedRole)
      throw Invalid(
        $"saved car {car.EntryId} role {savedRole} disagrees with resolved consist role " +
        $"{consistCar.Role.Role}");
    if (!string.Equals(
      consistCar.Role.ResourceName,
      consistCar.CarResource.Reference,
      StringComparison.Ordinal))
      throw Invalid(
        $"resolved consist car {car.WhichCar} on train {car.RideTrainInstance} changed exact " +
        "RIC identity");
    return consistCar;
  }

  private static RideCarResourceRuntimeStatus ResolveResourceStatus(
    RideInstanceTrainRuntimeEntry trainRuntime,
    RideInstanceTrainConsistRuntimeEntry? consist,
    RideInstanceTrainConsistCarRuntimeEntry? car
  ) {
    if (consist == null)
      return !trainRuntime.HasResolvedResource
        ? RideCarResourceRuntimeStatus.UnresolvedTrainResource
        : car == null
          ? RideCarResourceRuntimeStatus.UnresolvedConsist
          : throw Invalid("car resource cannot be resolved without a consist runtime");
    return consist.Status switch {
      RideInstanceTrainConsistRuntimeStatus.Resolved => ResolvedResource(consist, car),
      RideInstanceTrainConsistRuntimeStatus.UnresolvedRideTrainResource =>
        RideCarResourceRuntimeStatus.UnresolvedTrainResource,
      RideInstanceTrainConsistRuntimeStatus.UnresolvedRideCarResource =>
        RideCarResourceRuntimeStatus.UnresolvedCarResource,
      RideInstanceTrainConsistRuntimeStatus.UnresolvedTrackedRideResource or
      RideInstanceTrainConsistRuntimeStatus.MissingTrackedRideGraph or
      RideInstanceTrainConsistRuntimeStatus.MissingRideTrainGraph or
      RideInstanceTrainConsistRuntimeStatus.MissingPeepSlotEvidence =>
        RideCarResourceRuntimeStatus.UnresolvedConsist,
      _ => throw Invalid($"consist runtime has unknown status {consist.Status}"),
    };
  }

  private static RideCarResourceRuntimeStatus ResolvedResource(
    RideInstanceTrainConsistRuntimeEntry consist,
    RideInstanceTrainConsistCarRuntimeEntry? car
  ) {
    if (car?.CarResource.IsResolved != true)
      throw Invalid(
        $"resolved consist for train {consist.TrainRuntime.TrainInstanceEntryId} exposes an " +
        "unresolved car resource");
    return RideCarResourceRuntimeStatus.Resolved;
  }

  private static RideTrainCarRole SavedRole(DatRideCarInstanceData car) {
    return car.WhichRideTrainCar switch {
      0 => RideTrainCarRole.Front,
      1 => RideTrainCarRole.Second,
      2 => RideTrainCarRole.Middle,
      3 => RideTrainCarRole.Penultimate,
      4 => RideTrainCarRole.Rear,
      5 => RideTrainCarRole.Link,
      _ => throw Invalid(
        $"saved car {car.EntryId} has unsupported RIT role {car.WhichRideTrainCar}"),
    };
  }

  private static TrackPieceIndex GetTrackPieceIndex(
    RideInstanceTrainRuntimeEntry train,
    IDictionary<ulong, TrackPieceIndex> cache
  ) {
    var rideId = train.RideInstanceEntryId;
    if (cache.TryGetValue(rideId, out var cached)) return cached;

    var runtime = train.TrackRuntime;
    if (!runtime.IsResolved) {
      var unresolved = TrackPieceIndex.Unresolved;
      cache.Add(rideId, unresolved);
      return unresolved;
    }

    var ids = runtime.Track.TrackPieceSourceEntryIds;
    IReadOnlyList<TrackPiece> pieces;
    if (runtime.Circuit != null && runtime.Graph == null)
      pieces = runtime.Circuit.Pieces.Select(piece => piece.Piece).ToArray();
    else if (runtime.Graph != null && runtime.Circuit == null)
      pieces = runtime.Graph.Edges.Select(edge => edge.Piece).ToArray();
    else
      throw Invalid($"ride {rideId} has inconsistent resolved track geometry");
    if (ids.Count != pieces.Count)
      throw Invalid(
        $"ride {rideId} has {ids.Count} saved track-piece identities but " +
        $"{pieces.Count} runtime pieces");

    var byId = new Dictionary<ulong, ResolvedTrackPiece>(ids.Count);
    foreach (var index in Enumerable.Range(0, ids.Count)) {
      var id = ids[index];
      if (id == 0 || !byId.TryAdd(id, new(index, pieces[index])))
        throw Invalid($"ride {rideId} has a missing or duplicate track-piece identity {id}");
    }
    var resolved = new TrackPieceIndex(byId);
    cache.Add(rideId, resolved);
    return resolved;
  }

  private static RideCarTrackPieceRuntimeLink ResolveTrackPiece(
    ulong savedTrackPieceEntryId,
    TrackPieceIndex index,
    ulong trainId,
    ulong carId
  ) {
    if (savedTrackPieceEntryId == 0)
      return new(
        savedTrackPieceEntryId,
        RideCarTrackPieceRuntimeStatus.MissingSavedReference,
        PieceIndex: null,
        Piece: null);
    if (!index.IsResolved)
      return new(
        savedTrackPieceEntryId,
        RideCarTrackPieceRuntimeStatus.UnresolvedTrack,
        PieceIndex: null,
        Piece: null);
    if (!index.ById!.TryGetValue(savedTrackPieceEntryId, out var resolved))
      throw Invalid(
        $"saved car {carId} on train {trainId} references track piece " +
        $"{savedTrackPieceEntryId} outside its owning ride track");
    return new(
      savedTrackPieceEntryId,
      RideCarTrackPieceRuntimeStatus.Resolved,
      resolved.Index,
      resolved.Piece);
  }

  private static void ValidateLimits(RideCarInstanceRuntimeRegistryLimits limits) {
    if (limits.MaximumTrainCount < 0 || limits.MaximumCarCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-car runtime {description} count exceeds the limit {maximum}.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car runtime registry input is invalid: {message}.");

  private sealed record IndexedCar(int SavedIndex, DatRideCarInstanceData Instance);

  private sealed record ResolvedTrackPiece(int Index, TrackPiece Piece);

  private sealed record TrackPieceIndex(
    IReadOnlyDictionary<ulong, ResolvedTrackPiece>? ById
  ) {
    public static TrackPieceIndex Unresolved { get; } =
      new((IReadOnlyDictionary<ulong, ResolvedTrackPiece>?)null);
    public bool IsResolved => ById != null;
  }
}

internal readonly record struct RideCarInstanceRuntimeRegistryLimits(
  int MaximumTrainCount,
  int MaximumCarCount
) {
  public static RideCarInstanceRuntimeRegistryLimits Default { get; } =
    new(100_000, 1_000_000);
}
