// Ride Track Gunslinger Installed Pipeline Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackGunslingerInstalledPipelineTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the Gunslinger DAT.")]
  public void Gunslinger_ResolvesExactWoodenWildMineChainConstructionIdentity() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = Path.Combine(
      installRoot!,
      "Campaigns",
      "Base",
      "Gunslinger.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var configuredEmptyRide = data.TrackedRideInstances.Single(ride =>
      ride.EntryId == 3_122);
    var configuredEmptyRideTrains = data.RideTrainInstances.Where(train =>
      train.TrackedRideInstance == configuredEmptyRide.EntryId).ToArray();
    var savedTrainRegistry = RideTrainInstanceResourceRegistry.Build(
      data.TrackedRideInstances,
      data.RideTrainInstances,
      []);
    var targetRide = data.TrackedRideInstances.Single(ride =>
      ride.TrackedRideSymbolName.Equals(
        "WoodenWildMine:trr",
        StringComparison.OrdinalIgnoreCase));
    var targetTrainIds = targetRide.Trains.ToHashSet();
    var targetTrains = data.RideTrainInstances.Where(train =>
      targetTrainIds.Contains(train.EntryId)).ToArray();
    var targetTracks = data.RideTracks.Where(track =>
      track.TrackedRideInstance == targetRide.EntryId).ToArray();
    var targetTrackIds = targetTracks.Select(track => track.EntryId).ToHashSet();
    var targetSegments = data.TrackSegments.Where(segment =>
      targetTrackIds.Contains(segment.Track)).ToArray();
    var targetSegmentIds = targetSegments.Select(segment => segment.EntryId).ToHashSet();
    var targetPieces = data.TrackPieces.Where(piece =>
      targetSegmentIds.Contains(piece.Segment)).ToArray();
    var targetSceneryIds = targetPieces.Select(piece => piece.SceneryItem).ToHashSet();
    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems.Where(item =>
          targetSceneryIds.Contains(item.EntryId)).ToArray(),
        data.SceneryItemPlacements.Where(placement =>
          targetSceneryIds.Contains(placement.SceneryItem)).ToArray());
      RideTrackManagerLoader.Load(park, terrain, targetPieces);
      RideTrackTopologyLoader.Load(park, targetTracks, targetSegments);

      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        [targetRide],
        targetTrains);
      var resources = loaded.Catalog.ResolveAll(park.RideTrackPlacements);
      var operatingTrain = targetTrains.Single(train =>
        train.HasSavedOperationalState &&
        train.State == 13 &&
        train.Speed == 14.1913595f);
      const string commonSuffix = ".common.ovl";
      var allowedResourcePaths = loaded.Context.LoadedCommonPaths.SelectMany(path =>
        path.EndsWith(commonSuffix, StringComparison.OrdinalIgnoreCase)
          ? new[] { path, path[..^commonSuffix.Length] + ".unique.ovl" }
          : new[] { path }).ToArray();
      var chainResource = loaded.Context.FindExactResource(
        allowedResourcePaths,
        "Medslope2steepslopechain:tks",
        FileType.TrackSection);

      TestContext.Progress.WriteLine(
        $"Gunslinger WoodenWildMine: placements={resources.Placements.Count}, " +
        $"archives={loaded.Context.LoadedCommonPaths.Count}, " +
        $"train={operatingTrain.EntryId}, " +
        $"state={operatingTrain.State}, speed={operatingTrain.Speed:R}");
      using (Assert.EnterMultipleScope()) {
        Assert.That(configuredEmptyRide.NTrains, Is.EqualTo(1));
        Assert.That(configuredEmptyRide.Trains, Is.Empty);
        Assert.That(configuredEmptyRideTrains, Is.Empty);
        Assert.That(savedTrainRegistry.Links,
          Has.Count.EqualTo(data.TrackedRideInstances.Sum(ride => ride.Trains.Count)));
        Assert.That(loaded.IsComplete, Is.True);
        Assert.That(resources.UnresolvedPlacementCount, Is.Zero);
        Assert.That(chainResource, Is.Not.Null);
        Assert.That(chainResource?.File.Name,
          Is.EqualTo("Medslope2steepslopechain").IgnoreCase);
        Assert.That(chainResource?.File.Type, Is.EqualTo(FileType.TrackSection));
        Assert.That(operatingTrain.HasSavedMotionState, Is.True);
        Assert.That(operatingTrain.Speed, Is.EqualTo(14.1913595f));
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }
}
