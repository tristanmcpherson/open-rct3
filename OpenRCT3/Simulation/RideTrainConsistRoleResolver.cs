// Ride Train Consist Role Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One runtime consist entry resolved from a configured RIT car count.</summary>
public readonly record struct RideTrainConsistRoleEntry(
  int RuntimeIndex,
  int? NonLinkIndex,
  RideTrainCarRole Role,
  string ResourceName,
  int PeepSlotCount
) {
  private readonly bool? _countsTowardConfiguredCarCount;

  /// <summary>Whether this entry consumes one configured car-count slot.</summary>
  public bool CountsTowardConfiguredCarCount =>
    _countsTowardConfiguredCarCount ?? PeepSlotCount > 0;

  internal RideTrainConsistRoleEntry(
    int runtimeIndex,
    int? nonLinkIndex,
    RideTrainCarRole role,
    string resourceName,
    int peepSlotCount,
    bool countsTowardConfiguredCarCount
  ) : this(runtimeIndex, nonLinkIndex, role, resourceName, peepSlotCount) {
    _countsTowardConfiguredCarCount = countsTowardConfiguredCarCount;
  }
}

/// <summary>An ordered RCT3 runtime consist-role resolution.</summary>
public sealed class RideTrainConsistRoleResolution {
  public int EffectiveCarCount { get; }
  public IReadOnlyList<RideTrainConsistRoleEntry> Entries { get; }

  internal RideTrainConsistRoleResolution(
    int effectiveCarCount,
    RideTrainConsistRoleEntry[] entries
  ) {
    EffectiveCarCount = effectiveCarCount;
    Entries = Array.AsReadOnly((RideTrainConsistRoleEntry[])entries.Clone());
  }
}

/// <summary>Builds the exact ordered car-role sequence used by RCT3 ride trains.</summary>
/// <remarks>
/// The configured count covers entries with at least one <c>Peep</c> slot. RCT3 adds each
/// referenced zero-slot ordinary role without consuming that count. A zero-slot Link is then
/// inserted between adjacent ordinary entries. This resolver selects roles only; it does not
/// infer car dimensions, spacing, track cursors, speed, or physics state.
/// </remarks>
public static class RideTrainConsistRoleResolver {
  /// <summary>Resolves one ordered consist from saved <c>NCarsPerTrain</c> state.</summary>
  public static RideTrainConsistRoleResolution Resolve(
    RideTrainCars cars,
    int savedCarCount,
    Func<string, int> getPeepSlotCount
  ) => Resolve(
    cars,
    savedCarCount,
    getPeepSlotCount,
    RideTrainConsistRoleLimits.Default);

  /// <summary>
  /// Resolves the exact saved car order by treating the RIT car fields as a resource palette.
  /// </summary>
  public static RideTrainConsistRoleResolution ResolveSaved(
    RideTrainCars cars,
    IReadOnlyList<RideTrainCarRole> savedRoles,
    int savedConfiguredCarCount,
    Func<string, int> getPeepSlotCount
  ) => ResolveSaved(
    cars,
    savedRoles,
    savedConfiguredCarCount,
    getPeepSlotCount,
    RideTrainConsistRoleLimits.Default);

  internal static RideTrainConsistRoleResolution ResolveSaved(
    RideTrainCars cars,
    IReadOnlyList<RideTrainCarRole> savedRoles,
    int savedConfiguredCarCount,
    Func<string, int> getPeepSlotCount,
    RideTrainConsistRoleLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(cars);
    ArgumentNullException.ThrowIfNull(savedRoles);
    ArgumentNullException.ThrowIfNull(getPeepSlotCount);
    if (limits.MaximumRuntimeEntryCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
    if (cars.MaximumCount == 0 || cars.MinimumCount > cars.MaximumCount)
      throw new InvalidDataException("RIT car-count limits are invalid.");
    if (savedRoles.Count == 0)
      throw new InvalidDataException("Saved ride train has no car-role selections.");
    if (savedRoles.Count > limits.MaximumRuntimeEntryCount)
      throw new InvalidDataException(
        $"Saved ride-train consist entry count {savedRoles.Count} exceeds the bound " +
        $"{limits.MaximumRuntimeEntryCount}.");
    if (savedConfiguredCarCount <= 0)
      throw new InvalidDataException("Saved configured car count must be positive.");

    string? Reference(RideTrainCarRole role, string? resourceName) {
      if (resourceName == null) return null;
      if (string.IsNullOrWhiteSpace(resourceName))
        throw new InvalidDataException($"RIT {role} car reference is empty.");
      return resourceName;
    }

    var references = new string?[] {
      Reference(RideTrainCarRole.Front, cars.Front),
      Reference(RideTrainCarRole.Second, cars.Second),
      Reference(RideTrainCarRole.Middle, cars.Middle),
      Reference(RideTrainCarRole.Penultimate, cars.Penultimate),
      Reference(RideTrainCarRole.Rear, cars.Rear),
      Reference(RideTrainCarRole.Link, cars.Link),
    };
    if (references[Convert.ToInt32(RideTrainCarRole.Front)] == null)
      throw new InvalidDataException("RIT Front car reference is missing.");

    var peepCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    var countedCarCount = 0;
    var nextNonLinkIndex = 0;
    var result = new RideTrainConsistRoleEntry[savedRoles.Count];
    foreach (var runtimeIndex in Enumerable.Range(0, result.Length)) {
      var role = savedRoles[runtimeIndex];
      var roleValue = Convert.ToInt32(role);
      if (roleValue < Convert.ToInt32(RideTrainCarRole.Front) ||
          roleValue > Convert.ToInt32(RideTrainCarRole.Link))
        throw new InvalidDataException(
          $"Saved ride-train car {runtimeIndex} has unsupported RIT role {role}.");
      var resourceName = references[roleValue]
        ?? throw new InvalidDataException(
          $"Saved ride-train car {runtimeIndex} selects undeclared RIT {role} role.");
      if (!peepCounts.TryGetValue(resourceName, out var peepSlotCount)) {
        peepSlotCount = getPeepSlotCount(resourceName);
        if (peepSlotCount < 0)
          throw new InvalidDataException(
            $"RIT {role} car '{resourceName}' has a negative Peep-slot count.");
        peepCounts.Add(resourceName, peepSlotCount);
      }
      var source = new RoleSource(role, resourceName, peepSlotCount);
      int? nonLinkIndex = role == RideTrainCarRole.Link ? null : nextNonLinkIndex++;
      result[runtimeIndex] = Entry(
        runtimeIndex,
        nonLinkIndex,
        source,
        role != RideTrainCarRole.Link);
      // The saved DAT role is authoritative membership evidence. Some real ordinary ride cars,
      // including Seizmic's Front car, deliberately expose no Peep markers.
      if (role != RideTrainCarRole.Link) countedCarCount++;
    }

    if (countedCarCount != savedConfiguredCarCount)
      throw new InvalidDataException(
        $"Saved ride-train roles contain {countedCarCount} configured cars, but the ride " +
        $"declares {savedConfiguredCarCount}.");
    if (countedCarCount < cars.MinimumCount || countedCarCount > cars.MaximumCount)
      throw new InvalidDataException(
        $"Saved configured car count {countedCarCount} is outside RIT limits " +
        $"{cars.MinimumCount} through {cars.MaximumCount}.");

    return new(countedCarCount, result);
  }

  internal static RideTrainConsistRoleResolution Resolve(
    RideTrainCars cars,
    int savedCarCount,
    Func<string, int> getPeepSlotCount,
    RideTrainConsistRoleLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(cars);
    ArgumentNullException.ThrowIfNull(getPeepSlotCount);
    if (limits.MaximumRuntimeEntryCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
    if (cars.MaximumCount == 0 || cars.MinimumCount > cars.MaximumCount)
      throw new InvalidDataException("RIT car-count limits are invalid.");

    var effectiveCount = savedCarCount <= 0
      ? cars.DefaultCount
      : Convert.ToUInt32(savedCarCount);
    if (effectiveCount > cars.MaximumCount)
      effectiveCount = cars.MaximumCount;
    else if (effectiveCount < cars.MinimumCount)
      effectiveCount = cars.MinimumCount;
    if (effectiveCount == 0 || effectiveCount > Convert.ToUInt32(int.MaxValue))
      throw new InvalidDataException("RIT effective car count is outside the supported range.");

    var peepCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    RoleSource? Source(RideTrainCarRole role, string? resourceName) {
      if (resourceName == null) return null;
      if (string.IsNullOrWhiteSpace(resourceName))
        throw new InvalidDataException($"RIT {role} car reference is empty.");
      if (!peepCounts.TryGetValue(resourceName, out var peepSlotCount)) {
        peepSlotCount = getPeepSlotCount(resourceName);
        if (peepSlotCount < 0)
          throw new InvalidDataException(
            $"RIT {role} car '{resourceName}' has a negative Peep-slot count.");
        peepCounts.Add(resourceName, peepSlotCount);
      }
      return new(role, resourceName, peepSlotCount);
    }

    var sources = new RoleSource?[] {
      Source(RideTrainCarRole.Front, cars.Front),
      Source(RideTrainCarRole.Second, cars.Second),
      Source(RideTrainCarRole.Middle, cars.Middle),
      Source(RideTrainCarRole.Penultimate, cars.Penultimate),
      Source(RideTrainCarRole.Rear, cars.Rear),
      Source(RideTrainCarRole.Link, cars.Link),
    };
    if (sources[Convert.ToInt32(RideTrainCarRole.Front)] == null)
      throw new InvalidDataException("RIT Front car reference is missing.");

    var nonLinkCount = Convert.ToInt64(effectiveCount);
    foreach (var role in OrdinaryRoles) {
      var source = sources[Convert.ToInt32(role)];
      if (source is { PeepSlotCount: 0 }) nonLinkCount++;
    }

    var link = sources[Convert.ToInt32(RideTrainCarRole.Link)];
    var insertLink = link is { PeepSlotCount: 0 };
    var runtimeCount = insertLink
      ? checked((nonLinkCount * 2) - 1)
      : nonLinkCount;
    if (runtimeCount > limits.MaximumRuntimeEntryCount)
      throw new InvalidDataException(
        $"Ride-train consist entry count {runtimeCount} exceeds the bound " +
        $"{limits.MaximumRuntimeEntryCount}.");

    var result = new RideTrainConsistRoleEntry[Convert.ToInt32(runtimeCount)];
    foreach (var runtimeIndex in Enumerable.Range(0, result.Length)) {
      if (insertLink && (runtimeIndex & 1) != 0 && runtimeIndex != result.Length - 1) {
        result[runtimeIndex] = Entry(runtimeIndex, null, link.GetValueOrDefault());
        continue;
      }

      var nonLinkIndex = insertLink ? runtimeIndex / 2 : runtimeIndex;
      var logicalNonLinkCount = insertLink ? (result.Length + 1) / 2 : result.Length;
      var source = SelectOrdinaryRole(sources, nonLinkIndex, logicalNonLinkCount);
      result[runtimeIndex] = Entry(runtimeIndex, nonLinkIndex, source);
    }

    return new(Convert.ToInt32(effectiveCount), result);
  }

  private static RideTrainConsistRoleEntry Entry(
    int runtimeIndex,
    int? nonLinkIndex,
    RoleSource source,
    bool? countsTowardConfiguredCarCount = null
  ) {
    var inferredMembership = source.PeepSlotCount > 0;
    if (!countsTowardConfiguredCarCount.HasValue ||
        countsTowardConfiguredCarCount.Value == inferredMembership)
      return new(
        runtimeIndex,
        nonLinkIndex,
        source.Role,
        source.ResourceName,
        source.PeepSlotCount);
    return new(
      runtimeIndex,
      nonLinkIndex,
      source.Role,
      source.ResourceName,
      source.PeepSlotCount,
      countsTowardConfiguredCarCount.Value);
  }

  private static RoleSource SelectOrdinaryRole(
    RoleSource?[] sources,
    int index,
    int count
  ) {
    if (index == count - 1)
      return SelectRequired(
        sources,
        RideTrainCarRole.Rear,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Front);
    if (index == 0)
      return SelectRequired(
        sources,
        RideTrainCarRole.Front,
        RideTrainCarRole.Second,
        RideTrainCarRole.Middle);
    if (index == 1)
      return SelectRequired(
        sources,
        RideTrainCarRole.Second,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Front);
    if (index == count - 2)
      return SelectRequired(
        sources,
        RideTrainCarRole.Penultimate,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Front);
    return SelectRequired(
      sources,
      RideTrainCarRole.Middle,
      RideTrainCarRole.Front);
  }

  private static RoleSource SelectRequired(
    RoleSource?[] sources,
    params RideTrainCarRole[] candidates
  ) {
    foreach (var role in candidates) {
      var source = sources[Convert.ToInt32(role)];
      if (source != null) return source.Value;
    }
    throw new InvalidDataException("RIT car-role sequence has no usable ordinary car.");
  }

  private static readonly RideTrainCarRole[] OrdinaryRoles = [
    RideTrainCarRole.Front,
    RideTrainCarRole.Second,
    RideTrainCarRole.Middle,
    RideTrainCarRole.Penultimate,
    RideTrainCarRole.Rear,
  ];

  private readonly record struct RoleSource(
    RideTrainCarRole Role,
    string ResourceName,
    int PeepSlotCount);
}

internal readonly record struct RideTrainConsistRoleLimits(int MaximumRuntimeEntryCount) {
  public static RideTrainConsistRoleLimits Default { get; } = new(16_384);
}
