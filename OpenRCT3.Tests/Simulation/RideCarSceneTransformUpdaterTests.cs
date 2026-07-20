// Ride Car Scene Transform Updater Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarSceneTransformUpdaterTests {
  [Test]
  public void Update_AppliesEachExactCarTransformToEveryMaterialBatch() {
    using var fixture = SceneFixture.Create();
    var firstTarget = Matrix4x4.CreateRotationZ(0.25f) *
      Matrix4x4.CreateTranslation(10f, 20f, 30f);
    var secondTarget = Matrix4x4.CreateRotationY(-0.5f) *
      Matrix4x4.CreateTranslation(40f, 50f, 60f);

    var result = RideCarSceneTransformUpdater.Update(
      fixture.Scene,
      [
        new(2, 303, secondTarget),
        new(0, 101, firstTarget),
      ]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.UpdatedCarCount, Is.EqualTo(2));
      Assert.That(result.UpdatedModelCount, Is.EqualTo(3));
      Assert.That(fixture.Models[0].Transform.Matrix, Is.EqualTo(firstTarget));
      Assert.That(fixture.Models[1].Transform.Matrix, Is.EqualTo(firstTarget));
      Assert.That(fixture.Models[2].Transform.Matrix, Is.EqualTo(secondTarget));
      Assert.That(fixture.Scene.ModelBindings.Select(binding => binding.RegistryIndex),
        Is.EqualTo([0, 0, 2]));
      Assert.That(fixture.Scene.ModelBindings.Select(binding => binding.MaterialBatchIndex),
        Is.EqualTo([0, 1, 0]));
      Assert.That(fixture.Models.All(model => model.Mesh.State != State.Disposed), Is.True);
      Assert.That(fixture.Models.All(model => model.Material!.State != State.Disposed),
        Is.True);
    }
  }

  [Test]
  public void Update_RejectsNonFiniteTargetBeforeMutatingAnyModel() {
    using var fixture = SceneFixture.Create();
    var invalid = Matrix4x4.Identity with { M43 = float.NaN };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(
        fixture.Scene,
        [
          new(0, 101, Matrix4x4.CreateTranslation(100f, 0f, 0f)),
          new(2, 303, invalid),
        ])));

    AssertUnchanged(fixture, error, "non-finite transform");
  }

  [Test]
  public void Update_RejectsDuplicateMissingAndExtraTargetsBeforeMutation() {
    AssertRejected(
      [
        new(0, 101, Matrix4x4.Identity),
        new(0, 404, Matrix4x4.Identity),
        new(2, 303, Matrix4x4.Identity),
      ],
      "registry index 0 is duplicated");
    AssertRejected(
      [new(0, 101, Matrix4x4.Identity)],
      "bound car 2 has no target transform");
    AssertRejected(
      [
        new(0, 101, Matrix4x4.Identity),
        new(1, 202, Matrix4x4.Identity),
        new(2, 303, Matrix4x4.Identity),
      ],
      "target car 1 has no model binding");
  }

  [Test]
  public void Update_RejectsChangedSavedCarIdentityBeforeMutation() {
    AssertRejected(
      [
        new(0, 999, Matrix4x4.Identity),
        new(2, 303, Matrix4x4.Identity),
      ],
      "car 0 changed exact saved-car entry identity");
  }

  [Test]
  public void Update_RejectsChangedBindingIdentityBeforeMutation() {
    using var fixture = SceneFixture.Create();
    var bindings = fixture.Scene.ModelBindings.ToArray();
    bindings[1] = bindings[1] with { Entry = Entry(0, 404) };
    var changed = fixture.Scene with { ModelBindings = bindings };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(
        changed,
        [
          new(0, 101, Matrix4x4.Identity),
          new(2, 303, Matrix4x4.Identity),
        ])));

    AssertUnchanged(fixture, error, "car 0 changed exact saved-car identity");
  }

  [Test]
  public void Update_RejectsOutOfRangeMaterialBatchBeforeMutation() {
    using var fixture = SceneFixture.Create();
    var bindings = fixture.Scene.ModelBindings.ToArray();
    bindings[1] = bindings[1] with {
      MaterialBatchIndex = bindings[1].Entry.MaterialBatches!.Count,
    };
    var changed = fixture.Scene with { ModelBindings = bindings };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(
        changed,
        [
          new(0, 101, Matrix4x4.Identity),
          new(2, 303, Matrix4x4.Identity),
        ])));

    AssertUnchanged(fixture, error, "duplicate or invalid material batch");
  }

  [Test]
  public void Update_RejectsBoundsBeforeMutation() {
    using var fixture = SceneFixture.Create();
    var targets = new RideCarSceneTransformTarget[] {
      new(0, 101, Matrix4x4.Identity),
      new(2, 303, Matrix4x4.Identity),
    };

    var carError = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(
        fixture.Scene,
        targets,
        new(MaximumCarCount: 1, MaximumModelCount: 3))));
    var modelError = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(
        fixture.Scene,
        targets,
        new(MaximumCarCount: 2, MaximumModelCount: 2))));

    using (Assert.EnterMultipleScope()) {
      Assert.That(carError!.Message, Does.Contain("car target count exceeds the limit 1"));
      Assert.That(modelError!.Message, Does.Contain("model count exceeds the limit 2"));
      Assert.That(fixture.Models.Select(model => model.Transform.Matrix),
        Is.EqualTo(fixture.InitialTransforms));
    }
  }

  private static void AssertRejected(
    IReadOnlyList<RideCarSceneTransformTarget> targets,
    string expectedMessage
  ) {
    using var fixture = SceneFixture.Create();
    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSceneTransformUpdater.Update(fixture.Scene, targets)));
    AssertUnchanged(fixture, error, expectedMessage);
  }

  private static void AssertUnchanged(
    SceneFixture fixture,
    Exception? error,
    string expectedMessage
  ) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(error, Is.Not.Null);
      Assert.That(error!.Message, Does.Contain(expectedMessage));
      Assert.That(fixture.Models.Select(model => model.Transform.Matrix),
        Is.EqualTo(fixture.InitialTransforms));
      Assert.That(fixture.Models.All(model => model.Mesh.State != State.Disposed), Is.True);
      Assert.That(fixture.Models.All(model => model.Material!.State != State.Disposed),
        Is.True);
    }
  }

  private static RideCarStaticInstanceEntry Entry(
    int registryIndex,
    ulong entryId,
    int materialBatchCount = 2
  ) {
    var saved = new DatRideCarInstanceData(
      entryId,
      rideTrainInstance: 1,
      whichCar: registryIndex,
      whichRideTrainCar: 0,
      trackPiece: 0,
      rearTrackPiece: 0,
      distance: 0f,
      reversed: false,
      speed: 0f);
    var runtime = new RideCarInstanceRuntimeEntry(
      registryIndex,
      registryIndex,
      TrainRuntime: null!,
      saved,
      SavedRole: default,
      TrackPiece: null!,
      RearTrackPiece: null!,
      RideCarResourceRuntimeStatus.UnresolvedTrainResource,
      CarResource: null,
      TrainConsist: null,
      ConsistCar: null);
    var batches = Enumerable.Range(0, materialBatchCount)
      .Select(index => new StaticShapeMeshBatch(
        index,
        $"template-{index}",
        SupportType: 0,
        FtxRef: null,
        TxsRef: null,
        Transparency: 0,
        TextureFlags: 0,
        Sides: 0,
        Mesh: null!))
      .ToArray();
    var template = new RideCarVisualMeshTemplate(
      Link: null!,
      Lod: null!,
      RideCarVisualTemplateShapeKind.BoneShape,
      StaticShape: null,
      BoneShape: null,
      batches);
    return new(
      registryIndex,
      runtime,
      SavedCursor: null!,
      RideCarStaticInstanceIssue.None,
      BodyTemplate: template,
      Geometry: null,
      Pose: null,
      GeometryUnavailableDetail: null,
      StaticPoseUnavailableDetail: null);
  }

  private sealed class SceneFixture : IDisposable {
    public RideCarStaticSceneBuildResult Scene { get; }
    public Model[] Models { get; }
    public Matrix4x4[] InitialTransforms { get; }

    private SceneFixture(
      RideCarStaticSceneBuildResult scene,
      Model[] models,
      Matrix4x4[] initialTransforms
    ) {
      Scene = scene;
      Models = models;
      InitialTransforms = initialTransforms;
    }

    public static SceneFixture Create() {
      var first = Entry(0, 101);
      var second = Entry(2, 303);
      var initialTransforms = new[] {
        Matrix4x4.CreateTranslation(1f, 2f, 3f),
        Matrix4x4.CreateTranslation(4f, 5f, 6f),
        Matrix4x4.CreateTranslation(7f, 8f, 9f),
      };
      var models = initialTransforms.Select(Model).ToArray();
      var bindings = new RideCarStaticSceneModelBinding[] {
        new(models[0], first, 0, 0),
        new(models[1], first, 0, 1),
        new(models[2], second, 2, 0),
      };
      var scene = new RideCarStaticSceneBuildResult(
        models,
        bindings,
        SourceCarCount: 3,
        BuiltCarCount: 2,
        SkippedCarCount: 1,
        ModelCount: 3,
        MissingMaterialBatchCount: 0,
        ClonedVertexCount: 0,
        ClonedIndexCount: 0,
        UnresolvedCarResourceCount: 0,
        UnresolvedSavedCursorCount: 0,
        MissingBodyTemplateCount: 0,
        UnavailableModelGeometryCount: 0,
        UnavailableStaticPoseCount: 0);
      return new(scene, models, initialTransforms);
    }

    public void Dispose() {
      foreach (var model in Models.Reverse()) model.Dispose();
    }

    private static Model Model(Matrix4x4 transform) {
      var model = new Model(new Mesh(new List<Vertex>(), new List<uint>())) {
        Material = new Flat(),
      };
      model.Transform.Matrix = transform;
      return model;
    }
  }
}
