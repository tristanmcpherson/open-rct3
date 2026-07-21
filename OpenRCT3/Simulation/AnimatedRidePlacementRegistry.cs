// Animated Ride Placement Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRCT3.Simulation;

internal enum AnimatedRidePlacementStatus {
  Resolved,
  MissingOverlayCatalog,
  MissingAnimatedRide,
  AmbiguousAnimatedRide,
  UnresolvedRideScenery,
}

internal sealed record AnimatedRidePlacementLink(
  SceneryPlacement Placement,
  AnimatedRideResourceLink? Resource,
  AnimatedRidePlacementStatus Status
) {
  public bool IsResolved => Status == AnimatedRidePlacementStatus.Resolved;
}

/// <summary>
/// Binds DAT scenery placements to ANRs only through their exact overlay and resolved SID identity.
/// It retains typed misses and does not claim animation or motion behavior.
/// </summary>
internal sealed class AnimatedRidePlacementRegistry {
  private const int MaximumPlacements = 100_000;
  private const int MaximumResources = 100_000;
  private const int MaximumIdentifierLength = 4_096;

  public IReadOnlyList<AnimatedRidePlacementLink> Links { get; }
  public int ResolvedCount => Links.Count(link => link.IsResolved);
  public int UnresolvedCount => Links.Count - ResolvedCount;

  private AnimatedRidePlacementRegistry(IReadOnlyList<AnimatedRidePlacementLink> links) =>
    Links = Array.AsReadOnly(links.ToArray());

  public static AnimatedRidePlacementRegistry Build(
    IReadOnlyList<SceneryPlacement> placements,
    IReadOnlyList<AnimatedRideResourceLink> resources
  ) {
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(resources);
    if (placements.Count > MaximumPlacements)
      throw Invalid($"placement count exceeds {MaximumPlacements}");
    if (resources.Count > MaximumResources)
      throw Invalid($"resource count exceeds {MaximumResources}");

    var byOverlay = BuildIndex(resources);
    var entryIds = new HashSet<ulong>();
    var links = new AnimatedRidePlacementLink[placements.Count];
    foreach (var index in Enumerable.Range(0, placements.Count)) {
      var placement = placements[index];
      if (placement.SourceEntryId == 0 || !entryIds.Add(placement.SourceEntryId))
        throw Invalid(
          $"placement entry ID {placement.SourceEntryId} is missing or duplicated");
      ValidateIdentifier(placement.ObjectKey, "placement object key");
      if (placement.ObjectKey.Contains(':', StringComparison.Ordinal))
        throw Invalid($"placement object key '{placement.ObjectKey}' is tagged or ambiguous");
      if (string.IsNullOrWhiteSpace(placement.OverlayPath)) {
        links[index] = new(
          placement,
          null,
          AnimatedRidePlacementStatus.MissingOverlayCatalog);
        continue;
      }
      ValidateIdentifier(placement.OverlayPath, "placement overlay");
      if (!byOverlay.TryGetValue(placement.OverlayPath, out var byScenery)) {
        links[index] = new(
          placement,
          null,
          AnimatedRidePlacementStatus.MissingOverlayCatalog);
        continue;
      }
      if (!byScenery.TryGetValue(placement.ObjectKey, out var candidates)) {
        links[index] = new(
          placement,
          null,
          AnimatedRidePlacementStatus.MissingAnimatedRide);
        continue;
      }
      if (candidates.Count != 1) {
        links[index] = new(
          placement,
          null,
          AnimatedRidePlacementStatus.AmbiguousAnimatedRide);
        continue;
      }
      var resource = candidates[0];
      links[index] = new(
        placement,
        resource,
        resource.IsResolved
          ? AnimatedRidePlacementStatus.Resolved
          : AnimatedRidePlacementStatus.UnresolvedRideScenery);
    }
    return new AnimatedRidePlacementRegistry(links);
  }

  private static Dictionary<
    string,
    Dictionary<string, List<AnimatedRideResourceLink>>> BuildIndex(
    IReadOnlyList<AnimatedRideResourceLink> resources
  ) {
    var result = new Dictionary<
      string,
      Dictionary<string, List<AnimatedRideResourceLink>>>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource == null || resource.Ride == null || resource.Ride.Resource == null)
        throw Invalid("resource list contains an incomplete ANR link");
      ValidateIdentifier(resource.RootOverlayName, "resource root overlay");
      ValidateIdentifier(resource.Ride.Resource.Name, "decoded ANR name");
      if (resource.Scenery != null) {
        ValidateIdentifier(resource.Scenery.Resource.Name, "resolved SID name");
        var expected = ParseSidReference(resource.Ride.Resource.SceneryItemReference);
        if (!expected.Equals(
          resource.Scenery.Resource.Name,
          StringComparison.OrdinalIgnoreCase))
          throw Invalid(
            $"ANR '{resource.Ride.Resource.Name}' carries a foreign resolved SID identity");
      }

      if (!result.TryGetValue(resource.RootOverlayName, out var byScenery)) {
        byScenery = new Dictionary<string, List<AnimatedRideResourceLink>>(
          StringComparer.OrdinalIgnoreCase);
        result.Add(resource.RootOverlayName, byScenery);
      }
      var sceneryName = ParseSidReference(resource.Ride.Resource.SceneryItemReference);
      if (!byScenery.TryGetValue(sceneryName, out var candidates))
        byScenery.Add(sceneryName, candidates = []);
      candidates.Add(resource);
    }
    return result;
  }

  private static string ParseSidReference(string reference) {
    ValidateIdentifier(reference, "ANR SID reference");
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        !reference[(separator + 1)..].Equals("sid", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"ANR SID reference '{reference}' is not one exact name:sid identity");
    var name = reference[..separator];
    ValidateIdentifier(name, "ANR SID resource name");
    return name;
  }

  private static void ValidateIdentifier(string? value, string description) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdentifierLength ||
        !value.Equals(value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds {MaximumIdentifierLength} characters");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid animated-ride placement registry: {message}.");
}
