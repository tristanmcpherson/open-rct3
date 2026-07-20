using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualTemplateRegistryTests {
  private const string RidePath = "rides.unique.ovl";
  private const string TrainPath = "trains.unique.ovl";
  private const string CarPath = "cars.unique.ovl";
  private const string VisualPath = "visuals.unique.ovl";
  private const string ShapePath = "shapes.unique.ovl";

  [Test]
  public void Build_SelectsFirstSerializedResolvedBodyLodAndPreservesBatches() {
    var missing = StaticLod("missing", "Missing:shs", 1f);
    var selected = BoneLod("serialized-first", "BodyBone:bsh", 1000f);
    var later = StaticLod("camera-nearer", "BodyStatic:shs", 0f);
    var bone = BoneShape(
      "BodyBone",
      BoneMesh("first", 2, "First:ftx", "StyleA:txs", 1, 17, 3),
      BoneMesh("second", 3, "Second:ftx", "StyleB:txs", 2, 19, 1));
    var staticShape = StaticShape("BodyStatic", StaticMesh("later"));
    var link = BodyLink(
      "One",
      Visual("BodyVisual", missing, selected, later),
      [
        new RideVisualShapeLodLink(missing, null, null),
        new RideVisualShapeLodLink(selected, null, BoneSource(bone)),
        new RideVisualShapeLodLink(later, StaticSource(staticShape), null),
      ]);

    using var registry = RideCarVisualTemplateRegistry.Build(Bridge(link));

    Assert.That(registry.BodyVisualOccurrenceCount, Is.EqualTo(1));
    Assert.That(registry.UnresolvedBodyVisualCount, Is.Zero);
    Assert.That(registry.DistinctShapeResourceCount, Is.EqualTo(1));
    Assert.That(registry.BatchCount, Is.EqualTo(2));
    Assert.That(registry.VertexCount, Is.EqualTo(6));
    Assert.That(registry.IndexCount, Is.EqualTo(6));
    Assert.That(registry.Templates, Has.Count.EqualTo(1));
    var template = registry.Templates.Single();
    Assert.That(template.Lod.Lod, Is.SameAs(selected));
    Assert.That(template.ShapeKind, Is.EqualTo(RideCarVisualTemplateShapeKind.BoneShape));
    Assert.That(template.BoneShape, Is.SameAs(bone));
    Assert.That(template.StaticShape, Is.Null);
    Assert.That(template.Batches.Select(batch => batch.SourceMeshName),
      Is.EqualTo(new[] { "first", "second" }));
    Assert.That(template.Batches.Select(batch => batch.FtxRef),
      Is.EqualTo(new[] { "First:ftx", "Second:ftx" }));
    Assert.That(template.Batches.Select(batch => batch.TxsRef),
      Is.EqualTo(new[] { "StyleA:txs", "StyleB:txs" }));
  }

  [Test]
  public void Build_CachesRepeatedDecodedShapeObjectIdentity() {
    var shape = StaticShape("SharedBody", StaticMesh("shared"));
    var firstLod = StaticLod("first", "SharedBody:shs", 100f);
    var secondLod = StaticLod("second", "SharedBody:shs", 10f);
    var source = StaticSource(shape);
    var first = BodyLink(
      "First",
      Visual("FirstVisual", firstLod),
      [new RideVisualShapeLodLink(firstLod, source, null)]);
    var second = BodyLink(
      "Second",
      Visual("SecondVisual", secondLod),
      [new RideVisualShapeLodLink(secondLod, source, null)]);
    var calls = 0;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape candidate) {
      calls++;
      return StaticShapeMeshBuilder.BuildBatches(candidate);
    }

    using var registry = RideCarVisualTemplateRegistry.Build(
      Bridge(first, second),
      Adapt,
      BoneShapeMeshBuilder.BuildBatches,
      mesh => mesh.Dispose(),
      RideCarVisualTemplateRegistryLimits.Default);

    Assert.That(calls, Is.EqualTo(1));
    Assert.That(registry.Templates, Has.Count.EqualTo(2));
    Assert.That(registry.DistinctShapeResourceCount, Is.EqualTo(1));
    Assert.That(registry.BatchCount, Is.EqualTo(1));
    Assert.That(registry.Templates[0].Batches, Is.SameAs(registry.Templates[1].Batches));
    Assert.That(
      registry.Templates[0].Batches[0].Mesh,
      Is.SameAs(registry.Templates[1].Batches[0].Mesh));
  }

  [Test]
  public void Build_UnresolvedBodyShapeRemainsExplicitWithoutAllocating() {
    var lod = StaticLod("missing", "Missing:shs", 10f);
    var link = BodyLink(
      "Missing",
      Visual("MissingVisual", lod),
      [new RideVisualShapeLodLink(lod, null, null)]);
    var adapted = false;

    using var registry = RideCarVisualTemplateRegistry.Build(
      Bridge(link),
      _ => {
        adapted = true;
        return [];
      },
      _ => {
        adapted = true;
        return [];
      },
      mesh => mesh.Dispose(),
      RideCarVisualTemplateRegistryLimits.Default);

    Assert.That(adapted, Is.False);
    Assert.That(registry.BodyVisualOccurrenceCount, Is.EqualTo(1));
    Assert.That(registry.UnresolvedBodyVisualCount, Is.EqualTo(1));
    Assert.That(registry.Templates, Is.Empty);
  }

  [Test]
  public void Build_RejectsGraphIdentityMismatchBeforeAdapting() {
    var (result, link) = SingleStaticResult("Identity");
    var impostor = link.Train with { Cars = link.Train.Cars };
    var malformed = new RideCarVisualResourceBridgeResult(
      [link with { Train = impostor }],
      result.UnresolvedShapeReferenceCount);
    var adapted = false;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        malformed,
        _ => {
          adapted = true;
          return [];
        },
        _ => {
          adapted = true;
          return [];
        },
        mesh => mesh.Dispose(),
        RideCarVisualTemplateRegistryLimits.Default)));

    Assert.That(adapted, Is.False);
    Assert.That(error!.Message, Does.Contain("not owned by its exact ride link"));
  }

  [Test]
  public void Build_RejectsLodOutsideSerializedObjectIdentityBeforeAdapting() {
    var (result, link) = SingleStaticResult("LodIdentity");
    var alien = StaticLod("body", "LodIdentityShape:shs", 10f);
    var malformedLod = link.Lods.Single() with { Lod = alien };
    var malformed = new RideCarVisualResourceBridgeResult(
      [link with { Lods = [malformedLod] }],
      result.UnresolvedShapeReferenceCount);
    var adapted = false;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        malformed,
        _ => {
          adapted = true;
          return [];
        },
        _ => {
          adapted = true;
          return [];
        },
        mesh => mesh.Dispose(),
        RideCarVisualTemplateRegistryLimits.Default)));

    Assert.That(adapted, Is.False);
    Assert.That(error!.Message, Does.Contain("does not preserve serialized identity"));
  }

  [Test]
  public void Build_GeometryLimitFailsBeforeAdapting() {
    var (result, _) = SingleStaticResult("Budget");
    var adapted = false;
    var limits = RideCarVisualTemplateRegistryLimits.Default with { MaximumVertices = 2 };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        result,
        _ => {
          adapted = true;
          return [];
        },
        _ => {
          adapted = true;
          return [];
        },
        mesh => mesh.Dispose(),
        limits)));

    Assert.That(adapted, Is.False);
    Assert.That(error!.Message, Does.Contain("vertices exceed the limit 2"));
  }

  [Test]
  public void Build_LaterAdapterFailureDisposesEarlierAdaptedMeshes() {
    var firstShape = StaticShape("FirstShape", StaticMesh("first"));
    var secondShape = StaticShape("SecondShape", StaticMesh("second"));
    var firstLod = StaticLod("first", "FirstShape:shs", 1f);
    var secondLod = StaticLod("second", "SecondShape:shs", 1f);
    var first = BodyLink(
      "FirstFailure",
      Visual("FirstFailureVisual", firstLod),
      [new RideVisualShapeLodLink(firstLod, StaticSource(firstShape), null)]);
    var second = BodyLink(
      "SecondFailure",
      Visual("SecondFailureVisual", secondLod),
      [new RideVisualShapeLodLink(secondLod, StaticSource(secondShape), null)]);
    Mesh? firstMesh = null;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      if (ReferenceEquals(shape, secondShape))
        throw new InvalidDataException("second adapter failed");
      var batches = StaticShapeMeshBuilder.BuildBatches(shape);
      firstMesh = batches.Single().Mesh;
      return batches;
    }

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        Bridge(first, second),
        Adapt,
        BoneShapeMeshBuilder.BuildBatches,
        mesh => mesh.Dispose(),
        RideCarVisualTemplateRegistryLimits.Default)));

    Assert.That(error!.Message, Is.EqualTo("second adapter failed"));
    Assert.That(firstMesh, Is.Not.Null);
    Assert.That(firstMesh!.State, Is.EqualTo(State.Disposed));
  }

  [Test]
  public void Build_EarlyMalformedBatchDisposesEveryReturnedMesh() {
    var shape = StaticShape(
      "MalformedBatchShape",
      StaticMesh("first"),
      StaticMesh("later"));
    var lod = StaticLod("body", "MalformedBatchShape:shs", 1f);
    var link = BodyLink(
      "MalformedBatch",
      Visual("MalformedBatchVisual", lod),
      [new RideVisualShapeLodLink(lod, StaticSource(shape), null)]);
    Mesh[]? adaptedMeshes = null;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      var batches = StaticShapeMeshBuilder.BuildBatches(shape).ToArray();
      adaptedMeshes = batches.Select(batch => batch.Mesh).ToArray();
      batches[0] = batches[0] with { FtxRef = "Changed:ftx" };
      return batches;
    }

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        Bridge(link),
        Adapt,
        BoneShapeMeshBuilder.BuildBatches,
        mesh => mesh.Dispose(),
        RideCarVisualTemplateRegistryLimits.Default)));

    Assert.That(error!.Message, Does.Contain("changed batch 0 order or material metadata"));
    Assert.That(adaptedMeshes, Is.Not.Null);
    Assert.That(adaptedMeshes!.Select(mesh => mesh.State),
      Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Build_BatchCountMismatchDisposesEveryReturnedMesh() {
    var (result, _) = SingleStaticResult("BatchCount");
    Mesh[]? adaptedMeshes = null;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      var first = StaticShapeMeshBuilder.BuildBatches(shape).Single();
      var extraMesh = new Mesh(
        new List<Vertex>(first.Mesh.Vertices),
        new List<uint>(first.Mesh.Indices)) { Name = "extra" };
      adaptedMeshes = [first.Mesh, extraMesh];
      return [
        first,
        first with {
          SourceMeshIndex = 1,
          SourceMeshName = "extra",
          Mesh = extraMesh,
        },
      ];
    }

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualTemplateRegistry.Build(
        result,
        Adapt,
        BoneShapeMeshBuilder.BuildBatches,
        mesh => mesh.Dispose(),
        RideCarVisualTemplateRegistryLimits.Default)));

    Assert.That(error!.Message, Does.Contain("returned 2 batches, expected 1"));
    Assert.That(adaptedMeshes, Is.Not.Null);
    Assert.That(adaptedMeshes!.Select(mesh => mesh.State),
      Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Dispose_IsReverseOrderAndIdempotent() {
    var firstShape = StaticShape("DisposeFirst", StaticMesh("first"));
    var secondShape = StaticShape("DisposeSecond", StaticMesh("second"));
    var firstLod = StaticLod("first", "DisposeFirst:shs", 1f);
    var secondLod = StaticLod("second", "DisposeSecond:shs", 1f);
    var first = BodyLink(
      "DisposeFirst",
      Visual("DisposeFirstVisual", firstLod),
      [new RideVisualShapeLodLink(firstLod, StaticSource(firstShape), null)]);
    var second = BodyLink(
      "DisposeSecond",
      Visual("DisposeSecondVisual", secondLod),
      [new RideVisualShapeLodLink(secondLod, StaticSource(secondShape), null)]);
    var disposedNames = new List<string>();
    void Dispose(Mesh mesh) {
      disposedNames.Add(mesh.Name!);
      mesh.Dispose();
    }
    var registry = RideCarVisualTemplateRegistry.Build(
      Bridge(first, second),
      StaticShapeMeshBuilder.BuildBatches,
      BoneShapeMeshBuilder.BuildBatches,
      Dispose,
      RideCarVisualTemplateRegistryLimits.Default);

    registry.Dispose();
    registry.Dispose();

    Assert.That(registry.IsDisposed, Is.True);
    Assert.That(disposedNames, Is.EqualTo(new[] { "second", "first" }));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_BodyTemplatesHaveDeterministicGeometryCounts() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(Directory.Exists(installRoot), Is.True, installRoot);
    var mapPath = Path.Combine(installRoot!, "Campaigns", "Base", "BoxOffice.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
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
      using var registry = RideCarVisualTemplateRegistry.Build(
        loaded.RideResources.CarVisuals);

      TestContext.Progress.WriteLine(
        $"BoxOffice ride-car body templates: " +
        $"bodyOccurrences={registry.BodyVisualOccurrenceCount}, " +
        $"templates={registry.Templates.Count}, " +
        $"unresolvedBodies={registry.UnresolvedBodyVisualCount}, " +
        $"shapeResources={registry.DistinctShapeResourceCount}, " +
        $"batches={registry.BatchCount}, vertices={registry.VertexCount}, " +
        $"indices={registry.IndexCount}");
      Assert.That(registry.BodyVisualOccurrenceCount, Is.EqualTo(14));
      Assert.That(registry.Templates, Has.Count.EqualTo(14));
      Assert.That(registry.UnresolvedBodyVisualCount, Is.Zero);
      Assert.That(registry.DistinctShapeResourceCount, Is.EqualTo(13));
      Assert.That(registry.BatchCount, Is.EqualTo(41));
      Assert.That(registry.VertexCount, Is.EqualTo(5780));
      Assert.That(registry.IndexCount, Is.EqualTo(12192));
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static (RideCarVisualResourceBridgeResult Result, RideCarVisualShapeLink Link)
    SingleStaticResult(string prefix) {
    var shape = StaticShape($"{prefix}Shape", StaticMesh("body"));
    var lod = StaticLod("body", $"{shape.Name}:shs", 10f);
    var link = BodyLink(
      prefix,
      Visual($"{prefix}Visual", lod),
      [new RideVisualShapeLodLink(lod, StaticSource(shape), null)]);
    return (Bridge(link), link);
  }

  private static RideCarVisualResourceBridgeResult Bridge(
    params RideCarVisualShapeLink[] links
  ) => new(
    links,
    links.Sum(link => link.Lods.Count(lod => !lod.IsResolved)));

  private static RideCarVisualShapeLink BodyLink(
    string prefix,
    SceneryItemVisual visual,
    IReadOnlyList<RideVisualShapeLodLink> lods
  ) {
    var visualSource = new RideVisualResourceSource(
      new OvlFile(visual.Name, FileType.SceneryItemVisual, VisualPath),
      visual);
    var visualLink = new RideVisualLink(
      RideVisualRole.Body,
      $"{visual.Name}:svd",
      visualSource);
    var car = Car($"{prefix}Car", visual.Name);
    var carSource = new RideCarResourceSource(
      new OvlFile(car.Name, FileType.RideCar, CarPath),
      car,
      [CarPath, VisualPath, ShapePath]);
    var carLink = new RideCarLink(
      RideTrainCarRole.Front,
      $"{car.Name}:ric",
      carSource,
      [visualLink]);
    var train = Train($"{prefix}Train", car.Name);
    var trainSource = new RideTrainResourceSource(
      new OvlFile(train.Name, FileType.RideTrain, TrainPath),
      train,
      [TrainPath, CarPath, VisualPath, ShapePath]);
    var trainLink = new RideTrainLink(train.Name, trainSource, [carLink]);
    var ride = Ride($"{prefix}Ride", train.Name);
    var rideSource = new TrackedRideResourceSource(
      new OvlFile(ride.Name, FileType.TrackedRide, RidePath),
      ride,
      [RidePath, TrainPath, CarPath, VisualPath, ShapePath]);
    var rideLink = new TrackedRideResourceLink(rideSource, [trainLink], null);
    return new RideCarVisualShapeLink(rideLink, trainLink, carLink, visualLink, lods);
  }

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 0f, 0f, 0f, lods, null);

  private static SceneryItemVisualLod StaticLod(
    string name,
    string reference,
    float distance
  ) => new(
    name,
    SvdLodType.StaticShape,
    reference,
    null,
    null,
    null,
    Billboard(),
    distance,
    []);

  private static SceneryItemVisualLod BoneLod(
    string name,
    string reference,
    float distance
  ) => new(
    name,
    SvdLodType.BoneShape,
    null,
    reference,
    null,
    null,
    Billboard(),
    distance,
    []);

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

  private static RideStaticShapeResourceSource StaticSource(StaticShape shape) =>
    new(new OvlFile(shape.Name, FileType.StaticShape, ShapePath), shape);

  private static RideBoneShapeResourceSource BoneSource(BoneShape shape) =>
    new(new OvlFile(shape.Name, FileType.BoneShape, ShapePath), shape);

  private static StaticShape StaticShape(
    string name,
    params StaticShapeMesh[] meshes
  ) => new(name, Vector3.Zero, Vector3.One, meshes, []);

  private static StaticShapeMesh StaticMesh(
    string name,
    int supportType = 0,
    string? ftxRef = null,
    string? txsRef = null,
    uint transparency = 0,
    uint textureFlags = 0,
    uint sides = 1
  ) {
    var vertices = new[] {
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One),
    };
    return new StaticShapeMesh(
      name,
      supportType,
      ftxRef,
      txsRef,
      transparency,
      textureFlags,
      sides,
      vertices,
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
    };
  }

  private static BoneShape BoneShape(string name, params BoneShapeMesh[] meshes) =>
    new(name, Vector3.Zero, Vector3.One, meshes, []);

  private static BoneShapeMesh BoneMesh(
    string name,
    int supportType = 0,
    string? ftxRef = null,
    string? txsRef = null,
    uint transparency = 0,
    uint textureFlags = 0,
    uint sides = 1
  ) {
    var skinning = new BoneShapeSkinning(-1, -1, -1, -1, 0, 0, 0, 0);
    var vertices = new[] {
      new BoneShapeVertex(
        Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One, skinning),
      new BoneShapeVertex(
        Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One, skinning),
    };
    return new BoneShapeMesh(
      name,
      supportType,
      ftxRef,
      txsRef,
      transparency,
      textureFlags,
      sides,
      vertices,
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3,
    };
  }

  private static RideCar Car(string name, string visualName) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    $"{visualName}:svd",
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
