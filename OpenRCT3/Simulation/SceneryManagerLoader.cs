// Scenery Manager Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>Converts ordinary RCT3 DAT scenery items into renderable park placements.</summary>
internal static class SceneryManagerLoader {
  public static void Load(
    Park park,
    Terrain terrain,
    IReadOnlyList<DatSceneryItemData> sceneryItems,
    IReadOnlyList<DatSceneryItemPlacementSingleData> placementSingles
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(sceneryItems);
    ArgumentNullException.ThrowIfNull(placementSingles);

    var itemsByEntryId = new Dictionary<ulong, DatSceneryItemData>();
    foreach (var source in sceneryItems) {
      if (!itemsByEntryId.TryAdd(source.EntryId, source))
        throw new InvalidDataException(
          $"Decoded scenery contains duplicate SceneryItem entry ID {source.EntryId}.");
    }

    var converted = new List<SceneryPlacement>(sceneryItems.Count);
    foreach (var source in sceneryItems)
      converted.Add(ConvertPlacement(source, terrain));

    ValidatePlacementSingles(placementSingles, itemsByEntryId);
    park.SceneryPlacements.AddRange(converted);
  }

  private static SceneryPlacement ConvertPlacement(DatSceneryItemData source, Terrain terrain) {
    var databaseEntry = source.ResolvedDatabaseEntry;
    if (databaseEntry is null)
      throw new InvalidDataException(
        $"Decoded SceneryItem entry {source.EntryId} has unresolved SID database reference " +
        $"{source.DatabaseEntry}.");
    if (databaseEntry.EntryId != source.DatabaseEntry)
      throw new InvalidDataException(
        $"Decoded SceneryItem entry {source.EntryId} resolved SID database entry " +
        $"{databaseEntry.EntryId}, expected {source.DatabaseEntry}.");

    var field = source.SceneryItemDataField;
    if (!terrain.HasTile(field.PosX, field.PosZ))
      throw new InvalidDataException(
        $"Decoded SceneryItem entry {source.EntryId} is outside the terrain grid.");

    var heightAdjust = field.HeightAdjust ?? 0f;
    if (!float.IsFinite(heightAdjust))
      throw new InvalidDataException(
        $"Decoded SceneryItem entry {source.EntryId} has a non-finite HEIGHTADJUST value.");

    var colours = source.FlexiColourField;
    return new SceneryPlacement(
      databaseEntry.SymbolName,
      field.PosX,
      field.PosZ,
      DecodeDirection(field.Direction, source.EntryId),
      field.Height
    ) {
      SourceEntryId = source.EntryId,
      DatabaseEntryReference = source.DatabaseEntry,
      OverlayPath = databaseEntry.OverlayFilename,
      Corner = field.Corner,
      SerializedDirection = field.Direction,
      HeightOffset = source.HeightOffset,
      HeightAdjust = heightAdjust,
      ForceAbsoluteHeight = source.ForceAbsoluteHeight,
      IsHidden = source.IsHidden,
      OwnerReference = source.Owner,
      FlexiColour0 = colours.Col0,
      FlexiColour1 = colours.Col1,
      FlexiColour2 = colours.Col2,
      FrameOffset = source.FrameOffset,
    };
  }

  private static void ValidatePlacementSingles(
    IReadOnlyList<DatSceneryItemPlacementSingleData> placementSingles,
    IReadOnlyDictionary<ulong, DatSceneryItemData> itemsByEntryId
  ) {
    foreach (var wrapper in placementSingles) {
      if (wrapper.SceneryItem == 0) continue;
      if (!itemsByEntryId.TryGetValue(wrapper.SceneryItem, out var sceneryItem)) continue;
      if (wrapper.SidDatabaseEntry == sceneryItem.DatabaseEntry) continue;

      throw new InvalidDataException(
        $"Decoded SceneryItemPlacementSingle entry {wrapper.EntryId} references SceneryItem " +
        $"{wrapper.SceneryItem} but SID database entry {wrapper.SidDatabaseEntry}, expected " +
        $"{sceneryItem.DatabaseEntry}.");
    }
  }

  // RCT3 scenery records use a scenery-specific direction ordinal that does not match Edge's
  // declaration order, so keep the explicit conversion rather than casting the raw value.
  private static Edge DecodeDirection(int value, ulong entryId) => value switch {
    0 => Edge.West,
    1 => Edge.North,
    2 => Edge.East,
    3 => Edge.South,
    _ => throw new InvalidDataException(
      $"Decoded SceneryItem entry {entryId} has invalid DIRECTION value {value}."),
  };
}
