// Ride Track Resource Catalog Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackResourceCatalogTests {
  [Test]
  public void ResolveAll_ComposesExactDatTksSplAndTrrProvenance() {
    var fixture = CreateFixture();
    var placement = Placement();

    var result = fixture.Catalog.ResolveAll([placement]);
    var link = result.Placements.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.UnresolvedPlacementCount, Is.Zero);
      Assert.That(link.IsResolved, Is.True);
      Assert.That(link.Placement, Is.SameAs(placement));
      Assert.That(link.Section, Is.SameAs(fixture.SectionGraph.Sections.Single()));
      Assert.That(link.PlacementSection.Source, Is.SameAs(fixture.PlacementSource));
      Assert.That(link.Splines, Has.Count.EqualTo(4));
      Assert.That(link.Splines.All(spline => spline.IsResolved), Is.True);
      Assert.That(link.Splines.Select(spline => spline.Source!.File.Path).Distinct(),
        Is.EquivalentTo(new[] { "splines\\local.common.ovl", "splines\\shared.common.ovl" }));
      Assert.That(link.Declarations, Has.Count.EqualTo(1));
      Assert.That(link.Declarations[0].Ride.Source, Is.SameAs(fixture.RideSource));
      Assert.That(
        link.Declarations[0].Section.Role,
        Is.EqualTo(TrackedRideTrackSectionRole.Construction));
      Assert.That(link.Declarations[0].Section.Source!.File.Path,
        Is.EqualTo(fixture.SectionSource.File.Path));
    }
  }

  [Test]
  public void ResolveAll_DoesNotFallbackToSameNameFromAnotherOverlay() {
    var fixture = CreateFixture();
    var placement = Placement(overlayPath: "Tracks/Exact/Synthetic");

    var result = fixture.Catalog.ResolveAll([placement]);
    var link = result.Placements.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.UnresolvedPlacementCount, Is.EqualTo(1));
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Section, Is.Null);
      Assert.That(link.Splines, Is.Empty);
      Assert.That(link.Declarations, Is.Empty);
    }
  }

  [Test]
  public void ResolveAll_DisambiguatesSameNameByOverlayAndExactOvlSource() {
    var first = SectionGraphSource("tracks\\first.unique.ovl", "first");
    var second = SectionGraphSource("tracks\\second.unique.ovl", "second");
    var graph = new TrackSectionResourceGraph([first.Link, second.Link], 0);
    var firstPlacementSource = new RideTrackSectionResourceSource(
      "Tracks\\First\\Synthetic",
      first.Source.File,
      first.Source.Resource);
    var secondPlacementSource = new RideTrackSectionResourceSource(
      "Tracks\\Second\\Synthetic",
      second.Source.File,
      second.Source.Resource);
    var catalog = new RideTrackResourceCatalog(
      [firstPlacementSource, secondPlacementSource],
      graph,
      new TrackedRideTrackResourceGraph([], 0));

    var result = catalog.ResolveAll([
      Placement(overlayPath: secondPlacementSource.OverlayPath),
    ]);

    Assert.That(
      result.Placements.Single().Section!.Source.File.Path,
      Is.EqualTo("tracks\\second.unique.ovl"));
  }

  [Test]
  public void Constructor_RejectsOverlaySourceAbsentFromGraphInsteadOfNameFallback() {
    var fixture = CreateFixture();
    var wrongArchiveSource = new RideTrackSectionResourceSource(
      fixture.PlacementSource.OverlayPath,
      new OvlFile(
        fixture.SectionSource.File.Name,
        FileType.TrackSection,
        "tracks\\other.unique.ovl"),
      fixture.SectionSource.Resource);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [wrongArchiveSource],
        fixture.SectionGraph,
        fixture.RideGraph)));

    Assert.That(exception!.Message, Does.Contain("absent from the TKS graph"));
  }

  [Test]
  public void Constructor_RejectsIndependentlyDecodedTksForSameOvlIdentity() {
    var fixture = CreateFixture();
    var ambiguous = new RideTrackSectionResourceSource(
      fixture.PlacementSource.OverlayPath,
      fixture.SectionSource.File,
      Section(fixture.SectionSource.Resource.Name));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [ambiguous],
        fixture.SectionGraph,
        fixture.RideGraph)));

    Assert.That(exception!.Message, Does.Contain("ambiguous decoded TKS identity"));
  }

  [Test]
  public void Constructor_RejectsDuplicateExactTksGraphIdentity() {
    var fixture = CreateFixture();
    var section = fixture.SectionGraph.Sections.Single();
    var duplicateGraph = new TrackSectionResourceGraph([section, section], 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [fixture.PlacementSource],
        duplicateGraph,
        fixture.RideGraph)));

    Assert.That(exception!.Message, Does.Contain("repeats exact source"));
  }

  [Test]
  public void Constructor_RejectsTrrTksTargetNotBackedBySectionGraph() {
    var fixture = CreateFixture();
    var ride = fixture.RideGraph.Rides.Single();
    var section = ride.TrackSections.Single();
    var foreignSource = new TrackSectionResourceSource(
      new OvlFile(section.Source!.File.Name, FileType.TrackSection, "foreign.unique.ovl"),
      section.Source.Resource);
    var foreignSection = section with { Source = foreignSource };
    var foreignRide = ride with { TrackSections = [foreignSection] };
    var foreignGraph = new TrackedRideTrackResourceGraph([foreignRide], 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [fixture.PlacementSource],
        fixture.SectionGraph,
        foreignGraph)));

    Assert.That(exception!.Message, Does.Contain("absent from the TKS graph"));
  }

  [Test]
  public void Constructor_RejectsNonFiniteResolvedSplineGeometry() {
    var fixture = CreateFixture();
    var section = fixture.SectionGraph.Sections.Single();
    var splines = section.Splines.ToArray();
    var carLeftIndex = Array.FindIndex(
      splines,
      spline => spline.Role == TrackSectionSplineRole.CarLeft);
    var original = splines[carLeftIndex];
    var malformedSpline = SplineSource(
      original.Source!.Resource.Name,
      original.Source.File.Path,
      position: new Vector3(float.NaN, 0f, 0f));
    splines[carLeftIndex] = original with { Source = malformedSpline };
    var malformedSection = section with { Splines = splines };
    var malformedGraph = new TrackSectionResourceGraph([malformedSection], 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [fixture.PlacementSource with { Resource = malformedSection.Source.Resource }],
        malformedGraph,
        new TrackedRideTrackResourceGraph([], 0))));

    Assert.That(exception!.Message, Does.Contain("non-finite node geometry"));
  }

  [Test]
  public void Constructor_RejectsMalformedAdvertisedUnresolvedCount() {
    var fixture = CreateFixture();
    var malformedGraph = fixture.SectionGraph with { UnresolvedReferenceCount = 1 };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [fixture.PlacementSource],
        malformedGraph,
        fixture.RideGraph)));

    Assert.That(exception!.Message, Does.Contain(
      "advertises 1 unresolved references, found 0"));
  }

  [Test]
  public void Constructor_PreflightsOverBudgetGraphBeforeEnumeration() {
    var limits = new RideTrackResourceCatalogLimits(
      MaximumResources: 1,
      MaximumRelationships: 10,
      MaximumSplineDataElements: 100,
      MaximumStringCharacters: 1_000,
      MaximumIdentifierLength: 100,
      MaximumPlacements: 10);
    var graph = new TrackSectionResourceGraph(
      new ThrowingList<TrackSectionResourceLink>(2),
      0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackResourceCatalog(
        [],
        graph,
        new TrackedRideTrackResourceGraph([], 0),
        limits)));

    Assert.That(exception!.Message, Does.Contain("count 2 exceeds the catalog limit 1"));
  }

  [Test]
  public void ResolveAll_PreflightsOverBudgetPlacementsBeforeEnumeration() {
    var fixture = CreateFixture();
    var limits = new RideTrackResourceCatalogLimits(
      MaximumResources: 100,
      MaximumRelationships: 100,
      MaximumSplineDataElements: 1_000,
      MaximumStringCharacters: 10_000,
      MaximumIdentifierLength: 100,
      MaximumPlacements: 1);
    var catalog = new RideTrackResourceCatalog(
      [fixture.PlacementSource],
      fixture.SectionGraph,
      fixture.RideGraph,
      limits);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      catalog.ResolveAll(new ThrowingList<RideTrackPlacement>(2))));

    Assert.That(exception!.Message, Does.Contain("DAT placements count 2"));
  }

  private static CatalogFixture CreateFixture() {
    const string sectionPath = "tracks\\synthetic.unique.ovl";
    var section = Section("Straight");
    var sectionSource = new TrackSectionResourceSource(
      new OvlFile(section.Name, FileType.TrackSection, sectionPath),
      section);
    var scenery = ScenerySource("StraightScenery", "scenery\\shared.unique.ovl");
    var splines = new[] {
      SplineSource("CarLeft", "splines\\local.common.ovl"),
      SplineSource("CarRight", "splines\\shared.common.ovl"),
      SplineSource("JoinLeft", "splines\\local.common.ovl"),
      SplineSource("JoinRight", "splines\\shared.common.ovl"),
    };
    var sectionGraph = TrackSectionResourceGraphResolver.Resolve(
      [sectionSource],
      [scenery],
      splines,
      [new OvlResourceDependencyClosure(
        sectionPath,
        [scenery.File.Path, .. splines
          .Select(spline => spline.File.Path)
          .Distinct(StringComparer.OrdinalIgnoreCase)])]);

    var ride = Ride("SyntheticRide", "Straight:tks", "straight");
    var rideSource = new TrackedRideTrackResourceSource(
      new OvlFile(ride.Name, FileType.TrackedRide, "rides\\synthetic.unique.ovl"),
      ride);
    var rideSpline = SplineSource("RideTrack", "rides\\synthetic.common.ovl");
    var rideGraph = TrackedRideTrackResourceGraphResolver.Resolve(
      [rideSource],
      [sectionSource],
      [rideSpline],
      [new OvlResourceDependencyClosure(
        rideSource.File.Path,
        [sectionSource.File.Path, rideSpline.File.Path])]);
    var placementSource = new RideTrackSectionResourceSource(
      "Tracks\\Exact\\Synthetic",
      sectionSource.File,
      sectionSource.Resource);
    var catalog = new RideTrackResourceCatalog(
      [placementSource],
      sectionGraph,
      rideGraph);
    return new CatalogFixture(
      catalog,
      placementSource,
      sectionSource,
      sectionGraph,
      rideSource,
      rideGraph);
  }

  private static SectionGraphFixture SectionGraphSource(string path, string suffix) {
    var resource = Section("Straight");
    var source = new TrackSectionResourceSource(
      new OvlFile(resource.Name, FileType.TrackSection, path),
      resource);
    var scenery = ScenerySource("StraightScenery", $"scenery\\{suffix}.unique.ovl");
    var splines = new[] {
      SplineSource("CarLeft", $"splines\\{suffix}-car-left.common.ovl"),
      SplineSource("CarRight", $"splines\\{suffix}-car-right.common.ovl"),
      SplineSource("JoinLeft", $"splines\\{suffix}-join-left.common.ovl"),
      SplineSource("JoinRight", $"splines\\{suffix}-join-right.common.ovl"),
    };
    var link = new TrackSectionResourceLink(
      source,
      new TrackSectionSceneryLink("StraightScenery:sid", scenery),
      [
        new(TrackSectionSplineRole.CarLeft, null, "CarLeft:spl", splines[0]),
        new(TrackSectionSplineRole.CarRight, null, "CarRight:spl", splines[1]),
        new(TrackSectionSplineRole.JoinLeft, null, "JoinLeft:spl", splines[2]),
        new(TrackSectionSplineRole.JoinRight, null, "JoinRight:spl", splines[3]),
      ]);
    return new SectionGraphFixture(source, link);
  }

  private static RideTrackPlacement Placement(
    string overlayPath = "Tracks\\Exact\\Synthetic",
    ulong entryId = 500
  ) => new(
    sourceEntryId: entryId,
    sceneryPlacementSourceEntryId: 100,
    sidDatabaseEntryReference: 200,
    symbolName: "Straight:tks",
    objectKey: "Straight",
    overlayPath,
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

  private static TrackedRide Ride(
    string name,
    string sectionReference,
    string internalName
  ) => new(
    name,
    TrackedRideVersion.Vanilla,
    [new TrackedRideTrackSection(sectionReference, internalName, 10)],
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

  private static SceneryItemResourceSource ScenerySource(string name, string path) => new(
    new OvlFile(name, FileType.SceneryItem, path),
    new SceneryItem(
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
      VisualRefs: []));

  private static SplineResourceSource SplineSource(
    string name,
    string path,
    Vector3? position = null
  ) => new(
    new OvlFile(name, FileType.Spline, path),
    new Spline(
      name,
      Cyclic: false,
      TotalLength: 1f,
      InverseTotalLength: 1f,
      MaximumY: 0f,
      Nodes: [
        new SplineNode(
          position ?? Vector3.Zero,
          Vector3.Zero,
          new Vector3(0.25f, 0f, 0f)),
        new SplineNode(
          Vector3.UnitX,
          new Vector3(-0.25f, 0f, 0f),
          Vector3.Zero),
      ],
      Segments: [new SplineSegment(1f, new byte[14])]));

  private sealed record CatalogFixture(
    RideTrackResourceCatalog Catalog,
    RideTrackSectionResourceSource PlacementSource,
    TrackSectionResourceSource SectionSource,
    TrackSectionResourceGraph SectionGraph,
    TrackedRideTrackResourceSource RideSource,
    TrackedRideTrackResourceGraph RideGraph
  );

  private sealed record SectionGraphFixture(
    TrackSectionResourceSource Source,
    TrackSectionResourceLink Link
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
