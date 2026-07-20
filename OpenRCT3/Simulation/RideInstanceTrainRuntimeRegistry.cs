// Ride Instance Train Runtime Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>
/// One exact saved train identity composed with its owning ride-instance runtime track.
/// </summary>
internal sealed record RideInstanceTrainRuntimeEntry(
  int SavedTrainIndex,
  RideInstanceTrackRuntimeEntry TrackRuntime,
  RideTrainInstanceResourceLink TrainResource
) {
  public ulong RideInstanceEntryId => TrackRuntime.InstanceEntryId;
  public ulong TrackEntryId => TrackRuntime.TrackEntryId;
  public ulong TrainInstanceEntryId => TrainResource.TrainInstanceEntryId;
  public int TrainOrdinal => TrainResource.Ordinal;
  public bool HasSavedMotionState => TrainResource.HasSavedMotionState;
  public float SavedDistance => TrainResource.SavedDistance;
  public bool SavedReversed => TrainResource.SavedReversed;
  public float SavedSpeed => TrainResource.SavedSpeed;
  public bool HasSavedOperationalState => TrainResource.HasSavedOperationalState;
  public int SavedOperationalState => TrainResource.SavedOperationalState;
  public float SavedOperationalStateTime => TrainResource.SavedOperationalStateTime;
  public int? SavedVisualVariant => TrainResource.SavedVisualVariant;
  public RideTrainMotionState? SavedMotionState => HasSavedMotionState
    ? new(SavedDistance, SavedSpeed, SavedReversed)
    : null;
  public bool HasResolvedTrack => TrackRuntime.IsResolved;
  public bool HasResolvedResource => TrainResource.IsResolved;
  public bool HasResolvedCircuit => TrackRuntime.CircuitTraversal != null;
}

/// <summary>
/// Bounded immutable composition of saved DAT train instances, exact decoded RIT resources, and
/// their owning ride-instance runtime tracks.
/// </summary>
/// <remarks>
/// This registry preserves saved train motion fields but deliberately does not advance them or infer
/// consist spacing and car roles. Those behaviors require the separate executable-backed layers.
/// </remarks>
internal sealed class RideInstanceTrainRuntimeRegistry {
  public IReadOnlyList<RideInstanceTrainRuntimeEntry> Entries { get; }
  public int RideInstanceCount { get; }
  public int LinkedTrainCount => Entries.Count;
  public int SavedTrainCount { get; }
  public int UnreferencedTrainCount => SavedTrainCount - LinkedTrainCount;
  public int ResolvedTrackTrainCount { get; }
  public int ResolvedResourceTrainCount { get; }
  public int ResolvedCircuitTrainCount { get; }

  private RideInstanceTrainRuntimeRegistry(
    RideInstanceTrainRuntimeEntry[] entries,
    int rideInstanceCount,
    int savedTrainCount
  ) {
    Entries = Array.AsReadOnly(entries);
    RideInstanceCount = rideInstanceCount;
    SavedTrainCount = savedTrainCount;
    ResolvedTrackTrainCount = entries.Count(entry => entry.HasResolvedTrack);
    ResolvedResourceTrainCount = entries.Count(entry => entry.HasResolvedResource);
    ResolvedCircuitTrainCount = entries.Count(entry => entry.HasResolvedCircuit);
  }

  public static RideInstanceTrainRuntimeRegistry Build(
    RideInstanceTrackRuntimeRegistry trackRuntime,
    RideTrainInstanceResourceRegistry trainResources
  ) => Build(trackRuntime, trainResources, RideInstanceTrainRuntimeRegistryLimits.Default);

  internal static RideInstanceTrainRuntimeRegistry Build(
    RideInstanceTrackRuntimeRegistry trackRuntime,
    RideTrainInstanceResourceRegistry trainResources,
    RideInstanceTrainRuntimeRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(trackRuntime);
    ArgumentNullException.ThrowIfNull(trainResources);
    ValidateLimits(limits);
    ValidateCount(trackRuntime.Entries.Count, limits.MaximumRideInstanceCount, "ride-instance");
    ValidateCount(trainResources.Links.Count, limits.MaximumTrainCount, "saved train");

    var runtimeByInstanceId = IndexRuntime(trackRuntime.Entries);
    var trainIds = new HashSet<ulong>();
    var entries = new RideInstanceTrainRuntimeEntry[trainResources.Links.Count];
    foreach (var index in Enumerable.Range(0, trainResources.Links.Count)) {
      var train = trainResources.Links[index];
      if (train is null || train.RideInstance is null || train.TrainInstance is null)
        throw Invalid($"saved train link {index} is incomplete");
      if (train.TrainInstanceEntryId == 0 || !trainIds.Add(train.TrainInstanceEntryId))
        throw Invalid(
          $"saved train instance ID {train.TrainInstanceEntryId} is missing or duplicated");
      if (!runtimeByInstanceId.TryGetValue(train.RideInstanceEntryId, out var runtime))
        throw Invalid(
          $"saved train {train.TrainInstanceEntryId} references missing ride instance " +
          $"{train.RideInstanceEntryId}");
      if (!ReferenceEquals(runtime.Instance, train.RideInstance))
        throw Invalid(
          $"ride instance {train.RideInstanceEntryId} changed exact DAT object identity");
      if (train.TrainInstance.TrackedRideInstance != train.RideInstanceEntryId)
        throw Invalid(
          $"saved train {train.TrainInstanceEntryId} has reciprocal owner " +
          $"{train.TrainInstance.TrackedRideInstance} instead of {train.RideInstanceEntryId}");
      if (train.Ordinal < 0 || train.Ordinal >= train.RideInstance.NTrains
          || train.WhichTrain != train.Ordinal)
        throw Invalid(
          $"saved train {train.TrainInstanceEntryId} has inconsistent ordinal " +
          $"{train.Ordinal} and WhichTrain {train.WhichTrain}");

      entries[index] = new(index, runtime, train);
    }

    return new(entries, runtimeByInstanceId.Count, trainResources.SavedInstanceCount);
  }

  private static IReadOnlyDictionary<ulong, RideInstanceTrackRuntimeEntry> IndexRuntime(
    IReadOnlyList<RideInstanceTrackRuntimeEntry> runtimeEntries
  ) {
    var result = new Dictionary<ulong, RideInstanceTrackRuntimeEntry>(runtimeEntries.Count);
    foreach (var entry in runtimeEntries) {
      if (entry is null || entry.Instance is null || entry.InstanceEntryId == 0
          || !result.TryAdd(entry.InstanceEntryId, entry))
        throw Invalid(
          $"runtime ride-instance ID {entry?.InstanceEntryId ?? 0} is missing or duplicated");
    }
    return result;
  }

  private static void ValidateLimits(RideInstanceTrainRuntimeRegistryLimits limits) {
    if (limits.MaximumRideInstanceCount <= 0 || limits.MaximumTrainCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-instance train runtime {description} count exceeds the limit {maximum}.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-instance train runtime input is invalid: {message}.");
}

internal readonly record struct RideInstanceTrainRuntimeRegistryLimits(
  int MaximumRideInstanceCount,
  int MaximumTrainCount
) {
  public static RideInstanceTrainRuntimeRegistryLimits Default { get; } =
    new(100_000, 100_000);
}
