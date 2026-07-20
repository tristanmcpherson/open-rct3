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
      var trackVisuals = RideTrackVisualResourceBridge.Resolve(
        loaded.TrackVisualResources);
      var chainVisuals = trackVisuals.Visuals.Where(visual =>
        visual.Section.Source.Resource.Name.Equals(
          "Medslope2steepslopechain",
          StringComparison.OrdinalIgnoreCase)).ToArray();
      var expectedTrackDirectory = Path.Combine(
        installRoot!,
        "Tracks",
        "Coasters",
        "Track21");
      var expectedTrackUniquePath = Path.Combine(
        expectedTrackDirectory,
        "Track21.unique.ovl");
      var expectedVisualUniquePath = Path.Combine(
        expectedTrackDirectory,
        "Medslope2steepslopechain_data.unique.ovl");
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
        $"trackVisuals={trackVisuals.Visuals.Count}, " +
        $"trackStaticLods={trackVisuals.StaticLodCount}, " +
        $"trackBoneLods={trackVisuals.BoneLodCount}, " +
        $"trackMeshes={trackVisuals.MeshCount}, " +
        $"chainVisuals={chainVisuals.Length}, " +
        $"chainLods={chainVisuals.Sum(visual => visual.Lods.Count)}, " +
        $"chainMeshes={chainVisuals.Sum(visual => visual.Lods.Sum(lod => lod.Materials.Count))}, " +
        $"train={operatingTrain.EntryId}, " +
        $"state={operatingTrain.State}, speed={operatingTrain.Speed:R}");
      foreach (var visual in chainVisuals) {
        TestContext.Progress.WriteLine(
          $"Chain visual: tks={visual.Section.Source.File.Path}|" +
          $"{visual.Section.Source.Resource.Name}, sid={visual.Scenery.Source?.File.Path}|" +
          $"{visual.Scenery.Source?.Resource.Name}, svd={visual.VisualSource.File.Path}|" +
          $"{visual.VisualSource.Resource.Name}");
        foreach (var lod in visual.Lods)
          TestContext.Progress.WriteLine(
            $"Chain LOD: name={lod.Lod.Name}, type={lod.Lod.Type}, " +
            $"shape={lod.StaticShapeSource?.File.Path}|{lod.StaticShape?.Name}, " +
            $"meshes={lod.Materials.Count}, materials=[" +
            string.Join(",", lod.Materials.Select(material =>
              $"{material.MeshName}:{material.FlexibleTextureReference}:" +
              $"{material.TextureStyleReference}")) + "]");
      }
      using (Assert.EnterMultipleScope()) {
        Assert.That(configuredEmptyRide.NTrains, Is.EqualTo(1));
        Assert.That(configuredEmptyRide.Trains, Is.Empty);
        Assert.That(configuredEmptyRideTrains, Is.Empty);
        Assert.That(savedTrainRegistry.Links,
          Has.Count.EqualTo(data.TrackedRideInstances.Sum(ride => ride.Trains.Count)));
        Assert.That(loaded.IsComplete, Is.True);
        Assert.That(resources.UnresolvedPlacementCount, Is.Zero);
        Assert.That(loaded.Context.LoadedCommonPaths, Has.Count.EqualTo(39));
        Assert.That(trackVisuals.Visuals, Has.Count.EqualTo(22));
        Assert.That(trackVisuals.StaticLodCount, Is.EqualTo(66));
        Assert.That(trackVisuals.BoneLodCount, Is.Zero);
        Assert.That(trackVisuals.MeshCount, Is.EqualTo(122));
        Assert.That(chainVisuals, Has.Length.EqualTo(1));
        var chainVisual = chainVisuals.Single();
        Assert.That(chainVisual.Section.Source.File.Path,
          Is.EqualTo(expectedTrackUniquePath).IgnoreCase);
        Assert.That(chainVisual.Section.Source.Resource.Name,
          Is.EqualTo("Medslope2steepslopechain").IgnoreCase);
        Assert.That(chainVisual.Scenery.Reference,
          Is.EqualTo("Medslope2steepslopechain:sid").IgnoreCase);
        Assert.That(chainVisual.Scenery.Source!.File.Path,
          Is.EqualTo(expectedTrackUniquePath).IgnoreCase);
        Assert.That(chainVisual.Scenery.Source.Resource.Name,
          Is.EqualTo("Medslope2steepslopechain").IgnoreCase);
        Assert.That(chainVisual.Scenery.Source.Resource.VisualRefs,
          Is.EqualTo(new[] { "Medslope2steepslopechain:svd" }).IgnoreCase);
        Assert.That(chainVisual.VisualSource.File.Path,
          Is.EqualTo(expectedVisualUniquePath).IgnoreCase);
        Assert.That(chainVisual.VisualSource.Resource.Name,
          Is.EqualTo("Medslope2steepslopechain").IgnoreCase);
        Assert.That(chainVisual.Lods, Has.Count.EqualTo(3));
        Assert.That(chainVisual.Lods.Select(lod => lod.Lod.Name), Is.EqualTo(new[] {
          "Medslope2steepslopechain_HI",
          "Medslope2steepslopechain_ME",
          "Medslope2steepslopechain_LO",
        }).IgnoreCase);
        Assert.That(chainVisual.Lods.Select(lod => lod.Lod.Type),
          Is.All.EqualTo(SvdLodType.StaticShape));
        Assert.That(chainVisual.Lods.Select(lod => lod.StaticShapeSource!.File.Path),
          Is.All.EqualTo(expectedVisualUniquePath).IgnoreCase);
        Assert.That(chainVisual.Lods.Select(lod => lod.StaticShape!.Name),
          Is.EqualTo(chainVisual.Lods.Select(lod => lod.Lod.Name)).IgnoreCase);
        Assert.That(chainVisual.Lods.Select(lod => lod.Materials.Count),
          Is.EqualTo(new[] { 3, 3, 1 }));
        Assert.That(chainVisual.Lods.Sum(lod => lod.Materials.Count),
          Is.EqualTo(7));
        Assert.That(chainVisual.Lods.SelectMany(lod => lod.Materials).Select(material =>
          material.FlexibleTextureReference), Is.EqualTo(new[] {
            "woodwild1:ftx", "chain:ftx", "ts2:ftx",
            "woodwild1:ftx", "chain:ftx", "ts2:ftx",
            "coaster_LO_textures05:ftx",
          }).IgnoreCase);
        Assert.That(chainVisual.Lods.SelectMany(lod => lod.Materials).Select(material =>
          material.TextureStyleReference), Is.EqualTo(new[] {
            "SIOpaqueSpecular50:txs", "SIOpaque:txs", "SIOpaque:txs",
            "SIOpaqueSpecular50:txs", "SIOpaque:txs", "SIOpaque:txs",
            "SIAlphaMaskLow:txs",
          }).IgnoreCase);
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
