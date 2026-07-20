using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;
using System.Numerics;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class TrackSectionResourceGraphTests {
  [Test]
  public void Resolve_LinksEverySidAndSplineRoleAcrossCatalogs() {
    var section = SectionSource(FullSection(), "track.common.ovl");
    var scenery = ScenerySource("SECTIONSCENERY", "track.common.ovl");
    var splineNames = new[] {
      "CarLeft", "CarRight", "JoinLeft", "JoinRight", "ExtraLeft", "ExtraRight",
      "WaterLeft", "WaterRight", "Loop", "PathA", "PathB", "SpeedLeft", "SpeedRight",
    };
    var splines = splineNames.Select((name, index) => SplineSource(
      name.ToUpperInvariant(),
      index % 2 == 0 ? "local.unique.ovl" : "external.unique.ovl")).ToList();

    var graph = TrackSectionResourceGraphResolver.Resolve(
      [section],
      [scenery],
      splines,
      [Closure(
        section.File.Path,
        section.File.Path,
        "local.unique.ovl",
        "external.unique.ovl")]);
    var link = graph.Sections.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.Zero);
      Assert.That(link.Source, Is.SameAs(section));
      Assert.That(link.Scenery.Source, Is.SameAs(scenery));
      Assert.That(link.Splines, Has.Count.EqualTo(13));
      Assert.That(link.Splines.All(spline => spline.IsResolved), Is.True);
      Assert.That(link.Splines.Where(spline =>
        spline.Role == TrackSectionSplineRole.Path).Select(spline => spline.Index),
        Is.EqualTo(new int?[] { 0, 1 }));
      Assert.That(link.Splines.Where(spline =>
        spline.Role is TrackSectionSplineRole.SpeedLeft or
          TrackSectionSplineRole.SpeedRight).Select(spline => spline.Index),
        Is.EqualTo(new int?[] { 0, 0 }));
      Assert.That(link.Splines.Select(spline => spline.Source!.File.Path).Distinct(),
        Is.EquivalentTo(new[] { "local.unique.ovl", "external.unique.ovl" }));
      Assert.That(link.Splines.Select(spline => spline.Source!.Resource),
        Is.EquivalentTo(splines.Select(spline => spline.Resource)));
    }
  }

  [Test]
  public void Resolve_PreservesMissingExternalTargets() {
    var section = SectionSource(BasicSection(), "track.common.ovl");

    var graph = TrackSectionResourceGraphResolver.Resolve(
      [section],
      [],
      [],
      [Closure(section.File.Path)]);
    var link = graph.Sections.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(5));
      Assert.That(link.Scenery.Reference, Is.EqualTo("SectionScenery:sid"));
      Assert.That(link.Scenery.IsResolved, Is.False);
      Assert.That(link.Splines, Has.Count.EqualTo(4));
      Assert.That(link.Splines.All(spline => !spline.IsResolved), Is.True);
    }
  }

  [TestCase("SectionScenery", "CarLeft:spl")]
  [TestCase("SectionScenery:sid", "CarLeft:sid")]
  public void Resolve_RejectsMalformedOrMismatchedTags(
    string sceneryReference,
    string carLeftReference
  ) {
    var section = SectionSource(
      BasicSection(
        sceneryReference: sceneryReference,
        carLeftReference: carLeftReference),
      "track.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve(
        [section],
        [],
        [],
        [Closure(section.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsCaseInsensitiveDuplicateSidResources() {
    var section = SectionSource(BasicSection(), "track.common.ovl");
    var first = ScenerySource("SectionScenery", "sid.common.ovl");
    var second = ScenerySource("SECTIONSCENERY", "sid.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve(
        [section],
        [first, second],
        [],
        [Closure(section.File.Path, first.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsCaseInsensitiveDuplicateSplineResources() {
    var section = SectionSource(BasicSection(), "track.common.ovl");
    var first = SplineSource("CarLeft", "local.unique.ovl");
    var second = SplineSource("CARLEFT", "external.unique.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve(
        [section],
        [],
        [first, second],
        [Closure(section.File.Path, first.File.Path, second.File.Path)])));
  }

  [Test]
  public void Resolve_RejectsNonSplineCatalogEntries() {
    var wrongType = SplineSource(
      "CarLeft",
      "external.unique.ovl",
      type: FileType.SceneryItem);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve([], [], [wrongType], [])));
  }

  [Test]
  public void Resolve_RejectsMismatchedOvlAndDecodedSplineNames() {
    var mismatched = SplineSource(
      "CarLeft",
      "external.unique.ovl",
      decodedName: "OtherSpline");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve([], [], [mismatched], [])));
  }

  [Test]
  public void Resolve_DoesNotBindLoneSameNameTargetsOutsideDependencyClosure() {
    var section = SectionSource(BasicSection(), "track.common.ovl");
    var scenery = ScenerySource("SectionScenery", "wrong.common.ovl");
    var spline = SplineSource("CarLeft", "wrong.common.ovl");

    var graph = TrackSectionResourceGraphResolver.Resolve(
      [section],
      [scenery],
      [spline],
      [Closure(section.File.Path, section.File.Path)]);
    var link = graph.Sections.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(5));
      Assert.That(link.Scenery.IsResolved, Is.False);
      Assert.That(link.Splines.Single(item =>
        item.Role == TrackSectionSplineRole.CarLeft).IsResolved, Is.False);
    }
  }

  [Test]
  public void Resolve_DoesNotNormalizeDependencyPaths() {
    var section = SectionSource(BasicSection(), "track.common.ovl");
    var scenery = ScenerySource("SectionScenery", "targets/./sid.common.ovl");

    var graph = TrackSectionResourceGraphResolver.Resolve(
      [section],
      [scenery],
      [],
      [Closure(section.File.Path, "targets/sid.common.ovl")]);

    Assert.That(graph.Sections.Single().Scenery.IsResolved, Is.False);
  }

  [Test]
  public void Resolve_RequiresExplicitDependencyClosureForEveryReferringArchive() {
    var section = SectionSource(BasicSection(), "track.common.ovl");

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve([section], [], [], [])));
  }

  [Test]
  public void Resolve_EnforcesAggregateRelationshipBudget() {
    var limits = new TrackSectionResourceGraphLimits(
      MaximumResourcesPerType: 100,
      MaximumResources: 100,
      MaximumRelationships: 4,
      MaximumStringCharacters: 100);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionResourceGraphResolver.Resolve(
        [SectionSource(BasicSection(), "track.common.ovl")],
        [],
        [],
        [Closure("track.common.ovl")],
        limits)));
  }

  [Test]
  public void Resolve_RejectsAggregateClosureTargetsBeforeMaterializingOverBudgetList() {
    var limits = new TrackSectionResourceGraphLimits(
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
      TrackSectionResourceGraphResolver.Resolve([], [], [], closures, limits)));

    Assert.That(exception!.Message, Does.Contain("aggregate resources"));
  }

  [TestCase("Track16", TrackSectionVersion.Vanilla, 15)]
  [TestCase("Track59", TrackSectionVersion.Wild, 145)]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Resolve_InstalledTrackArchiveLinksEverySidAndSpline(
    string archive,
    TrackSectionVersion expectedVersion,
    int expectedSectionCount
  ) {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      root,
      "Tracks",
      "coasters",
      archive,
      $"{archive}.common.ovl");
    Assert.That(path, Does.Exist, $"Installed {archive} OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var sections = TrackSections.Extract(ovl);
    var sceneryItems = SceneryItems.Extract(ovl);
    var sectionSources = TrackSectionSources(ovl, sections);
    var scenerySources = ScenerySources(ovl, sceneryItems);
    var splines = SplineSources(ovl);
    var sourcePath = sectionSources.Select(source => source.File.Path).Distinct().Single();
    var allowedTargetPaths = scenerySources.Select(source => source.File.Path)
      .Concat(splines.Select(source => source.File.Path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var graph = TrackSectionResourceGraphResolver.Resolve(
      sectionSources,
      scenerySources,
      splines,
      [Closure(sourcePath, allowedTargetPaths)]);

    TestContext.Progress.WriteLine(
      $"Installed TKS graph evidence: archive={archive}, sections={sections.Count}, " +
      $"sid={sceneryItems.Count}, spl={splines.Count}, " +
      $"edges={graph.Sections.Sum(section => section.Splines.Count + 1)}, " +
      $"unresolved={graph.UnresolvedReferenceCount}, " +
      $"external={string.Join(",", ovl.ExternalReferences)}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(sections, Has.Count.EqualTo(expectedSectionCount));
      Assert.That(sections.Any(section => section.Version == expectedVersion), Is.True);
      Assert.That(sceneryItems, Is.Not.Empty);
      Assert.That(splines, Is.Not.Empty);
      Assert.That(graph.Sections, Has.Count.EqualTo(sections.Count));
      Assert.That(graph.UnresolvedReferenceCount, Is.Zero);
      Assert.That(graph.Sections.All(section => section.Scenery.IsResolved), Is.True);
      Assert.That(graph.Sections.SelectMany(section => section.Splines)
        .All(spline => spline.IsResolved), Is.True);
      Assert.That(graph.Sections.SelectMany(section => section.Splines)
        .Select(spline => spline.Source!.File.Path).Distinct(),
        Is.EqualTo(new[] { path }).IgnoreCase);
      Assert.That(graph.Sections.Select(section => section.Source.File.Path).Distinct(),
        Is.EqualTo(new[] { sourcePath }).IgnoreCase);
      Assert.That(graph.Sections.Select(section => section.Scenery.Source!.File.Path).Distinct(),
        Is.EqualTo(scenerySources.Select(source => source.File.Path).Distinct()).IgnoreCase);
    }
  }

  private static TrackSection FullSection() => BasicSection(
    extraSplines: new TrackSectionSplinePair("ExtraLeft:spl", "ExtraRight:spl"),
    waterSplines: new TrackSectionSplinePair("WaterLeft:spl", "WaterRight:spl"),
    expansion: new TrackSectionExpansion(
      LoopSpline: "Loop:spl",
      PathSplines: ["PathA:spl", "PathB:spl"],
      StationLimits: [],
      TowerUnknown2: 0,
      Aquarium: 0,
      AutoGroup: null,
      GiantFlume: 0,
      Unknown68: 0,
      Unknown69: 0,
      EntryWideFlag: 0,
      ExitWideFlag: 0,
      SpeedSplines: [new TrackSectionSpeedSpline(
        1,
        new TrackSectionSplinePair("SpeedLeft:spl", "SpeedRight:spl"))],
      SlideEndToLiftHill: 0,
      SoakedOptions: 0,
      SpeedSplineValue: 0,
      Unknown77: 0,
      Unknown78: 0,
      Unknown79: 0,
      Groups: new TrackSectionGroups([], [], [], [], [], [])));

  private static TrackSection BasicSection(
    string name = "Section",
    string sceneryReference = "SectionScenery:sid",
    string carLeftReference = "CarLeft:spl",
    TrackSectionSplinePair? extraSplines = null,
    TrackSectionSplinePair? waterSplines = null,
    TrackSectionExpansion? expansion = null
  ) => new(
    name,
    expansion == null ? TrackSectionVersion.Vanilla : TrackSectionVersion.Soaked,
    $"{name} internal",
    sceneryReference,
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    SpecialCurves: 0,
    Direction: 0,
    new TrackSectionSplinePair(carLeftReference, "CarRight:spl"),
    new TrackSectionSplinePair("JoinLeft:spl", "JoinRight:spl"),
    extraSplines,
    waterSplines,
    Speeds: [],
    new TrackSectionAnimations(0, 0, 0, 0, 0, 0, 0, 0, 0, null, null),
    new TrackSectionOptions(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    expansion,
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

  private static IReadOnlyList<TrackSectionResourceSource> TrackSectionSources(
    Ovl ovl,
    IReadOnlyList<TrackSection> resources
  ) => resources.Select(resource => new TrackSectionResourceSource(
    FindFile(ovl, resource.Name, FileType.TrackSection, ".unique.ovl"),
    resource)).ToList();

  private static IReadOnlyList<SceneryItemResourceSource> ScenerySources(
    Ovl ovl,
    IReadOnlyList<SceneryItem> resources
  ) => resources.Select(resource => new SceneryItemResourceSource(
    FindFile(ovl, resource.Name, FileType.SceneryItem, ".unique.ovl"),
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

  private static TrackSectionResourceSource SectionSource(
    TrackSection resource,
    string path
  ) => new(new OvlFile(resource.Name, FileType.TrackSection, path), resource);

  private static SceneryItemResourceSource ScenerySource(string name, string path) =>
    new(new OvlFile(name, FileType.SceneryItem, path), Scenery(name));

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
