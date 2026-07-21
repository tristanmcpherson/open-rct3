using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualHierarchyInstalledTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the RaidersOfTheLostCoaster DAT.")]
  public void RaidersOfTheLostCoaster_ReportsUndeclaredOptionalBodyRoleHierarchySlots() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = Path.Combine(
      installRoot!,
      "Campaigns",
      "Base",
      "Wild",
      "RaidersOfTheLostCoaster.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var targetRideIds = new ulong[] { 4_021, 4_120 };
    var targetRides = targetRideIds
      .Select(id => data.TrackedRideInstances.Single(ride => ride.EntryId == id))
      .ToArray();
    var targetTrackIds = targetRides.Select(ride => ride.Track).ToHashSet();
    var targetTracks = data.RideTracks
      .Where(track => targetTrackIds.Contains(track.EntryId))
      .ToArray();
    var targetTrainIds = targetRides.SelectMany(ride => ride.Trains).ToHashSet();
    var targetTrains = data.RideTrainInstances
      .Where(train => targetTrainIds.Contains(train.EntryId))
      .ToArray();
    var targetSegments = data.TrackSegments
      .Where(segment => targetTrackIds.Contains(segment.Track))
      .ToArray();
    var targetSegmentIds = targetSegments.Select(segment => segment.EntryId).ToHashSet();
    var targetPieces = data.TrackPieces
      .Where(piece => targetSegmentIds.Contains(piece.Segment))
      .ToArray();
    var targetSceneryIds = targetPieces.Select(piece => piece.SceneryItem).ToHashSet();

    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems.Where(item => targetSceneryIds.Contains(item.EntryId)).ToArray(),
        data.SceneryItemPlacements.Where(placement =>
          targetSceneryIds.Contains(placement.SceneryItem)).ToArray());
      RideTrackManagerLoader.Load(park, terrain, targetPieces);
      RideTrackTopologyLoader.Load(park, targetTracks, targetSegments);

      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        targetRides,
        targetTrains);
      var registry = RideCarVisualHierarchyResolver.Resolve(
        loaded.RideResources.Graph,
        loaded.RideResources.CarVisuals);

      foreach (var car in registry.Cars) {
        TestContext.Progress.WriteLine(
          $"Hierarchy car={car.Car.Reference} bodyRole={car.BodyRole} " +
          $"bodyStatus={car.BodyStatus} body={Describe(car.BodyShapeVisual)}");
        foreach (var part in car.Parts)
          TestContext.Progress.WriteLine(
            $"  part={part.Role} status={part.Status} type={part.Type} " +
            $"reference={part.SerializedVisualReference ?? "<none>"} " +
            $"visual={Describe(part.ShapeVisual)}");
      }

      using (Assert.EnterMultipleScope()) {
        Assert.That(registry.Cars, Has.Count.EqualTo(11));
        Assert.That(registry.AmbiguousPartCount, Is.Zero);
        Assert.That(registry.BodyRolePartSlotCount, Is.EqualTo(66));
        Assert.That(registry.DeclaredBodyRolePartCount, Is.Zero);
        Assert.That(registry.ResolvedPartCount, Is.Zero);
        Assert.That(registry.UnresolvedDeclaredBodyRolePartCount, Is.Zero);
        Assert.That(registry.UndeclaredBodyRolePartCount, Is.EqualTo(66));
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static string Describe(RideCarVisualShapeLink? visual) {
    if (visual == null) return "<none>";
    return string.Join(" | ", visual.Lods.Select(lod => {
      var staticName = lod.StaticShapeSource?.Resource.Name ?? "-";
      var boneName = lod.BoneShapeSource?.Resource.Name ?? "-";
      var boneList = lod.BoneShapeSource?.Resource.Bones;
      var anchors = boneList == null
        ? "-"
        : string.Join(",", boneList
          .Select(bone => bone.Name)
          .Where(name =>
            name.Contains("ax", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("wheel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("car", StringComparison.OrdinalIgnoreCase))
          .Take(32));
      return $"{lod.Lod.Name}:{lod.Lod.Type}:shs={staticName}:bsh={boneName}:" +
        $"boneCount={boneList?.Count ?? 0}:anchors=[{anchors}]";
    }));
  }
}
