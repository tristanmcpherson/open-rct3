// Ride Car Variant Visual Template Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVariantVisualTemplateRegistryTests {
  [Test]
  public void Build_NormalSelectionRetainsFirstSerializedBodyLodAndMaterialOrder() {
    var first = StaticShape(
      "NormalFirst",
      StaticMesh("paint", 2, "Paint:ftx", "Gloss:txs", 1, 17, 3),
      StaticMesh("metal", 3, "Metal:ftx", "Steel:txs", 2, 19, 1));
    var later = StaticShape("NormalLater", StaticMesh("later"));
    var fixture = Create("Normal", RideCarVisualVariant.Normal, first, laterShape: later);

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(fixture.Selection));

    var entry = registry.Entries.Single();
    var template = entry.Template!;
    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsResolved, Is.True);
      Assert.That(entry.Selection, Is.SameAs(fixture.Selection));
      Assert.That(template.Link, Is.SameAs(fixture.Body));
      Assert.That(template.Lod, Is.SameAs(fixture.FirstLod));
      Assert.That(template.StaticShape, Is.SameAs(first));
      Assert.That(template.ShapeKind, Is.EqualTo(RideCarVisualTemplateShapeKind.StaticShape));
      Assert.That(template.Batches.Select(batch => batch.SourceMeshName),
        Is.EqualTo(new[] { "paint", "metal" }));
      Assert.That(template.Batches.Select(batch => batch.FtxRef),
        Is.EqualTo(new[] { "Paint:ftx", "Metal:ftx" }));
      Assert.That(template.Batches.Select(batch => batch.TxsRef),
        Is.EqualTo(new[] { "Gloss:txs", "Steel:txs" }));
      Assert.That(registry.ResolvedCount, Is.EqualTo(1));
      Assert.That(registry.DistinctShapeResourceCount, Is.EqualTo(1));
      Assert.That(registry.BatchCount, Is.EqualTo(2));
      Assert.That(registry.VertexCount, Is.EqualTo(6));
      Assert.That(registry.IndexCount, Is.EqualTo(6));
    }
    Assert.Throws<NotSupportedException>(new Action(() =>
      ((IList<RideCarVariantVisualTemplateEntry>)registry.Entries).Clear()));
  }

  [Test]
  public void Build_WildSelectionAdaptsExactFlippedBoneBody() {
    var flipped = BoneShape("WildFlipped", BoneMesh("wild-body"));
    var fixture = Create("Wild", RideCarVisualVariant.WildFlipped, flipped);

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(fixture.Selection));

    var template = registry.Entries.Single().Template!;
    using (Assert.EnterMultipleScope()) {
      Assert.That(template.Link.Visual.Role, Is.EqualTo(RideVisualRole.WildFlippedBody));
      Assert.That(template.Link, Is.SameAs(fixture.Body));
      Assert.That(template.BoneShape, Is.SameAs(flipped));
      Assert.That(template.StaticShape, Is.Null);
      Assert.That(template.ShapeKind, Is.EqualTo(RideCarVisualTemplateShapeKind.BoneShape));
      Assert.That(template.Batches.Single().SourceMeshName, Is.EqualTo("wild-body"));
    }
  }

  [Test]
  public void Build_UpstreamFailureStaysTypedAndDoesNotAdapt() {
    var fixture = Create(
      "Upstream",
      RideCarVisualVariant.Normal,
      StaticShape("UpstreamShape", StaticMesh("unused")));
    var failed = fixture.Selection with {
      SelectedVariant = null,
      RequiredBodyRole = null,
      RequiredMovingRole = null,
      Body = null,
      Moving = null,
      BodyControlFallback = null,
      Issue = new(
        RideCarVisualVariantSelectionStatus.UnsupportedVariant,
        Role: null,
        MatchingOccurrenceCount: 0,
        UnresolvedLodCount: 0),
    };
    var adapted = false;

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(failed),
      _ => {
        adapted = true;
        return [];
      },
      _ => {
        adapted = true;
        return [];
      },
      mesh => mesh.Dispose(),
      RideCarVariantVisualTemplateRegistryLimits.Default);

    var entry = registry.Entries.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(adapted, Is.False);
      Assert.That(entry.Template, Is.Null);
      Assert.That(
        entry.Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed));
      Assert.That(
        entry.Issue!.SelectionStatus,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnsupportedVariant));
      Assert.That(registry.UpstreamSelectionFailureCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_ReusesBatchesForRepeatedDecodedShapeObjectIdentity() {
    var shared = StaticShape("Shared", StaticMesh("shared"));
    var first = Create("SharedFirst", RideCarVisualVariant.Normal, shared, registryIndex: 0);
    var second = Create("SharedSecond", RideCarVisualVariant.Normal, shared, registryIndex: 1);
    var calls = 0;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      calls++;
      return StaticShapeMeshBuilder.BuildBatches(shape);
    }

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(first.Selection, second.Selection),
      Adapt,
      BoneShapeMeshBuilder.BuildBatches,
      mesh => mesh.Dispose(),
      RideCarVariantVisualTemplateRegistryLimits.Default);

    using (Assert.EnterMultipleScope()) {
      Assert.That(calls, Is.EqualTo(1));
      Assert.That(registry.Entries.Select(entry => entry.RegistryIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(registry.Entries[0].Template!.Batches,
        Is.SameAs(registry.Entries[1].Template!.Batches));
      Assert.That(registry.DistinctShapeResourceCount, Is.EqualTo(1));
      Assert.That(registry.BatchCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_CopiedSerializedLodIdentityFailsBeforeAdapting() {
    var fixture = Create(
      "ChangedLod",
      RideCarVisualVariant.Normal,
      StaticShape("ChangedLodShape", StaticMesh("body")));
    var copiedLod = fixture.FirstLod.Lod with { };
    var copiedLink = fixture.FirstLod with { Lod = copiedLod };
    var changedBody = fixture.Body with { Lods = [copiedLink] };
    var changed = fixture.Selection with {
      Body = changedBody,
      BodyControlFallback = fixture.Selection.BodyControlFallback! with {
        Body = changedBody,
      },
    };
    var adapted = false;

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(changed),
      _ => {
        adapted = true;
        return [];
      },
      _ => {
        adapted = true;
        return [];
      },
      mesh => mesh.Dispose(),
      RideCarVariantVisualTemplateRegistryLimits.Default);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adapted, Is.False);
      Assert.That(registry.Entries.Single().Template, Is.Null);
      Assert.That(
        registry.Entries.Single().Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.ChangedSelectionIdentity));
      Assert.That(registry.Entries.Single().Issue!.Detail,
        Does.Contain("serialized object identity"));
    }
  }

  [Test]
  public void Build_ForgedSameNameShapeSourceCannotReplaceAuthorizedBodyOccurrence() {
    var shape = StaticShape("AuthorizedShape", StaticMesh("authorized"));
    var fixture = Create("Forged", RideCarVisualVariant.Normal, shape);
    var originalLod = fixture.FirstLod;
    var originalSource = originalLod.StaticShapeSource!;
    var forgedShape = StaticShape(shape.Name, StaticMesh("forged"));
    var forgedSource = new RideStaticShapeResourceSource(
      new OvlFile(
        originalSource.File.Name,
        originalSource.File.Type,
        originalSource.File.Path),
      forgedShape);
    var forgedLod = originalLod with { StaticShapeSource = forgedSource };
    var forgedBody = fixture.Body with { Lods = [forgedLod] };
    var forged = fixture.Selection with {
      Body = forgedBody,
      BodyControlFallback = fixture.Selection.BodyControlFallback! with {
        Body = forgedBody,
      },
    };
    var selections = new RideCarVisualVariantSelectionRegistry(
      [forged],
      [fixture.Body]);
    var adapted = false;

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      selections,
      _ => {
        adapted = true;
        return [];
      },
      _ => {
        adapted = true;
        return [];
      },
      mesh => mesh.Dispose(),
      RideCarVariantVisualTemplateRegistryLimits.Default);

    var entry = registry.Entries.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(forgedShape.Name, Is.EqualTo(shape.Name));
      Assert.That(forgedSource.File, Is.EqualTo(originalSource.File));
      Assert.That(adapted, Is.False);
      Assert.That(entry.Template, Is.Null);
      Assert.That(
        entry.Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.ChangedSelectionIdentity));
      Assert.That(entry.Issue!.Detail, Does.Contain("authorized bridge occurrence"));
    }
  }

  [Test]
  public void Build_UnresolvedSelectedBodyLodIsTypedAndDoesNotAdapt() {
    var fixture = Create(
      "Unavailable",
      RideCarVisualVariant.Normal,
      StaticShape("UnavailableShape", StaticMesh("body")));
    var unavailableLod = fixture.FirstLod with {
      StaticShapeSource = null,
      BoneShapeSource = null,
    };
    var unavailableBody = fixture.Body with { Lods = [unavailableLod] };
    var unavailable = fixture.Selection with {
      Body = unavailableBody,
      BodyControlFallback = fixture.Selection.BodyControlFallback! with {
        Body = unavailableBody,
      },
    };
    var adapted = false;

    using var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(unavailable),
      _ => {
        adapted = true;
        return [];
      },
      _ => {
        adapted = true;
        return [];
      },
      mesh => mesh.Dispose(),
      RideCarVariantVisualTemplateRegistryLimits.Default);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adapted, Is.False);
      Assert.That(registry.Entries.Single().Template, Is.Null);
      Assert.That(
        registry.Entries.Single().Status,
        Is.EqualTo(RideCarVariantVisualTemplateStatus.UnavailableRequiredLod));
    }
  }

  [Test]
  public void Build_GeometryLimitFailsBeforeAdapting() {
    var fixture = Create(
      "Bounded",
      RideCarVisualVariant.Normal,
      StaticShape("BoundedShape", StaticMesh("body")));
    var adapted = false;
    var limits = RideCarVariantVisualTemplateRegistryLimits.Default with {
      MaximumVertices = 2,
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVariantVisualTemplateRegistry.Build(
        Registry(fixture.Selection),
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

    using (Assert.EnterMultipleScope()) {
      Assert.That(adapted, Is.False);
      Assert.That(error!.Message, Does.Contain("vertices exceed the limit 2"));
    }
  }

  [Test]
  public void Build_LaterAdapterFailureDisposesEarlierOwnedMesh() {
    var firstShape = StaticShape("FirstFailure", StaticMesh("first"));
    var secondShape = StaticShape("SecondFailure", StaticMesh("second"));
    var first = Create(
      "FailureFirst",
      RideCarVisualVariant.Normal,
      firstShape,
      registryIndex: 0);
    var second = Create(
      "FailureSecond",
      RideCarVisualVariant.Normal,
      secondShape,
      registryIndex: 1);
    Mesh? firstMesh = null;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      if (ReferenceEquals(shape, secondShape))
        throw new InvalidDataException("second adapter failed");
      var batches = StaticShapeMeshBuilder.BuildBatches(shape);
      firstMesh = batches.Single().Mesh;
      return batches;
    }

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVariantVisualTemplateRegistry.Build(
        Registry(first.Selection, second.Selection),
        Adapt,
        BoneShapeMeshBuilder.BuildBatches,
        mesh => mesh.Dispose(),
        RideCarVariantVisualTemplateRegistryLimits.Default)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("second adapter failed"));
      Assert.That(firstMesh, Is.Not.Null);
      Assert.That(firstMesh!.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Dispose_ReleasesUniqueMeshesInReverseOrderAndIsIdempotent() {
    var first = Create(
      "DisposeFirst",
      RideCarVisualVariant.Normal,
      StaticShape("DisposeFirstShape", StaticMesh("first")),
      registryIndex: 0);
    var second = Create(
      "DisposeSecond",
      RideCarVisualVariant.Normal,
      StaticShape("DisposeSecondShape", StaticMesh("second")),
      registryIndex: 1);
    var disposed = new List<string>();
    void Dispose(Mesh mesh) {
      disposed.Add(mesh.Name!);
      mesh.Dispose();
    }
    var registry = RideCarVariantVisualTemplateRegistry.Build(
      Registry(first.Selection, second.Selection),
      StaticShapeMeshBuilder.BuildBatches,
      BoneShapeMeshBuilder.BuildBatches,
      Dispose,
      RideCarVariantVisualTemplateRegistryLimits.Default);

    registry.Dispose();
    registry.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.IsDisposed, Is.True);
      Assert.That(disposed, Is.EqualTo(new[] { "second", "first" }));
    }
  }

  private static RideCarVisualVariantSelectionRegistry Registry(
    params RideCarVisualVariantSelectionEntry[] entries
  ) => new(
    entries,
    entries.SelectMany(entry => new[] { entry.Body, entry.Moving })
      .Where(occurrence => occurrence != null)
      .Select(occurrence => occurrence!)
      .ToArray());

  private static Fixture Create(
    string prefix,
    RideCarVisualVariant variant,
    object shape,
    object? laterShape = null,
    int registryIndex = 0
  ) {
    const string ridePath = "ride.unique.ovl";
    const string trainPath = "train.unique.ovl";
    const string carPath = "car.unique.ovl";
    const string visualPath = "visual.unique.ovl";
    const string shapePath = "shape.unique.ovl";
    var bodyRole = variant == RideCarVisualVariant.Normal
      ? RideVisualRole.Body
      : RideVisualRole.WildFlippedBody;
    var movingRole = variant == RideCarVisualVariant.Normal
      ? RideVisualRole.Moving
      : RideVisualRole.WildFlippedMoving;
    var savedVariant = variant == RideCarVisualVariant.Normal ? 0 : 1;
    var bodyName = $"{prefix}Body";
    var first = ShapeLod(shape, "first", 1_000f, shapePath);
    var lods = new List<SceneryItemVisualLod> { first.Lod };
    var linkedLods = new List<RideVisualShapeLodLink> { first.Link };
    if (laterShape != null) {
      var later = ShapeLod(laterShape, "later", 0f, shapePath);
      lods.Add(later.Lod);
      linkedLods.Add(later.Link);
    }
    var visual = new SceneryItemVisual(
      bodyName,
      0,
      0f,
      0f,
      0f,
      0f,
      lods,
      null);
    var visualLink = new RideVisualLink(
      bodyRole,
      $"{bodyName}:svd",
      new RideVisualResourceSource(
        new OvlFile(bodyName, FileType.SceneryItemVisual, visualPath),
        visual));
    var carResource = CarResource(
      prefix,
      variant,
      $"{bodyName}:svd",
      movingReference: null);
    var car = new RideCarLink(
      RideTrainCarRole.Front,
      $"{carResource.Name}:ric",
      new RideCarResourceSource(
        new OvlFile(carResource.Name, FileType.RideCar, carPath),
        carResource,
        [carPath, visualPath, shapePath]),
      [visualLink]);
    var trainResource = TrainResource($"{prefix}Train", car.Reference);
    var trainSource = new RideTrainResourceSource(
      new OvlFile(trainResource.Name, FileType.RideTrain, trainPath),
      trainResource,
      [trainPath, carPath, visualPath, shapePath]);
    var train = new RideTrainLink(trainResource.Name, trainSource, [car]);
    var rideResource = TrackedRide($"{prefix}Ride", train.Reference);
    var ride = new TrackedRideResourceLink(
      new TrackedRideResourceSource(
        new OvlFile(rideResource.Name, FileType.TrackedRide, ridePath),
        rideResource,
        [ridePath, trainPath, carPath, visualPath, shapePath]),
      [train],
      WildSplitter: null);
    var body = new RideCarVisualShapeLink(
      ride,
      train,
      car,
      visualLink,
      Array.AsReadOnly(linkedLods.ToArray()));

    var rideInstance = RideInstance(900ul + Convert.ToUInt64(registryIndex), prefix);
    var trainInstanceId = 1_000ul + Convert.ToUInt64(registryIndex);
    var carInstanceId = 2_000ul + Convert.ToUInt64(registryIndex);
    var trainInstance = TrainInstance(
      trainInstanceId,
      rideInstance.EntryId,
      trainResource.Name,
      carInstanceId,
      savedVariant);
    var trainResourceLink = new RideTrainInstanceResourceLink(
      rideInstance,
      trainInstance,
      Ordinal: 0,
      trainSource);
    var trainRuntime = new RideInstanceTrainRuntimeEntry(
      SavedTrainIndex: registryIndex,
      TrackRuntime: null!,
      trainResourceLink);
    var role = new RideTrainConsistRoleEntry(
      RuntimeIndex: 0,
      NonLinkIndex: 0,
      RideTrainCarRole.Front,
      ResourceName: car.Reference,
      PeepSlotCount: 0);
    var consistCar = new RideInstanceTrainConsistCarRuntimeEntry(
      role,
      car,
      new RideCarPeepSlotEvidence(car, body, PeepSlotCount: 0, MarkerLods: []));
    var consist = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime,
      ride,
      train,
      new RideTrainConsistRoleResolution(1, [role]),
      [consistCar],
      RideInstanceTrainConsistRuntimeStatus.Resolved);
    var carInstance = new DatRideCarInstanceData(
      carInstanceId,
      trainInstanceId,
      whichCar: 0,
      whichRideTrainCar: Convert.ToInt32(RideTrainCarRole.Front),
      trackPiece: 0,
      rearTrackPiece: 0,
      distance: 0f,
      reversed: false,
      speed: 0f);
    var missingTrack = new RideCarTrackPieceRuntimeLink(
      SavedTrackPieceEntryId: 0,
      RideCarTrackPieceRuntimeStatus.MissingSavedReference,
      PieceIndex: null,
      Piece: null);
    var carRuntime = new RideCarInstanceRuntimeEntry(
      SavedCarIndex: registryIndex,
      RegistryIndex: registryIndex,
      trainRuntime,
      carInstance,
      RideTrainCarRole.Front,
      missingTrack,
      missingTrack,
      RideCarResourceRuntimeStatus.Resolved,
      car,
      consist,
      consistCar);
    var fallback = new RideCarBodyControlVisualFallback(car, body, movingRole);
    var selection = new RideCarVisualVariantSelectionEntry(
      registryIndex,
      carRuntime,
      savedVariant,
      variant,
      bodyRole,
      movingRole,
      car,
      body,
      Moving: null,
      fallback,
      Issue: null);
    return new(selection, body, first.Link);
  }

  private static ShapeLodFixture ShapeLod(
    object shape,
    string lodName,
    float distance,
    string shapePath
  ) {
    switch (shape) {
      case StaticShape staticShape:
        var staticLod = new SceneryItemVisualLod(
          lodName,
          SvdLodType.StaticShape,
          $"{staticShape.Name}:shs",
          null,
          null,
          null,
          Billboard(),
          distance,
          []);
        return new(
          staticLod,
          new RideVisualShapeLodLink(
            staticLod,
            new RideStaticShapeResourceSource(
              new OvlFile(staticShape.Name, FileType.StaticShape, shapePath),
              staticShape),
            BoneShapeSource: null));
      case BoneShape boneShape:
        var boneLod = new SceneryItemVisualLod(
          lodName,
          SvdLodType.BoneShape,
          null,
          $"{boneShape.Name}:bsh",
          null,
          null,
          Billboard(),
          distance,
          []);
        return new(
          boneLod,
          new RideVisualShapeLodLink(
            boneLod,
            StaticShapeSource: null,
            new RideBoneShapeResourceSource(
              new OvlFile(boneShape.Name, FileType.BoneShape, shapePath),
              boneShape)));
      default:
        throw new ArgumentException("Shape must be SHS or BSH.", nameof(shape));
    }
  }

  private static SceneryVisualBillboardSettings Billboard() =>
    new(1f, 1f, 0f, 0f, 1f, 1f);

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
  ) => new(
    name,
    supportType,
    ftxRef,
    txsRef,
    transparency,
    textureFlags,
    sides,
    new[] {
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One),
    },
    new uint[] { 0, 1, 2 }) {
    IndexLayout = StaticShapeIndexLayout.TriangleList,
    StoredIndexCount = 3,
  };

  private static BoneShape BoneShape(
    string name,
    params BoneShapeMesh[] meshes
  ) => new(name, Vector3.Zero, Vector3.One, meshes, []);

  private static BoneShapeMesh BoneMesh(string name) {
    var skinning = new BoneShapeSkinning(-1, -1, -1, -1, 0, 0, 0, 0);
    return new(
      name,
      0,
      null,
      null,
      0,
      0,
      1,
      new[] {
        new BoneShapeVertex(
          Vector3.Zero, Vector3.UnitY, Vector2.Zero, Vector4.One, skinning),
        new BoneShapeVertex(
          Vector3.UnitX, Vector3.UnitY, Vector2.UnitX, Vector4.One, skinning),
        new BoneShapeVertex(
          Vector3.UnitZ, Vector3.UnitY, Vector2.UnitY, Vector4.One, skinning),
      },
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3,
    };
  }

  private static RideCar CarResource(
    string prefix,
    RideCarVisualVariant variant,
    string bodyReference,
    string? movingReference
  ) => new(
    $"{prefix}Car",
    variant == RideCarVisualVariant.WildFlipped
      ? RideCarVersion.Wild
      : RideCarVersion.Vanilla,
    prefix,
    prefix,
    0,
    0,
    variant == RideCarVisualVariant.Normal ? bodyReference : $"{prefix}Normal:svd",
    1f,
    variant == RideCarVisualVariant.Normal ? movingReference : null,
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
    Soaked: null,
    variant == RideCarVisualVariant.WildFlipped
      ? new RideCarWildSettings(
        1,
        bodyReference,
        movingReference,
        0,
        1f,
        1f,
        null,
        4.1f,
        0,
        0,
        0)
      : null);

  private static RideTrain TrainResource(string name, string carReference) => new(
    name,
    RideTrainVersion.Vanilla,
    name,
    name,
    new RideTrainCars(carReference, null, null, null, null, null, 1, 1, 1, null),
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

  private static TrackedRide TrackedRide(string name, string trainName) => new(
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

  private static DatTrackedRideInstanceData RideInstance(ulong entryId, string name) => new(
    entryId,
    name,
    track: entryId + 100,
    trackedRideOverlayName: $@"Tracks\{name}",
    trackedRideSymbolName: $"{name}Ride:trr",
    nTrains: 1,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains: [entryId + 100]);

  private static DatRideTrainInstanceData TrainInstance(
    ulong entryId,
    ulong rideInstanceId,
    string resourceName,
    ulong carInstanceId,
    int visualVariant
  ) => new(
    entryId,
    rideTrainOverlayName: $@"Cars\{resourceName}",
    rideTrainSymbolName: $"{resourceName}:rit",
    trackedRideInstance: rideInstanceId,
    whichTrain: 0,
    length: 1f,
    mass: 1f,
    cars: [carInstanceId],
    whichRideCarSivVariant: visualVariant);

  private sealed record ShapeLodFixture(
    SceneryItemVisualLod Lod,
    RideVisualShapeLodLink Link);

  private sealed record Fixture(
    RideCarVisualVariantSelectionEntry Selection,
    RideCarVisualShapeLink Body,
    RideVisualShapeLodLink FirstLod);
}
