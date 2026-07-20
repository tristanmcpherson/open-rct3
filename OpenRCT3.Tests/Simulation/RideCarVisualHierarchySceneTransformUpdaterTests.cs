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
    return new((IDisposable)owner, source, hierarchy, scene);
  }

  private static object Property(Type type, object owner, string name) =>
    type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(owner)!;

  private sealed record SceneFixture(
    IDisposable Owner,
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
