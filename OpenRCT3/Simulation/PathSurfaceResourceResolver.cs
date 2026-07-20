// Path Surface Resource Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>A decoded PTD or QTD resource linked to one DAT path surface.</summary>
internal abstract record ResolvedPathSurfaceResource(string SystemName);

/// <summary>A DAT ordinary-path surface linked to its decoded PTD resource.</summary>
internal sealed record ResolvedPathTypeResource(PathType Resource)
  : ResolvedPathSurfaceResource(Resource.InternalName);

/// <summary>A DAT queue-path surface linked to its decoded QTD resource.</summary>
internal sealed record ResolvedQueueTypeResource(QueueType Resource)
  : ResolvedPathSurfaceResource(Resource.InternalName);

/// <summary>
/// Resolves exact, case-insensitive DAT path surface system names against decoded PTD/QTD internal
/// names while keeping the two resource namespaces type-safe.
/// </summary>
internal sealed class PathSurfaceResourceResolver {
  private const int MaximumResourceCount = 100_000;
  private const int MaximumSystemNameLength = 4_096;
  private readonly IReadOnlyDictionary<string, ResolvedPathTypeResource> pathTypes;
  private readonly IReadOnlyDictionary<string, ResolvedQueueTypeResource> queueTypes;

  public PathSurfaceResourceResolver(
    IReadOnlyList<PathType> pathTypes,
    IReadOnlyList<QueueType> queueTypes
  ) {
    ArgumentNullException.ThrowIfNull(pathTypes);
    ArgumentNullException.ThrowIfNull(queueTypes);
    if (pathTypes.Count > MaximumResourceCount || queueTypes.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"Path surface resource count exceeds the resolver limit {MaximumResourceCount}.");

    this.pathTypes = BuildPathTypeIndex(pathTypes);
    this.queueTypes = BuildQueueTypeIndex(queueTypes);
  }

  public bool TryResolve(
    PathTile tile,
    out ResolvedPathSurfaceResource? resource
  ) {
    resource = null;
    var systemName = tile.SurfaceSystemName;
    if (string.IsNullOrWhiteSpace(systemName)) return false;
    if (systemName.Length > MaximumSystemNameLength)
      throw new InvalidDataException(
        $"DAT path surface system name exceeds {MaximumSystemNameLength} characters.");

    if (tile.IsQueue) {
      if (queueTypes.TryGetValue(systemName, out var queueType)) {
        resource = queueType;
        return true;
      }
      if (pathTypes.ContainsKey(systemName))
        throw TypeMismatch(systemName, expected: "QTD", actual: "PTD");
      return false;
    }

    if (pathTypes.TryGetValue(systemName, out var pathType)) {
      resource = pathType;
      return true;
    }
    if (queueTypes.ContainsKey(systemName))
      throw TypeMismatch(systemName, expected: "PTD", actual: "QTD");
    return false;
  }

  private static IReadOnlyDictionary<string, ResolvedPathTypeResource> BuildPathTypeIndex(
    IReadOnlyList<PathType> resources
  ) {
    var index = new Dictionary<string, ResolvedPathTypeResource>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource is null)
        throw new ArgumentException("PTD resources cannot contain null.", nameof(resources));
      ValidateResourceIdentity(resource.Name, resource.InternalName, "PTD");
      if (!index.TryAdd(resource.InternalName, new ResolvedPathTypeResource(resource)))
        throw new InvalidDataException(
          $"Path surface resolver has duplicate PTD internal name '{resource.InternalName}'.");
    }
    return index;
  }

  private static IReadOnlyDictionary<string, ResolvedQueueTypeResource> BuildQueueTypeIndex(
    IReadOnlyList<QueueType> resources
  ) {
    var index = new Dictionary<string, ResolvedQueueTypeResource>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource is null)
        throw new ArgumentException("QTD resources cannot contain null.", nameof(resources));
      ValidateResourceIdentity(resource.Name, resource.InternalName, "QTD");
      if (!index.TryAdd(resource.InternalName, new ResolvedQueueTypeResource(resource)))
        throw new InvalidDataException(
          $"Path surface resolver has duplicate QTD internal name '{resource.InternalName}'.");
    }
    return index;
  }

  private static void ValidateIdentifier(string value, string description) {
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidDataException(
        $"Path surface resolver has an empty {description}.");
    if (value.Length > MaximumSystemNameLength)
      throw new InvalidDataException(
        $"Path surface resolver {description} exceeds " +
        $"{MaximumSystemNameLength} characters.");
  }

  private static void ValidateResourceIdentity(
    string loaderName,
    string internalName,
    string resourceKind
  ) {
    ValidateIdentifier(loaderName, $"{resourceKind} loader name");
    ValidateIdentifier(internalName, $"{resourceKind} internal name");
    if (!string.Equals(loaderName, internalName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Path surface resolver cannot disambiguate {resourceKind} loader name '{loaderName}' " +
        $"from internal name '{internalName}'.");
  }

  private static InvalidDataException TypeMismatch(
    string systemName,
    string expected,
    string actual
  ) => new(
    $"DAT path surface '{systemName}' requires a {expected} resource but resolves only to {actual}.");
}
