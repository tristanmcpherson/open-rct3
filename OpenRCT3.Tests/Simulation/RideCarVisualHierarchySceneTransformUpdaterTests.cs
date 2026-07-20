// Ride Car Visual Hierarchy Scene Transform Updater Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenRCT3.Simulation;
using System.Numerics;
using System.Reflection;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualHierarchySceneTransformUpdaterTests {
  [Test]
  public void Update_ReplansEveryPartAndAcceptsTheOrderedBodyTargetSet() {
    using var fixture = Fixture();
    var widenedScene = fixture.Scene with { SourceCarCount = 2 };
    var bodyTransform = Matrix4x4.CreateRotationZ(0.31f) *
      Matrix4x4.CreateTranslation(20f, 30f, 40f);
    var targets = new[] {
      new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        bodyTransform),
      new RideCarSceneTransformTarget(1, 999, Matrix4x4.Identity),
    };
    var meshes = fixture.Scene.Models.Select(model => model.Mesh).ToArray();
    var materials = fixture.Scene.Models.Select(model => model.Material).ToArray();

    var result = RideCarVisualHierarchySceneTransformUpdater.Update(widenedScene, targets);
    var expected = RideCarVisualHierarchyPosePlanner.Resolve(
      bodyTransform,
      fixture.Hierarchy);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.TargetCarCount, Is.EqualTo(2));
      Assert.That(result.UpdatedCarCount, Is.EqualTo(1));
      Assert.That(result.UpdatedPartCount, Is.EqualTo(2));
      Assert.That(result.UpdatedModelCount, Is.EqualTo(2));
      Assert.That(result.UnusedTargetCount, Is.EqualTo(1));
      Assert.That(fixture.Scene.Models.Select(model => model.Mesh), Is.EqualTo(meshes));
      Assert.That(fixture.Scene.Models.Select(model => model.Material), Is.EqualTo(materials));
    }
    foreach (var binding in fixture.Scene.ModelBindings) {
      var pose = expected.Parts.Single(candidate =>
        ReferenceEquals(candidate.Part, binding.Instance.Part));
      Assert.That(binding.Model.Transform.Matrix, Is.EqualTo(pose.WorldTransform));
    }
  }

  [Test]
  public void Update_InvalidLateBindingLeavesEveryModelTransformUnchanged() {
    using var fixture = Fixture();
    var before = fixture.Scene.Models.Select(model => model.Transform.Matrix).ToArray();
    var bindings = fixture.Scene.ModelBindings.ToArray();
    bindings[1] = bindings[1] with {
      PartRegistryIndex = bindings[0].PartRegistryIndex,
    };
    var malformed = fixture.Scene with {
      ModelBindings = Array.AsReadOnly(bindings),
    };
    var target = new RideCarSceneTransformTarget(
      0,
      fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
      Matrix4x4.CreateTranslation(90f, 80f, 70f));

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchySceneTransformUpdater.Update(malformed, [target])));

    Assert.That(
      fixture.Scene.Models.Select(model => model.Transform.Matrix),
      Is.EqualTo(before));
  }

  [Test]
  public void Transaction_AppliesBodyAndHierarchyOnlyAfterBothPreflightsSucceed() {
    using var fixture = Fixture();
    var bodyScene = BuildBodyScene(fixture.Cars);
    try {
      var bodyTransform = Matrix4x4.CreateRotationY(0.41f) *
        Matrix4x4.CreateTranslation(60f, 70f, 80f);
      var target = new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        bodyTransform);

      var result = RideCarVisualSceneTransformTransaction.Update(
        bodyScene,
        [target],
        fixture.Scene);
      var expectedHierarchy = RideCarVisualHierarchyPosePlanner.Resolve(
        bodyTransform,
        fixture.Hierarchy);

      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Body.UpdatedCarCount, Is.EqualTo(1));
        Assert.That(result.Body.UpdatedModelCount, Is.EqualTo(bodyScene.ModelCount));
        Assert.That(result.Hierarchy, Is.Not.Null);
        Assert.That(result.Hierarchy!.Value.UpdatedCarCount, Is.EqualTo(1));
        Assert.That(result.Hierarchy.Value.UpdatedPartCount, Is.EqualTo(2));
        Assert.That(bodyScene.Models.Select(model => model.Transform.Matrix),
          Is.All.EqualTo(bodyTransform));
      }
      foreach (var binding in fixture.Scene.ModelBindings) {
        var expected = expectedHierarchy.Parts.Single(pose =>
          ReferenceEquals(pose.Part, binding.Instance.Part));
        Assert.That(binding.Model.Transform.Matrix, Is.EqualTo(expected.WorldTransform));
      }
    } finally {
      DisposeModels(bodyScene.Models);
    }
  }

  [Test]
  public void Transaction_InvalidHierarchyLeavesBodyAndHierarchyTransformsUnchanged() {
    using var fixture = Fixture();
    var bodyScene = BuildBodyScene(fixture.Cars);
    try {
      var bodyBefore = bodyScene.Models.Select(model => model.Transform.Matrix).ToArray();
      var hierarchyBefore = fixture.Scene.Models
        .Select(model => model.Transform.Matrix)
        .ToArray();
      var bindings = fixture.Scene.ModelBindings.ToArray();
      bindings[1] = bindings[1] with {
        PartRegistryIndex = bindings[0].PartRegistryIndex,
      };
      var malformed = fixture.Scene with {
        ModelBindings = Array.AsReadOnly(bindings),
      };
      var target = new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        Matrix4x4.CreateTranslation(90f, 80f, 70f));

      Assert.Throws<InvalidDataException>(new Action(() =>
        RideCarVisualSceneTransformTransaction.Update(bodyScene, [target], malformed)));

      using (Assert.EnterMultipleScope()) {
        Assert.That(bodyScene.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(bodyBefore));
        Assert.That(fixture.Scene.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(hierarchyBefore));
      }
    } finally {
      DisposeModels(bodyScene.Models);
    }
  }

  [Test]
  public void Transaction_SharedModelAcrossLayersIsRejectedWithoutMutation() {
    using var fixture = Fixture();
    var bodyScene = BuildBodyScene(fixture.Cars);
    try {
      var hierarchyModels = fixture.Scene.Models.ToArray();
      var hierarchyBindings = fixture.Scene.ModelBindings.ToArray();
      hierarchyModels[0] = bodyScene.Models[0];
      hierarchyBindings[0] = hierarchyBindings[0] with {
        Model = bodyScene.Models[0],
      };
      var aliased = fixture.Scene with {
        Models = Array.AsReadOnly(hierarchyModels),
        ModelBindings = Array.AsReadOnly(hierarchyBindings),
      };
      var bodyBefore = bodyScene.Models.Select(model => model.Transform.Matrix).ToArray();
      var hierarchyBefore = aliased.Models
        .Select(model => model.Transform.Matrix)
        .ToArray();
      var target = new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        Matrix4x4.CreateTranslation(91f, 81f, 71f));

      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        RideCarVisualSceneTransformTransaction.Update(bodyScene, [target], aliased)));

      using (Assert.EnterMultipleScope()) {
        Assert.That(error!.Message, Does.Contain("reuse a model identity"));
        Assert.That(bodyScene.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(bodyBefore));
        Assert.That(aliased.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(hierarchyBefore));
      }
    } finally {
      DisposeModels(bodyScene.Models);
    }
  }

  [Test]
  public void Transaction_SharedTransformAcrossDistinctModelsIsRejectedWithoutMutation() {
    using var fixture = Fixture();
    var bodyScene = BuildBodyScene(fixture.Cars);
    try {
      var bodyModel = bodyScene.Models[0];
      var hierarchyModel = fixture.Scene.Models[0];
      Assert.That(hierarchyModel, Is.Not.SameAs(bodyModel));
      hierarchyModel.Transform = bodyModel.Transform;
      var bodyBefore = bodyScene.Models.Select(model => model.Transform.Matrix).ToArray();
      var hierarchyBefore = fixture.Scene.Models
        .Select(model => model.Transform.Matrix)
        .ToArray();
      var target = new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        Matrix4x4.CreateTranslation(92f, 82f, 72f));

      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        RideCarVisualSceneTransformTransaction.Update(
          bodyScene,
          [target],
          fixture.Scene)));

      using (Assert.EnterMultipleScope()) {
        Assert.That(error!.Message, Does.Contain("reuse a mutable transform identity"));
        Assert.That(bodyScene.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(bodyBefore));
        Assert.That(fixture.Scene.Models.Select(model => model.Transform.Matrix),
          Is.EqualTo(hierarchyBefore));
      }
    } finally {
      DisposeModels(bodyScene.Models);
    }
  }

  [Test]
  public void Transaction_WithoutHierarchyAppliesThePreparedBodyUpdate() {
    using var fixture = Fixture();
    var bodyScene = BuildBodyScene(fixture.Cars);
    try {
      var bodyTransform = Matrix4x4.CreateTranslation(12f, 34f, 56f);
      var target = new RideCarSceneTransformTarget(
        0,
        fixture.Source.Instances[0].SavedCar.CarInstanceEntryId,
        bodyTransform);

      var result = RideCarVisualSceneTransformTransaction.Update(bodyScene, [target]);

      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Body.UpdatedCarCount, Is.EqualTo(1));
        Assert.That(result.Hierarchy, Is.Null);
        Assert.That(bodyScene.Models.Select(model => model.Transform.Matrix),
          Is.All.EqualTo(bodyTransform));
      }
    } finally {
      DisposeModels(bodyScene.Models);
    }
  }

  private static SceneFixture Fixture() {
    var method = typeof(RideCarVisualHierarchyStaticInstanceRegistryTests).GetMethod(
      "Fixture",
      BindingFlags.Static | BindingFlags.NonPublic);
    Assert.That(method, Is.Not.Null);
    var owner = method!.Invoke(null, [false, true, false]);
    Assert.That(owner, Is.Not.Null);
    var type = owner!.GetType();
    var cars = (RideCarVariantStaticInstanceRegistry)Property(type, owner, "Cars");
    var hierarchy = (RideCarVisualHierarchyResolution)Property(
      type,
      owner,
      "Hierarchy");
    var templates = (RideCarVisualTemplateRegistry)Property(type, owner, "Templates");
    var hierarchies = new RideCarVisualHierarchyRegistry([hierarchy], 2, 0, true, 0);
    var source = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      cars,
      hierarchies,
      templates);
    var scene = RideCarVisualHierarchySceneBuilder.Build(
      source,
      (_, _) => new Flat());
    return new((IDisposable)owner, cars, source, hierarchy, scene);
  }

  private static RideCarStaticSceneBuildResult BuildBodyScene(
    RideCarVariantStaticInstanceRegistry cars
  ) => RideCarStaticSceneBuilder.Build(cars, (_, _) => new Flat());

  private static void DisposeModels(IEnumerable<OpenCobra.GDK.Model> models) {
    foreach (var model in models.Reverse()) model.Dispose();
  }

  private static object Property(Type type, object owner, string name) =>
    type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(owner)!;

  private sealed record SceneFixture(
    IDisposable Owner,
    RideCarVariantStaticInstanceRegistry Cars,
    RideCarVisualHierarchyStaticInstanceRegistry Source,
    RideCarVisualHierarchyResolution Hierarchy,
    RideCarVisualHierarchySceneBuildResult Scene
  ) : IDisposable {
    public void Dispose() {
      foreach (var model in Scene.Models) model.Dispose();
      Owner.Dispose();
    }
  }
}
