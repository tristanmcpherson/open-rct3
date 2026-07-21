// Animated Ride Resource Catalog Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class AnimatedRideResourceCatalogLoaderTests {
  [Test]
  public void Load_ResolvesRootAnrToExactSidInDeclaredDependencyClosure() {
    var root = Path.GetFullPath("SyntheticInstall");
    var ridePath = Path.Combine(root, "Rides", "Roundup.common.ovl");
    var sceneryPath = Path.Combine(root, "Rides", "Shared.common.ovl");
    var source = new FakeSource([
      Pair(ridePath, ["Shared"], [Ride("Roundup", ridePath)], []),
      Pair(sceneryPath, [], [], [Scenery("Roundup", sceneryPath)]),
    ]);

    using var result = AnimatedRideResourceCatalogLoader.Load(
      root,
      [Placement(1, @"Rides\Roundup", "Roundup")],
      source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Issues, Is.Empty);
      Assert.That(result.Links, Has.Count.EqualTo(1));
      Assert.That(result.Links[0].Status, Is.EqualTo(AnimatedRideResourceLinkStatus.Resolved));
      Assert.That(result.Links[0].Scenery!.File.Path, Is.EqualTo(ToUnique(sceneryPath)));
    }
  }

  [Test]
  public void Load_RetainsAmbiguousSidAsTypedUnresolvedOutcome() {
    var root = Path.GetFullPath("SyntheticInstall");
    var ridePath = Path.Combine(root, "Rides", "Roundup.common.ovl");
    var firstPath = Path.Combine(root, "Rides", "First.common.ovl");
    var secondPath = Path.Combine(root, "Rides", "Second.common.ovl");
    var source = new FakeSource([
      Pair(ridePath, ["First", "Second"], [Ride("Roundup", ridePath)], []),
      Pair(firstPath, [], [], [Scenery("Roundup", firstPath)]),
      Pair(secondPath, [], [], [Scenery("Roundup", secondPath)]),
    ]);

    using var result = AnimatedRideResourceCatalogLoader.Load(
      root,
      [Placement(1, @"Rides\Roundup", "Roundup")],
      source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Links[0].Status,
        Is.EqualTo(AnimatedRideResourceLinkStatus.AmbiguousSceneryItem));
      Assert.That(result.Links[0].Scenery, Is.Null);
      Assert.That(result.IsComplete, Is.False);
    }
  }

  [Test]
  public void Load_DoesNotResolveSidFromAnUnrelatedLoadedRoot() {
    var root = Path.GetFullPath("SyntheticInstall");
    var ridePath = Path.Combine(root, "Rides", "Roundup.common.ovl");
    var unrelatedPath = Path.Combine(root, "Scenery", "Roundup.common.ovl");
    var source = new FakeSource([
      Pair(ridePath, [], [Ride("Roundup", ridePath)], []),
      Pair(unrelatedPath, [], [], [Scenery("Roundup", unrelatedPath)]),
    ]);

    using var result = AnimatedRideResourceCatalogLoader.Load(
      root,
      [
        Placement(1, @"Rides\Roundup", "Roundup"),
        Placement(2, @"Scenery\Roundup", "Roundup"),
      ],
      source);

    Assert.That(result.Links.Single().Status,
      Is.EqualTo(AnimatedRideResourceLinkStatus.MissingSceneryItem));
  }

  [Test]
  public void Load_RetainsMissingDependencyPairAsTypedIssue() {
    var root = Path.GetFullPath("SyntheticInstall");
    var ridePath = Path.Combine(root, "Rides", "Roundup.common.ovl");
    var source = new FakeSource([
      Pair(ridePath, ["Missing"], [Ride("Roundup", ridePath)], []),
    ]);

    using var result = AnimatedRideResourceCatalogLoader.Load(
      root,
      [Placement(1, @"Rides\Roundup", "Roundup")],
      source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Issues, Has.Count.EqualTo(1));
      Assert.That(result.Issues[0].Kind,
        Is.EqualTo(AnimatedRideResourceCatalogIssueKind.MissingDependencyPair));
      Assert.That(result.Links[0].Status,
        Is.EqualTo(AnimatedRideResourceLinkStatus.MissingSceneryItem));
    }
  }

  [Test]
  public void Load_RejectsOverlayTraversalBeforeProbingFiles() {
    var source = new FakeSource([]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      AnimatedRideResourceCatalogLoader.Load(
        Path.GetFullPath("SyntheticInstall"),
        [Placement(1, @"..\Outside", "Roundup")],
        source)));
    Assert.That(source.ProbedPaths, Is.Empty);
  }

  private static AnimatedRideArchiveSnapshot Pair(
    string commonPath,
    IReadOnlyList<string> references,
    IReadOnlyList<AnimatedRideResourceSource> rides,
    IReadOnlyList<AnimatedRideSceneryResourceSource> scenery
  ) => new(commonPath, ToUnique(commonPath), references, rides, scenery);

  private static AnimatedRideResourceSource Ride(string name, string commonPath) => new(
    new OvlFile(name, FileType.AnimatedRide, ToUnique(commonPath)),
    new AnimatedRide(
      name,
      AnimatedRideVersion.Vanilla,
      new AnimatedRideAttraction(0, "Name:txt", "Description:txt", "", 0, "", [], 0,
        0, null, null, new Dictionary<int, uint>()),
      new AnimatedRideSettings(0, 0, [], 0, 0, 0, [], new Dictionary<int, uint>()),
      name + ":sid",
      [],
      new Dictionary<int, uint>()));

  private static AnimatedRideSceneryResourceSource Scenery(
    string name,
    string commonPath
  ) => new(
    new OvlFile(name, FileType.SceneryItem, ToUnique(commonPath)),
    new SceneryItem(
      name,
      0,
      0,
      0,
      1,
      1,
      0,
      0,
      0,
      1,
      1,
      1,
      SidType.Ride,
      []));

  private static SceneryPlacement Placement(
    ulong entryId,
    string overlay,
    string objectKey
  ) {
    var placement = new SceneryPlacement(objectKey, 0, 0) {
      SourceEntryId = entryId,
      OverlayPath = overlay,
    };
    return placement;
  }

  private static string ToUnique(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";

  private sealed class FakeSource : IAnimatedRideResourceCatalogLoaderSource {
    private readonly Dictionary<string, AnimatedRideArchiveSnapshot> pairs;

    public List<string> ProbedPaths { get; } = [];

    public FakeSource(IReadOnlyList<AnimatedRideArchiveSnapshot> pairs) =>
      this.pairs = pairs.ToDictionary(pair => pair.CommonPath, StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) {
      ProbedPaths.Add(path);
      return pairs.ContainsKey(path) || pairs.Values.Any(pair =>
        pair.UniquePath.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    public AnimatedRideArchiveSnapshot LoadPair(string commonPath) => pairs[commonPath];

    public void DisposePair(AnimatedRideArchiveSnapshot snapshot) { }
  }
}
