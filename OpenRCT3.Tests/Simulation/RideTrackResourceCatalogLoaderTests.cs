// Ride Track Resource Catalog Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackResourceCatalogLoaderTests {
  private string installRoot = null!;

  [SetUp]
  public void SetUp() {
    installRoot = Path.Combine(
      Path.GetTempPath(),
      $"openrct3-track-loader-{Guid.NewGuid():N}");
  }

  [Test]
  public void Load_BuildsExactCatalogFromRootAndSerializedDependencyOnly() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var dependency = PairPath("Tracks", "Shared", "Geometry");
    source.AddPair(
      root,
      references: [@"..\Shared\Geometry"],
      sections: [Section("Straight")],
      sceneryItems: [Scenery("StraightScenery")],
      rides: [Ride("SyntheticRide")]);
    source.AddPair(
      dependency,
      splines: [
        Spline("CarLeft"),
        Spline("CarRight"),
        Spline("JoinLeft"),
        Spline("JoinRight"),
        Spline("RideTrack"),
      ]);
    var placement = Placement();

    var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [placement],
      source);
    var link = result.Catalog.ResolveAll([placement]).Placements.Single();
    var rideSpline = link.Declarations.Single().Ride.Splines.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.True);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root, dependency }));
      Assert.That(result.Context.LoadedCommonPaths, Is.EqualTo(source.LoadedPaths));
      Assert.That(link.IsResolved, Is.True);
      Assert.That(link.Section!.Source.File.Path, Is.EqualTo(UniquePath(root)));
      Assert.That(link.Splines, Has.Count.EqualTo(4));
      Assert.That(link.Splines.All(item => item.IsResolved), Is.True);
      Assert.That(link.Splines.Select(item => item.Source!.File.Path).Distinct(),
        Is.EqualTo(new[] { dependency }));
      Assert.That(link.Declarations, Has.Count.EqualTo(1));
      Assert.That(rideSpline.Source!.File.Path, Is.EqualTo(dependency));
    }

    result.Dispose();
    result.Dispose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Context.IsDisposed, Is.True);
      Assert.That(source.DisposedArchives, Has.Count.EqualTo(2));
    }
  }

  [Test]
  public void Load_ReturnsTypedMissingDependencyAndAnExactPartialCatalog() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var dependency = PairPath("Tracks", "Shared", "MissingGeometry");
    source.AddPair(
      root,
      references: [@"..\Shared\MissingGeometry"],
      sections: [Section("Straight")],
      sceneryItems: [Scenery("StraightScenery")]);
    var placement = Placement();

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [placement],
      source);
    var link = result.Catalog.ResolveAll([placement]).Placements.Single();
    var issue = result.Issues.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.False);
      Assert.That(issue.Kind,
        Is.EqualTo(RideTrackResourceCatalogLoadIssueKind.MissingDependencyPair));
      Assert.That(issue.DeclaringCommonPath, Is.EqualTo(root));
      Assert.That(issue.Reference, Is.EqualTo(@"..\Shared\MissingGeometry"));
      Assert.That(issue.CommonPath, Is.EqualTo(dependency));
      Assert.That(issue.CommonExists, Is.False);
      Assert.That(issue.UniqueExists, Is.False);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root }));
      Assert.That(link.IsResolved, Is.True);
      Assert.That(link.Splines, Has.Count.EqualTo(4));
      Assert.That(link.Splines.All(item => !item.IsResolved), Is.True);
    }
  }

  [Test]
  public void Load_DoesNotProbeAnUndeclaredSameNameArchive() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var unrelated = PairPath("Tracks", "Elsewhere", "Geometry");
    source.AddPair(
      root,
      sections: [Section("Straight")],
      sceneryItems: [Scenery("StraightScenery")]);
    source.AddPair(
      unrelated,
      splines: [
        Spline("CarLeft"),
        Spline("CarRight"),
        Spline("JoinLeft"),
        Spline("JoinRight"),
      ]);
    var placement = Placement();

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [placement],
      source);
    var link = result.Catalog.ResolveAll([placement]).Placements.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.True);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root }));
      Assert.That(link.Splines.All(item => !item.IsResolved), Is.True);
    }
  }

  [Test]
  public void Load_RejectsDependencyThatLeavesRootAndDisposesLoadedPair() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    source.AddPair(
      root,
      references: [@"..\..\..\outside"],
      sections: [Section("Straight")]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackResourceCatalogLoader.Load(
        installRoot,
        [Placement()],
        source)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("leaves the install root"));
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root }));
      Assert.That(source.DisposedArchives, Has.Count.EqualTo(1));
    }
  }

  [Test]
  public void Load_PreflightsPlacementLimitBeforeTouchingFilesystemSource() {
    var source = new FakeLoaderSource();
    var limits = new RideTrackResourceCatalogLoaderLimits(
      MaximumPlacements: 1,
      MaximumPairs: 10,
      MaximumDependenciesPerPair: 10,
      MaximumDependencyEdges: 10,
      MaximumDependencyDepth: 10,
      MaximumIdentifierLength: 100);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackResourceCatalogLoader.Load(
        installRoot,
        new ThrowingList<RideTrackPlacement>(2),
        source,
        limits)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("DAT placement count 2 exceeds 1"));
      Assert.That(source.FileExistsCount, Is.Zero);
      Assert.That(source.LoadedPaths, Is.Empty);
    }
  }

  [Test]
  public void Load_RejectsDecoderIdentityMismatchAndDisposesLoadedPair() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    source.AddPair(root, sections: [Section("Straight")]);
    source.ReplaceSections(root, [Section("Different")]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackResourceCatalogLoader.Load(
        installRoot,
        [Placement()],
        source)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain(
        "OVL identity 'Straight' does not match decoded name 'Different'"));
      Assert.That(source.DisposedArchives, Has.Count.EqualTo(1));
    }
  }

  [Test]
  public void ContextDispose_AttemptsEveryArchiveInReverseOrderAndAggregatesFailures() {
    var first = new Ovl("first");
    var second = new Ovl("second");
    var third = new Ovl("third");
    var firstFailure = new InvalidOperationException("first disposal failed");
    var thirdFailure = new InvalidOperationException("third disposal failed");
    var attempts = new List<Ovl>();
    var context = new RideTrackResourceCatalogLoadContext(
      ["first", "second", "third"],
      [first, second, third],
      archive => {
        attempts.Add(archive);
        if (ReferenceEquals(archive, third)) throw thirdFailure;
        if (ReferenceEquals(archive, first)) throw firstFailure;
      });

    try {
      var exception = Assert.Throws<AggregateException>(new Action(context.Dispose));
      Assert.DoesNotThrow(new Action(context.Dispose));

      using (Assert.EnterMultipleScope()) {
        Assert.That(context.IsDisposed, Is.True);
        Assert.That(attempts, Is.EqualTo(new[] { third, second, first }));
        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(exception.InnerExceptions[0], Is.SameAs(thirdFailure));
        Assert.That(exception.InnerExceptions[1], Is.SameAs(firstFailure));
      }
    } finally {
      first.Dispose();
      second.Dispose();
      third.Dispose();
    }
  }

  [Test]
  public void Load_PreservesPrimaryFailureBeforeEveryReverseCleanupFailure() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var dependency = PairPath("Tracks", "Shared", "Geometry");
    var rootFailure = new InvalidOperationException("root cleanup failed");
    var dependencyFailure = new InvalidOperationException("dependency cleanup failed");
    source.AddPair(root, references: [@"..\Shared\Geometry"]);
    source.AddPair(dependency, references: [@"..\..\..\outside"]);
    source.FailDispose(root, rootFailure);
    source.FailDispose(dependency, dependencyFailure);

    try {
      var exception = Assert.Throws<AggregateException>(new Action(() =>
        RideTrackResourceCatalogLoader.Load(
          installRoot,
          [Placement()],
          source)));

      using (Assert.EnterMultipleScope()) {
        Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root, dependency }));
        Assert.That(source.DisposedArchives, Is.EqualTo(new[] {
          source.GetArchive(dependency),
          source.GetArchive(root),
        }));
        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(3));
        Assert.That(exception.InnerExceptions[0], Is.TypeOf<InvalidDataException>());
        Assert.That(exception.InnerExceptions[0].Message, Does.Contain("leaves the install root"));
        Assert.That(exception.InnerExceptions[1], Is.SameAs(dependencyFailure));
        Assert.That(exception.InnerExceptions[2], Is.SameAs(rootFailure));
      }
    } finally {
      source.GetArchive(root).Dispose();
      source.GetArchive(dependency).Dispose();
    }
  }

  private string PairPath(params string[] parts) =>
    Path.GetFullPath(Path.Combine(new[] { installRoot }.Concat(parts).ToArray())) +
    ".common.ovl";

  private static string UniquePath(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";

  private static RideTrackPlacement Placement() => new(
    sourceEntryId: 500,
    sceneryPlacementSourceEntryId: 100,
    sidDatabaseEntryReference: 200,
    symbolName: "Straight:tks",
    objectKey: "Straight",
    overlayPath: @"Tracks\Exact\Synthetic",
    tileX: 1,
    tileY: 1,
    rotation: Edge.East,
    serializedDirection: 2,
    serializedHeight: 0,
    corner: 0,
    ownerReference: 600,
    segmentReference: 700,
    previousPieceReference: 0,
    nextPieceReference: 0,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0);

  private static TrackSection Section(string name) => new(
    name,
    TrackSectionVersion.Vanilla,
    name + " internal",
    "StraightScenery:sid",
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    SpecialCurves: 0,
    Direction: 0,
    new TrackSectionSplinePair("CarLeft:spl", "CarRight:spl"),
    new TrackSectionSplinePair("JoinLeft:spl", "JoinRight:spl"),
    ExtraSplines: null,
    WaterSplines: null,
    Speeds: [],
    new TrackSectionAnimations(0, 0, 0, 0, 0, 0, 0, 0, 0, null, null),
    new TrackSectionOptions(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    Expansion: null,
    Wild: null);

  private static SceneryItem Scenery(string name) => new(
    name,
    Flags: default,
    PositionType: default,
    StructureVersion: 0,
    SquaresX: 1,
    SquaresZ: 1,
    PositionX: 0,
    PositionY: 0,
    PositionZ: 0,
    SizeX: 0,
    SizeY: 0,
    SizeZ: 0,
    Type: default,
    VisualRefs: []);

  private static Spline Spline(string name) => new(
    name,
    Cyclic: false,
    TotalLength: 1f,
    InverseTotalLength: 1f,
    MaximumY: 0f,
    Nodes: [
      new SplineNode(
        Vector3.Zero,
        Vector3.Zero,
        new Vector3(0.25f, 0f, 0f)),
      new SplineNode(
        Vector3.UnitX,
        new Vector3(-0.25f, 0f, 0f),
        Vector3.Zero),
    ],
    Segments: [new SplineSegment(1f, new byte[14])]);

  private static TrackedRide Ride(string name) => new(
    name,
    TrackedRideVersion.Vanilla,
    [new TrackedRideTrackSection("Straight:tks", "straight", 10)],
    TrainNames: [],
    CableLift: null,
    LiftCar: null,
    VanillaTrackPath: "../Synthetic/Synthetic",
    new TrackedRideStation(null, 0, 0, 0, 0, 0),
    new TrackedRideMotion(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    new TrackedRideOptions(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideCosts(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideReferences(
      null,
      null,
      null,
      null,
      "RideTrack:spl",
      null,
      null,
      null),
    Expansion: null,
    Wild: null);

  private sealed class FakeLoaderSource : IRideTrackResourceCatalogLoaderSource {
    private readonly HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Ovl> archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Ovl, PairResources> resources =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Ovl, IReadOnlyList<string>> references =
      new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Ovl, Exception> disposeFailures =
      new(ReferenceEqualityComparer.Instance);

    public List<string> LoadedPaths { get; } = [];
    public List<Ovl> DisposedArchives { get; } = [];
    public int FileExistsCount { get; private set; }

    public void AddPair(
      string commonPath,
      IReadOnlyList<string>? references = null,
      IReadOnlyList<TrackSection>? sections = null,
      IReadOnlyList<SceneryItem>? sceneryItems = null,
      IReadOnlyList<Spline>? splines = null,
      IReadOnlyList<TrackedRide>? rides = null
    ) {
      var uniquePath = UniquePath(commonPath);
      var archive = new Ovl(Path.GetFileName(commonPath));
      var pairResources = new PairResources(
        sections ?? [],
        sceneryItems ?? [],
        splines ?? [],
        rides ?? []);
      AddFiles(archive, pairResources.TrackSections, FileType.TrackSection, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.SceneryItems, FileType.SceneryItem, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.Splines, FileType.Spline, commonPath,
        item => item.Name);
      AddFiles(archive, pairResources.TrackedRides, FileType.TrackedRide, uniquePath,
        item => item.Name);
      archives.Add(commonPath, archive);
      resources.Add(archive, pairResources);
      this.references.Add(archive, references ?? []);
      existing.Add(commonPath);
      existing.Add(uniquePath);
    }

    public void ReplaceSections(
      string commonPath,
      IReadOnlyList<TrackSection> sections
    ) {
      var archive = archives[commonPath];
      resources[archive] = resources[archive] with { TrackSections = sections };
    }

    public Ovl GetArchive(string commonPath) => archives[commonPath];

    public void FailDispose(string commonPath, Exception error) {
      ArgumentNullException.ThrowIfNull(error);
      disposeFailures.Add(archives[commonPath], error);
    }

    public bool FileExists(string path) {
      FileExistsCount++;
      return existing.Contains(path);
    }

    public Ovl LoadPair(string commonOvlPath) {
      LoadedPaths.Add(commonOvlPath);
      return archives[commonOvlPath];
    }

    public IReadOnlyList<string> GetExternalReferences(Ovl archive) => references[archive];
    public IReadOnlyList<TrackSection> ExtractTrackSections(Ovl archive) =>
      resources[archive].TrackSections;
    public IReadOnlyList<SceneryItem> ExtractSceneryItems(Ovl archive) =>
      resources[archive].SceneryItems;
    public IReadOnlyList<Spline> ExtractSplines(Ovl archive) => resources[archive].Splines;
    public IReadOnlyList<TrackedRide> ExtractTrackedRides(Ovl archive) =>
      resources[archive].TrackedRides;

    public void DisposePair(Ovl archive) {
      DisposedArchives.Add(archive);
      if (disposeFailures.TryGetValue(archive, out var error)) throw error;
      archive.Dispose();
    }

    private static void AddFiles<T>(
      Ovl archive,
      IEnumerable<T> items,
      FileType type,
      string path,
      Func<T, string> getName
    ) {
      foreach (var item in items)
        archive.Add(new OvlFile(getName(item), type, path), new OvlEntry(0, 1));
    }
  }

  private sealed record PairResources(
    IReadOnlyList<TrackSection> TrackSections,
    IReadOnlyList<SceneryItem> SceneryItems,
    IReadOnlyList<Spline> Splines,
    IReadOnlyList<TrackedRide> TrackedRides
  );

  private sealed class ThrowingList<T>(int count) : IReadOnlyList<T> {
    public int Count { get; } = count;
    public T this[int index] => throw new InvalidOperationException(
      "Over-budget list must not be materialized.");
    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException(
      "Over-budget list must not be materialized.");
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
      GetEnumerator();
  }
}
