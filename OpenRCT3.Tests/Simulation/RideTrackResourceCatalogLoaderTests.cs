// Ride Track Resource Catalog Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
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
  public void Load_AttachesInferredTrackVisualPairToCrossPairSidOwner() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var sidOwner = PairPath("Tracks", "Shared", "TrackSid");
    var visualOwner = PairPath("Tracks", "Shared", "SyntheticVisual_data");
    source.AddPair(
      root,
      references: [@"..\Shared\TrackSid"],
      sections: [Section("Straight")]);
    source.AddPair(
      sidOwner,
      sceneryItems: [Scenery(
        "StraightScenery",
        ["SyntheticVisual:svd"],
        SidType.RideTrack)],
      rides: [Ride("SidOwnerRide")]);
    source.AddPair(
      visualOwner,
      visuals: [Visual("SyntheticVisual")]);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [Placement()],
      source);

    var rootClosure = result.TrackVisualResources.DependencyClosures.Single(closure =>
      string.Equals(
        closure.SourcePath,
        UniquePath(root),
        StringComparison.OrdinalIgnoreCase));
    var sidOwnerClosure = result.TrackVisualResources.DependencyClosures.Single(closure =>
      string.Equals(
        closure.SourcePath,
        UniquePath(sidOwner),
        StringComparison.OrdinalIgnoreCase));
    var bridge = RideTrackVisualResourceBridge.Resolve(result.TrackVisualResources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.True);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root, sidOwner, visualOwner }));
      Assert.That(rootClosure.AllowedTargetPaths,
        Does.Contain(UniquePath(visualOwner)).IgnoreCase);
      Assert.That(sidOwnerClosure.AllowedTargetPaths,
        Does.Contain(UniquePath(visualOwner)).IgnoreCase);
      Assert.That(bridge.Visuals, Has.Count.EqualTo(1));
      Assert.That(bridge.Visuals.Single().Section.Scenery.Source!.File.Path,
        Is.EqualTo(UniquePath(sidOwner)).IgnoreCase);
      Assert.That(bridge.Visuals.Single().VisualSource.File.Path,
        Is.EqualTo(UniquePath(visualOwner)).IgnoreCase);
    }
  }

  [Test]
  public void Load_UsesExactSamePairSvdWithoutProbingSiblingConvention() {
    var source = new FakeLoaderSource();
    var root = PairPath("Tracks", "Exact", "Synthetic");
    var inferred = PairPath("Tracks", "Exact", "SyntheticVisual_data");
    source.AddPair(
      root,
      sections: [Section("Straight")],
      sceneryItems: [Scenery(
        "StraightScenery",
        ["SyntheticVisual:svd"],
        SidType.RideTrack)],
      visuals: [Visual("SyntheticVisual")]);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [Placement()],
      source);
    var bridge = RideTrackVisualResourceBridge.Resolve(result.TrackVisualResources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.True);
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { root }));
      Assert.That(source.ProbedPaths, Does.Not.Contain(inferred).IgnoreCase);
      Assert.That(source.ProbedPaths, Does.Not.Contain(UniquePath(inferred)).IgnoreCase);
      Assert.That(bridge.Visuals, Has.Count.EqualTo(1));
      Assert.That(bridge.Visuals.Single().VisualSource.File.Path,
        Is.EqualTo(UniquePath(root)).IgnoreCase);
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
      MaximumRideInstances: 1,
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
  public void Load_WithRideInstancesBuildsExactTransitiveRideResourceGraph() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var trainPair = PairPath("Cars", "Synthetic", "SyntheticTrain");
    var carPair = PairPath("Cars", "Synthetic", "SyntheticCar");
    var visualPair = PairPath("Cars", "Synthetic", "SyntheticVisual");
    source.AddPair(
      rideRoot,
      references: [@"..\..\Cars\Synthetic\SyntheticTrain"],
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      trainPair,
      references: ["SyntheticCar"],
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    source.AddPair(
      carPair,
      references: ["SyntheticVisual"],
      rideCars: [Car("SyntheticCar", "SyntheticVisual:svd")]);
    source.AddPair(
      visualPair,
      visuals: [Visual("SyntheticVisual")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      source);

    var instanceLink = result.RideResources.Instances.Single();
    var rideLink = result.RideResources.Graph.Rides.Single();
    var trainLink = rideLink.Trains.Single();
    var carLink = trainLink.Cars.Single();
    var visualLink = carLink.Visuals.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsComplete, Is.True);
      Assert.That(source.LoadedPaths,
        Is.EqualTo(new[] { rideRoot, trainPair, carPair, visualPair }));
      Assert.That(instanceLink.IsResolved, Is.True);
      Assert.That(instanceLink.Source!.OverlayName,
        Is.EqualTo(instance.TrackedRideOverlayName));
      Assert.That(rideLink.Source.File.Path, Is.EqualTo(UniquePath(rideRoot)));
      Assert.That(trainLink.Source!.File.Path, Is.EqualTo(UniquePath(trainPair)));
      Assert.That(carLink.Source!.File.Path, Is.EqualTo(UniquePath(carPair)));
      Assert.That(visualLink.Source!.File.Path, Is.EqualTo(UniquePath(visualPair)));
      Assert.That(result.RideResources.Graph.UnresolvedReferenceCount, Is.Zero);
      Assert.That(result.RideResources.CarVisuals.Visuals, Has.Count.EqualTo(1));
      Assert.That(result.RideResources.CarVisuals.Visuals.Single().Visual,
        Is.SameAs(visualLink));
      Assert.That(result.RideResources.CarVisuals.ResolvedShapeLodCount, Is.Zero);
      Assert.That(result.RideResources.CarVisuals.UnresolvedShapeReferenceCount, Is.Zero);
      Assert.That(result.RideResources.ResolvedInstanceCount, Is.EqualTo(1));
      Assert.That(result.RideResources.UnresolvedInstanceCount, Is.Zero);
      Assert.That(result.RideResources.DecodedCounts,
        Is.EqualTo(new RideResourceDecodeCounts(1, 1, 1, 1)));
    }
  }

  [Test]
  public void Load_WithMissingRideRootRetainsTypedUnresolvedInstance() {
    var source = new FakeLoaderSource();
    var instance = RideInstance(
      900,
      @"Rides\Missing\MissingRide",
      "MissingRide:trr");

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Issues.Single().Kind,
        Is.EqualTo(RideTrackResourceCatalogLoadIssueKind.MissingRootPair));
      Assert.That(result.RideResources.Instances.Single().IsResolved, Is.False);
      Assert.That(result.RideResources.Graph.Rides, Is.Empty);
      Assert.That(result.RideResources.ResolvedInstanceCount, Is.Zero);
      Assert.That(result.RideResources.UnresolvedInstanceCount, Is.EqualTo(1));
      Assert.That(result.RideResources.DecodedCounts,
        Is.EqualTo(new RideResourceDecodeCounts(0, 0, 0, 0)));
    }
  }

  [Test]
  public void Load_SavedExactRideTrainRootWinsWithoutConventionProbes() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var savedTrainRoot = PairPath("CustomCars", "SavedTrain");
    var conventionalTrainRoot = PairPath("Cars", "SyntheticTrain", "SyntheticTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      savedTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    source.AddPair(
      conventionalTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");
    var savedTrain = RideTrainInstance(
      1_000,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 900);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      [savedTrain],
      source);

    var trainLink = result.RideResources.Graph.Rides.Single().Trains.Single();
    var savedLink = result.RideResources.TrainInstances.Links.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { rideRoot, savedTrainRoot }));
      Assert.That(source.ProbedPaths.Any(path =>
        path.Contains("SyntheticTrain", StringComparison.OrdinalIgnoreCase)), Is.False);
      Assert.That(trainLink.IsResolved, Is.True);
      Assert.That(trainLink.Source!.File.Path, Is.EqualTo(UniquePath(savedTrainRoot)));
      Assert.That(savedLink.RideInstance, Is.SameAs(instance));
      Assert.That(savedLink.TrainInstance, Is.SameAs(savedTrain));
      Assert.That(savedLink.RideInstanceEntryId, Is.EqualTo(900));
      Assert.That(savedLink.TrainInstanceEntryId, Is.EqualTo(1_000));
      Assert.That(savedLink.Ordinal, Is.Zero);
      Assert.That(savedLink.WhichTrain, Is.Zero);
      Assert.That(savedLink.Length, Is.EqualTo(12.5f));
      Assert.That(savedLink.Mass, Is.EqualTo(1_000f));
      Assert.That(savedLink.Source!.File.Path, Is.EqualTo(UniquePath(savedTrainRoot)));
      Assert.That(savedLink.Resource!.Name, Is.EqualTo("SyntheticTrain"));
      Assert.That(result.RideResources.SavedTrainCount, Is.EqualTo(1));
      Assert.That(result.RideResources.ResolvedSavedTrainCount, Is.EqualTo(1));
      Assert.That(result.RideResources.UnresolvedSavedTrainCount, Is.Zero);
      Assert.That(result.RideResources.TrainInstances.UnreferencedInstances, Is.Empty);
    }
  }

  [Test]
  public void Load_MissingSavedRideTrainRemainsUnresolvedWithoutCompatibleSubstitution() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var conventionalTrainRoot = PairPath("Cars", "SyntheticTrain", "SyntheticTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      conventionalTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");
    var savedTrain = RideTrainInstance(
      1_000,
      @"CustomCars\MissingSaved",
      "SyntheticTrain:rit",
      trackedRideInstance: 900);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      [savedTrain],
      source);

    var savedLink = result.RideResources.TrainInstances.Links.Single();
    var graphTrain = result.RideResources.Graph.Rides.Single().Trains.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { rideRoot }));
      Assert.That(source.ProbedPaths, Does.Not.Contain(conventionalTrainRoot));
      Assert.That(source.ProbedPaths, Does.Not.Contain(UniquePath(conventionalTrainRoot)));
      Assert.That(result.Issues.Single().Kind,
        Is.EqualTo(RideTrackResourceCatalogLoadIssueKind.MissingRootPair));
      Assert.That(savedLink.TrainInstance, Is.SameAs(savedTrain));
      Assert.That(savedLink.Source, Is.Null);
      Assert.That(savedLink.IsResolved, Is.False);
      Assert.That(graphTrain.IsResolved, Is.False);
      Assert.That(result.RideResources.SavedTrainCount, Is.EqualTo(1));
      Assert.That(result.RideResources.ResolvedSavedTrainCount, Is.Zero);
      Assert.That(result.RideResources.UnresolvedSavedTrainCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_UnreferencedSavedRideTrainIsRetainedWithoutInventingAnOwnerLink() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var savedTrainRoot = PairPath("CustomCars", "SavedTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      savedTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");
    var referencedTrain = RideTrainInstance(
      1_000,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 900);
    var unreferencedTrain = RideTrainInstance(
      1_001,
      @"CustomCars\Unreferenced",
      "UnreferencedTrain:rit",
      trackedRideInstance: 900);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      [referencedTrain, unreferencedTrain],
      source);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.RideResources.TrainInstances.Links, Has.Count.EqualTo(1));
      Assert.That(result.RideResources.TrainInstances.Links.Single().TrainInstance,
        Is.SameAs(referencedTrain));
      Assert.That(result.RideResources.TrainInstances.UnreferencedInstances,
        Is.EqualTo(new[] { unreferencedTrain }));
      Assert.That(result.RideResources.SavedTrainCount, Is.EqualTo(2));
      Assert.That(result.RideResources.ResolvedSavedTrainCount, Is.EqualTo(1));
      Assert.That(result.RideResources.UnresolvedSavedTrainCount, Is.EqualTo(1));
      Assert.That(source.ProbedPaths.Any(path =>
        path.Contains("Unreferenced", StringComparison.OrdinalIgnoreCase)), Is.False);
    }
  }

  [Test]
  public void Load_WrongSavedSymbolDoesNotSubstituteCompatibleRideTrain() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var savedTrainRoot = PairPath("CustomCars", "WrongSaved");
    var conventionalTrainRoot = PairPath("Cars", "SyntheticTrain", "SyntheticTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      savedTrainRoot,
      rideTrains: [Train("DifferentTrain", "SyntheticCar:ric")]);
    source.AddPair(
      conventionalTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");
    var savedTrain = RideTrainInstance(
      1_000,
      @"CustomCars\WrongSaved",
      "SyntheticTrain:rit",
      trackedRideInstance: 900);

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      [savedTrain],
      source);

    var savedLink = result.RideResources.TrainInstances.Links.Single();
    var graphTrain = result.RideResources.Graph.Rides.Single().Trains.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { rideRoot, savedTrainRoot }));
      Assert.That(source.ProbedPaths, Does.Not.Contain(conventionalTrainRoot));
      Assert.That(source.ProbedPaths, Does.Not.Contain(UniquePath(conventionalTrainRoot)));
      Assert.That(result.IsComplete, Is.True);
      Assert.That(savedLink.Source, Is.Null);
      Assert.That(savedLink.IsResolved, Is.False);
      Assert.That(graphTrain.IsResolved, Is.False);
      Assert.That(result.RideResources.DecodedCounts.RideTrains, Is.EqualTo(1));
      Assert.That(result.RideResources.SavedTrainCount, Is.EqualTo(1));
      Assert.That(result.RideResources.ResolvedSavedTrainCount, Is.Zero);
      Assert.That(result.RideResources.UnresolvedSavedTrainCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_AllowsConfiguredRideTrainSlotsWithNoSavedReferences() {
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr",
      nTrains: 1,
      trains: []);

    var registry = RideTrainInstanceResourceRegistry.Build([instance], [], []);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.Links, Is.Empty);
      Assert.That(registry.UnreferencedInstances, Is.Empty);
      Assert.That(registry.SavedInstanceCount, Is.Zero);
    }
  }

  [Test]
  public void Load_RejectsSavedRideTrainCountMismatch() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var savedTrainRoot = PairPath("CustomCars", "SavedTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      savedTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr",
      nTrains: 2,
      trains: [1_000]);
    var savedTrain = RideTrainInstance(
      1_000,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 900);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackResourceCatalogLoader.Load(
        installRoot,
        [],
        [instance],
        [savedTrain],
        source)));

    Assert.That(exception!.Message,
      Does.Contain("declares 2 trains but references 1"));
  }

  [Test]
  public void Build_RejectsSavedRideTrainWithForeignReciprocalOwner() {
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");
    var foreignTrain = RideTrainInstance(
      1_000,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 901);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainInstanceResourceRegistry.Build([instance], [foreignTrain], [])));

    Assert.That(exception!.Message,
      Does.Contain("reciprocal owner is 901"));
  }

  [Test]
  public void Load_RejectsSavedRideTrainOrderMismatch() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var savedTrainRoot = PairPath("CustomCars", "SavedTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      savedTrainRoot,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr",
      nTrains: 2,
      trains: [1_000, 1_001]);
    var firstTrain = RideTrainInstance(
      1_000,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 900,
      whichTrain: 1);
    var secondTrain = RideTrainInstance(
      1_001,
      @"CustomCars\SavedTrain",
      "SyntheticTrain:rit",
      trackedRideInstance: 900,
      whichTrain: 0);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackResourceCatalogLoader.Load(
        installRoot,
        [],
        [instance],
        [firstTrain, secondTrain],
        source)));

    Assert.That(exception!.Message,
      Does.Contain("index 1 does not match forward reference ordinal 0"));
  }

  [Test]
  public void Load_CompatibleRideTrainUsesFirstCompleteBoundedCandidateOnly() {
    var source = new FakeLoaderSource();
    var rideRoot = PairPath("Rides", "Synthetic", "SyntheticRide");
    var coasterCandidate = PairPath(
      "Cars",
      "CoasterCars",
      "SyntheticTrain",
      "SyntheticTrain");
    var trackedRideCandidate = PairPath(
      "Cars",
      "TrackedRideCars",
      "SyntheticTrain",
      "SyntheticTrain");
    source.AddPair(
      rideRoot,
      rides: [Ride("SyntheticRide", ["SyntheticTrain"])]);
    source.AddPair(
      coasterCandidate,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    source.AddPair(
      trackedRideCandidate,
      rideTrains: [Train("SyntheticTrain", "SyntheticCar:ric")]);
    var instance = RideInstance(
      900,
      @"Rides\Synthetic\SyntheticRide",
      "SyntheticRide:trr");

    using var result = RideTrackResourceCatalogLoader.Load(
      installRoot,
      [],
      [instance],
      source);

    var trainLink = result.RideResources.Graph.Rides.Single().Trains.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(source.LoadedPaths, Is.EqualTo(new[] { rideRoot, coasterCandidate }));
      Assert.That(source.ProbedPaths.Any(path =>
        path.Contains("TrackedRideCars", StringComparison.OrdinalIgnoreCase)), Is.False);
      Assert.That(trainLink.IsResolved, Is.True);
      Assert.That(trainLink.Source!.File.Path, Is.EqualTo(UniquePath(coasterCandidate)));
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

  private static SceneryItem Scenery(
    string name,
    IReadOnlyList<string>? visualRefs = null,
    SidType type = default
  ) => new(
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
    Type: type,
    VisualRefs: visualRefs ?? []);

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

  private static TrackedRide Ride(
    string name,
    IReadOnlyList<string>? trainNames = null
  ) => new(
    name,
    TrackedRideVersion.Vanilla,
    [new TrackedRideTrackSection("Straight:tks", "straight", 10)],
    TrainNames: trainNames ?? [],
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

  private static DatTrackedRideInstanceData RideInstance(
    ulong entryId,
    string overlayName,
    string symbolName,
    int nTrains = 1,
    ulong[]? trains = null
  ) => new(
    entryId,
    "Synthetic ride",
    track: 700,
    overlayName,
    symbolName,
    nTrains,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains: trains ?? [1_000]);

  private static DatRideTrainInstanceData RideTrainInstance(
    ulong entryId,
    string overlayName,
    string symbolName,
    ulong trackedRideInstance,
    int whichTrain = 0
  ) => new(
    entryId,
    overlayName,
    symbolName,
    trackedRideInstance,
    whichTrain,
    length: 12.5f,
    mass: 1_000f);

  private static RideTrain Train(string name, string frontCar) => new(
    name,
    RideTrainVersion.Vanilla,
    "Synthetic Train",
    "Synthetic train description",
    new RideTrainCars(frontCar, null, null, null, null, null, 1, 1, 1, null),
    new RideTrainSpeedSettings(0, 0, 0),
    new RideTrainCameraSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    new RideTrainWaterSettings(0, 0, 0, 0, 0, 0, 0, 0),
    0,
    new RideTrainUnknownSettings(0, 0, 0, 0, 0, 0, 0, 0),
    null,
    null,
    null,
    null,
    null);

  private static RideCar Car(string name, string visual) => new(
    name,
    RideCarVersion.Vanilla,
    "Synthetic Car",
    "Synthetic Username",
    0,
    0,
    visual,
    1,
    null,
    -1,
    new RideCarAxisSettings(0, 0, 0, 0, 0, 0, 0, 0),
    new RideCarBobbingSettings(0, 0, 0, 0),
    new RideCarAnimationSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0, 0, 0, 0, 0, 0),
    null,
    null);

  private static SceneryItemVisual Visual(string name) =>
    new(name, default, 0, 0, 0, 1, [], null);

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
    public List<string> ProbedPaths { get; } = [];
    public List<Ovl> DisposedArchives { get; } = [];
    public int FileExistsCount { get; private set; }

    public void AddPair(
      string commonPath,
      IReadOnlyList<string>? references = null,
      IReadOnlyList<TrackSection>? sections = null,
      IReadOnlyList<SceneryItem>? sceneryItems = null,
      IReadOnlyList<Spline>? splines = null,
      IReadOnlyList<TrackedRide>? rides = null,
      IReadOnlyList<RideTrain>? rideTrains = null,
      IReadOnlyList<RideCar>? rideCars = null,
      IReadOnlyList<SceneryItemVisual>? visuals = null
    ) {
      var uniquePath = UniquePath(commonPath);
      var archive = new Ovl(Path.GetFileName(commonPath));
      var pairResources = new PairResources(
        sections ?? [],
        sceneryItems ?? [],
        splines ?? [],
        rides ?? [],
        rideTrains ?? [],
        rideCars ?? [],
        visuals ?? []);
      AddFiles(archive, pairResources.TrackSections, FileType.TrackSection, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.SceneryItems, FileType.SceneryItem, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.Splines, FileType.Spline, commonPath,
        item => item.Name);
      AddFiles(archive, pairResources.TrackedRides, FileType.TrackedRide, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.RideTrains, FileType.RideTrain, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.RideCars, FileType.RideCar, uniquePath,
        item => item.Name);
      AddFiles(archive, pairResources.Visuals, FileType.SceneryItemVisual, uniquePath,
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
      ProbedPaths.Add(path);
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
    public IReadOnlyList<RideTrain> ExtractRideTrains(Ovl archive) =>
      resources[archive].RideTrains;
    public IReadOnlyList<RideCar> ExtractRideCars(Ovl archive) =>
      resources[archive].RideCars;
    public IReadOnlyList<SceneryItemVisual> ExtractSceneryItemVisuals(Ovl archive) =>
      resources[archive].Visuals;
    public IReadOnlyList<StaticShape> ExtractStaticShapes(Ovl archive) => [];
    public IReadOnlyList<BoneShape> ExtractBoneShapes(Ovl archive) => [];

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
    IReadOnlyList<TrackedRide> TrackedRides,
    IReadOnlyList<RideTrain> RideTrains,
    IReadOnlyList<RideCar> RideCars,
    IReadOnlyList<SceneryItemVisual> Visuals
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
