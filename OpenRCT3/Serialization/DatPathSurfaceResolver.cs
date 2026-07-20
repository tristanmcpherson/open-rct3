// DAT Path Surface Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>
/// Resolves DAT path and queue surface references without discarding raw entry IDs.
/// </summary>
internal static class DatPathSurfaceResolver {
  public static void Resolve(
    IReadOnlyList<DatPathData> paths,
    IReadOnlyList<DatPathSurfaceEntryData> surfaceEntries
  ) {
    ArgumentNullException.ThrowIfNull(paths);
    ArgumentNullException.ThrowIfNull(surfaceEntries);

    var entriesById = new Dictionary<ulong, DatPathSurfaceEntryData>();
    foreach (var entry in surfaceEntries) {
      if (entry is null)
        throw new ArgumentException(
          "Path surface entries cannot contain null.",
          nameof(surfaceEntries));
      if (!entriesById.TryAdd(entry.EntryId, entry))
        throw new InvalidDataException(
          $"DAT contains duplicate path surface entry ID {entry.EntryId}.");
    }

    ResolveQueueGroundSurfaces(surfaceEntries, entriesById);
    foreach (var path in paths) {
      if (path is null)
        throw new ArgumentException("Paths cannot contain null.", nameof(paths));
      if (path.Surface == 0 || !entriesById.TryGetValue(path.Surface, out var surface)) continue;

      ValidateSurfaceKind(path, surface);
      path.ResolveSurface(surface);
    }
  }

  private static void ResolveQueueGroundSurfaces(
    IReadOnlyList<DatPathSurfaceEntryData> surfaceEntries,
    IReadOnlyDictionary<ulong, DatPathSurfaceEntryData> entriesById
  ) {
    foreach (var entry in surfaceEntries) {
      if (entry is not DatQueueTypeGroundSurfaceData ground || ground.QueueType == 0) continue;
      if (!entriesById.TryGetValue(ground.QueueType, out var referenced)) continue;
      if (referenced is not DatQueueTypeDatabaseEntryData queueType)
        throw new InvalidDataException(
          $"QueueTypeGroundSurface entry {ground.EntryId} references path surface entry " +
          $"{ground.QueueType}, which is not a QueueTypeDatabaseEntry.");
      ground.ResolveQueueType(queueType);
    }
  }

  private static void ValidateSurfaceKind(
    DatPathData path,
    DatPathSurfaceEntryData surface
  ) {
    var valid = path.StructureKind == DatPathStructureKind.PathQueue
      ? surface is DatQueueTypeDatabaseEntryData or DatQueueTypeGroundSurfaceData
      : surface is DatPathTypeDatabaseEntryData;
    if (valid) return;

    throw new InvalidDataException(
      $"Decoded {path.StructureKind} entry {path.EntryId} references surface entry " +
      $"{surface.EntryId} of incompatible type {surface.GetType().Name}.");
  }
}
