// Path Manager Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>Converts ordinary RCT3 DAT path entries into editable park path tiles.</summary>
internal static class PathManagerLoader {
  // PersistentLegacyPathTileListReader uses this int32 marker before rebuilding the value from the
  // older half-resolution BaseHeight field.
  private const int MissingQuantisedHeight = -286_331_154;
  private const int HeightUnitsPerMeter = 100;

  public static void Load(Park park, Terrain terrain, IReadOnlyList<DatPathData> paths) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(paths);

    foreach (var source in paths) {
      var tileX = Convert.ToInt32(source.ColIndex);
      var tileY = Convert.ToInt32(source.RowIndex);
      if (!terrain.HasTile(tileX, tileY))
        throw new InvalidDataException(
          $"Decoded {source.StructureKind} entry {source.EntryId} is outside the terrain grid.");

      var expectedPathType = source.StructureKind switch {
        DatPathStructureKind.PathTile => 0,
        DatPathStructureKind.PathGround => 0,
        DatPathStructureKind.PathFlying => 1,
        DatPathStructureKind.PathQueue => 2,
        _ => throw new ArgumentOutOfRangeException(
          nameof(source), source.StructureKind, "Unsupported DAT path structure."),
      };
      if (source.PathType != expectedPathType)
        throw new InvalidDataException(
          $"Decoded {source.StructureKind} entry {source.EntryId} has path type " +
          $"{source.PathType}, expected {expectedPathType}.");

      var direction = DecodeOptionalDirection(source.Direction, source.EntryId, "Direction");
      var tile = new PathTile {
        Direction = direction,
        SurfaceReference = source.Surface,
        SurfaceType = source.SurfaceType,
      };
      ApplyResolvedSurface(ref tile, source);
      switch (source) {
        case DatPathFlyingData flying:
          ApplyElevatedFields(ref tile, flying, direction);
          tile.Underground = flying.UndergroundFlag ?? false;
          break;
        case DatPathQueueData queue:
          ApplyElevatedFields(ref tile, queue, direction);
          tile.IsQueue = true;
          tile.QueueStartDirection = DecodeOptionalDirection(
            queue.StartDirection, queue.EntryId, "StartDirection");
          tile.QueueEndDirection = DecodeOptionalDirection(
            queue.EndDirection, queue.EntryId, "EndDirection");
          tile.QueueFlowDirection = tile.QueueEndDirection;
          tile.QueueLineReference = queue.QueueLine;
          tile.Underground = queue.UndergroundFlag ?? false;
          break;
      }

      park.PathPlacements.Add(new PathPlacement(tileX, tileY, tile));
      if (!park.Paths.TryGetValue((tileX, tileY), out var primary) || primary.Raised)
        park.Paths[(tileX, tileY)] = tile;
    }
  }

  private static void ApplyResolvedSurface(ref PathTile tile, DatPathData source) {
    switch (source.ResolvedSurface) {
      case DatPathTypeDatabaseEntryData pathType:
        tile.SurfaceSystemName = pathType.SystemName;
        break;
      case DatQueueTypeDatabaseEntryData queueType:
        tile.SurfaceSystemName = queueType.SystemName;
        break;
      case DatQueueTypeGroundSurfaceData queueGround:
        tile.SurfaceSystemName = queueGround.ResolvedQueueType?.SystemName;
        var colours = queueGround.Colours;
        tile.SurfaceColours = new PathSurfaceColours(
          colours.Col0,
          colours.Col1,
          colours.Col2);
        break;
    }
  }

  private static void ApplyElevatedFields(
    ref PathTile tile,
    DatPathFlyingData source,
    Edge? direction
  ) {
    ApplyElevatedFields(
      ref tile,
      source.EntryId,
      source.BaseHeight,
      source.QuantisedHeight,
      source.SlopeType,
      direction);
  }

  private static void ApplyElevatedFields(
    ref PathTile tile,
    DatPathQueueData source,
    Edge? direction
  ) {
    ApplyElevatedFields(
      ref tile,
      source.EntryId,
      source.BaseHeight,
      source.QuantisedHeight,
      source.SlopeType,
      direction);
  }

  private static void ApplyElevatedFields(
    ref PathTile tile,
    ulong entryId,
    int baseHeight,
    int quantisedHeight,
    byte slopeType,
    Edge? direction
  ) {
    if (slopeType == 3) return;
    if (slopeType > 5)
      throw new InvalidDataException(
        $"Decoded path entry {entryId} has unsupported slope type {slopeType}.");

    var (raisedSlope, raisedSlopeDirection) = slopeType switch {
      0 => (PathRaisedSlope.Flat, direction ?? Edge.North),
      1 => (PathRaisedSlope.Sloped, RequireSlopeDirection(direction, entryId, slopeType)),
      2 => (PathRaisedSlope.Sloped,
        RequireSlopeDirection(direction, entryId, slopeType).Opposite()),
      4 => (PathRaisedSlope.Gentle, RequireSlopeDirection(direction, entryId, slopeType)),
      5 => (PathRaisedSlope.Gentle,
        RequireSlopeDirection(direction, entryId, slopeType).Opposite()),
      _ => throw new InvalidDataException(
        $"Decoded path entry {entryId} has unsupported slope type {slopeType}."),
    };

    int raisedHeight;
    try {
      var heightMeters = quantisedHeight == MissingQuantisedHeight
        ? checked(baseHeight * 2)
        : quantisedHeight;
      raisedHeight = checked(heightMeters * HeightUnitsPerMeter);
      _ = checked(raisedHeight + raisedSlope.RiseInHeightStepUnits());
    } catch (OverflowException exception) {
      throw new InvalidDataException(
        $"Decoded path entry {entryId} height exceeds the simulation range.", exception);
    }

    tile.Raised = true;
    tile.RaisedHeight = raisedHeight;
    tile.RaisedSlope = raisedSlope;
    tile.RaisedSlopeDirection = raisedSlopeDirection;
  }

  private static Edge RequireSlopeDirection(Edge? direction, ulong entryId, byte slopeType) =>
    direction ?? throw new InvalidDataException(
      $"Decoded path entry {entryId} has no direction for slope type {slopeType}.");

  private static Edge? DecodeOptionalDirection(byte value, ulong entryId, string fieldName)
    => value == byte.MaxValue ? null : DecodeDirection(value, entryId, fieldName);

  private static Edge DecodeDirection(byte value, ulong entryId, string fieldName) => value switch {
    0 => Edge.West,
    1 => Edge.East,
    2 => Edge.South,
    3 => Edge.North,
    _ => throw new InvalidDataException(
      $"Decoded path entry {entryId} has invalid {fieldName} value {value}."),
  };
}
