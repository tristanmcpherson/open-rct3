// Ride Circuit Kinematic Simulation
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One caller-configured train seed on an exact ride-instance circuit.</summary>
/// <remarks>
/// The piece-local lead coordinate preserves seam identity. Contact offsets are caller-defined and
/// are not inferred from vehicle resources, train selection, or car dimensions.
/// </remarks>
internal sealed record RideCircuitKinematicTrainSeed(
  ulong InstanceEntryId,
  int LeadPieceIndex,
  double LeadPieceArcLength,
  double Speed,
  IReadOnlyList<double> ContactOffsetsBehindLead
);

/// <summary>
/// One exact ride-instance identity composed with an immutable kinematic train state.
/// </summary>
internal sealed record RideCircuitKinematicTrain(
  int SeedIndex,
  RideInstanceTrackRuntimeEntry RuntimeEntry,
  TrackCircuitKinematicTrainState State
) {
  public ulong InstanceEntryId => RuntimeEntry.InstanceEntryId;
  public ulong TrackEntryId => RuntimeEntry.TrackEntryId;
}

/// <summary>
/// A bounded immutable snapshot of explicitly seeded trains on resolved ride-instance circuits.
/// </summary>
/// <remarks>
/// This composes identity, circuit traversal, and constant-speed geometry sampling only. It does
/// not select trains, infer dimensions, dispatch vehicles, or model ride physics.
/// </remarks>
internal sealed class RideCircuitKinematicSimulation {
  public IReadOnlyList<RideCircuitKinematicTrain> Trains { get; }
  public int TrainCount => Trains.Count;

  private RideCircuitKinematicSimulation(RideCircuitKinematicTrain[] trains) {
    Trains = Array.AsReadOnly((RideCircuitKinematicTrain[])trains.Clone());
  }

  public static RideCircuitKinematicSimulation Build(
    RideInstanceTrackRuntimeRegistry registry,
    IReadOnlyList<RideCircuitKinematicTrainSeed> seeds
  ) => Build(registry, seeds, RideCircuitKinematicSimulationLimits.Default);

  internal static RideCircuitKinematicSimulation Build(
    RideInstanceTrackRuntimeRegistry registry,
    IReadOnlyList<RideCircuitKinematicTrainSeed> seeds,
    RideCircuitKinematicSimulationLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(registry);
    ArgumentNullException.ThrowIfNull(seeds);
    ValidateLimits(limits);
    if (seeds.Count == 0)
      throw new ArgumentException(
        "A ride-circuit simulation needs at least one explicit train seed.",
        nameof(seeds));
    if (seeds.Count > limits.MaximumTrainCount)
      throw Limit("train", limits.MaximumTrainCount);

    var entriesByInstanceId = IndexEntries(registry);
    var trains = new RideCircuitKinematicTrain[seeds.Count];
    var contactCount = 0ul;
    foreach (var seedIndex in Enumerable.Range(0, seeds.Count)) {
      var seed = seeds[seedIndex];
      if (seed is null)
        throw Invalid($"train seed {seedIndex} is null");
      if (seed.InstanceEntryId == 0)
        throw Invalid($"train seed {seedIndex} has missing ride-instance ID 0");
      if (!entriesByInstanceId.TryGetValue(seed.InstanceEntryId, out var entry))
        throw Invalid(
          $"train seed {seedIndex} references missing ride instance {seed.InstanceEntryId}");
      if (entry.Status != RideTrackGeometryStatus.Circuit
          || entry.Circuit is null
          || entry.CircuitTraversal is null)
        throw Invalid(
          $"ride instance {seed.InstanceEntryId} has track outcome {entry.Status}, " +
          "not a resolved circuit");

      ReserveContacts(seed.ContactOffsetsBehindLead, limits, ref contactCount);
      var lead = entry.CircuitTraversal.AtPiece(
        seed.LeadPieceIndex,
        seed.LeadPieceArcLength);
      var state = new TrackCircuitKinematicTrainState(
        lead,
        seed.Speed,
        seed.ContactOffsetsBehindLead);
      trains[seedIndex] = new(seedIndex, entry, state);
    }

    return new(trains);
  }

  /// <summary>Advances every train by one finite, non-negative fixed time step.</summary>
  public RideCircuitKinematicSimulation Advance(TimeSpan elapsed) {
    ValidateElapsed(elapsed);
    if (elapsed == TimeSpan.Zero) return this;

    var advanced = new RideCircuitKinematicTrain[Trains.Count];
    foreach (var index in Enumerable.Range(0, Trains.Count)) {
      var train = Trains[index];
      advanced[index] = train with { State = train.State.Advance(elapsed) };
    }
    return new(advanced);
  }

  private static IReadOnlyDictionary<ulong, RideInstanceTrackRuntimeEntry> IndexEntries(
    RideInstanceTrackRuntimeRegistry registry
  ) {
    var entries = new Dictionary<ulong, RideInstanceTrackRuntimeEntry>(registry.Entries.Count);
    foreach (var entry in registry.Entries) {
      if (entry is null || entry.InstanceEntryId == 0
          || !entries.TryAdd(entry.InstanceEntryId, entry))
        throw Invalid(
          $"runtime registry instance ID {entry?.InstanceEntryId ?? 0} is missing or duplicated");
    }
    return entries;
  }

  private static void ReserveContacts(
    IReadOnlyList<double> offsets,
    RideCircuitKinematicSimulationLimits limits,
    ref ulong contactCount
  ) {
    ArgumentNullException.ThrowIfNull(offsets);
    var addition = Convert.ToUInt64(offsets.Count);
    var maximum = Convert.ToUInt64(limits.MaximumContactCount);
    if (addition > maximum || contactCount > maximum - addition)
      throw Limit("contact", limits.MaximumContactCount);
    contactCount += addition;
  }

  private static void ValidateElapsed(TimeSpan elapsed) {
    if (elapsed < TimeSpan.Zero || !double.IsFinite(elapsed.TotalSeconds))
      throw new ArgumentOutOfRangeException(nameof(elapsed));
  }

  private static void ValidateLimits(RideCircuitKinematicSimulationLimits limits) {
    if (limits.MaximumTrainCount <= 0 || limits.MaximumContactCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-circuit kinematic simulation input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, int maximum) =>
    new($"Ride-circuit kinematic simulation {resource} count exceeds the limit {maximum}.");
}

internal readonly record struct RideCircuitKinematicSimulationLimits(
  int MaximumTrainCount,
  int MaximumContactCount
) {
  public static RideCircuitKinematicSimulationLimits Default { get; } =
    new(100_000, 1_000_000);
}
