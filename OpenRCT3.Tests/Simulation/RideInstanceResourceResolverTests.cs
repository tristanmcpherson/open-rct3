// Ride Instance Resource Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceResourceResolverTests {
  [Test]
  public void Resolve_UsesObservedExactOverlayAndCaseInsensitiveTaggedSymbol() {
    const string overlay = "Tracks\\Coasters\\RoboCoaster\\RoboCoaster";
    var source = CreateSource(overlay, "RoboCoaster");
    var resolver = new RideInstanceResourceResolver([source]);
    var instance = CreateInstance(
      entryId: 7794,
      overlayName: "tracks\\coasters\\robocoaster\\robocoaster",
      symbolName: "robocoaster:trr");

    var link = resolver.Resolve(instance);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.True);
      Assert.That(link.Instance, Is.SameAs(instance));
      Assert.That(link.Source, Is.SameAs(source));
      Assert.That(link.Resource, Is.SameAs(source.Resource));
    }
  }

  [Test]
  public void Resolve_DoesNotNormalizeOverlaySeparatorsOrFallbackBySymbol() {
    const string overlay = "Tracks\\TrackedRides\\Mono\\Mono";
    var resolver = new RideInstanceResourceResolver([CreateSource(overlay, "Mono")]);
    var instance = CreateInstance(
      overlayName: "Tracks/TrackedRides/Mono/Mono",
      symbolName: "Mono:trr");

    var link = resolver.Resolve(instance);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Source, Is.Null);
      Assert.That(link.Instance.TrackedRideOverlayName, Is.EqualTo(
        "Tracks/TrackedRides/Mono/Mono"));
      Assert.That(link.Instance.TrackedRideSymbolName, Is.EqualTo("Mono:trr"));
    }
  }

  [Test]
  public void Resolve_PreservesUnresolvedExternalOrCustomIdentity() {
    var resolver = new RideInstanceResourceResolver([]);
    var instance = CreateInstance(
      overlayName: "Custom\\TrackedRides\\BuilderRide\\BuilderRide",
      symbolName: "BuilderRide:trr");

    var link = resolver.Resolve(instance);

    using (Assert.EnterMultipleScope()) {
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Source, Is.Null);
      Assert.That(link.Instance, Is.SameAs(instance));
    }
  }

  [Test]
  public void Constructor_RejectsCaseInsensitiveDuplicateCompositeIdentity() {
    var first = CreateSource("Tracks\\TrackedRides\\Mono\\Mono", "Mono");
    var second = CreateSource("tracks\\trackedrides\\mono\\mono", "MONO");

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideInstanceResourceResolver([first, second])));
  }

  [Test]
  public void Constructor_AllowsSameSymbolInDifferentExactOverlays() {
    var first = CreateSource("Tracks\\First\\Shared", "Shared");
    var second = CreateSource("Tracks\\Second\\Shared", "Shared");
    var resolver = new RideInstanceResourceResolver([first, second]);

    var firstLink = resolver.Resolve(CreateInstance(
      overlayName: first.OverlayName,
      symbolName: "Shared:trr"));
    var secondLink = resolver.Resolve(CreateInstance(
      entryId: 901,
      overlayName: second.OverlayName,
      symbolName: "Shared:trr"));

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstLink.Source, Is.SameAs(first));
      Assert.That(secondLink.Source, Is.SameAs(second));
    }
  }

  [Test]
  public void Constructor_RejectsNonTrackedRideOvlType() {
    var ride = CreateRide("Mono");
    var source = new RideInstanceResourceSource(
      "Tracks\\TrackedRides\\Mono\\Mono",
      new OvlFile("Mono", FileType.RideTrain, "Mono.unique.ovl"),
      ride);

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideInstanceResourceResolver([source])));
  }

  [Test]
  public void Constructor_RejectsAmbiguousOvlAndDecodedNames() {
    var source = new RideInstanceResourceSource(
      "Tracks\\TrackedRides\\Mono\\Mono",
      new OvlFile("MonoAlias", FileType.TrackedRide, "Mono.unique.ovl"),
      CreateRide("Mono"));

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideInstanceResourceResolver([source])));
  }

  [TestCase("Mono")]
  [TestCase("Mono:rit")]
  [TestCase("Mono:trr:extra")]
  public void Resolve_RejectsMissingWrongOrAmbiguousResourceTag(string symbolName) {
    var resolver = new RideInstanceResourceResolver([
      CreateSource("Tracks\\TrackedRides\\Mono\\Mono", "Mono"),
    ]);
    var instance = CreateInstance(symbolName: symbolName);

    Assert.Throws<InvalidDataException>(new Action(() => resolver.Resolve(instance)));
  }

  [Test]
  public void ResolveAll_RejectsDuplicateDatEntryIds() {
    var resolver = new RideInstanceResourceResolver([]);
    var first = CreateInstance(entryId: 900);
    var second = CreateInstance(entryId: 900);

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.ResolveAll([first, second])));
  }

  [Test]
  public void Constructor_RejectsResourceCountAboveBoundBeforeIndexing() {
    var source = CreateSource("Tracks\\TrackedRides\\Mono\\Mono", "Mono");
    var resources = Enumerable.Repeat(source, 100_001).ToArray();

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new RideInstanceResourceResolver(resources)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Resolve_InstalledCampaignIdentitiesAgainstTheirExactOvlOverlays() {
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
      var actualIdentities = data.TrackedRideInstances.Select(instance =>
        new InstalledIdentity(
          instance.TrackedRideOverlayName,
          instance.TrackedRideSymbolName)).ToArray();
      Assert.That(actualIdentities, Is.EquivalentTo(campaign.Identities), campaign.RelativePath);

      var sources = new List<RideInstanceResourceSource>();
      foreach (var overlayName in actualIdentities
        .Select(identity => identity.OverlayName)
        .Distinct(StringComparer.OrdinalIgnoreCase)) {
        var commonPath = Path.Combine(root!, overlayName + ".common.ovl");
        Assert.That(File.Exists(commonPath), Is.True, commonPath);
        using var ovl = Ovl.Load(commonPath);
        foreach (var ride in TrackedRides.Extract(ovl)) {
          var file = ovl.Keys.Single(candidate =>
            candidate.Type == FileType.TrackedRide
            && candidate.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
              candidate.Name,
              ride.Name,
              StringComparison.OrdinalIgnoreCase));
          sources.Add(new RideInstanceResourceSource(overlayName, file, ride));
        }
      }

      var links = new RideInstanceResourceResolver(sources)
        .ResolveAll(data.TrackedRideInstances);
      using (Assert.EnterMultipleScope()) {
        Assert.That(links, Has.Count.EqualTo(campaign.Identities.Count));
        Assert.That(links.All(link => link.IsResolved), Is.True, campaign.RelativePath);
        foreach (var link in links) {
          Assert.That(
            link.Source!.OverlayName,
            Is.EqualTo(link.Instance.TrackedRideOverlayName).IgnoreCase,
            campaign.RelativePath);
          Assert.That(
            link.Resource!.Name + ":trr",
            Is.EqualTo(link.Instance.TrackedRideSymbolName).IgnoreCase,
            campaign.RelativePath);
          Assert.That(link.Source.File.Type, Is.EqualTo(FileType.TrackedRide));
        }
      }

      TestContext.Progress.WriteLine(
        $"{campaign.RelativePath}: resolved {links.Count}/{links.Count} exact DAT->TRR identities");
    }
  }

  private static RideInstanceResourceSource CreateSource(
    string overlayName,
    string name
  ) => new(
    overlayName,
    new OvlFile(name, FileType.TrackedRide, name + ".unique.ovl"),
    CreateRide(name));

  private static DatTrackedRideInstanceData CreateInstance(
    ulong entryId = 900,
    string overlayName = "Tracks\\TrackedRides\\Mono\\Mono",
    string symbolName = "Mono:trr"
  ) => new(
    entryId,
    "Synthetic coaster",
    track: 700,
    overlayName,
    symbolName,
    nTrains: 1,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains: [1_000]);

  private static TrackedRide CreateRide(string name) => new(
    name,
    TrackedRideVersion.Vanilla,
    [],
    [],
    null,
    null,
    "SyntheticTrack",
    new TrackedRideStation(null, 0, 0, 0, 0, 0),
    new TrackedRideMotion(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    new TrackedRideOptions(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideCosts(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideReferences(null, null, null, null, null, null, null, null),
    null,
    null);

  private static readonly InstalledCampaign[] InstalledCampaigns = [
    new(
      "Campaigns/Base/BoxOffice.dat",
      [new("Tracks\\TrackedRides\\Mono\\Mono", "Mono:trr")]),
    new(
      "Campaigns/Base/Soaked/Atlantis.dat",
      [
        new("Tracks\\TrackedRides\\Elevator\\Elevator", "Elevator:trr"),
        new("Tracks\\TrackedRides\\Elevator\\Elevator", "Elevator:trr"),
      ]),
    new(
      "Campaigns/Base/Wild/GeminiBasin.dat",
      [
        new(
          "Tracks\\Coasters\\InvertedRollerCoaster\\InvertedRollerCoaster",
          "InvertedRollerCoaster:trr"),
        new("Tracks\\Coasters\\RoboCoaster\\RoboCoaster", "robocoaster:trr"),
      ]),
  ];

  private sealed record InstalledCampaign(
    string RelativePath,
    IReadOnlyList<InstalledIdentity> Identities);

  private sealed record InstalledIdentity(string OverlayName, string SymbolName);
}
