// Ride Track Section Resource Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackSectionResourceResolverTests {
  [Test]
  public void Resolve_UsesExactOverlayAndCaseInsensitiveTaggedSymbol() {
    const string overlay = "Tracks\\TrackedRides\\TrackBased07\\TrackBased07";
    var source = CreateSource(overlay, "StationStraight");
    var resolver = new RideTrackSectionResourceResolver([source]);
    var placement = CreatePlacement(
      overlayPath: "tracks\\trackedrides\\trackbased07\\trackbased07",
      objectKey: "stationstraight",
      symbolName: "stationstraight:TKS");

    var link = resolver.Resolve(placement);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.True);
      Assert.That(link.Placement, Is.SameAs(placement));
      Assert.That(link.Source, Is.SameAs(source));
      Assert.That(link.Resource, Is.SameAs(source.Resource));
    }
  }

  [Test]
  public void Resolve_DoesNotNormalizeOverlaySeparatorsOrFallbackBySymbol() {
    const string overlay = "Tracks\\TrackedRides\\TrackBased07\\TrackBased07";
    var resolver = new RideTrackSectionResourceResolver([
      CreateSource(overlay, "StationStraight"),
    ]);
    var placement = CreatePlacement(
      overlayPath: "Tracks/TrackedRides/TrackBased07/TrackBased07");

    var link = resolver.Resolve(placement);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Source, Is.Null);
      Assert.That(link.Placement.OverlayPath, Is.EqualTo(
        "Tracks/TrackedRides/TrackBased07/TrackBased07"));
      Assert.That(link.Placement.SymbolName, Is.EqualTo("StationStraight:tks"));
    }
  }

  [Test]
  public void Resolve_PreservesUnresolvedExternalOrCustomIdentity() {
    var resolver = new RideTrackSectionResourceResolver([]);
    var placement = CreatePlacement(
      overlayPath: "Custom\\TrackSections\\BuilderTrack\\BuilderTrack",
      objectKey: "BuilderSection",
      symbolName: "BuilderSection:tks");

    var link = resolver.Resolve(placement);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Source, Is.Null);
      Assert.That(link.Placement, Is.SameAs(placement));
    }
  }

  [Test]
  public void Constructor_RejectsCaseInsensitiveDuplicateCompositeIdentity() {
    var first = CreateSource(
      "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
      "StationStraight");
    var second = CreateSource(
      "tracks\\trackedrides\\trackbased07\\trackbased07",
      "STATIONSTRAIGHT");

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackSectionResourceResolver([first, second])));
  }

  [Test]
  public void Constructor_AllowsSameResourceNameInDifferentExactOverlays() {
    var first = CreateSource("Tracks\\First\\Shared", "Shared");
    var second = CreateSource("Tracks\\Second\\Shared", "Shared");
    var resolver = new RideTrackSectionResourceResolver([first, second]);

    var firstLink = resolver.Resolve(CreatePlacement(
      overlayPath: first.OverlayPath,
      objectKey: "Shared",
      symbolName: "Shared:tks"));
    var secondLink = resolver.Resolve(CreatePlacement(
      entryId: 501,
      overlayPath: second.OverlayPath,
      objectKey: "Shared",
      symbolName: "Shared:tks"));

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstLink.Source, Is.SameAs(first));
      Assert.That(secondLink.Source, Is.SameAs(second));
    }
  }

  [Test]
  public void Constructor_RejectsNonTrackSectionOvlType() {
    var source = new RideTrackSectionResourceSource(
      "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
      new OvlFile("StationStraight", FileType.SceneryItem, "fixture.unique.ovl"),
      CreateSection("StationStraight"));

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackSectionResourceResolver([source])));
  }

  [Test]
  public void Constructor_RejectsAmbiguousOvlAndDecodedNames() {
    var source = new RideTrackSectionResourceSource(
      "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
      new OvlFile("StationAlias", FileType.TrackSection, "fixture.unique.ovl"),
      CreateSection("StationStraight"));

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackSectionResourceResolver([source])));
  }

  [TestCase("StationStraight")]
  [TestCase("StationStraight:sid")]
  [TestCase("StationStraight:tks:extra")]
  public void Resolve_RejectsMissingWrongOrAmbiguousResourceTag(string symbolName) {
    var resolver = new RideTrackSectionResourceResolver([
      CreateSource(
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
        "StationStraight"),
    ]);
    var placement = CreatePlacement(symbolName: symbolName);

    Assert.Throws<InvalidDataException>(new Action(() => resolver.Resolve(placement)));
  }

  [Test]
  public void Resolve_RejectsObjectKeyThatDoesNotMatchTaggedSymbol() {
    var resolver = new RideTrackSectionResourceResolver([]);
    var placement = CreatePlacement(
      objectKey: "StationStraight",
      symbolName: "SlopeStraight:tks");

    Assert.Throws<InvalidDataException>(new Action(() => resolver.Resolve(placement)));
  }

  [Test]
  public void ResolveAll_RejectsDuplicateDatEntryIds() {
    var resolver = new RideTrackSectionResourceResolver([]);
    var first = CreatePlacement(entryId: 500);
    var second = CreatePlacement(entryId: 500);

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.ResolveAll([first, second])));
  }

  [Test]
  public void Constructor_RejectsResourceCountAboveBoundBeforeIndexing() {
    var source = CreateSource(
      "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
      "StationStraight");
    var resources = Enumerable.Repeat(source, 100_001).ToArray();

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideTrackSectionResourceResolver(resources)));
  }

  [Test]
  public void ResolveAll_RejectsPlacementCountAboveBoundBeforeResolving() {
    var resolver = new RideTrackSectionResourceResolver([]);
    var placement = CreatePlacement();
    var placements = Enumerable.Repeat(placement, 100_001).ToArray();

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.ResolveAll(placements)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Resolve_InstalledCampaignsLinkExactDatOverlayAndTksIdentities() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(root),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(root),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");

    foreach (var campaign in InstalledCampaigns) {
      var path = Path.Combine(
        root!,
        campaign.RelativePath.Replace('/', Path.DirectorySeparatorChar));
      Assert.That(File.Exists(path), Is.True, path);
      var data = DatTerrainReader.Read(path);
      var terrain = Terrain.FromData(data);
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);

      var sources = LoadInstalledSources(root!, park.RideTrackPlacements);
      var links = new RideTrackSectionResourceResolver(sources)
        .ResolveAll(park.RideTrackPlacements);
      var identities = links.Select(link =>
        $"{link.Placement.OverlayPath}|{link.Placement.SymbolName}")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

      using (Assert.EnterMultipleScope()) {
        Assert.That(data.TrackPieces, Has.Count.EqualTo(campaign.PlacementCount));
        Assert.That(links, Has.Count.EqualTo(campaign.PlacementCount));
        Assert.That(identities, Is.EquivalentTo(campaign.Identities));
        Assert.That(links.All(link => link.IsResolved), Is.True, campaign.RelativePath);
        foreach (var link in links) {
          Assert.That(
            link.Source!.OverlayPath,
            Is.EqualTo(link.Placement.OverlayPath).IgnoreCase,
            campaign.RelativePath);
          Assert.That(
            link.Resource!.Name,
            Is.EqualTo(link.Placement.ObjectKey).IgnoreCase,
            campaign.RelativePath);
          Assert.That(
            link.Resource.Name + ":tks",
            Is.EqualTo(link.Placement.SymbolName).IgnoreCase,
            campaign.RelativePath);
          Assert.That(link.Source.File.Type, Is.EqualTo(FileType.TrackSection));
        }
      }

      TestContext.Progress.WriteLine(
        $"{campaign.RelativePath}: resolved {links.Count}/{links.Count}; " +
        $"identities={string.Join(",", identities)}");
    }
  }

  private static IReadOnlyList<RideTrackSectionResourceSource> LoadInstalledSources(
    string root,
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    var sources = new List<RideTrackSectionResourceSource>();
    foreach (var overlayPath in placements
      .Select(placement => placement.OverlayPath)
      .Distinct(StringComparer.OrdinalIgnoreCase)) {
      var commonPath = Path.Combine(root, overlayPath + ".common.ovl");
      Assert.That(File.Exists(commonPath), Is.True, commonPath);
      using var ovl = Ovl.Load(commonPath);
      foreach (var section in TrackSections.Extract(ovl)) {
        var file = ovl.Keys.Single(candidate =>
          candidate.Type == FileType.TrackSection
          && string.Equals(
            candidate.Name,
            section.Name,
            StringComparison.OrdinalIgnoreCase));
        sources.Add(new RideTrackSectionResourceSource(overlayPath, file, section));
      }
    }
    return sources;
  }

  private static RideTrackSectionResourceSource CreateSource(
    string overlayPath,
    string name
  ) => new(
    overlayPath,
    new OvlFile(name, FileType.TrackSection, name + ".unique.ovl"),
    CreateSection(name));

  private static RideTrackPlacement CreatePlacement(
    ulong entryId = 500,
    string overlayPath = "Tracks\\TrackedRides\\TrackBased07\\TrackBased07",
    string objectKey = "StationStraight",
    string? symbolName = null
  ) => new(
    sourceEntryId: entryId,
    sceneryPlacementSourceEntryId: 100,
    sidDatabaseEntryReference: 200,
    symbolName: symbolName ?? objectKey + ":tks",
    objectKey,
    overlayPath,
    tileX: 1,
    tileY: 1,
    rotation: Edge.North,
    serializedDirection: 1,
    serializedHeight: 6,
    corner: 3,
    ownerReference: 700,
    segmentReference: 800,
    previousPieceReference: 0,
    nextPieceReference: 0,
    platformPieceReference: 0,
    reversed: false,
    userAngleDegrees: 0,
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0);

  private static TrackSection CreateSection(string name) => new(
    name,
    TrackSectionVersion.Vanilla,
    name + " internal",
    name + ":sid",
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

  private static readonly InstalledCampaign[] InstalledCampaigns = [
    new(
      "Campaigns/Base/BoxOffice.dat",
      PlacementCount: 216,
      Identities: [
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|45medslope2straight:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|45medslope:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|45straight2medslope:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|45straight:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Loosecurveleft:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Loosecurveright:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Medcurve:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Medslope2straight:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Medslope:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Sbendright:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Stationmiddle:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Straight2medslope:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Straight:tks",
        "Tracks\\TrackedRides\\TrackBased07\\TrackBased07|Tightcurve:tks",
      ]),
    new(
      "Campaigns/Base/Soaked/Atlantis.dat",
      PlacementCount: 14,
      Identities: [
        "Tracks\\TrackedRides\\TrackBased16\\TrackBased16|ElevatorBaseSec:tks",
        "Tracks\\TrackedRides\\TrackBased16\\TrackBased16|ElevatorMidSec:tks",
      ]),
  ];

  private sealed record InstalledCampaign(
    string RelativePath,
    int PlacementCount,
    IReadOnlyList<string> Identities);
}
