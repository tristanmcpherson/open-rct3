// World
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Streaming;
using OpenRCT3.Serialization;
using System.Collections.Generic;
using GDK = OpenCobra.GDK;

namespace OpenRCT3.Simulation;

/// <summary>
/// Represents the game world including the current park, terrain, objects, and people.
/// </summary>
public class World : GDK.Game.World {
  public Terrain? Terrain { get; private set; }
  public Park? Park { get; private set; }

  // FIXME: Load() blocks until every task completes since callers (e.g. Game's constructor) dereference
  // Terrain/Park synchronously right after calling it. Progress.MeasureTasks runs tasks on a background
  // Task.Run and returns immediately without waiting; without this .Wait(), Terrain/Park may still be
  // null when the caller reads them. Revisit once a progress bar actually consumes Progress
  // asynchronously (see the TODO in Game.cs) instead of blocking here.
  public override void Load() {
    var measurement = Progress.MeasureTasks([
      new(() => {
        var terrain = Terrain.Load(
          out var waterManager,
          out var paths,
          out var sceneryItems,
          out var sceneryItemPlacements,
          out var trackPieces,
          out var rideTracks,
          out var trackSegments,
          out var trackedRideInstances);
        try {
          var park = BuildPark(
            terrain,
            waterManager,
            paths,
            sceneryItems,
            sceneryItemPlacements,
            trackPieces,
            rideTracks,
            trackSegments,
            trackedRideInstances);
          Terrain = terrain;
          Park = park;
        } catch {
          terrain.TextureCatalog?.Dispose();
          throw;
        }
      }, "Loading park terrain"),
    ]);
    Progress = measurement.Progress;
    measurement.Task.Wait();
  }

  internal static Park BuildPark(
    Terrain terrain,
    DatWaterManagerData? waterManager,
    IReadOnlyList<DatPathData> paths,
    IReadOnlyList<DatSceneryItemData> sceneryItems,
    IReadOnlyList<DatSceneryItemPlacementSingleData> sceneryItemPlacements,
    IReadOnlyList<DatTrackPieceData> trackPieces,
    IReadOnlyList<DatRideTrackData> rideTracks,
    IReadOnlyList<DatTrackSegmentData> trackSegments
  ) => BuildPark(
    terrain,
    waterManager,
    paths,
    sceneryItems,
    sceneryItemPlacements,
    trackPieces,
    rideTracks,
    trackSegments,
    Array.Empty<DatTrackedRideInstanceData>());

  internal static Park BuildPark(
    Terrain terrain,
    DatWaterManagerData? waterManager,
    IReadOnlyList<DatPathData> paths,
    IReadOnlyList<DatSceneryItemData> sceneryItems,
    IReadOnlyList<DatSceneryItemPlacementSingleData> sceneryItemPlacements,
    IReadOnlyList<DatTrackPieceData> trackPieces,
    IReadOnlyList<DatRideTrackData> rideTracks,
    IReadOnlyList<DatTrackSegmentData> trackSegments,
    IReadOnlyList<DatTrackedRideInstanceData> trackedRideInstances
  ) {
    var park = new Park(terrain);
    if (waterManager != null) WaterManagerLoader.Load(park, terrain, waterManager);
    PathManagerLoader.Load(park, terrain, paths);
    SceneryManagerLoader.Load(park, terrain, sceneryItems, sceneryItemPlacements);
    RideTrackManagerLoader.Load(park, terrain, trackPieces);
    RideTrackTopologyLoader.Load(park, rideTracks, trackSegments);
    park.TrackedRideInstances.AddRange(trackedRideInstances);
    return park;
  }

  protected override void Dispose(bool disposing) {
    if (disposing) {
      Terrain?.TextureCatalog?.Dispose();
    }

    Terrain = null;
    Park = null;
    base.Dispose(disposing);
  }
}
