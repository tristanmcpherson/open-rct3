// Ride Instance Resource Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>A decoded TRR resource with its exact DAT overlay-stem identity.</summary>
internal sealed record RideInstanceResourceSource(
  string OverlayName,
  OvlFile File,
  TrackedRide Resource);

/// <summary>
/// One DAT ride instance linked to its exact decoded TRR source, or retained as an unresolved
/// external/custom identity.
/// </summary>
internal sealed record RideInstanceResourceLink(
  DatTrackedRideInstanceData Instance,
  RideInstanceResourceSource? Source
) {
  public bool IsResolved => Source != null;
  public TrackedRide? Resource => Source?.Resource;
}

/// <summary>
/// Resolves the exact DAT overlay stem plus tagged <c>name:trr</c> identity against decoded OVL
/// tracked rides. No path, separator, extension, or symbol normalization is attempted.
/// </summary>
internal sealed class RideInstanceResourceResolver {
  private const int MaximumResourceCount = 100_000;
  private const int MaximumInstanceCount = 100_000;
  private const int MaximumIdentifierLength = 4_096;
  private readonly Dictionary<
    string,
    Dictionary<string, RideInstanceResourceSource>> resourcesByOverlay;

  public RideInstanceResourceResolver(IReadOnlyList<RideInstanceResourceSource> resources) {
    ArgumentNullException.ThrowIfNull(resources);
    if (resources.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"Ride-instance resource count exceeds the resolver limit {MaximumResourceCount}.");

    resourcesByOverlay = new Dictionary<
      string,
      Dictionary<string, RideInstanceResourceSource>>(
        resources.Count,
        StringComparer.OrdinalIgnoreCase);
    foreach (var source in resources) AddResource(source);
  }

  /// <summary>
  /// Resolves one instance while retaining valid identities whose OVL is not loaded.
  /// </summary>
  public RideInstanceResourceLink Resolve(DatTrackedRideInstanceData instance) {
    ArgumentNullException.ThrowIfNull(instance);
    ValidateInstanceEntryId(instance.EntryId);
    ValidateIdentifier(instance.TrackedRideOverlayName, "DAT tracked-ride overlay name");
    var resourceName = ParseTrackedRideSymbol(instance.TrackedRideSymbolName);

    if (resourcesByOverlay.TryGetValue(instance.TrackedRideOverlayName, out var byName)
      && byName.TryGetValue(resourceName, out var source))
      return new RideInstanceResourceLink(instance, source);
    return new RideInstanceResourceLink(instance, null);
  }

  /// <summary>Resolves a bounded set and rejects duplicate DAT instance identities.</summary>
  public IReadOnlyList<RideInstanceResourceLink> ResolveAll(
    IReadOnlyList<DatTrackedRideInstanceData> instances
  ) {
    ArgumentNullException.ThrowIfNull(instances);
    if (instances.Count > MaximumInstanceCount)
      throw new InvalidDataException(
        $"Ride-instance count exceeds the resolver limit {MaximumInstanceCount}.");

    var entryIds = new HashSet<ulong>();
    var links = new RideInstanceResourceLink[instances.Count];
    for (var index = 0; index < instances.Count; index++) {
      var instance = instances[index]
        ?? throw new ArgumentException(
          "Ride instances cannot contain null.",
          nameof(instances));
      if (!entryIds.Add(instance.EntryId))
        throw new InvalidDataException(
          $"Ride-instance resolver has duplicate DAT entry ID {instance.EntryId}.");
      links[index] = Resolve(instance);
    }
    return Array.AsReadOnly(links);
  }

  private void AddResource(RideInstanceResourceSource source) {
    if (source is null)
      throw new ArgumentException(
        "Tracked-ride resources cannot contain null.",
        nameof(source));
    if (source.File is null || source.Resource is null)
      throw new ArgumentException(
        "Tracked-ride resource sources require an OVL file and decoded resource.",
        nameof(source));

    ValidateIdentifier(source.OverlayName, "TRR overlay name");
    ValidateBareResourceName(source.File.Name, "TRR OVL file name");
    ValidateBareResourceName(source.Resource.Name, "decoded TRR resource name");
    if (source.File.Type != FileType.TrackedRide)
      throw new InvalidDataException(
        $"Ride-instance resource '{source.File.Name}' has OVL type " +
        $"'{source.File.Type.ToTagString()}' instead of 'trr'.");
    if (!string.Equals(
      source.File.Name,
      source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Ride-instance resource cannot disambiguate OVL file name '{source.File.Name}' " +
        $"from decoded TRR name '{source.Resource.Name}'.");

    if (!resourcesByOverlay.TryGetValue(source.OverlayName, out var byName)) {
      byName = new Dictionary<string, RideInstanceResourceSource>(
        StringComparer.OrdinalIgnoreCase);
      resourcesByOverlay.Add(source.OverlayName, byName);
    }
    if (!byName.TryAdd(source.Resource.Name, source))
      throw new InvalidDataException(
        $"Ride-instance resolver has duplicate TRR identity " +
        $"'{source.OverlayName}|{source.Resource.Name}:trr'.");
  }

  private static string ParseTrackedRideSymbol(string reference) {
    ValidateIdentifier(reference, "DAT tracked-ride symbol name");
    var separator = reference.IndexOf(':');
    if (separator <= 0
      || separator != reference.LastIndexOf(':')
      || separator == reference.Length - 1)
      throw new InvalidDataException(
        $"DAT tracked-ride symbol '{reference}' is not one exact tagged identity.");

    var name = reference[..separator];
    var tag = reference[(separator + 1)..];
    ValidateIdentifier(name, "DAT tracked-ride resource name");
    if (!string.Equals(tag, "trr", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"DAT tracked-ride symbol '{reference}' identifies '{tag}' instead of 'trr'.");
    return name;
  }

  private static void ValidateBareResourceName(string value, string description) {
    ValidateIdentifier(value, description);
    if (value.Contains(':', StringComparison.Ordinal))
      throw new InvalidDataException(
        $"Ride-instance resolver {description} '{value}' is tagged or type-ambiguous.");
  }

  private static void ValidateIdentifier(string value, string description) {
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidDataException(
        $"Ride-instance resolver has an empty {description}.");
    if (value.Length > MaximumIdentifierLength)
      throw new InvalidDataException(
        $"Ride-instance resolver {description} exceeds " +
        $"{MaximumIdentifierLength} characters.");
  }

  private static void ValidateInstanceEntryId(ulong entryId) {
    if (entryId == 0)
      throw new InvalidDataException(
        "Ride-instance resolver has a missing DAT entry ID 0.");
  }
}
