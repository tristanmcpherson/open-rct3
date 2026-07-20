// Ride Instance Train Consist Runtime Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The exact resolution outcome for one saved ride-train consist.</summary>
internal enum RideInstanceTrainConsistRuntimeStatus {
  Resolved,
  UnresolvedTrackedRideResource,
  UnresolvedRideTrainResource,
  MissingTrackedRideGraph,
  MissingRideTrainGraph,
  UnresolvedRideCarResource,
  MissingPeepSlotEvidence,
}

/// <summary>One runtime consist position and its exact graph-backed RIC evidence.</summary>
internal sealed record RideInstanceTrainConsistCarRuntimeEntry(
  RideTrainConsistRoleEntry Role,
  RideCarLink CarResource,
  RideCarPeepSlotEvidence PeepSlotEvidence
);

/// <summary>One saved train composed with its exact TRR/RIT/RIC graph occurrence.</summary>
internal sealed record RideInstanceTrainConsistRuntimeEntry(
  RideInstanceTrainRuntimeEntry TrainRuntime,
  TrackedRideResourceLink? RideGraph,
  RideTrainLink? TrainGraph,
  RideTrainConsistRoleResolution? Roles,
  IReadOnlyList<RideInstanceTrainConsistCarRuntimeEntry> Cars,
  RideInstanceTrainConsistRuntimeStatus Status
) {
  public bool IsResolved => Status == RideInstanceTrainConsistRuntimeStatus.Resolved;
}

/// <summary>
/// Composes each saved train with exact graph identities and native Peep-marker consist roles.
/// </summary>
/// <remarks>
/// This registry retains decoded objects and dependency-closure identities. It does not seed a
/// circuit cursor, speed, spacing, motion state, or render ownership.
/// </remarks>
internal sealed class RideInstanceTrainConsistRuntimeRegistry {
  public IReadOnlyList<RideInstanceTrainConsistRuntimeEntry> Entries { get; }
  public int ResolvedCount { get; }
  public int UnresolvedCount => Entries.Count - ResolvedCount;

  internal RideInstanceTrainConsistRuntimeRegistry(
    RideInstanceTrainConsistRuntimeEntry[] entries
  ) {
    ArgumentNullException.ThrowIfNull(entries);
    entries = (RideInstanceTrainConsistRuntimeEntry[])entries.Clone();
    Entries = Array.AsReadOnly(entries);
    ResolvedCount = entries.Count(entry => entry.IsResolved);
  }

  public static RideInstanceTrainConsistRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    RideInstanceResourceLoadResult resources
  ) => Build(
    trainRuntime,
    resources,
    RideCarPeepSlotEvidenceIndex.Build(resources.CarVisuals),
    RideInstanceTrainConsistRuntimeRegistryLimits.Default);

  internal static RideInstanceTrainConsistRuntimeRegistry Build(
    RideInstanceTrainRuntimeRegistry trainRuntime,
    RideInstanceResourceLoadResult resources,
    RideCarPeepSlotEvidenceIndex peepSlots,
    RideInstanceTrainConsistRuntimeRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(trainRuntime);
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(peepSlots);
    ValidateLimits(limits);
    ValidateCount(trainRuntime.Entries.Count, limits.MaximumTrainCount, "runtime train");
    ValidateCount(resources.Instances.Count, limits.MaximumRideCount, "saved ride instance");
    ValidateCount(resources.Graph.Rides.Count, limits.MaximumRideCount, "resource-graph ride");

    var savedRides = IndexSavedRides(resources.Instances);
    var savedTrains = IndexSavedTrains(resources.TrainInstances.Links);
    var graphRides = IndexGraphRides(resources.Graph.Rides);
    var entries = new RideInstanceTrainConsistRuntimeEntry[trainRuntime.Entries.Count];
    var seenRuntimeTrains = new HashSet<ulong>();
    foreach (var index in Enumerable.Range(0, entries.Length)) {
      var runtime = trainRuntime.Entries[index];
      if (runtime == null || runtime.TrackRuntime == null || runtime.TrainResource == null)
        throw Invalid($"runtime train entry {index} is incomplete");
      if (!seenRuntimeTrains.Add(runtime.TrainInstanceEntryId))
        throw Invalid($"runtime train ID {runtime.TrainInstanceEntryId} is duplicated");
      if (!savedTrains.TryGetValue(runtime.TrainInstanceEntryId, out var savedTrain) ||
          !ReferenceEquals(savedTrain, runtime.TrainResource))
        throw Invalid(
          $"runtime train {runtime.TrainInstanceEntryId} changed exact saved-link identity");
      if (!savedRides.TryGetValue(runtime.RideInstanceEntryId, out var savedRide) ||
          !ReferenceEquals(savedRide.Instance, runtime.TrackRuntime.Instance) ||
          !ReferenceEquals(savedRide.Instance, savedTrain.RideInstance))
        throw Invalid(
          $"runtime train {runtime.TrainInstanceEntryId} changed exact ride-instance identity");

      entries[index] = Compose(runtime, savedRide, graphRides, peepSlots, limits);
    }
    return new(entries);
  }

  private static RideInstanceTrainConsistRuntimeEntry Compose(
    RideInstanceTrainRuntimeEntry runtime,
    RideInstanceResourceLink savedRide,
    IReadOnlyDictionary<TrackedRide, TrackedRideResourceLink> graphRides,
    RideCarPeepSlotEvidenceIndex peepSlots,
    RideInstanceTrainConsistRuntimeRegistryLimits limits
  ) {
    if (!savedRide.IsResolved)
      return Unresolved(
        runtime,
        null,
        null,
        RideInstanceTrainConsistRuntimeStatus.UnresolvedTrackedRideResource);
    if (!runtime.TrainResource.IsResolved)
      return Unresolved(
        runtime,
        null,
        null,
        RideInstanceTrainConsistRuntimeStatus.UnresolvedRideTrainResource);

    var rideSource = savedRide.Source!;
    if (!graphRides.TryGetValue(rideSource.Resource, out var rideGraph))
      return Unresolved(
        runtime,
        null,
        null,
        RideInstanceTrainConsistRuntimeStatus.MissingTrackedRideGraph);
    ValidateRideIdentity(rideSource, rideGraph);

    var savedTrainSource = runtime.TrainResource.Source!;
    var trainGraph = FindTrainGraph(rideGraph, savedTrainSource, limits);
    if (trainGraph == null)
      return Unresolved(
        runtime,
        rideGraph,
        null,
        RideInstanceTrainConsistRuntimeStatus.MissingRideTrainGraph);

    var carsByRole = ValidateAndIndexCars(trainGraph, limits);
    foreach (var pair in carsByRole) {
      if (pair.Key == RideTrainCarRole.WildUnknown) continue;
      if (!pair.Value.IsResolved)
        return Unresolved(
          runtime,
          rideGraph,
          trainGraph,
          RideInstanceTrainConsistRuntimeStatus.UnresolvedRideCarResource);
      if (!peepSlots.TryGet(pair.Value, out _))
        return Unresolved(
          runtime,
          rideGraph,
          trainGraph,
          RideInstanceTrainConsistRuntimeStatus.MissingPeepSlotEvidence);
    }

    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var car in carsByRole.Values) {
      if (car.Role == RideTrainCarRole.WildUnknown) continue;
      if (!peepSlots.TryGet(car, out var evidence))
        throw Invalid($"RIC '{car.Reference}' lost Peep-slot evidence while composing");
      if (counts.TryGetValue(car.Reference, out var existing) &&
          existing != evidence.PeepSlotCount)
        throw Invalid(
          $"RIC reference '{car.Reference}' has conflicting Peep-slot evidence");
      counts[car.Reference] = evidence.PeepSlotCount;
    }

    var roles = RideTrainConsistRoleResolver.Resolve(
      trainGraph.Train!.Cars,
      runtime.TrackRuntime.Instance.NCarsPerTrain,
      reference => counts.TryGetValue(reference, out var count)
        ? count
        : throw Invalid($"consist role references absent RIC '{reference}'"),
      new RideTrainConsistRoleLimits(limits.MaximumRuntimeCarCount));
    var cars = new RideInstanceTrainConsistCarRuntimeEntry[roles.Entries.Count];
    foreach (var index in Enumerable.Range(0, cars.Length)) {
      var role = roles.Entries[index];
      if (!carsByRole.TryGetValue(role.Role, out var car) ||
          !string.Equals(car.Reference, role.ResourceName, StringComparison.Ordinal))
        throw Invalid(
          $"runtime consist entry {role.RuntimeIndex} changed exact RIC role identity");
      if (!peepSlots.TryGet(car, out var evidence))
        throw Invalid($"runtime RIC '{car.Reference}' lost Peep-slot evidence");
      if (evidence.PeepSlotCount != role.PeepSlotCount)
        throw Invalid($"runtime RIC '{car.Reference}' changed Peep-slot count");
      cars[index] = new(role, car, evidence);
    }

    return new(
      runtime,
      rideGraph,
      trainGraph,
      roles,
      Array.AsReadOnly(cars),
      RideInstanceTrainConsistRuntimeStatus.Resolved);
  }

  private static RideTrainLink? FindTrainGraph(
    TrackedRideResourceLink ride,
    RideTrainResourceSource savedSource,
    RideInstanceTrainConsistRuntimeRegistryLimits limits
  ) {
    if (ride.Trains == null) throw Invalid($"TRR '{ride.Ride.Name}' train list is null");
    ValidateCount(ride.Trains.Count, limits.MaximumTrainsPerRide, $"TRR '{ride.Ride.Name}' train");
    RideTrainLink? match = null;
    foreach (var train in ride.Trains) {
      if (train == null) throw Invalid($"TRR '{ride.Ride.Name}' train list contains null");
      if (train.Source?.Resource == null ||
          !ReferenceEquals(train.Source.Resource, savedSource.Resource))
        continue;
      if (match != null)
        throw Invalid(
          $"TRR '{ride.Ride.Name}' contains duplicate exact RIT '{savedSource.Resource.Name}'");
      match = train;
    }
    if (match == null) return null;
    if (!ReferenceEquals(match.Source!.File, savedSource.File) ||
        !ReferenceEquals(match.Source.Resource, savedSource.Resource) ||
        !SameClosure(match.Source.AllowedArchivePaths, savedSource.AllowedArchivePaths))
      throw Invalid(
        $"RIT '{savedSource.Resource.Name}' changed exact source or archive-closure identity");
    ValidateClosure(savedSource.File, savedSource.AllowedArchivePaths, "saved RIT");
    ValidateTrainSource(match);
    return match;
  }

  private static IReadOnlyDictionary<RideTrainCarRole, RideCarLink> ValidateAndIndexCars(
    RideTrainLink train,
    RideInstanceTrainConsistRuntimeRegistryLimits limits
  ) {
    if (train.Cars == null) throw Invalid($"RIT '{train.Reference}' car list is null");
    ValidateCount(train.Cars.Count, limits.MaximumCarsPerTrain, $"RIT '{train.Reference}' car");
    var expected = ExpectedCars(train.Train!.Cars);
    var result = new Dictionary<RideTrainCarRole, RideCarLink>();
    var repeated = new Dictionary<string, RideCarResourceSource?>(StringComparer.Ordinal);
    foreach (var car in train.Cars) {
      if (car == null) throw Invalid($"RIT '{train.Reference}' car list contains null");
      if (!result.TryAdd(car.Role, car))
        throw Invalid($"RIT '{train.Reference}' duplicates {car.Role} car role");
      if (!expected.TryGetValue(car.Role, out var reference))
        throw Invalid($"RIT '{train.Reference}' exposes unexpected {car.Role} car role");
      if (!string.Equals(car.Reference, reference, StringComparison.Ordinal))
        throw Invalid($"RIT '{train.Reference}' {car.Role} car reference changed identity");
      ValidateCarSource(car);
      if (repeated.TryGetValue(car.Reference, out var prior)) {
        if (!SameCarSource(prior, car.Source))
          throw Invalid($"RIT '{train.Reference}' repeats RIC '{car.Reference}' ambiguously");
      } else {
        repeated.Add(car.Reference, car.Source);
      }
    }
    foreach (var pair in expected)
      if (!result.ContainsKey(pair.Key))
        throw Invalid($"RIT '{train.Reference}' omits {pair.Key} car '{pair.Value}'");
    return result;
  }

  private static Dictionary<RideTrainCarRole, string> ExpectedCars(RideTrainCars cars) {
    if (cars == null) throw Invalid("RIT car configuration is null");
    var result = new Dictionary<RideTrainCarRole, string>();
    Add(RideTrainCarRole.Front, cars.Front);
    Add(RideTrainCarRole.Second, cars.Second);
    Add(RideTrainCarRole.Middle, cars.Middle);
    Add(RideTrainCarRole.Penultimate, cars.Penultimate);
    Add(RideTrainCarRole.Rear, cars.Rear);
    Add(RideTrainCarRole.Link, cars.Link);
    Add(RideTrainCarRole.WildUnknown, cars.WildUnknown);
    return result;

    void Add(RideTrainCarRole role, string? reference) {
      if (reference != null) result.Add(role, reference);
    }
  }

  private static void ValidateRideIdentity(
    RideInstanceResourceSource saved,
    TrackedRideResourceLink graph
  ) {
    if (graph.Source == null ||
        !ReferenceEquals(graph.Source.File, saved.File) ||
        !ReferenceEquals(graph.Source.Resource, saved.Resource))
      throw Invalid($"TRR '{saved.Resource.Name}' changed exact source identity");
    if (graph.Source.File.Type != FileType.TrackedRide ||
        !string.Equals(
          graph.Source.File.Name,
          graph.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid($"TRR '{saved.Resource.Name}' has inconsistent source provenance");
    ValidateClosure(graph.Source.File, graph.Source.AllowedArchivePaths, "TRR");
  }

  private static void ValidateTrainSource(RideTrainLink train) {
    if (!train.IsResolved || train.Source?.Resource == null)
      throw Invalid($"RIT '{train.Reference}' exact graph match is unresolved");
    if (train.Source.File.Type != FileType.RideTrain ||
        !string.Equals(
          train.Source.File.Name,
          train.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          train.Reference,
          train.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIT '{train.Reference}' has inconsistent source provenance");
    ValidateClosure(train.Source.File, train.Source.AllowedArchivePaths, "RIT");
  }

  private static void ValidateCarSource(RideCarLink car) {
    ValidateTaggedReference(car.Reference, "ric", $"{car.Role} RIC");
    if (!car.IsResolved) {
      if (car.Visuals == null || car.Visuals.Count != 0)
        throw Invalid($"unresolved RIC '{car.Reference}' exposes visual links");
      return;
    }
    if (car.Source?.Resource == null || car.Visuals == null)
      throw Invalid($"RIC '{car.Reference}' source is incomplete");
    if (car.Source.File.Type != FileType.RideCar ||
        !string.Equals(
          car.Source.File.Name,
          car.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' has inconsistent source provenance");
    var separator = car.Reference.LastIndexOf(':');
    if (!string.Equals(
      car.Reference[..separator],
      car.Source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' tagged identity disagrees with its source");
    ValidateClosure(car.Source.File, car.Source.AllowedArchivePaths, "RIC");
    if (car.Visuals.Any(visual => visual == null))
      throw Invalid($"RIC '{car.Reference}' visual list contains null");
  }

  private static bool SameCarSource(
    RideCarResourceSource? left,
    RideCarResourceSource? right
  ) => left == null || right == null
    ? left == null && right == null
    : ReferenceEquals(left.File, right.File) &&
      ReferenceEquals(left.Resource, right.Resource) &&
      SameClosure(left.AllowedArchivePaths, right.AllowedArchivePaths);

  private static bool SameClosure(
    IReadOnlyList<string>? left,
    IReadOnlyList<string>? right
  ) {
    if (left == null || right == null || left.Count != right.Count) return false;
    return new HashSet<string>(left, StringComparer.OrdinalIgnoreCase).SetEquals(right);
  }

  private static void ValidateTaggedReference(
    string reference,
    string tag,
    string description
  ) {
    if (string.IsNullOrWhiteSpace(reference)) throw Invalid($"{description} reference is empty");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(tag, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} reference '{reference}' is not name:{tag}");
  }

  private static void ValidateClosure(
    OvlFile file,
    IReadOnlyList<string>? closure,
    string tag
  ) {
    if (closure == null || !closure.Contains(file.Path, StringComparer.OrdinalIgnoreCase))
      throw Invalid($"{tag} '{file.Name}' archive closure omits its source archive");
    if (closure.Distinct(StringComparer.OrdinalIgnoreCase).Count() != closure.Count)
      throw Invalid($"{tag} '{file.Name}' archive closure contains duplicates");
  }

  private static IReadOnlyDictionary<ulong, RideInstanceResourceLink> IndexSavedRides(
    IReadOnlyList<RideInstanceResourceLink> rides
  ) {
    var result = new Dictionary<ulong, RideInstanceResourceLink>(rides.Count);
    foreach (var ride in rides) {
      if (ride == null || ride.Instance == null || ride.Instance.EntryId == 0 ||
          !result.TryAdd(ride.Instance.EntryId, ride))
        throw Invalid($"saved ride-instance ID {ride?.Instance?.EntryId ?? 0} is missing or duplicated");
    }
    return result;
  }

  private static IReadOnlyDictionary<ulong, RideTrainInstanceResourceLink> IndexSavedTrains(
    IReadOnlyList<RideTrainInstanceResourceLink> trains
  ) {
    var result = new Dictionary<ulong, RideTrainInstanceResourceLink>(trains.Count);
    foreach (var train in trains) {
      if (train == null || train.TrainInstance == null || train.TrainInstanceEntryId == 0 ||
          !result.TryAdd(train.TrainInstanceEntryId, train))
        throw Invalid(
          $"saved ride-train-instance ID {train?.TrainInstanceEntryId ?? 0} is missing or duplicated");
    }
    return result;
  }

  private static IReadOnlyDictionary<TrackedRide, TrackedRideResourceLink> IndexGraphRides(
    IReadOnlyList<TrackedRideResourceLink> rides
  ) {
    var result = new Dictionary<TrackedRide, TrackedRideResourceLink>(
      ReferenceEqualityComparer.Instance);
    foreach (var ride in rides) {
      if (ride == null || ride.Source?.Resource == null)
        throw Invalid("resource-graph ride is incomplete");
      if (!result.TryAdd(ride.Source.Resource, ride))
        throw Invalid($"resource graph duplicates exact TRR '{ride.Source.Resource.Name}'");
    }
    return result;
  }

  private static RideInstanceTrainConsistRuntimeEntry Unresolved(
    RideInstanceTrainRuntimeEntry runtime,
    TrackedRideResourceLink? ride,
    RideTrainLink? train,
    RideInstanceTrainConsistRuntimeStatus status
  ) => new(runtime, ride, train, null, [], status);

  private static void ValidateLimits(RideInstanceTrainConsistRuntimeRegistryLimits limits) {
    if (limits.MaximumRideCount <= 0 ||
        limits.MaximumTrainCount < 0 ||
        limits.MaximumTrainsPerRide <= 0 ||
        limits.MaximumCarsPerTrain <= 0 ||
        limits.MaximumRuntimeCarCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw Invalid($"{description} count {count} exceeds the limit {maximum}");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-instance train consist input is invalid: {message}.");
}

internal readonly record struct RideInstanceTrainConsistRuntimeRegistryLimits(
  int MaximumRideCount,
  int MaximumTrainCount,
  int MaximumTrainsPerRide,
  int MaximumCarsPerTrain,
  int MaximumRuntimeCarCount
) {
  public static RideInstanceTrainConsistRuntimeRegistryLimits Default { get; } =
    new(100_000, 100_000, 65_536, 7, 16_384);
}
