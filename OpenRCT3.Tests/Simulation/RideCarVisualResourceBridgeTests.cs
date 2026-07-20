using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualResourceBridgeTests {
  private const string RidePath = "rides.unique.ovl";
  private const string TrainPath = "trains.unique.ovl";
  private const string CarPath = "cars.unique.ovl";
  private const string VisualPath = "visuals.unique.ovl";
  private const string StaticPath = "static.unique.ovl";
  private const string BonePath = "bone.unique.ovl";

  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_ExactRideClosureResolvesCarVisualShapeResources() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var configuredMap = Environment.GetEnvironmentVariable("OPENRCT3_MAP_PATH");
    var mapPath = string.IsNullOrWhiteSpace(configuredMap)
      ? Path.Combine(installRoot!, "Campaigns", "Base", "BoxOffice.dat")
      : Path.GetFullPath(Path.IsPathRooted(configuredMap)
        ? configuredMap
        : Path.Combine(installRoot!, configuredMap));
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    var archives = new List<Ovl>();
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);
      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        data.TrackedRideInstances,
        data.RideTrainInstances);
      foreach (var path in loaded.Context.LoadedCommonPaths)
        archives.Add(Ovl.Load(path));
      var shapes = RideVisualShapeResourceDecoder.Decode(archives);
      var result = RideCarVisualResourceBridge.Resolve(
        loaded.RideResources.Graph,
        shapes);

      TestContext.Progress.WriteLine(
        $"BoxOffice ride visuals: pairs={archives.Count}, " +
        $"shs={shapes.StaticShapes.Count}, bsh={shapes.BoneShapes.Count}, " +
        $"visualOccurrences={result.Visuals.Count}, " +
        $"resolvedShapeLods={result.ResolvedShapeLodCount}, " +
        $"unresolvedShapeRefs={result.UnresolvedShapeReferenceCount}");
      var bodyShapes = result.Visuals
        .Where(link => link.Visual.Role == RideVisualRole.Body)
        .SelectMany(link => link.Lods
          .Where(lod => lod.BoneShape != null)
          .Select(lod => new { Car = link.Car.Car!.Name, Shape = lod.BoneShape! }))
        .DistinctBy(item => $"{item.Car}|{item.Shape.Name}", StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item.Car, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Shape.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
      foreach (var bodyShape in bodyShapes) {
        var peepBones = bodyShape.Shape.Bones
          .Where(bone => bone.Name.StartsWith("Peep", StringComparison.OrdinalIgnoreCase))
          .Select(bone => bone.Name)
          .ToArray();
        TestContext.Progress.WriteLine(
          $"Body BSH: car={bodyShape.Car}, shape={bodyShape.Shape.Name}, " +
          $"bones={bodyShape.Shape.Bones.Count}, peepBones={peepBones.Length}, " +
          $"peepNames=[{string.Join(",", peepBones)}]");
      }
      Assert.That(loaded.RideResources.Graph.UnresolvedReferenceCount, Is.Zero);
      Assert.That(result.Visuals, Is.Not.Empty);
      Assert.That(result.ResolvedShapeLodCount, Is.GreaterThan(0));
      Assert.That(result.UnresolvedShapeReferenceCount, Is.Zero);
    } finally {
      foreach (var archive in archives.AsEnumerable().Reverse())
        archive.Dispose();
      terrain.TextureCatalog?.Dispose();
    }
  }

  [Test]
  public void Decode_AssociatesShapesWithExactOvlSymbols() {
    using var archive = new Ovl("ride visuals");
    archive.Add(
      new OvlFile("BodyStatic", FileType.StaticShape, StaticPath),
      new OvlEntry(0, 0));
    archive.Add(
      new OvlFile("BodyBone", FileType.BoneShape, BonePath),
      new OvlEntry(0, 0));
    var staticShape = StaticShape("BodyStatic");
    var boneShape = BoneShape("BodyBone");

    var result = RideVisualShapeResourceDecoder.Decode(
      [archive],
      _ => [staticShape],
      _ => [boneShape],
      RideCarVisualResourceBridgeLimits.Default);

    Assert.That(result.StaticShapes, Has.Count.EqualTo(1));
    Assert.That(result.StaticShapes[0].File.Path, Is.EqualTo(StaticPath));
    Assert.That(result.StaticShapes[0].Resource, Is.SameAs(staticShape));
    Assert.That(result.BoneShapes, Has.Count.EqualTo(1));
    Assert.That(result.BoneShapes[0].File.Path, Is.EqualTo(BonePath));
    Assert.That(result.BoneShapes[0].Resource, Is.SameAs(boneShape));
  }

  [Test]
  public void Decode_AssociatesCommonResidentStaticShapeWithExactOvlSymbol() {
    const string commonStaticPath = "station.common.ovl";
    using var archive = new Ovl("ride visuals");
    archive.Add(
      new OvlFile("StationMiddle", FileType.StaticShape, commonStaticPath),
      new OvlEntry(0, 0));
    var staticShape = StaticShape("StationMiddle");

    var result = RideVisualShapeResourceDecoder.Decode(
      [archive],
      _ => [staticShape],
      _ => [],
      RideCarVisualResourceBridgeLimits.Default);

    Assert.That(result.StaticShapes, Has.Count.EqualTo(1));
    Assert.That(result.StaticShapes[0].File.Path, Is.EqualTo(commonStaticPath));
    Assert.That(result.StaticShapes[0].Resource, Is.SameAs(staticShape));
  }

  [Test]
  public void Decode_RejectsDecodedShapeWithoutExactOvlSymbol() {
    using var archive = new Ovl("ride visuals");
    archive.Add(
      new OvlFile("Different", FileType.StaticShape, StaticPath),
      new OvlEntry(0, 0));

    var error = Assert.Throws<InvalidDataException>(
      new Action(() => RideVisualShapeResourceDecoder.Decode(
        [archive],
        _ => [StaticShape("BodyStatic")],
        _ => [],
        RideCarVisualResourceBridgeLimits.Default)));

    Assert.That(error!.Message, Does.Contain("does not have one exact OVL symbol"));
  }

  [Test]
  public void Resolve_LinksStaticAndBoneLodsWithinTheCarClosure() {
    var visual = Visual(
      "Body",
      StaticLod("near", "BodyStatic:shs"),
      BoneLod("far", "BodyBone:bsh"),
      BillboardLod("billboard"));
    var graph = Graph(
      visual,
      [RidePath, TrainPath, CarPath, VisualPath, StaticPath, BonePath]);
    var staticShape = StaticShape("BodyStatic");
    var boneShape = BoneShape("BodyBone");

    var result = RideCarVisualResourceBridge.Resolve(
      graph,
      new RideVisualShapeResourceSet(
        [StaticSource(StaticPath, staticShape)],
        [BoneSource(BonePath, boneShape)]));

    Assert.That(result.Visuals, Has.Count.EqualTo(1));
    var linkedVisual = result.Visuals.Single();
    Assert.That(linkedVisual.Car.Role, Is.EqualTo(RideTrainCarRole.Front));
    Assert.That(linkedVisual.Visual.Role, Is.EqualTo(RideVisualRole.Body));
    Assert.That(linkedVisual.Lods, Has.Count.EqualTo(2));
    Assert.That(result.ResolvedShapeLodCount, Is.EqualTo(2));
    Assert.That(result.UnresolvedShapeReferenceCount, Is.Zero);
    var staticLod = linkedVisual.Lods[0];
    var boneLod = linkedVisual.Lods[1];
    Assert.That(staticLod.StaticShape, Is.SameAs(staticShape));
    Assert.That(staticLod.BoneShape, Is.Null);
    Assert.That(boneLod.StaticShape, Is.Null);
    Assert.That(boneLod.BoneShape, Is.SameAs(boneShape));
  }

  [Test]
  public void Resolve_MissingShapeRemainsExplicit() {
    var graph = Graph(
      Visual("Body", StaticLod("near", "Missing:shs")),
      [RidePath, TrainPath, CarPath, VisualPath]);

    var result = RideCarVisualResourceBridge.Resolve(
      graph,
      new RideVisualShapeResourceSet([], []));

    Assert.That(result.Visuals.Single().Lods.Single().IsResolved, Is.False);
    Assert.That(result.ResolvedShapeLodCount, Is.Zero);
    Assert.That(result.UnresolvedShapeReferenceCount, Is.EqualTo(1));
  }

  [Test]
  public void Resolve_DoesNotLinkShapeOutsideCarDependencyClosure() {
    var graph = Graph(
      Visual("Body", StaticLod("near", "BodyStatic:shs")),
      [RidePath, TrainPath, CarPath, VisualPath]);
    var resources = new RideVisualShapeResourceSet(
      [StaticSource(StaticPath, StaticShape("BodyStatic"))],
      []);

    var result = RideCarVisualResourceBridge.Resolve(graph, resources);

    Assert.That(result.Visuals.Single().Lods.Single().IsResolved, Is.False);
    Assert.That(result.UnresolvedShapeReferenceCount, Is.EqualTo(1));
  }

  [Test]
  public void Resolve_RejectsAmbiguousShapeAcrossAllowedArchives() {
    var otherPath = "other.unique.ovl";
    var graph = Graph(
      Visual("Body", StaticLod("near", "BodyStatic:shs")),
      [RidePath, TrainPath, CarPath, VisualPath, StaticPath, otherPath]);
    var resources = new RideVisualShapeResourceSet(
      [
        StaticSource(StaticPath, StaticShape("BodyStatic")),
        StaticSource(otherPath, StaticShape("BodyStatic")),
      ],
      []);

    var error = Assert.Throws<InvalidDataException>(
      new Action(() => RideCarVisualResourceBridge.Resolve(graph, resources)));

    Assert.That(error!.Message, Does.Contain("ambiguous"));
  }

  [Test]
  public void Resolve_RejectsWrongShapeReferenceTag() {
    var graph = Graph(
      Visual("Body", StaticLod("near", "BodyStatic:bsh")),
      [RidePath, TrainPath, CarPath, VisualPath, StaticPath]);

    var error = Assert.Throws<InvalidDataException>(
      new Action(() => RideCarVisualResourceBridge.Resolve(
        graph,
        new RideVisualShapeResourceSet(
          [StaticSource(StaticPath, StaticShape("BodyStatic"))],
          []))));

    Assert.That(error!.Message, Does.Contain("exact name:shs key"));
  }

  [Test]
  public void Resolve_RejectsResolvedVisualOutsideCarDependencyClosure() {
    var graph = Graph(
      Visual("Body", StaticLod("near", "BodyStatic:shs")),
      [RidePath, TrainPath, CarPath, StaticPath],
      visualPath: VisualPath);

    var error = Assert.Throws<InvalidDataException>(
      new Action(() => RideCarVisualResourceBridge.Resolve(
        graph,
        new RideVisualShapeResourceSet(
          [StaticSource(StaticPath, StaticShape("BodyStatic"))],
          []))));

    Assert.That(error!.Message, Does.Contain("outside its proven dependency closure"));
  }

  [Test]
  public void Resolve_ReusesOneExactShapeSourceAcrossLods() {
    var visual = Visual(
      "Body",
      StaticLod("near", "BodyStatic:shs"),
      StaticLod("far", "BodyStatic:shs"));
    var graph = Graph(
      visual,
      [RidePath, TrainPath, CarPath, VisualPath, StaticPath]);
    var shape = StaticShape("BodyStatic");

    var result = RideCarVisualResourceBridge.Resolve(
      graph,
      new RideVisualShapeResourceSet([StaticSource(StaticPath, shape)], []));

    var lods = result.Visuals.Single().Lods;
    Assert.That(lods, Has.Count.EqualTo(2));
    Assert.That(
      ReferenceEquals(lods[0].StaticShapeSource, lods[1].StaticShapeSource),
      Is.True);
  }

  private static RideResourceGraph Graph(
    SceneryItemVisual visual,
    IReadOnlyList<string> allowedPaths,
    string visualPath = VisualPath
  ) {
    var visualSource = new RideVisualResourceSource(
      new OvlFile(visual.Name, FileType.SceneryItemVisual, visualPath),
      visual);
    var car = Car("Car", $"{visual.Name}:svd");
    var carSource = new RideCarResourceSource(
      new OvlFile(car.Name, FileType.RideCar, CarPath),
      car,
      allowedPaths);
    var carLink = new RideCarLink(
      RideTrainCarRole.Front,
      $"{car.Name}:ric",
      carSource,
      [new RideVisualLink(RideVisualRole.Body, car.Visual!, visualSource)]);
    var train = Train("Train", car.Name);
    var trainSource = new RideTrainResourceSource(
      new OvlFile(train.Name, FileType.RideTrain, TrainPath),
      train,
      allowedPaths);
    var trainLink = new RideTrainLink(train.Name, trainSource, [carLink]);
    var ride = Ride("Ride", train.Name);
    var rideSource = new TrackedRideResourceSource(
      new OvlFile(ride.Name, FileType.TrackedRide, RidePath),
      ride,
      allowedPaths);
    return new RideResourceGraph(
      [new TrackedRideResourceLink(rideSource, [trainLink], null)],
      0);
  }

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 0f, 0f, 0f, lods, null);

  private static SceneryItemVisualLod StaticLod(string name, string reference) =>
    new(name, SvdLodType.StaticShape, reference, null, null, null, Billboard(), 10f, []);

  private static SceneryItemVisualLod BoneLod(string name, string reference) =>
    new(name, SvdLodType.BoneShape, null, reference, null, null, Billboard(), 20f, []);

  private static SceneryItemVisualLod BillboardLod(string name) =>
    new(name, SvdLodType.Billboard, null, null, "Billboard:ftx", "Style:txs",
      Billboard(), 30f, []);

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

  private static RideStaticShapeResourceSource StaticSource(
    string path,
    StaticShape shape
  ) => new(new OvlFile(shape.Name, FileType.StaticShape, path), shape);

  private static RideBoneShapeResourceSource BoneSource(
    string path,
    BoneShape shape
  ) => new(new OvlFile(shape.Name, FileType.BoneShape, path), shape);

  private static StaticShape StaticShape(string name) {
    var vertices = new[] {
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One),
    };
    var mesh = new StaticShapeMesh(
      "mesh", 0, null, null, 0, 0, 0, vertices, new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
    };
    return new StaticShape(name, Vector3.Zero, Vector3.One, [mesh], []);
  }

  private static BoneShape BoneShape(string name) {
    var skinning = new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0);
    var vertices = new[] {
      new BoneShapeVertex(
        Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One, skinning),
    };
    var mesh = new BoneShapeMesh(
      "mesh", 0, null, null, 0, 0, 0, vertices, new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3,
    };
    return new BoneShape(name, Vector3.Zero, Vector3.One, [mesh], []);
  }

  private static RideCar Car(string name, string visual) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    visual,
    1f,
    null,
    -1f,
    new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideCarBobbingSettings(0, 0f, 0f, 0f),
    new RideCarAnimationSettings(
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0f, 0, 0f, 0f, 0f, 0f),
    null,
    null);

  private static RideTrain Train(string name, string carName) => new(
    name,
    RideTrainVersion.Vanilla,
    name,
    name,
    new RideTrainCars($"{carName}:ric", null, null, null, null, null, 1, 1, 1, null),
    new RideTrainSpeedSettings(0f, 0f, 0f),
    new RideTrainCameraSettings(
      0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideTrainWaterSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    0,
    new RideTrainUnknownSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    null,
    null,
    null,
    null,
    null);

  private static TrackedRide Ride(string name, string trainName) => new(
    name,
    TrackedRideVersion.Vanilla,
    [],
    [trainName],
    null,
    null,
    null,
    null!,
    null!,
    null!,
    null!,
    null!,
    null,
    null);
}
