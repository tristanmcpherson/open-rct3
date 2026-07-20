// Ride Track Section Resource Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>A decoded TKS resource with its exact DAT overlay-stem identity.</summary>
internal sealed record RideTrackSectionResourceSource(
  string OverlayPath,
  OvlFile File,
  TrackSection Resource);

/// <summary>
/// One semantic DAT track placement linked to its exact decoded TKS source, or retained as an
/// unresolved external/custom identity.
/// </summary>
internal sealed record RideTrackSectionResourceLink(
  RideTrackPlacement Placement,
  RideTrackSectionResourceSource? Source
) {
  public bool IsResolved => Source != null;
  public TrackSection? Resource => Source?.Resource;
}

/// <summary>
/// Resolves the exact DAT overlay stem plus tagged <c>name:tks</c> identity against decoded OVL
/// track sections. No path, separator, extension, or symbol normalization is attempted.
/// </summary>
internal sealed class RideTrackSectionResourceResolver {
  private const int MaximumResourceCount = 100_000;
  private const int MaximumPlacementCount = 100_000;
  private const int MaximumIdentifierLength = 4_096;
  private readonly Dictionary<
    string,
    Dictionary<string, RideTrackSectionResourceSource>> resourcesByOverlay;

  public RideTrackSectionResourceResolver(
    IReadOnlyList<RideTrackSectionResourceSource> resources
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    if (resources.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"Ride-track-section resource count exceeds the resolver limit {MaximumResourceCount}.");

    resourcesByOverlay = new Dictionary<
      string,
      Dictionary<string, RideTrackSectionResourceSource>>(
        resources.Count,
        StringComparer.OrdinalIgnoreCase);
    foreach (var source in resources) AddResource(source);
  }

  /// <summary>
  /// Resolves one placement while retaining valid identities whose OVL is not loaded.
  /// </summary>
  public RideTrackSectionResourceLink Resolve(RideTrackPlacement placement) {
    ArgumentNullException.ThrowIfNull(placement);
    ValidatePlacementEntryId(placement.SourceEntryId);
    ValidateIdentifier(placement.OverlayPath, "DAT track overlay path");
    ValidateBareResourceName(placement.ObjectKey, "DAT track object key");
    var resourceName = ParseTrackSectionSymbol(placement.SymbolName);
    if (!string.Equals(
      placement.ObjectKey,
      resourceName,
      StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Ride-track-section resolver cannot disambiguate DAT object key " +
        $"'{placement.ObjectKey}' from symbol '{placement.SymbolName}'.");

    if (resourcesByOverlay.TryGetValue(placement.OverlayPath, out var byName)
      && byName.TryGetValue(resourceName, out var source))
      return new RideTrackSectionResourceLink(placement, source);
    return new RideTrackSectionResourceLink(placement, null);
  }

  /// <summary>Resolves a bounded set and rejects duplicate DAT TrackPiece identities.</summary>
  public IReadOnlyList<RideTrackSectionResourceLink> ResolveAll(
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    ArgumentNullException.ThrowIfNull(placements);
    if (placements.Count > MaximumPlacementCount)
      throw new InvalidDataException(
        $"Ride-track placement count exceeds the resolver limit {MaximumPlacementCount}.");

    var entryIds = new HashSet<ulong>();
    var links = new RideTrackSectionResourceLink[placements.Count];
    foreach (var index in Enumerable.Range(0, placements.Count)) {
      var placement = placements[index]
        ?? throw new ArgumentException(
          "Ride-track placements cannot contain null.",
          nameof(placements));
      if (!entryIds.Add(placement.SourceEntryId))
        throw new InvalidDataException(
          $"Ride-track-section resolver has duplicate DAT entry ID " +
          $"{placement.SourceEntryId}.");
      links[index] = Resolve(placement);
    }
    return Array.AsReadOnly(links);
  }

  private void AddResource(RideTrackSectionResourceSource source) {
    if (source is null)
      throw new ArgumentException(
        "Track-section resources cannot contain null.",
        nameof(source));
    if (source.File is null || source.Resource is null)
      throw new ArgumentException(
        "Track-section resource sources require an OVL file and decoded resource.",
        nameof(source));

    ValidateIdentifier(source.OverlayPath, "TKS overlay path");
    ValidateBareResourceName(source.File.Name, "TKS OVL file name");
    ValidateBareResourceName(source.Resource.Name, "decoded TKS resource name");
    if (source.File.Type != FileType.TrackSection)
      throw new InvalidDataException(
        $"Ride-track-section resource '{source.File.Name}' has OVL type " +
        $"'{source.File.Type.ToTagString()}' instead of 'tks'.");
    if (!string.Equals(
      source.File.Name,
      source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Ride-track-section resource cannot disambiguate OVL file name " +
        $"'{source.File.Name}' from decoded TKS name '{source.Resource.Name}'.");

    if (!resourcesByOverlay.TryGetValue(source.OverlayPath, out var byName)) {
      byName = new Dictionary<string, RideTrackSectionResourceSource>(
        StringComparer.OrdinalIgnoreCase);
      resourcesByOverlay.Add(source.OverlayPath, byName);
    }
    if (!byName.TryAdd(source.Resource.Name, source))
      throw new InvalidDataException(
        $"Ride-track-section resolver has duplicate TKS identity " +
        $"'{source.OverlayPath}|{source.Resource.Name}:tks'.");
  }

  private static string ParseTrackSectionSymbol(string reference) {
    ValidateIdentifier(reference, "DAT track symbol name");
    var separator = reference.IndexOf(':');
    if (separator <= 0
      || separator != reference.LastIndexOf(':')
      || separator == reference.Length - 1)
      throw new InvalidDataException(
        $"DAT track symbol '{reference}' is not one exact tagged identity.");

    var name = reference[..separator];
    var tag = reference[(separator + 1)..];
    ValidateIdentifier(name, "DAT track-section resource name");
    if (!string.Equals(tag, "tks", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"DAT track symbol '{reference}' identifies '{tag}' instead of 'tks'.");
    return name;
  }

  private static void ValidateBareResourceName(string value, string description) {
    ValidateIdentifier(value, description);
    if (value.Contains(':', StringComparison.Ordinal))
      throw new InvalidDataException(
        $"Ride-track-section resolver {description} '{value}' is tagged or type-ambiguous.");
  }

  private static void ValidateIdentifier(string value, string description) {
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidDataException(
        $"Ride-track-section resolver has an empty {description}.");
    if (value.Length > MaximumIdentifierLength)
      throw new InvalidDataException(
        $"Ride-track-section resolver {description} exceeds " +
        $"{MaximumIdentifierLength} characters.");
  }

  private static void ValidatePlacementEntryId(ulong entryId) {
    if (entryId == 0)
      throw new InvalidDataException(
        "Ride-track-section resolver has a missing DAT entry ID 0.");
  }
}
