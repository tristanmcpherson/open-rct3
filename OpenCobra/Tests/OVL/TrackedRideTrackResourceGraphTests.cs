using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;
using System.Numerics;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class TrackedRideTrackResourceGraphTests {
  [Test]
  public void Resolve_LinksEveryTrackSectionAndSplineRole() {
    var ride = RideSource(FullRide(), "ride.common.ovl");
    var sectionNames = new[] {
      "SectionA", "SectionB", "OtherTop", "OtherMiddle", "TowerTop", "TowerMiddle",
      "OtherTopFlipped", "OtherMiddleFlipped",
    };
    var sections = sectionNames.Select(name => SectionSource(
      name.ToUpperInvariant(),
      "track.common.ovl")).ToList();
    var splineNames = new[] { "Track", "TrackBig", "Car", "CarSwing" };
    var splines = splineNames.Select((name, index) => SplineSource(
      name.ToUpperInvariant(),
      index % 2 == 0 ? "ride.common.ovl" : "external.common.ovl")).ToList();

    var graph = TrackedRideTrackResourceGraphResolver.Resolve(
      [ride],
      sections,
      splines,
      [Closure(
        ride.File.Path,
        ride.File.Path,
        "track.common.ovl",
        "external.common.ovl")]);
    var link = graph.Rides.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.Zero);
      Assert.That(link.Source, Is.SameAs(ride));
      Assert.That(link.TrackSections, Has.Count.EqualTo(8));
      Assert.That(link.TrackSections.All(section => section.IsResolved), Is.True);
      Assert.That(link.TrackSections.Where(section =>
        section.Role == TrackedRideTrackSectionRole.Construction)
        .Select(section => section.Index), Is.EqualTo(new int?[] { 0, 1 }));
      Assert.That(link.TrackSections.Where(section =>
        section.Role == TrackedRideTrackSectionRole.Construction)
        .All(section => section.Metadata != null), Is.True);
      Assert.That(link.TrackSections.Where(section =>
        section.Role == TrackedRideTrackSectionRole.Construction)
        .Any(section => !string.Equals(
          section.Metadata!.InternalName,
          section.Source!.Resource.InternalName,
          StringComparison.OrdinalIgnoreCase)), Is.True);
      Assert.That(link.Splines.Select(spline => spline.Role), Is.EqualTo(new[] {
        TrackedRideTrackSplineRole.Track,
        TrackedRideTrackSplineRole.TrackBig,
        TrackedRideTrackSplineRole.Car,
        TrackedRideTrackSplineRole.CarSwing,
      }));
      Assert.That(link.Splines.All(spline => spline.IsResolved), Is.True);
      Assert.That(link.Splines.Select(spline => spline.Source),
        Is.EquivalentTo(splines));
      Assert.That(link.Splines.Select(spline => spline.Source!.File.Path).Distinct(),
        Is.EquivalentTo(new[] { "ride.common.ovl", "external.common.ovl" }));
    }
  }

  [Test]
  public void Resolve_PreservesMissingExternalTargets() {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("MissingSection:tks", "missingsection", 10)],
        new TrackedRideReferences(
          null, null, null, null, "MissingSpline:spl", null, null, null)),
      "ride.common.ovl");

    var graph = TrackedRideTrackResourceGraphResolver.Resolve(
      [ride],
      [],
      [],
      [Closure(ride.File.Path)]);
    var link = graph.Rides.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(2));
      Assert.That(link.TrackSections.Single().Reference,
        Is.EqualTo("MissingSection:tks"));
      Assert.That(link.TrackSections.Single().IsResolved, Is.False);
      Assert.That(link.Splines.Single().Reference, Is.EqualTo("MissingSpline:spl"));
      Assert.That(link.Splines.Single().IsResolved, Is.False);
    }
  }

  [TestCase("Section:sid", null)]
  [TestCase("Section:tks", "Track:tks")]
  public void Resolve_RejectsMalformedOrMismatchedTags(
    string sectionReference,
    string? splineReference
  ) {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection(sectionReference, "section", 10)],
        new TrackedRideReferences(
          null, null, null, null, splineReference, null, null, null)),
      "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [ride],
        [],
        [],
        [Closure(ride.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsCaseInsensitiveDuplicateTksResources() {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("Section:tks", "section", 10)],
        EmptyReferences()),
      "ride.common.ovl");
    var first = SectionSource("Section", "track.common.ovl");
    var second = SectionSource("SECTION", "external.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [ride],
        [first, second],
        [],
        [Closure(ride.File.Path, first.File.Path, second.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsConstructionMetadataThatDoesNotMatchReferenceKey() {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("Section:tks", "DifferentSection", 10)],
        EmptyReferences()),
      "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [ride],
        [SectionSource("Section", "track.common.ovl")],
        [],
        [Closure(ride.File.Path, "track.common.ovl")])));
  }

  [Test]
  public void Resolve_RejectsCaseInsensitiveDuplicateSplineResources() {
    var ride = RideSource(
      Ride(
        [],
        new TrackedRideReferences(
          null, null, null, null, "Track:spl", null, null, null)),
      "ride.common.ovl");
    var first = SplineSource("Track", "ride.common.ovl");
    var second = SplineSource("TRACK", "external.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [ride],
        [],
        [first, second],
        [Closure(ride.File.Path, first.File.Path, second.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsCaseInsensitiveDuplicateTrackedRideResources() {
    var first = RideSource(Ride([], EmptyReferences(), "Ride"), "ride.common.ovl");
    var second = RideSource(Ride([], EmptyReferences(), "RIDE"), "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [first, second],
        [],
        [],
        [Closure(first.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsMalformedSplineResourceName() {
    var malformed = SplineSource(string.Empty, "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve([], [], [malformed], [])));
  }

  [Test]
  public void Resolve_RejectsMismatchedOvlAndDecodedSplineNames() {
    var mismatched = SplineSource(
      "Track",
      "ride.common.ovl",
      decodedName: "OtherSpline");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve([], [], [mismatched], [])));
  }

  [Test]
  public void Resolve_RejectsNonSplineCatalogEntries() {
    var wrongType = SplineSource(
      "Track",
      "ride.common.ovl",
      type: FileType.SceneryItem);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve([], [], [wrongType], [])));
  }

  [Test]
  public void Resolve_DoesNotBindLoneSameNameTargetsOutsideDependencyClosure() {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("Section:tks", "section", 10)],
        new TrackedRideReferences(
          null, null, null, null, "Track:spl", null, null, null)),
      "ride.common.ovl");
    var section = SectionSource("Section", "wrong.common.ovl");
    var spline = SplineSource("Track", "wrong.common.ovl");

    var graph = TrackedRideTrackResourceGraphResolver.Resolve(
      [ride],
      [section],
      [spline],
      [Closure(ride.File.Path, ride.File.Path)]);
    var link = graph.Rides.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(2));
      Assert.That(link.TrackSections.Single().IsResolved, Is.False);
      Assert.That(link.Splines.Single().IsResolved, Is.False);
    }
  }

  [Test]
  public void Resolve_DoesNotNormalizeDependencyPaths() {
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("Section:tks", "section", 10)],
        EmptyReferences()),
      "ride.common.ovl");
    var section = SectionSource("Section", "tracks/./track.common.ovl");

    var graph = TrackedRideTrackResourceGraphResolver.Resolve(
      [ride],
      [section],
      [],
      [Closure(ride.File.Path, "tracks/track.common.ovl")]);

    Assert.That(graph.Rides.Single().TrackSections.Single().IsResolved, Is.False);
  }

  [Test]
  public void Resolve_RequiresExplicitDependencyClosureForEveryReferringArchive() {
    var ride = RideSource(Ride([], EmptyReferences()), "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve([ride], [], [], [])));
  }

  [Test]
  public void Resolve_EnforcesAggregateRelationshipBudget() {
    var limits = new TrackedRideTrackResourceGraphLimits(
      MaximumResourcesPerType: 100,
      MaximumResources: 100,
      MaximumRelationships: 1,
      MaximumStringCharacters: 100);
    var ride = RideSource(
      Ride(
        [new TrackedRideTrackSection("Section:tks", "section", 10)],
        new TrackedRideReferences(
          null, null, null, null, "Track:spl", null, null, null)),
      "ride.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve(
        [ride],
        [],
        [],
        [Closure(ride.File.Path)],
        limits)));
  }

  [Test]
  public void Resolve_RejectsAggregateClosureTargetsBeforeMaterializingOverBudgetList() {
    var limits = new TrackedRideTrackResourceGraphLimits(
      MaximumResourcesPerType: 2,
      MaximumResources: 3,
      MaximumRelationships: 100,
      MaximumStringCharacters: 100);
    var closures = new[] {
      Closure("first.common.ovl", "first-a.common.ovl", "first-b.common.ovl"),
      new OvlResourceDependencyClosure(
        "second.common.ovl",
        new ThrowingTargetPaths(2)),
    };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRideTrackResourceGraphResolver.Resolve([], [], [], closures, limits)));

    Assert.That(exception!.Message, Does.Contain("aggregate resources"));
  }

  [TestCase("Mono", "TrackBased07", 15, 15)]
  [TestCase("LogFlume", "TrackBased10", 33, 39)]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Resolve_InstalledRideLinksExternalTrackSectionsAndLocalSplines(
    string rideName,
    string trackArchive,
    int expectedRequestedSectionCount,
    int expectedCatalogSectionCount
  ) {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var ridePath = Path.Combine(
      root,
      "Tracks",
      "TrackedRides",
      rideName,
      $"{rideName}.common.ovl");
    Assert.That(ridePath, Does.Exist, $"Installed {rideName} OVL not found: {ridePath}");

    using var rideOvl = Ovl.Load(ridePath);
    var ride = TrackedRides.Extract(rideOvl).Single(item => item.Name == rideName);
    var serializedTrackPath = ride.Expansion == null
      ? ride.VanillaTrackPath!
      : ride.Expansion.TrackPaths.Single();
    Assert.That(serializedTrackPath, Is.Not.Null.And.Not.Empty);
    var trackPath = Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(ridePath)!,
      $"{serializedTrackPath}.common.ovl"));
    var expectedTrackPath = Path.Combine(
      root,
      "Tracks",
      "TrackedRides",
      trackArchive,
      $"{trackArchive}.common.ovl");
    Assert.That(trackPath, Is.EqualTo(expectedTrackPath).IgnoreCase);
    Assert.That(trackPath, Does.Exist, $"Installed {trackArchive} OVL not found: {trackPath}");

    using var trackOvl = Ovl.Load(trackPath);
    var trackSections = TrackSections.Extract(trackOvl);
    var rideSource = new TrackedRideTrackResourceSource(
      FindFile(rideOvl, ride.Name, FileType.TrackedRide, ".unique.ovl"),
      ride);
    var trackSectionSources = TrackSectionSources(trackOvl, trackSections);
    var splines = SplineSources(rideOvl);
    var allowedTargetPaths = trackSectionSources.Select(source => source.File.Path)
      .Concat(splines.Select(source => source.File.Path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var graph = TrackedRideTrackResourceGraphResolver.Resolve(
      [rideSource],
      trackSectionSources,
      splines,
      [Closure(rideSource.File.Path, allowedTargetPaths)]);
    var link = graph.Rides.Single();
    var construction = link.TrackSections.Where(section =>
      section.Role == TrackedRideTrackSectionRole.Construction).ToList();
    var mismatchedInternalNames = construction.Where(section =>
      !string.Equals(
        section.Metadata!.InternalName,
        section.Source!.Resource.InternalName,
        StringComparison.OrdinalIgnoreCase)).ToList();

    TestContext.Progress.WriteLine(
      $"Installed TRR track graph evidence: ride={ride.Name}, " +
      $"track-path={serializedTrackPath}, requested-tks={ride.TrackSections.Count}, " +
      $"catalog-tks={trackSections.Count}, local-spl={splines.Count}, " +
      $"tks-edges={link.TrackSections.Count}, spl-edges={link.Splines.Count}, " +
      $"metadata-mismatches={mismatchedInternalNames.Count}, " +
      $"splines={string.Join(",", link.Splines.Select(spline =>
        $"{spline.Role}:{spline.Reference}:{spline.IsResolved}"))}, " +
      $"unresolved={graph.UnresolvedReferenceCount}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.TrackSections, Has.Count.EqualTo(expectedRequestedSectionCount));
      Assert.That(trackSections, Has.Count.EqualTo(expectedCatalogSectionCount));
      Assert.That(construction, Has.Count.EqualTo(ride.TrackSections.Count));
      Assert.That(construction.All(section => section.IsResolved), Is.True);
      Assert.That(construction.Select(section => section.Source!.File.Path).Distinct(),
        Is.EqualTo(trackSectionSources.Select(source => source.File.Path).Distinct()).IgnoreCase);
      Assert.That(mismatchedInternalNames, Is.Empty);
      Assert.That(link.Splines, Has.Count.EqualTo(4));
      Assert.That(link.Splines.Count(spline => spline.IsResolved), Is.EqualTo(2));
      Assert.That(link.Splines.Where(spline => spline.IsResolved)
        .Select(spline => spline.Source!.File.Path).Distinct(),
        Is.EqualTo(new[] { ridePath }).IgnoreCase);
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(2));
    }
  }

  private static TrackedRide FullRide() => Ride(
    [
      new TrackedRideTrackSection("SectionA:tks", "sectiona", 10),
      new TrackedRideTrackSection("SectionB:tks", "sectionb", 20),
    ],
    new TrackedRideReferences(
      "OtherTop:tks",
      "OtherMiddle:tks",
      "TowerTop:tks",
      "TowerMiddle:tks",
      "Track:spl",
      "TrackBig:spl",
      "Car:spl",
      "CarSwing:spl"),
    expansion: new TrackedRideExpansion(
      OtherTopFlipped: "OtherTopFlipped:tks",
      OtherMiddleFlipped: "OtherMiddleFlipped:tks",
      GroupDefinitionCount: 0,
      TrackPaths: ["../External/External"],
      MinimumLength: 0,
      WaterSectionFlag: 0,
      WaterSectionSpeed: 0,
      WaterSectionAcceleration: 0,
      WaterSectionDeceleration: 0,
      Unknown102: 0,
      Unknown103: 0));

  private static TrackedRide Ride(
    IReadOnlyList<TrackedRideTrackSection> trackSections,
    TrackedRideReferences references,
    string name = "Ride",
    TrackedRideExpansion? expansion = null
  ) => new(
    name,
    expansion == null ? TrackedRideVersion.Vanilla : TrackedRideVersion.Soaked,
    trackSections,
    TrainNames: [],
    CableLift: null,
    LiftCar: null,
    VanillaTrackPath: expansion == null ? "SyntheticTrack" : null,
    new TrackedRideStation(null, 0, 0, 0, 0, 0),
    new TrackedRideMotion(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    new TrackedRideOptions(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideCosts(0, 0, 0, 0, 0, 0, 0),
    references,
    expansion,
    Wild: null);

  private static TrackedRideReferences EmptyReferences() =>
    new(null, null, null, null, null, null, null, null);

  private static TrackSection Section(string name) => new(
    name,
    TrackSectionVersion.Vanilla,
    $"{name} internal",
    $"{name}:sid",
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    SpecialCurves: 0,
    Direction: 0,
    new TrackSectionSplinePair($"{name}CarLeft:spl", $"{name}CarRight:spl"),
    new TrackSectionSplinePair($"{name}JoinLeft:spl", $"{name}JoinRight:spl"),
    ExtraSplines: null,
    WaterSplines: null,
    Speeds: [],
    new TrackSectionAnimations(0, 0, 0, 0, 0, 0, 0, 0, 0, null, null),
    new TrackSectionOptions(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    Expansion: null,
    Wild: null);

  private static IReadOnlyList<TrackSectionResourceSource> TrackSectionSources(
    Ovl ovl,
    IReadOnlyList<TrackSection> resources
  ) => resources.Select(resource => new TrackSectionResourceSource(
    FindFile(ovl, resource.Name, FileType.TrackSection, ".unique.ovl"),
    resource)).ToList();

  private static IReadOnlyList<SplineResourceSource> SplineSources(Ovl ovl) {
    return Splines.Extract(ovl).Select(resource => new SplineResourceSource(
      FindFile(ovl, resource.Name, FileType.Spline, ".common.ovl"),
      resource)).ToList();
  }

  private static OvlFile FindFile(
    Ovl ovl,
    string name,
    FileType type,
    string pathSuffix
  ) =>
    ovl.Keys.Single(file =>
      file.Type == type &&
      file.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));

  private static TrackedRideTrackResourceSource RideSource(
    TrackedRide resource,
    string path
  ) => new(new OvlFile(resource.Name, FileType.TrackedRide, path), resource);

  private static TrackSectionResourceSource SectionSource(string name, string path) {
    var resource = Section(name);
    return new TrackSectionResourceSource(
      new OvlFile(name, FileType.TrackSection, path),
      resource);
  }

  private static OvlResourceDependencyClosure Closure(
    string sourcePath,
    params string[] targetPaths
  ) => new(sourcePath, targetPaths);

  private sealed class ThrowingTargetPaths(int count) : IReadOnlyList<string> {
    public int Count { get; } = count;

    public string this[int index] => throw new InvalidOperationException(
      "Over-budget target paths must not be materialized.");

    public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException(
      "Over-budget target paths must not be materialized.");

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
      GetEnumerator();
  }

  private static SplineResourceSource SplineSource(
    string name,
    string path,
    string? decodedName = null,
    FileType type = FileType.Spline
  ) => new(
    new OvlFile(name, type, path),
    new Spline(
      decodedName ?? name,
      Cyclic: false,
      TotalLength: 1,
      InverseTotalLength: 1,
      MaximumY: 0,
      Nodes: [new SplineNode(Vector3.Zero, Vector3.Zero, Vector3.Zero)],
      Segments: []));
}
