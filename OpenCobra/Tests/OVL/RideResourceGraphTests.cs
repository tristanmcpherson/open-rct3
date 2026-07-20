using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class RideResourceGraphTests {
  private const string RideArchive = "rides.unique.ovl";
  private const string TrainArchive = "trains.unique.ovl";
  private const string CarArchive = "cars.unique.ovl";
  private const string VisualArchive = "visuals.unique.ovl";

  [Test]
  public void Resolve_LinksExactRideTrainCarAndVisualResourcesWithinEachClosure() {
    var ride = CreateRide(
      "SyntheticRide",
      ["SyntheticTrain", "MissingTrain"],
      "Splitter:svd");
    var train = CreateTrain(
      "SyntheticTrain",
      "FrontCar:ric",
      "MissingCar:ric");
    var car = CreateCar(
      "FrontCar",
      "Body:svd",
      "MissingMoving:svd",
      "Wheel:svd");
    var rideSource = RideSource(
      ride,
      RideArchive,
      [RideArchive, TrainArchive, VisualArchive]);
    var trainSource = TrainSource(
      train,
      TrainArchive,
      [TrainArchive, CarArchive]);
    var carSource = CarSource(
      car,
      CarArchive,
      [CarArchive, VisualArchive]);
    var visuals = new[] {
      VisualSource(CreateVisual("Body"), VisualArchive),
      VisualSource(CreateVisual("Wheel"), VisualArchive),
      VisualSource(CreateVisual("Splitter"), VisualArchive),
    };

    var graph = RideResourceGraphResolver.Resolve(
      [rideSource],
      [trainSource],
      [carSource],
      visuals);

    var linkedRide = graph.Rides.Single();
    var linkedTrain = linkedRide.Trains[0];
    var front = linkedTrain.Cars.Single(link => link.Role == RideTrainCarRole.Front);
    var missingCar = linkedTrain.Cars.Single(link => link.Role == RideTrainCarRole.Middle);
    using (Assert.EnterMultipleScope()) {
      Assert.That(linkedRide.Source, Is.SameAs(rideSource));
      Assert.That(linkedTrain.Source, Is.SameAs(trainSource));
      Assert.That(linkedRide.Trains[1].IsResolved, Is.False);
      Assert.That(front.Source, Is.SameAs(carSource));
      Assert.That(front.Visuals.Single(link => link.Role == RideVisualRole.Body).Source,
        Is.SameAs(visuals[0]));
      Assert.That(front.Visuals.Single(link => link.Role == RideVisualRole.Moving).IsResolved,
        Is.False);
      Assert.That(front.Visuals.Single(
        link => link.Role == RideVisualRole.FrontRightWheel).Source,
        Is.SameAs(visuals[1]));
      Assert.That(missingCar.IsResolved, Is.False);
      Assert.That(linkedRide.WildSplitter!.Source, Is.SameAs(visuals[2]));
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(3));
    }
  }

  [Test]
  public void Resolve_DoesNotUseUnrelatedLoneSameNameTrain() {
    var ride = CreateRide("SyntheticRide", ["SharedTrain"]);
    var unrelated = CreateTrain("SharedTrain", "Front:ric");
    var graph = RideResourceGraphResolver.Resolve(
      [RideSource(ride, RideArchive, [RideArchive])],
      [TrainSource(unrelated, "wrong.unique.ovl", ["wrong.unique.ovl"])],
      [],
      []);

    var link = graph.Rides.Single().Trains.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(link.Reference, Is.EqualTo("SharedTrain"));
      Assert.That(link.IsResolved, Is.False);
      Assert.That(link.Source, Is.Null);
      Assert.That(graph.UnresolvedReferenceCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_UsesLastTagSeparatorWhenResourceNameContainsAColon() {
    var ride = CreateRide("SyntheticRide", ["SyntheticTrain"]);
    var train = CreateTrain("SyntheticTrain", "Vendor:FrontCar:ric");
    var car = CreateCar("Vendor:FrontCar", "Vendor:Body:svd");
    var trainSource = TrainSource(
      train,
      TrainArchive,
      [TrainArchive, CarArchive, VisualArchive]);
    var carSource = CarSource(
      car,
      CarArchive,
      [CarArchive, VisualArchive]);
    var visualSource = VisualSource(CreateVisual("Vendor:Body"), VisualArchive);
    var rideSource = RideSource(
      ride,
      RideArchive,
      [RideArchive, TrainArchive, CarArchive, VisualArchive]);

    var graph = RideResourceGraphResolver.Resolve(
      [rideSource],
      [trainSource],
      [carSource],
      [visualSource]);

    var linkedCar = graph.Rides.Single().Trains.Single().Cars.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(linkedCar.Source, Is.SameAs(carSource));
      Assert.That(linkedCar.Visuals.Single().Source, Is.SameAs(visualSource));
      Assert.That(graph.UnresolvedReferenceCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_SelectsOnlyAllowedSameNameSource() {
    var ride = CreateRide("SyntheticRide", ["SharedTrain"]);
    var allowed = TrainSource(
      CreateTrain("SharedTrain", "Front:ric"),
      TrainArchive,
      [TrainArchive]);
    var unrelated = TrainSource(
      CreateTrain("SharedTrain", "Front:ric"),
      "wrong.unique.ovl",
      ["wrong.unique.ovl"]);
    var graph = RideResourceGraphResolver.Resolve(
      [RideSource(ride, RideArchive, [RideArchive, TrainArchive])],
      [allowed, unrelated],
      [],
      []);

    Assert.That(graph.Rides.Single().Trains.Single().Source, Is.SameAs(allowed));
  }

  [Test]
  public void Resolve_RejectsSameNameSourcesWhenBothArchivesAreAllowed() {
    var ride = CreateRide("SyntheticRide", ["SharedTrain"]);
    var first = TrainSource(
      CreateTrain("SharedTrain", "Front:ric"),
      "first.unique.ovl",
      ["first.unique.ovl"]);
    var second = TrainSource(
      CreateTrain("SharedTrain", "Front:ric"),
      "second.unique.ovl",
      ["second.unique.ovl"]);
    var rideSource = RideSource(
      ride,
      RideArchive,
      [RideArchive, "first.unique.ovl", "second.unique.ovl"]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([rideSource], [first, second], [], [])));
  }

  [Test]
  public void Resolve_RejectsDuplicateCompositeResourceIdentity() {
    var first = TrainSource(
      CreateTrain("Duplicate", "Front:ric"),
      TrainArchive,
      [TrainArchive]);
    var second = TrainSource(
      CreateTrain("duplicate", "Front:ric"),
      TrainArchive,
      [TrainArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([], [first, second], [], [])));
  }

  [Test]
  public void Resolve_RejectsDuplicateTrackedRideIdentityInOneArchive() {
    var first = RideSource(
      CreateRide("Duplicate", []),
      RideArchive,
      [RideArchive]);
    var second = RideSource(
      CreateRide("duplicate", []),
      RideArchive,
      [RideArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([first, second], [], [], [])));
  }

  [Test]
  public void Resolve_RejectsMismatchedOvlAndDecodedIdentity() {
    var train = CreateTrain("Decoded", "Front:ric");
    var source = new RideTrainResourceSource(
      new OvlFile("Alias", FileType.RideTrain, TrainArchive),
      train,
      [TrainArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([], [source], [], [])));
  }

  [Test]
  public void Resolve_RejectsClosureThatOmitsItsOwnSourceArchive() {
    var source = TrainSource(
      CreateTrain("SyntheticTrain", "Front:ric"),
      TrainArchive,
      [CarArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([], [source], [], [])));
  }

  [Test]
  public void Resolve_RejectsMalformedTaggedReference() {
    var ride = CreateRide("SyntheticRide", ["SyntheticTrain"]);
    var train = CreateTrain("SyntheticTrain", "Front:shs");
    var rideSource = RideSource(
      ride,
      RideArchive,
      [RideArchive, TrainArchive]);
    var trainSource = TrainSource(
      train,
      TrainArchive,
      [TrainArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve([rideSource], [trainSource], [], [])));
  }

  [Test]
  public void Resolve_EnforcesAggregateRelationshipBudget() {
    var ride = CreateRide("SyntheticRide", ["SyntheticTrain"]);
    var train = CreateTrain("SyntheticTrain", "Front:ric");
    var limits = new RideResourceGraphLimits(10, 40, 1, 128);
    var rideSource = RideSource(
      ride,
      RideArchive,
      [RideArchive, TrainArchive]);
    var trainSource = TrainSource(
      train,
      TrainArchive,
      [TrainArchive]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideResourceGraphResolver.Resolve(
        [rideSource],
        [trainSource],
        [],
        [],
        limits)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Resolve_InstalledLogFlumeLinksCrocLogThroughRealDependencyClosures() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var trackedRidePath = Path.Combine(
      rct3Path,
      "Tracks",
      "TrackedRides",
      "LogFlume",
      "LogFlume.common.ovl");
    var logFlumePath = Path.Combine(
      rct3Path,
      "Cars",
      "TrackedRideCars",
      "LogFlume",
      "LogFlume.common.ovl");
    var crocLogPath = Path.Combine(
      rct3Path,
      "Cars",
      "TrackedRideCars",
      "CrocLog",
      "CrocLog.common.ovl");
    Assert.That(trackedRidePath, Does.Exist);
    Assert.That(logFlumePath, Does.Exist);
    Assert.That(crocLogPath, Does.Exist);

    using var trackedRideOvl = Ovl.Load(trackedRidePath);
    using var logFlumeOvl = Ovl.Load(logFlumePath);
    using var crocLogOvl = Ovl.Load(crocLogPath);
    var closureCache = new Dictionary<string, IReadOnlyList<string>>(
      StringComparer.OrdinalIgnoreCase);
    var trackedRideClosure = BuildDependencyClosure(trackedRidePath, closureCache);
    var logFlumeClosure = BuildDependencyClosure(logFlumePath, closureCache);
    var crocLogClosure = BuildDependencyClosure(crocLogPath, closureCache);
    // TRR train names are relocated bare strings, so ManagerTRR does not emit OVL dependencies for
    // them. The caller explicitly selects both installed train archive roots and supplies the union
    // of their real transitive dependency closures.
    var rideAllowedArchives = MergeClosures(
      trackedRideClosure,
      logFlumeClosure,
      crocLogClosure);
    var ride = TrackedRides.Extract(trackedRideOvl)
      .Single(resource => resource.Name == "LogFlume");
    var rideSource = new TrackedRideResourceSource(
      FindFile(trackedRideOvl, FileType.TrackedRide, ride.Name),
      ride,
      rideAllowedArchives);
    var trains = TrainSources(logFlumeOvl, logFlumeClosure)
      .Concat(TrainSources(crocLogOvl, crocLogClosure))
      .ToArray();
    var cars = CarSources(logFlumeOvl, logFlumeClosure)
      .Concat(CarSources(crocLogOvl, crocLogClosure))
      .ToArray();
    var visuals = VisualSources(logFlumeOvl)
      .Concat(VisualSources(crocLogOvl))
      .ToArray();

    var graph = RideResourceGraphResolver.Resolve([rideSource], trains, cars, visuals);

    var trainLinks = graph.Rides.Single().Trains;
    TestContext.Progress.WriteLine(
      $"TRR refs={string.Join(",", trackedRideOvl.ExternalReferences)}; " +
      $"ride-closure={string.Join(",", rideAllowedArchives.Select(Path.GetFileName))}; " +
      $"train-links={string.Join(",", trainLinks.Select(link =>
        $"{link.Reference}:{link.Source?.File.Path ?? "unresolved"}"))}");
    var frontCars = trainLinks.Select(link =>
      link.Cars.Single(car => car.Role == RideTrainCarRole.Front)).ToArray();
    var bodyVisuals = frontCars.Select(link =>
      link.Visuals.Single(visual => visual.Role == RideVisualRole.Body)).ToArray();
    TestContext.Progress.WriteLine(
      $"Installed ride graph evidence: ride-closure={rideAllowedArchives.Count}, " +
      $"LogFlume-closure={logFlumeClosure.Count}, CrocLog-closure={crocLogClosure.Count}, " +
      $"trains={string.Join(",", trainLinks.Select(
        link => $"{link.Reference}:{Path.GetFileName(link.Source?.File.Path)}"))}, " +
      $"front-cars={string.Join(",", frontCars.Select(
        link => $"{link.Reference}:{Path.GetFileName(link.Source?.File.Path)}"))}, " +
      $"unresolved={graph.UnresolvedReferenceCount}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(trainLinks, Has.Count.EqualTo(2));
      Assert.That(trainLinks.All(link => link.IsResolved), Is.True);
      Assert.That(frontCars.All(link => link.IsResolved), Is.True);
      Assert.That(bodyVisuals.All(link => link.IsResolved), Is.True);
      Assert.That(trainLinks.All(link => rideAllowedArchives.Contains(
        link.Source!.File.Path,
        StringComparer.OrdinalIgnoreCase)), Is.True);
      Assert.That(frontCars.All(link => trainLinks.Single(train =>
        train.Cars.Contains(link)).Source!.AllowedArchivePaths.Contains(
          link.Source!.File.Path,
          StringComparer.OrdinalIgnoreCase)), Is.True);
      Assert.That(bodyVisuals.All(link => frontCars.Single(car =>
        car.Visuals.Contains(link)).Source!.AllowedArchivePaths.Contains(
          link.Source!.File.Path,
          StringComparer.OrdinalIgnoreCase)), Is.True);
      Assert.That(trainLinks.Select(link => Path.GetFileName(
        link.Source!.File.Path)), Is.EquivalentTo([
          "LogFlume.unique.ovl",
          "CrocLog.unique.ovl",
        ]));
    }
  }

  private static IReadOnlyList<string> MergeClosures(
    params IReadOnlyList<string>[] closures
  ) {
    var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var merged = new List<string>();
    foreach (var closure in closures) {
      foreach (var archivePath in closure) {
        if (unique.Add(archivePath)) merged.Add(archivePath);
      }
    }
    return Array.AsReadOnly(merged.ToArray());
  }

  private static IReadOnlyList<RideTrainResourceSource> TrainSources(
    Ovl ovl,
    IReadOnlyList<string> allowedArchivePaths
  ) => RideTrains.Extract(ovl).Select(resource => new RideTrainResourceSource(
    FindFile(ovl, FileType.RideTrain, resource.Name),
    resource,
    allowedArchivePaths)).ToArray();

  private static IReadOnlyList<RideCarResourceSource> CarSources(
    Ovl ovl,
    IReadOnlyList<string> allowedArchivePaths
  ) => RideCars.Extract(ovl).Select(resource => new RideCarResourceSource(
    FindFile(ovl, FileType.RideCar, resource.Name),
    resource,
    allowedArchivePaths)).ToArray();

  private static IReadOnlyList<RideVisualResourceSource> VisualSources(Ovl ovl) =>
    SceneryItemVisuals.Extract(ovl).Select(resource => new RideVisualResourceSource(
      FindFile(ovl, FileType.SceneryItemVisual, resource.Name),
      resource)).ToArray();

  private static OvlFile FindFile(Ovl ovl, FileType type, string name) =>
    ovl.Keys.Single(file =>
      file.Type == type &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase) &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));

  private static IReadOnlyList<string> BuildDependencyClosure(
    string rootCommonPath,
    IDictionary<string, IReadOnlyList<string>> cache
  ) {
    if (cache.TryGetValue(rootCommonPath, out var cached)) return cached;

    const int maximumArchives = 1_024;
    var queue = new Queue<string>();
    var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var ordered = new List<string>();
    scheduled.Add(Path.GetFullPath(rootCommonPath));
    queue.Enqueue(Path.GetFullPath(rootCommonPath));
    foreach (var _ in Enumerable.Range(0, maximumArchives)) {
      if (queue.Count == 0) break;
      var commonPath = queue.Dequeue();
      AddAllowed(ToUniquePath(commonPath));
      using var ovl = Ovl.Load(commonPath);
      foreach (var reference in ovl.ExternalReferences) {
        var dependencyCommonPath = ResolveDependencyCommonPath(commonPath, reference);
        AddAllowed(ToUniquePath(dependencyCommonPath));
        if (!File.Exists(dependencyCommonPath) ||
            !File.Exists(ToUniquePath(dependencyCommonPath)) ||
            !scheduled.Add(dependencyCommonPath))
          continue;
        queue.Enqueue(dependencyCommonPath);
      }
    }
    if (queue.Count > 0)
      throw new InvalidDataException(
        $"Installed OVL dependency closure exceeds {maximumArchives} archives.");
    var result = Array.AsReadOnly(ordered.ToArray());
    cache.Add(rootCommonPath, result);
    return result;

    void AddAllowed(string uniquePath) {
      if (allowed.Add(uniquePath)) ordered.Add(uniquePath);
    }
  }

  private static string ResolveDependencyCommonPath(
    string declaringCommonPath,
    string reference
  ) {
    if (string.IsNullOrWhiteSpace(reference) ||
        !string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
      throw new InvalidDataException(
        $"Installed OVL has malformed dependency '{reference}'.");
    var normalized = reference
      .Replace('\\', Path.DirectorySeparatorChar)
      .Replace('/', Path.DirectorySeparatorChar);
    if (normalized.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^".common.ovl".Length];
    else if (normalized.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^".unique.ovl".Length];
    else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Installed OVL dependency '{reference}' has an unknown suffix.");
    return Path.GetFullPath(Path.Combine(
      Path.GetDirectoryName(declaringCommonPath)!,
      normalized + ".common.ovl"));
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase)
      ? commonPath[..^".common.ovl".Length] + ".unique.ovl"
      : throw new InvalidDataException($"OVL path is not a common archive: '{commonPath}'.");

  private static TrackedRideResourceSource RideSource(
    TrackedRide resource,
    string archivePath,
    IReadOnlyList<string> allowedArchivePaths
  ) => new(
    new OvlFile(resource.Name, FileType.TrackedRide, archivePath),
    resource,
    allowedArchivePaths);

  private static RideTrainResourceSource TrainSource(
    RideTrain resource,
    string archivePath,
    IReadOnlyList<string> allowedArchivePaths
  ) => new(
    new OvlFile(resource.Name, FileType.RideTrain, archivePath),
    resource,
    allowedArchivePaths);

  private static RideCarResourceSource CarSource(
    RideCar resource,
    string archivePath,
    IReadOnlyList<string> allowedArchivePaths
  ) => new(
    new OvlFile(resource.Name, FileType.RideCar, archivePath),
    resource,
    allowedArchivePaths);

  private static RideVisualResourceSource VisualSource(
    SceneryItemVisual resource,
    string archivePath
  ) => new(
    new OvlFile(resource.Name, FileType.SceneryItemVisual, archivePath),
    resource);

  private static TrackedRide CreateRide(
    string name,
    IReadOnlyList<string> trainNames,
    string? splitter = null
  ) => new(
    name,
    splitter == null ? TrackedRideVersion.Vanilla : TrackedRideVersion.Wild,
    [],
    trainNames,
    null,
    null,
    "SyntheticTrack",
    new TrackedRideStation(null, 0, 0, 0, 0, 0),
    new TrackedRideMotion(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    new TrackedRideOptions(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideCosts(0, 0, 0, 0, 0, 0, 0),
    new TrackedRideReferences(null, null, null, null, null, null, null, null),
    null,
    splitter == null ? null : new TrackedRideWild(splitter, 0, 0, 0, 0, 0, 0, null));

  private static RideTrain CreateTrain(
    string name,
    string front,
    string? middle = null
  ) => new(
    name,
    RideTrainVersion.Vanilla,
    "Synthetic Train",
    "Synthetic train description",
    new RideTrainCars(front, null, middle, null, null, null, 1, 5, 3, null),
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

  private static RideCar CreateCar(
    string name,
    string body,
    string? moving = null,
    string? frontRightWheel = null
  ) => new(
    name,
    RideCarVersion.Vanilla,
    "Synthetic Car",
    "Synthetic Username",
    0,
    0,
    body,
    1,
    moving,
    -1,
    new RideCarAxisSettings(0, 0, 0, 0, 0, 0, 0, 0),
    new RideCarBobbingSettings(0, 0, 0, 0),
    new RideCarAnimationSettings(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(frontRightWheel, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0, 0, 0, 0, 0, 0),
    null,
    null);

  private static SceneryItemVisual CreateVisual(string name) =>
    new(name, default, 0, 0, 0, 1, [], null);
}
