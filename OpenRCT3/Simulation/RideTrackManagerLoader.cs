// Ride Track Manager Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>
/// Converts DAT <c>TrackPiece</c> records into semantic placements linked to their already-loaded
/// scenery instances.
/// </summary>
internal static class RideTrackManagerLoader {
  public static void Load(
    Park park,
    Terrain terrain,
    IReadOnlyList<DatTrackPieceData> trackPieces
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(trackPieces);

    var sceneryByEntryId = IndexSceneryPlacements(park.SceneryPlacements);
    var converted = new List<RideTrackPlacement>(trackPieces.Count);
    var trackEntryIds = new HashSet<ulong>();
    var sceneryEntryIds = new HashSet<ulong>();
    foreach (var source in trackPieces) {
      if (!trackEntryIds.Add(source.EntryId))
        throw new InvalidDataException(
          $"Decoded ride track contains duplicate TrackPiece entry ID {source.EntryId}.");
      if (source.SceneryItem == 0 || !sceneryEntryIds.Add(source.SceneryItem))
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {source.EntryId} has missing or duplicate SceneryItem " +
          $"reference {source.SceneryItem}.");
      if (!sceneryByEntryId.TryGetValue(source.SceneryItem, out var scenery))
        throw new InvalidDataException(
          $"Decoded TrackPiece entry {source.EntryId} references missing SceneryItem " +
          $"entry {source.SceneryItem}.");

      converted.Add(ConvertPlacement(source, scenery, terrain));
    }

    park.RideTrackPlacements.AddRange(converted);
    park.RideTrackPieceRecords.AddRange(trackPieces);
  }

  private static IReadOnlyDictionary<ulong, SceneryPlacement> IndexSceneryPlacements(
    IReadOnlyList<SceneryPlacement> placements
  ) {
    var sceneryByEntryId = new Dictionary<ulong, SceneryPlacement>();
    foreach (var placement in placements) {
      if (placement.SourceEntryId == 0) continue;
      if (!sceneryByEntryId.TryAdd(placement.SourceEntryId, placement))
        throw new InvalidDataException(
          $"Loaded scenery contains duplicate source entry ID {placement.SourceEntryId}.");
    }
    return sceneryByEntryId;
  }

  private static RideTrackPlacement ConvertPlacement(
    DatTrackPieceData source,
    SceneryPlacement scenery,
    Terrain terrain
  ) {
    var field = source.SceneryItemDataField;
    if (!terrain.HasTile(field.PosX, field.PosZ))
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {source.EntryId} is outside the terrain grid.");
    if (field.Direction is < 0 or > 3)
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {source.EntryId} has invalid DIRECTION value " +
        $"{field.Direction}.");

    var objectKey = ParseTrackSymbol(source.SymbolName, source.EntryId);
    if (source.SidDatabaseEntry != scenery.DatabaseEntryReference)
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {source.EntryId} has SID database reference " +
        $"{source.SidDatabaseEntry}, but linked SceneryItem {source.SceneryItem} uses " +
        $"{scenery.DatabaseEntryReference}.");
    if (!string.Equals(objectKey, scenery.ObjectKey, StringComparison.Ordinal))
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {source.EntryId} names track object '{objectKey}', but linked " +
        $"SceneryItem {source.SceneryItem} uses '{scenery.ObjectKey}'.");
    if (field.Corner != scenery.Corner ||
        field.Direction != scenery.SerializedDirection ||
        field.Height != scenery.SerializedHeight ||
        field.PosX != scenery.TileX ||
        field.PosZ != scenery.TileY)
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {source.EntryId} placement does not match linked SceneryItem " +
        $"{source.SceneryItem}.");
    if (scenery.OverlayPath is null)
      throw new InvalidDataException(
        $"Linked SceneryItem {source.SceneryItem} has no resolved overlay path.");

    var colours = source.FlexiColourField;
    return new RideTrackPlacement(
      source.EntryId,
      source.SceneryItem,
      source.SidDatabaseEntry,
      source.SymbolName,
      objectKey,
      scenery.OverlayPath,
      field.PosX,
      field.PosZ,
      scenery.Rotation,
      field.Direction,
      field.Height,
      field.Corner,
      source.Owner,
      source.Segment,
      source.Prev,
      source.Next,
      source.PlatformPiece,
      source.Reversed,
      source.UserAngleDegrees,
      colours.Col0,
      colours.Col1,
      colours.Col2);
  }

  private static string ParseTrackSymbol(string symbolName, ulong entryId) {
    if (string.IsNullOrWhiteSpace(symbolName))
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {entryId} has an empty SYMBOLNAME.");
    var separator = symbolName.LastIndexOf(':');
    if (separator <= 0 || separator == symbolName.Length - 1)
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {entryId} has malformed track symbol '{symbolName}'.");

    var name = symbolName[..separator];
    var tag = symbolName[(separator + 1)..];
    if (!string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
        !string.Equals(tag, tag.Trim(), StringComparison.Ordinal) ||
        !string.Equals(tag, "tks", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Decoded TrackPiece entry {entryId} has non-TKS symbol '{symbolName}'.");
    return name;
  }
}
