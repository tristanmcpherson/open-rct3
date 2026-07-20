// Ride Train Instance Resource Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>A decoded RIT source paired with its exact saved overlay-stem identity.</summary>
internal sealed record RideTrainInstanceResourceSource(
  string OverlayName,
  RideTrainResourceSource Source);

/// <summary>
/// One forward DAT ride-to-train reference linked only to its exact saved RIT identity.
/// </summary>
internal sealed record RideTrainInstanceResourceLink(
  DatTrackedRideInstanceData RideInstance,
  DatRideTrainInstanceData TrainInstance,
  int Ordinal,
  RideTrainResourceSource? Source
) {
  public ulong RideInstanceEntryId => RideInstance.EntryId;
  public ulong TrainInstanceEntryId => TrainInstance.EntryId;
  public int WhichTrain => TrainInstance.WhichTrain;
  public float Length => TrainInstance.Length;
  public float Mass => TrainInstance.Mass;
  public bool HasSavedMotionState => TrainInstance.HasSavedMotionState;
  public float SavedDistance => TrainInstance.Distance;
  public bool SavedReversed => TrainInstance.Reversed;
  public float SavedSpeed => TrainInstance.Speed;
  public bool HasSavedOperationalState => TrainInstance.HasSavedOperationalState;
  public int SavedOperationalState => TrainInstance.State;
  public float SavedOperationalStateTime => TrainInstance.StateTime;
  public int? SavedVisualVariant => TrainInstance.WhichRideCarSivVariant;
  public bool IsResolved => Source != null;
  public RideTrain? Resource => Source?.Resource;
}

/// <summary>
/// Immutable links for saved ride-train instances. Compatible TRR choices are never substituted
/// for a missing saved overlay-and-symbol identity.
/// </summary>
internal sealed class RideTrainInstanceResourceRegistry {
  private const int MaximumRideInstanceCount = 100_000;
  private const int MaximumTrainInstanceCount = 100_000;
  private const int MaximumResourceCount = 100_000;
  private const int MaximumIdentifierLength = 4_096;

  public static RideTrainInstanceResourceRegistry Empty { get; } = new([], []);

  public IReadOnlyList<RideTrainInstanceResourceLink> Links { get; }
  public IReadOnlyList<DatRideTrainInstanceData> UnreferencedInstances { get; }
  public int SavedInstanceCount => Links.Count + UnreferencedInstances.Count;
  public int ResolvedInstanceCount { get; }
  public int UnresolvedInstanceCount => SavedInstanceCount - ResolvedInstanceCount;

  private RideTrainInstanceResourceRegistry(
    IReadOnlyList<RideTrainInstanceResourceLink> links,
    IReadOnlyList<DatRideTrainInstanceData> unreferencedInstances
  ) {
    Links = Array.AsReadOnly(links.ToArray());
    UnreferencedInstances = Array.AsReadOnly(unreferencedInstances.ToArray());
    ResolvedInstanceCount = Links.Count(link => link.IsResolved);
  }

  public static RideTrainInstanceResourceRegistry Build(
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> trainInstances,
    IReadOnlyList<RideTrainInstanceResourceSource> resources
  ) {
    ArgumentNullException.ThrowIfNull(rideInstances);
    ArgumentNullException.ThrowIfNull(trainInstances);
    ArgumentNullException.ThrowIfNull(resources);
    ValidateCount(rideInstances.Count, MaximumRideInstanceCount, "ride-instance");
    ValidateCount(trainInstances.Count, MaximumTrainInstanceCount, "ride-train-instance");
    ValidateCount(resources.Count, MaximumResourceCount, "saved RIT resource");

    var resourcesByOverlay = BuildResourceIndex(resources);
    var trainsById = BuildTrainIndex(trainInstances);
    var rideIds = new HashSet<ulong>();
    var referencedTrainIds = new HashSet<ulong>();
    var links = new List<RideTrainInstanceResourceLink>(trainInstances.Count);
    foreach (var ride in rideInstances) {
      if (ride is null)
        throw new ArgumentException(
          "Ride instances cannot contain null.",
          nameof(rideInstances));
      if (ride.EntryId == 0 || !rideIds.Add(ride.EntryId))
        throw Invalid($"ride-instance entry ID {ride.EntryId} is missing or duplicated");
      if (ride.NTrains < 0 || ride.NTrains > MaximumTrainInstanceCount)
        throw Invalid(
          $"ride instance {ride.EntryId} train count {ride.NTrains} is outside 0 through " +
          $"{MaximumTrainInstanceCount}");
      if (ride.Trains.Count != ride.NTrains)
        throw Invalid(
          $"ride instance {ride.EntryId} declares {ride.NTrains} trains but references " +
          $"{ride.Trains.Count}");

      for (var ordinal = 0; ordinal < ride.Trains.Count; ordinal++) {
        var trainId = ride.Trains[ordinal];
        if (trainId == 0 || !referencedTrainIds.Add(trainId))
          throw Invalid(
            $"ride-train-instance entry ID {trainId} is missing or referenced more than once");
        if (!trainsById.TryGetValue(trainId, out var train))
          throw Invalid(
            $"ride instance {ride.EntryId} references missing ride-train instance {trainId}");
        if (train.TrackedRideInstance != ride.EntryId)
          throw Invalid(
            $"ride instance {ride.EntryId} references ride-train instance {trainId}, whose " +
            $"reciprocal owner is {train.TrackedRideInstance}");
        if (train.WhichTrain < 0 || train.WhichTrain >= ride.NTrains)
          throw Invalid(
            $"ride-train instance {trainId} index {train.WhichTrain} is outside ride " +
            $"{ride.EntryId}'s {ride.NTrains} trains");
        if (train.WhichTrain != ordinal)
          throw Invalid(
            $"ride-train instance {trainId} index {train.WhichTrain} does not match forward " +
            $"reference ordinal {ordinal} on ride {ride.EntryId}");

        var resourceName = ParseRideTrainSymbol(train.RideTrainSymbolName);
        RideTrainResourceSource? resource = null;
        if (resourcesByOverlay.TryGetValue(train.RideTrainOverlayName, out var byName))
          byName.TryGetValue(resourceName, out resource);
        links.Add(new RideTrainInstanceResourceLink(ride, train, ordinal, resource));
      }
    }

    var unreferenced = trainInstances
      .Where(train => !referencedTrainIds.Contains(train.EntryId))
      .ToArray();
    return new RideTrainInstanceResourceRegistry(links, unreferenced);
  }

  private static Dictionary<string, Dictionary<string, RideTrainResourceSource>>
    BuildResourceIndex(IReadOnlyList<RideTrainInstanceResourceSource> resources) {
    var result = new Dictionary<string, Dictionary<string, RideTrainResourceSource>>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource is null)
        throw new ArgumentException(
          "Saved RIT resources cannot contain null.",
          nameof(resources));
      ValidateIdentifier(resource.OverlayName, "saved RIT overlay name");
      if (resource.Source is null ||
          resource.Source.File is null ||
          resource.Source.Resource is null ||
          resource.Source.AllowedArchivePaths is null)
        throw new ArgumentException(
          "Saved RIT resources require a decoded source and dependency closure.",
          nameof(resources));
      if (resource.Source.File.Type != FileType.RideTrain)
        throw Invalid(
          $"saved RIT source '{resource.Source.File.Name}' has OVL type " +
          $"'{resource.Source.File.Type.ToTagString()}' instead of 'rit'");
      ValidateBareName(resource.Source.File.Name, "saved RIT OVL file name");
      ValidateBareName(resource.Source.Resource.Name, "saved decoded RIT name");
      if (!string.Equals(
        resource.Source.File.Name,
        resource.Source.Resource.Name,
        StringComparison.OrdinalIgnoreCase))
        throw Invalid(
          $"saved RIT OVL identity '{resource.Source.File.Name}' does not match decoded name " +
          $"'{resource.Source.Resource.Name}'");

      if (!result.TryGetValue(resource.OverlayName, out var byName)) {
        byName = new Dictionary<string, RideTrainResourceSource>(
          StringComparer.OrdinalIgnoreCase);
        result.Add(resource.OverlayName, byName);
      }
      if (!byName.TryAdd(resource.Source.Resource.Name, resource.Source))
        throw Invalid(
          $"duplicate saved RIT identity '{resource.OverlayName}|" +
          $"{resource.Source.Resource.Name}:rit'");
    }
    return result;
  }

  private static Dictionary<ulong, DatRideTrainInstanceData> BuildTrainIndex(
    IReadOnlyList<DatRideTrainInstanceData> trainInstances
  ) {
    var result = new Dictionary<ulong, DatRideTrainInstanceData>(trainInstances.Count);
    foreach (var train in trainInstances) {
      if (train is null)
        throw new ArgumentException(
          "Ride-train instances cannot contain null.",
          nameof(trainInstances));
      if (train.EntryId == 0 || !result.TryAdd(train.EntryId, train))
        throw Invalid(
          $"ride-train-instance entry ID {train.EntryId} is missing or duplicated");
      ValidateIdentifier(train.RideTrainOverlayName, "DAT ride-train overlay name");
      _ = ParseRideTrainSymbol(train.RideTrainSymbolName);
      if (!float.IsFinite(train.Length) || !float.IsFinite(train.Mass))
        throw Invalid($"ride-train instance {train.EntryId} has non-finite dimensions");
    }
    return result;
  }

  private static string ParseRideTrainSymbol(string reference) {
    ValidateIdentifier(reference, "DAT ride-train symbol");
    var separator = reference.IndexOf(':');
    if (separator <= 0 ||
        separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals("rit", StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"DAT ride-train symbol '{reference}' is not one exact name:rit identity");
    var name = reference[..separator];
    ValidateBareName(name, "DAT ride-train resource name");
    return name;
  }

  private static void ValidateBareName(string value, string description) {
    ValidateIdentifier(value, description);
    if (value.Contains(':', StringComparison.Ordinal))
      throw Invalid($"{description} '{value}' is tagged or type-ambiguous");
  }

  private static void ValidateIdentifier(string value, string description) {
    if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{description} is empty");
    if (value.Length > MaximumIdentifierLength)
      throw Invalid($"{description} exceeds {MaximumIdentifierLength} characters");
    if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid($"{description} has outer whitespace");
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw Invalid($"{description} count {count} exceeds {maximum}");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid saved ride-train resource registry: {message}.");
}
