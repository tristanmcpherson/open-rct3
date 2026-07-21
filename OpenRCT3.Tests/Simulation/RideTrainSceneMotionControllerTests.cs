// Ride Train Scene Motion Controller Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainSceneMotionControllerTests {
  [Test]
  public void Build_ExplicitRuntimeCarsRejectsNull() {
    using var fixture = SceneFixture.Create();

    Assert.Throws<ArgumentNullException>(new Action(() =>
      RideTrainSceneMotionController.Build(
        [fixture.Train],
        (IReadOnlyList<RideCarInstanceRuntimeEntry>)null!,
        fixture.Scene)));
  }

  [Test]
  public void Build_SuppliesFullRuntimeConsistAndRenderedSubsetForCircuitAuthorization() {
    using var fixture = SceneFixture.Create();
    var originalTrain = fixture.Train.TrainResource.TrainInstance;
    var savedTrain = new DatRideTrainInstanceData(
      originalTrain.EntryId,
      originalTrain.RideTrainOverlayName,
      originalTrain.RideTrainSymbolName,
      originalTrain.TrackedRideInstance,
      originalTrain.WhichTrain,
      originalTrain.Length,
      originalTrain.Mass,
      distance: originalTrain.Distance,
      reversed: originalTrain.Reversed,
      speed: originalTrain.Speed,
      cars: [40UL, 41UL, 42UL],
      whichRideCarSivVariant: originalTrain.WhichRideCarSivVariant,
      state: originalTrain.State,
      stateTime: originalTrain.StateTime);
    var trainResource = new RideTrainInstanceResourceLink(
      fixture.Train.TrainResource.RideInstance,
      savedTrain,
      fixture.Train.TrainResource.Ordinal,
      fixture.Train.TrainResource.Source);
    var train = new RideInstanceTrainRuntimeEntry(
      fixture.Train.SavedTrainIndex,
      fixture.Train.TrackRuntime,
      trainResource);

    RideCarInstanceRuntimeEntry RuntimeCar(
      int index,
      ulong entryId,
      RideTrainCarRole role
    ) {
      var savedCar = new DatRideCarInstanceData(
        entryId,
        savedTrain.EntryId,
        whichCar: index,
        whichRideTrainCar: Convert.ToInt32(role),
        frontWheelDistance: 0f,
        rearWheelDistance: 0f,
        trackPiece: 0,
        rearTrackPiece: 0,
        distance: 0f,
        reversed: false,
        speed: 0f,
        length: 4f,
        mass: 100f,
        positionValid: true);
      return fixture.Car.CarRuntime with {
        SavedCarIndex = index,
        RegistryIndex = index,
        TrainRuntime = train,
        CarInstance = savedCar,
        SavedRole = role,
      };
    }

    RideCarStaticInstanceEntry RenderedCar(RideCarInstanceRuntimeEntry runtime) {
      var cursor = fixture.Car.SavedCursor with {
        RegistryIndex = runtime.RegistryIndex,
        CarRuntime = runtime,
      };
      return fixture.Car with {
        RegistryIndex = runtime.RegistryIndex,
        CarRuntime = runtime,
        SavedCursor = cursor,
      };
    }

    var runtimeCars = new[] {
      RuntimeCar(0, 40, RideTrainCarRole.Front),
      RuntimeCar(1, 41, RideTrainCarRole.Link),
      RuntimeCar(2, 42, RideTrainCarRole.Rear),
    };
    var renderedCars = new[] {
      RenderedCar(runtimeCars[0]),
      RenderedCar(runtimeCars[2]),
    };
    using var rearModel = new Model(new Mesh(new List<Vertex>(), new List<uint>())) {
      Material = new Flat(),
    };
    var scene = new RideCarStaticSceneBuildResult(
      [fixture.Model, rearModel],
      [
        new RideCarStaticSceneModelBinding(fixture.Model, renderedCars[0], 0, 0),
        new RideCarStaticSceneModelBinding(rearModel, renderedCars[1], 2, 0),
      ],
      SourceCarCount: 3,
      BuiltCarCount: 2,
      SkippedCarCount: 1,
      ModelCount: 2,
      MissingMaterialBatchCount: 0,
      ClonedVertexCount: 0,
      ClonedIndexCount: 0,
      UnresolvedCarResourceCount: 0,
      UnresolvedSavedCursorCount: 0,
      MissingBodyTemplateCount: 0,
      UnavailableModelGeometryCount: 0,
      UnavailableStaticPoseCount: 0);
    IReadOnlyList<RideCarInstanceRuntimeEntry>? suppliedRuntimeCars = null;
    IReadOnlyList<RideCarStaticInstanceEntry>? suppliedRenderedCars = null;
    var operations = new RideTrainSceneMotionControllerOperations(
      authorizedTrain => new(
        RideTrainMotionAdvanceAuthorizationStatus.AuthorizedByNativeOperationalState,
        authorizedTrain,
        13,
        1f),
      (_, _, exactRuntimeCars, exactRenderedCars) => {
        suppliedRuntimeCars = exactRuntimeCars;
        suppliedRenderedCars = exactRenderedCars;
        return new(
          RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal,
          null);
      },
      RideTrainCircuitMotionStepper.Advance,
      (_, _, _) => throw new AssertionException("Circuit rejection must stop planning."),
      (_, _, _) => throw new AssertionException("Circuit rejection must stop scene updates."));

    var controller = RideTrainSceneMotionController.Build(
      [train],
      runtimeCars,
      scene,
      hierarchyScene: null,
      RideTrainSceneMotionControllerLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Entries.Single().Status,
        Is.EqualTo(RideTrainSceneMotionStatus.CircuitNotAuthorized));
      Assert.That(suppliedRuntimeCars, Has.Count.EqualTo(3));
      Assert.That(suppliedRuntimeCars!.Select(car => car.SavedRole),
        Is.EqualTo(new[] {
          RideTrainCarRole.Front,
          RideTrainCarRole.Link,
          RideTrainCarRole.Rear,
        }));
      Assert.That(
        suppliedRuntimeCars.Select((car, index) => ReferenceEquals(car, runtimeCars[index])),
        Is.All.True);
      Assert.That(suppliedRenderedCars, Has.Count.EqualTo(2));
      Assert.That(suppliedRenderedCars!.Select(car => car.RegistryIndex),
        Is.EqualTo(new[] { 0, 2 }));
      Assert.That(
        suppliedRenderedCars.Select((car, index) => ReferenceEquals(car, renderedCars[index])),
        Is.All.True);
    }
  }

  [Test]
  public void Build_AppliesTheExactInitialPlanBeforeTheFirstTick() {
    using var fixture = SceneFixture.Create();

    var controller = fixture.BuildController();

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Entries.Single().MotionState,
        Is.EqualTo(new RideTrainMotionState(10f, 2f, Reversed: false)));
      Assert.That(fixture.Model.Transform.Matrix,
        Is.EqualTo(Matrix4x4.CreateTranslation(10f, 0f, 0f)));
    }
  }

  [Test]
  public void Update_AdvancesAuthorizedStateAndAppliesItsExactCarTarget() {
    using var fixture = SceneFixture.Create();
    var controller = fixture.BuildController();
    var initialState = controller.Entries.Single().MotionState!.Value;
    var initialTransform = fixture.Model.Transform.Matrix;

    var result = controller.Update(TimeSpan.FromSeconds(0.5d));

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Entries.Single().Status,
        Is.EqualTo(RideTrainSceneMotionStatus.Animated));
      Assert.That(controller.Entries.Single().MotionState,
        Is.EqualTo(new RideTrainMotionState(11f, 2f, Reversed: false)));
      Assert.That(initialState.Speed, Is.Not.Zero);
      Assert.That(controller.Entries.Single().MotionState!.Value.Distance,
        Is.Not.EqualTo(initialState.Distance));
      Assert.That(result.AnimatedTrainCount, Is.EqualTo(1));
      Assert.That(result.AnimatedCarCount, Is.EqualTo(1));
      Assert.That(result.UpdatedBodyModelCount, Is.EqualTo(1));
      Assert.That(result.UpdatedHierarchyModelCount, Is.Zero);
      Assert.That(fixture.Model.Transform.Matrix,
        Is.EqualTo(Matrix4x4.CreateTranslation(11f, 0f, 0f)));
      Assert.That(fixture.Model.Transform.Matrix, Is.Not.EqualTo(initialTransform));
    }
  }

  [Test]
  public void Update_PlanningFailureChangesNeitherSceneNorMotionState() {
    using var fixture = SceneFixture.Create();
    var planCalls = 0;
    var controller = fixture.BuildController(plan: (traversal, state, cars) => {
      planCalls++;
      if (planCalls > 1)
        throw new InvalidDataException("injected next-pose failure");
      return fixture.Plan(traversal, state, cars);
    });
    var initialTransform = fixture.Model.Transform.Matrix;
    var initialState = controller.Entries.Single().MotionState;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      controller.Update(TimeSpan.FromSeconds(0.5d))));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("injected next-pose failure"));
      Assert.That(controller.Entries.Single().MotionState, Is.EqualTo(initialState));
      Assert.That(fixture.Model.Transform.Matrix, Is.EqualTo(initialTransform));
    }
  }

  [Test]
  public void Update_ApplyFailureDoesNotCommitPendingMotionState() {
    using var fixture = SceneFixture.Create();
    var applyCalls = 0;
    var controller = fixture.BuildController(apply: (scene, targets, hierarchy) => {
      applyCalls++;
      if (applyCalls > 1)
        throw new InvalidDataException("injected scene transaction failure");
      return RideCarVisualSceneTransformTransaction.Update(scene, targets, hierarchy);
    });
    var initialState = controller.Entries.Single().MotionState;
    var initialTransform = fixture.Model.Transform.Matrix;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      controller.Update(TimeSpan.FromSeconds(0.5d))));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("injected scene transaction failure"));
      Assert.That(controller.Entries.Single().MotionState, Is.EqualTo(initialState));
      Assert.That(fixture.Model.Transform.Matrix, Is.EqualTo(initialTransform));
    }
  }

  [Test]
  public void TryUpdate_ContainsLaterPoseFailureWithoutCommittingState() {
    using var fixture = SceneFixture.Create();
    var planCalls = 0;
    var controller = fixture.BuildController(plan: (traversal, state, cars) => {
      planCalls++;
      if (planCalls > 1)
        throw new InvalidDataException("injected later pose failure");
      return fixture.Plan(traversal, state, cars);
    });
    var initialState = controller.Entries.Single().MotionState;
    var initialTransform = fixture.Model.Transform.Matrix;

    var updated = controller.TryUpdate(
      TimeSpan.FromSeconds(0.5d),
      out var result,
      out var error);

    using (Assert.EnterMultipleScope()) {
      Assert.That(updated, Is.False);
      Assert.That(result, Is.EqualTo(default(RideTrainSceneMotionUpdateResult)));
      Assert.That(error, Is.TypeOf<InvalidDataException>());
      Assert.That(error!.Message, Does.Contain("injected later pose failure"));
      Assert.That(controller.Entries.Single().MotionState, Is.EqualTo(initialState));
      Assert.That(fixture.Model.Transform.Matrix, Is.EqualTo(initialTransform));
    }
  }

  [Test]
  public void Build_FiltersHierarchyOnlyCarsFromTheMotionTransaction() {
    using var fixture = SceneFixture.Create();
    var hierarchyScene = fixture.CreateHierarchyOnlyScene(out var hierarchyModel);
    using (hierarchyModel) {
      var appliedHierarchyScenes = new List<RideCarVisualHierarchySceneBuildResult?>();
      var controller = fixture.BuildController(
        hierarchyScene: hierarchyScene,
        apply: (scene, targets, hierarchy) => {
          appliedHierarchyScenes.Add(hierarchy);
          return RideCarVisualSceneTransformTransaction.Update(scene, targets, hierarchy);
        });

      var updated = controller.TryUpdate(
        TimeSpan.FromSeconds(0.5d),
        out _,
        out var error);

      using (Assert.EnterMultipleScope()) {
        Assert.That(updated, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(appliedHierarchyScenes, Has.Count.EqualTo(2));
        Assert.That(appliedHierarchyScenes, Has.All.Null);
        Assert.That(hierarchyModel.Transform.Matrix, Is.EqualTo(Matrix4x4.Identity));
      }
    }
  }

  [Test]
  public void Build_RejectsForeignHierarchyCarWithMatchingScalarIdentity() {
    using var fixture = SceneFixture.Create();
    var hierarchyScene = fixture.CreateHierarchyOnlyScene(
      out var hierarchyModel,
      registryIndex: 0);
    using (hierarchyModel) {
      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        fixture.BuildController(hierarchyScene: hierarchyScene)));

      using (Assert.EnterMultipleScope()) {
        Assert.That(error!.Message, Does.Contain("exact selected-car identity"));
        Assert.That(fixture.Model.Transform.Matrix, Is.EqualTo(Matrix4x4.Identity));
        Assert.That(hierarchyModel.Transform.Matrix, Is.EqualTo(Matrix4x4.Identity));
      }
    }
  }

  [Test]
  public void Build_LeavesAuthorizedTrainStaticWithoutOneExactCircuit() {
    using var fixture = SceneFixture.Create(hasCircuit: false);
    var planCalls = 0;
    var controller = fixture.BuildController(plan: (traversal, state, cars) => {
      planCalls++;
      return fixture.Plan(traversal, state, cars);
    });

    var result = controller.Update(TimeSpan.FromSeconds(1d));

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Entries.Single().Status,
        Is.EqualTo(RideTrainSceneMotionStatus.CircuitNotAuthorized));
      Assert.That(controller.AnimatedTrainCount, Is.Zero);
      Assert.That(controller.AnimatedCarCount, Is.Zero);
      Assert.That(planCalls, Is.Zero);
      Assert.That(result.UpdatedBodyModelCount, Is.Zero);
      Assert.That(fixture.Model.Transform.Matrix, Is.EqualTo(Matrix4x4.Identity));
    }
  }

  [Test]
  public void Build_UsesExactSavedCursorSelectedSegmentTraversal() {
    using var fixture = SceneFixture.Create(
      hasCircuit: false,
      useSegmentCircuit: true);
    var suppliedTraversals = new List<TrackCircuitTraversal>();

    var controller = fixture.BuildController(plan: (traversal, state, cars) => {
      suppliedTraversals.Add(traversal);
      return fixture.Plan(traversal, state, cars);
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Entries.Single().Status,
        Is.EqualTo(RideTrainSceneMotionStatus.Animated));
      Assert.That(controller.Entries.Single().CircuitAuthorization.Traversal,
        Is.SameAs(fixture.Traversal));
      Assert.That(controller.Entries.Single().Traversal, Is.SameAs(fixture.Traversal));
      Assert.That(suppliedTraversals, Is.EqualTo(new[] { fixture.Traversal }));
    }
  }

  private sealed class SceneFixture : IDisposable {
    public RideInstanceTrainRuntimeEntry Train { get; }
    public RideCarStaticSceneBuildResult Scene { get; }
    public Model Model { get; }
    public RideCarStaticInstanceEntry Car => car;
    public TrackCircuitTraversal Traversal => traversal;

    private readonly RideCarStaticInstanceEntry car;
    private readonly TrackCircuitTraversal traversal;

    private SceneFixture(
      RideInstanceTrainRuntimeEntry train,
      RideCarStaticSceneBuildResult scene,
      Model model,
      RideCarStaticInstanceEntry car,
      TrackCircuitTraversal traversal
    ) {
      Train = train;
      Scene = scene;
      Model = model;
      this.car = car;
      this.traversal = traversal;
    }

    public static SceneFixture Create(
      bool hasCircuit = true,
      bool useSegmentCircuit = false
    ) {
      const ulong rideId = 10;
      const ulong trackId = 20;
      const ulong trainId = 30;
      const ulong carId = 40;
      var traversal = new TrackCircuitTraversal(LongStadium());
      var ride = new DatTrackedRideInstanceData(
        rideId,
        "Test Ride",
        trackId,
        "test-ride",
        "test-ride:trr",
        1,
        1,
        0,
        [trainId]);
      var savedTrain = new DatRideTrainInstanceData(
        trainId,
        "test-train",
        "test-train:rit",
        rideId,
        0,
        4f,
        1_000f,
        distance: 10f,
        reversed: false,
        speed: 2f,
        cars: [carId],
        state: 13,
        stateTime: 1f);
      var trainLink = new RideTrainInstanceResourceLink(ride, savedTrain, 0, null);
      var trackRuntime = new RideInstanceTrackRuntimeEntry(
        0,
        0,
        null!,
        null!,
        GraphTraversal: null,
        hasCircuit && !useSegmentCircuit ? traversal : null) {
        SegmentCircuitTraversals = useSegmentCircuit
          ? Array.AsReadOnly(new[] {
            new RideTrackSegmentCircuitTraversal(
              new RideTrackSegmentCircuit(
                50,
                Array.AsReadOnly(new ulong[] { 60 }),
                traversal.Circuit),
              traversal),
          })
          : Array.Empty<RideTrackSegmentCircuitTraversal>(),
      };
      var train = new RideInstanceTrainRuntimeEntry(0, trackRuntime, trainLink);
      var savedCar = new DatRideCarInstanceData(
        carId,
        trainId,
        whichCar: 0,
        whichRideTrainCar: Convert.ToInt32(RideTrainCarRole.Front),
        frontWheelDistance: 0f,
        rearWheelDistance: 0f,
        trackPiece: 0,
        rearTrackPiece: 0,
        distance: 0f,
        reversed: false,
        speed: 0f,
        length: 4f,
        mass: 100f,
        positionValid: true);
      var missingPiece = new RideCarTrackPieceRuntimeLink(
        0,
        RideCarTrackPieceRuntimeStatus.MissingSavedReference,
        null,
        null);
      var runtime = new RideCarInstanceRuntimeEntry(
        0,
        0,
        train,
        savedCar,
        RideTrainCarRole.Front,
        missingPiece,
        missingPiece,
        RideCarResourceRuntimeStatus.UnresolvedTrainResource,
        CarResource: null,
        TrainConsist: null,
        ConsistCar: null);
      var savedContactCursor = traversal.Start;
      var contact = new RideCarSavedWheelContactCursor(
        RideCarSavedWheelCursorStatus.Resolved,
        0f,
        0d,
        0,
        null,
        RideCarSavedWheelSplineStart.Equivalent,
        savedContactCursor,
        savedContactCursor.Sample());
      var cursor = new RideCarSavedWheelCursorEntry(0, runtime, contact, contact);
      var geometry = Geometry();
      var batch = new StaticShapeMeshBatch(
        0,
        "body",
        SupportType: 0,
        FtxRef: null,
        TxsRef: null,
        Transparency: 0,
        TextureFlags: 0,
        Sides: 0,
        Mesh: null!);
      var template = new RideCarVisualMeshTemplate(
        Link: null!,
        Lod: null!,
        RideCarVisualTemplateShapeKind.BoneShape,
        StaticShape: null,
        BoneShape: null,
        [batch]);
      var car = new RideCarStaticInstanceEntry(
        0,
        runtime,
        cursor,
        RideCarStaticInstanceIssue.None,
        template,
        geometry,
        Pose: null,
        GeometryUnavailableDetail: null,
        StaticPoseUnavailableDetail: null);
      var model = new Model(new Mesh(new List<Vertex>(), new List<uint>())) {
        Material = new Flat(),
      };
      model.Transform.Matrix = Matrix4x4.Identity;
      var binding = new RideCarStaticSceneModelBinding(model, car, 0, 0);
      var scene = new RideCarStaticSceneBuildResult(
        [model],
        [binding],
        SourceCarCount: 1,
        BuiltCarCount: 1,
        SkippedCarCount: 0,
        ModelCount: 1,
        MissingMaterialBatchCount: 0,
        ClonedVertexCount: 0,
        ClonedIndexCount: 0,
        UnresolvedCarResourceCount: 0,
        UnresolvedSavedCursorCount: 0,
        MissingBodyTemplateCount: 0,
        UnavailableModelGeometryCount: 0,
        UnavailableStaticPoseCount: 0);
      return new(train, scene, model, car, traversal);
    }

    public RideTrainSceneMotionController BuildController(
      Func<
        TrackCircuitTraversal,
        RideTrainMotionState,
        IReadOnlyList<RideTrainOrdinaryScenePoseCarInput>,
        RideTrainOrdinaryScenePosePlan>? plan = null,
      Func<
        RideCarStaticSceneBuildResult,
        IReadOnlyList<RideCarSceneTransformTarget>,
        RideCarVisualHierarchySceneBuildResult?,
        RideCarVisualSceneTransformTransactionResult>? apply = null,
      RideCarVisualHierarchySceneBuildResult? hierarchyScene = null
    ) {
      var operations = new RideTrainSceneMotionControllerOperations(
        train => new(
          RideTrainMotionAdvanceAuthorizationStatus.AuthorizedByNativeOperationalState,
          train,
          13,
          1f),
        (train, track, runtimeCars, renderedCars) =>
          (track.CircuitTraversal ??
           track.SegmentCircuitTraversals.SingleOrDefault()?.Traversal) == null
          ? new(
            RideTrainCircuitMotionAuthorizationStatus.UnresolvedCircuitTraversal,
            null)
          : new(
            RideTrainCircuitMotionAuthorizationStatus.AuthorizedByReciprocalCircuit,
            track.CircuitTraversal ??
              track.SegmentCircuitTraversals.Single().Traversal),
        RideTrainCircuitMotionStepper.Advance,
        plan ?? Plan,
        apply ?? RideCarVisualSceneTransformTransaction.Update);
      return RideTrainSceneMotionController.Build(
        [Train],
        Scene,
        hierarchyScene,
        RideTrainSceneMotionControllerLimits.Default,
        operations);
    }

    public RideCarVisualHierarchySceneBuildResult CreateHierarchyOnlyScene(
      out Model model,
      int registryIndex = 1
    ) {
      var savedCar = new RideCarVariantStaticInstanceEntry(
        registryIndex,
        car.CarRuntime,
        car.SavedCursor,
        VisualSelection: null!,
        VisualTemplate: null!,
        Issues: RideCarVariantStaticInstanceIssue.None,
        Geometry: null,
        Pose: null,
        GeometryUnavailableDetail: null,
        StaticPoseUnavailableDetail: null);
      var instance = new RideCarVisualHierarchyStaticPartInstance(
        0,
        savedCar,
        Hierarchy: null!,
        Role: RideVisualRole.FrontAxle,
        Type: 0,
        Part: null!,
        Visual: null!,
        Template: null!,
        MaterialBatches: [],
        WorldTransform: Matrix4x4.Identity);
      model = new Model(new Mesh(new List<Vertex>(), new List<uint>())) {
        Material = new Flat(),
      };
      var binding = new RideCarVisualHierarchySceneModelBinding(model, instance, 0, 0);
      return new(
        [model],
        [binding],
        SourceCarCount: 2,
        PlannedCarCount: 1,
        SourcePartCount: 1,
        BuiltPartCount: 1,
        SkippedPartCount: 0,
        ModelCount: 1,
        MissingMaterialBatchCount: 0,
        UpstreamIssueCount: 0,
        TemplateUnavailableCount: 0,
        ClonedVertexCount: 0,
        ClonedIndexCount: 0);
    }

    public RideTrainOrdinaryScenePosePlan Plan(
      TrackCircuitTraversal suppliedTraversal,
      RideTrainMotionState state,
      IReadOnlyList<RideTrainOrdinaryScenePoseCarInput> cars
    ) {
      Assert.That(suppliedTraversal, Is.SameAs(traversal));
      Assert.That(cars.Single().StaticEntry, Is.SameAs(car));
      var target = new RideCarSceneTransformTarget(
        car.RegistryIndex,
        car.CarInstanceEntryId,
        Matrix4x4.CreateTranslation(state.Distance, 0f, 0f));
      return new(
        suppliedTraversal,
        state,
        new(RideTrainNativeLengthStatus.Resolved, 1, null, 4f),
        new([state.Distance]),
        [],
        [target]);
    }

    public void Dispose() => Model.Dispose();

    private static RideCarLongitudinalGeometry Geometry() => new(
      new(2f, 0f, 0f),
      new(-2f, 0f, 0f),
      new(1f, 0f, 0f),
      new(-1f, 0f, 0f),
      Vector3.UnitX,
      4f,
      1f,
      -1f,
      2f,
      2f,
      2f);

    private static TrackCircuit LongStadium() {
      const float tangentScale = 3f;
      return new TrackCircuit([
        CircuitPiece("bottom", new(-5f, 0f, 0f), new(5f, 0f, 0f),
          Vector3.UnitX * tangentScale, Vector3.UnitX * tangentScale),
        CircuitPiece("lower-right", new(5f, 0f, 0f), new(7f, 0f, 2f),
          Vector3.UnitX * tangentScale, Vector3.UnitZ * tangentScale),
        CircuitPiece("right", new(7f, 0f, 2f), new(7f, 0f, 8f),
          Vector3.UnitZ * tangentScale, Vector3.UnitZ * tangentScale),
        CircuitPiece("upper-right", new(7f, 0f, 8f), new(5f, 0f, 10f),
          Vector3.UnitZ * tangentScale, -Vector3.UnitX * tangentScale),
        CircuitPiece("top", new(5f, 0f, 10f), new(-5f, 0f, 10f),
          -Vector3.UnitX * tangentScale, -Vector3.UnitX * tangentScale),
        CircuitPiece("upper-left", new(-5f, 0f, 10f), new(-7f, 0f, 8f),
          -Vector3.UnitX * tangentScale, -Vector3.UnitZ * tangentScale),
        CircuitPiece("left", new(-7f, 0f, 8f), new(-7f, 0f, 2f),
          -Vector3.UnitZ * tangentScale, -Vector3.UnitZ * tangentScale),
        CircuitPiece("lower-left", new(-7f, 0f, 2f), new(-5f, 0f, 0f),
          -Vector3.UnitZ * tangentScale, Vector3.UnitX * tangentScale),
      ]);
    }

    private static TrackCircuitPiece CircuitPiece(
      string id,
      Vector3 start,
      Vector3 end,
      Vector3 startTangent,
      Vector3 endTangent
    ) {
      var gauge = Vector3.UnitY * 0.5f;
      return new(id, new TrackPiece(TrackPieceGeometry.FromHandAuthored([
        new RailControlPair(
          0f,
          start - gauge,
          startTangent,
          start + gauge,
          startTangent,
          0f),
        new RailControlPair(
          1f,
          end - gauge,
          endTangent,
          end + gauge,
          endTangent,
          0f),
      ])));
    }
  }
}
