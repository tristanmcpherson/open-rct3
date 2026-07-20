// Ride Track Installed Pipeline Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackInstalledPipelineTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_DatThroughCatalogProducesTypedGeometryOutcomes() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = ResolveMapPath(installRoot!);
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);
      RideTrackTopologyLoader.Load(park, data.RideTracks, data.TrackSegments);

      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements);
      var resources = loaded.Catalog.ResolveAll(park.RideTrackPlacements);
      var resolvedPlacements = resources.Placements.Count(link => link.IsResolved);
      var issueCounts = loaded.Issues
        .GroupBy(issue => issue.Kind)
        .ToDictionary(group => group.Key, group => group.Count());
      var reversedPlacements = park.RideTrackPlacements.Count(placement => placement.Reversed);
      var userAngledPlacements = park.RideTrackPlacements.Count(placement =>
        placement.UserAngleDegrees != 0);

      TestContext.Progress.WriteLine(
        $"BoxOffice DAT: pieces={data.TrackPieces.Count}, tracks={data.RideTracks.Count}, " +
        $"segments={data.TrackSegments.Count}, semanticPlacements=" +
        $"{park.RideTrackPlacements.Count}, semanticTracks={park.RideTracks.Count}");
      TestContext.Progress.WriteLine(
        $"Catalog: pairs={loaded.Context.LoadedCommonPaths.Count}, " +
        $"issues={loaded.Issues.Count}, resolvedPlacements={resolvedPlacements}, " +
        $"unresolvedPlacements={resources.UnresolvedPlacementCount}, " +
        $"issueKinds={FormatCounts(issueCounts)}");
      TestContext.Progress.WriteLine(
        $"Placement flags: reversed={reversedPlacements}, userAngled={userAngledPlacements}");

      using (Assert.EnterMultipleScope()) {
        Assert.That(data.TrackPieces, Has.Count.EqualTo(216));
        Assert.That(data.RideTracks, Has.Count.EqualTo(1));
        Assert.That(data.TrackSegments, Has.Count.EqualTo(1));
        Assert.That(park.RideTrackPlacements, Has.Count.EqualTo(data.TrackPieces.Count));
        Assert.That(park.RideTracks, Has.Count.EqualTo(data.RideTracks.Count));
        Assert.That(park.RideTrackSegments, Has.Count.EqualTo(data.TrackSegments.Count));
        Assert.That(loaded.Context.LoadedCommonPaths, Has.Count.EqualTo(2));
        Assert.That(loaded.Issues, Is.Empty);
        Assert.That(resources.Placements, Has.Count.EqualTo(data.TrackPieces.Count));
        Assert.That(resolvedPlacements, Is.EqualTo(216));
        Assert.That(resources.UnresolvedPlacementCount, Is.Zero);
        Assert.That(reversedPlacements, Is.EqualTo(58));
        Assert.That(userAngledPlacements, Is.EqualTo(13));
        Assert.That(
          resolvedPlacements + resources.UnresolvedPlacementCount,
          Is.EqualTo(data.TrackPieces.Count));
        Assert.That(loaded.IsComplete, Is.EqualTo(loaded.Issues.Count == 0));
        Assert.That(loaded.Issues.All(issue => Enum.IsDefined(issue.Kind)), Is.True);
      }

      var geometry = RideTrackGeometryResolver.Resolve(
        terrain,
        park.RideTracks,
        resources);
      var statusCounts = geometry.Tracks
        .GroupBy(link => link.Status)
        .ToDictionary(group => group.Key, group => group.Count());
      var openTracks = Count(statusCounts, RideTrackGeometryStatus.OpenTrack);
      var circuits = Count(statusCounts, RideTrackGeometryStatus.Circuit);
      var unresolvedTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnresolvedResources);
      var unsupportedGeometryTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnsupportedGeometry);
      var unsupportedTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnsupportedTopology);

      TestContext.Progress.WriteLine(
        $"Geometry: total={geometry.Tracks.Count}, open={openTracks}, circuits={circuits}, " +
        $"unresolved={unresolvedTracks}, unsupportedGeometry={unsupportedGeometryTracks}, " +
        $"unsupportedTopology={unsupportedTracks}");

      using (Assert.EnterMultipleScope()) {
        Assert.That(geometry.Tracks, Has.Count.EqualTo(data.RideTracks.Count));
        Assert.That(
          openTracks + circuits + unresolvedTracks + unsupportedGeometryTracks +
            unsupportedTracks,
          Is.EqualTo(data.RideTracks.Count));
        Assert.That(openTracks, Is.Zero);
        Assert.That(circuits, Is.Zero);
        Assert.That(unresolvedTracks, Is.Zero);
        Assert.That(unsupportedGeometryTracks, Is.EqualTo(1));
        Assert.That(unsupportedTracks, Is.Zero);
        Assert.That(
          geometry.UnresolvedResourceTrackCount,
          Is.EqualTo(unresolvedTracks));
        Assert.That(
          geometry.UnsupportedGeometryTrackCount,
          Is.EqualTo(unsupportedGeometryTracks));
        Assert.That(
          geometry.UnsupportedTopologyTrackCount,
          Is.EqualTo(unsupportedTracks));
        Assert.That(geometry.Tracks.Where(link => link.IsResolved).All(link =>
          link.Status is RideTrackGeometryStatus.OpenTrack or RideTrackGeometryStatus.Circuit),
          Is.True);
        Assert.That(geometry.Tracks.Where(link => !link.IsResolved).All(link =>
          link.Status is RideTrackGeometryStatus.UnresolvedResources or
            RideTrackGeometryStatus.UnsupportedGeometry or
            RideTrackGeometryStatus.UnsupportedTopology),
          Is.True);
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static string ResolveMapPath(string installRoot) {
    var configured = Environment.GetEnvironmentVariable("OPENRCT3_MAP_PATH");
    if (string.IsNullOrWhiteSpace(configured))
      return Path.Combine(installRoot, "Campaigns", "Base", "BoxOffice.dat");
    return Path.GetFullPath(Path.IsPathRooted(configured)
      ? configured
      : Path.Combine(installRoot, configured));
  }

  private static int Count(
    IReadOnlyDictionary<RideTrackGeometryStatus, int> counts,
    RideTrackGeometryStatus status
  ) => counts.TryGetValue(status, out var count) ? count : 0;

  private static string FormatCounts(
    IReadOnlyDictionary<RideTrackResourceCatalogLoadIssueKind, int> counts
  ) => string.Join(
    ",",
    Enum.GetValues<RideTrackResourceCatalogLoadIssueKind>().Select(kind =>
      $"{kind}={counts.GetValueOrDefault(kind)}"));
}
