// Ride Car Visual Hierarchy Static Instance Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;
using System.Reflection;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualHierarchyStaticInstanceRegistryTests {
  [TestCase(false)]
  [TestCase(true)]
  public void Build_ComposesExactNormalAndWildPartInstances(bool wild) {
    using var fixture = Fixture(wild);

    var result = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      fixture.Cars,
      Hierarchies(fixture.Hierarchy),
      fixture.Templates);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.SourceCarCount, Is.EqualTo(1));
      Assert.That(result.EligibleCarCount, Is.EqualTo(1));
      Assert.That(result.PlannedCarCount, Is.EqualTo(1));
      Assert.That(result.Instances.Select(instance => instance.Role), Is.EqualTo(new[] {
        RideVisualRole.FrontAxle,
        RideVisualRole.FrontRightWheel,
      }));
      Assert.That(result.Instances.Select(instance => instance.Type),
        Is.EqualTo(new uint[] { 71, 72 }));
      Assert.That(result.Instances.All(instance =>
        ReferenceEquals(instance.SavedCar, fixture.Car)), Is.True);
      Assert.That(result.Instances[0].Template, Is.SameAs(fixture.PartTemplates[0]));
      Assert.That(result.Instances[1].Template, Is.SameAs(fixture.PartTemplates[1]));
      Assert.That(result.Instances[0].MaterialBatches,
        Is.SameAs(fixture.PartTemplates[0].Batches));
      Assert.That(result.Instances[1].Visual,
        Is.SameAs(fixture.Hierarchy.FrontRightWheel.ShapeVisual));
      Assert.That(result.Instances[0].WorldTransform.Translation,
        Is.EqualTo(new Vector3(101f, 203f, 302f)));
      Assert.That(result.Instances[1].WorldTransform.Translation,
        Is.EqualTo(new Vector3(105f, 209f, 307f)));
      Assert.That(result.Issues, Is.Empty);
    }
  }

  [Test]
  public void Build_FailsClosedForMissingTemplateUnresolvedPartAndForeignKey() {
    using var missing = Fixture(wild: false, includeWheelTemplate: false);
    var missingResult = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      missing.Cars,
      Hierarchies(missing.Hierarchy),
      missing.Templates);

    using var unresolved = Fixture(wild: false, unresolvedWheel: true);
    var unresolvedResult = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      unresolved.Cars,
      Hierarchies(unresolved.Hierarchy),
      unresolved.Templates);

    using var foreign = Fixture(wild: false);
    var foreignCar = new RideCarLink(RideTrainCarRole.Front, "foreign:ric", null, []);
    var foreignResult = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      foreign.Cars,
      Hierarchies(foreign.Hierarchy with { Car = foreignCar }),
      foreign.Templates);

    using (Assert.EnterMultipleScope()) {
      Assert.That(missingResult.Instances, Has.Count.EqualTo(1));
      Assert.That(missingResult.Instances.Single().Role,
        Is.EqualTo(RideVisualRole.FrontAxle));
      Assert.That(missingResult.Issues.Single().Status,
        Is.EqualTo(RideCarVisualHierarchyStaticInstanceIssueStatus.TemplateUnavailable));
      Assert.That(unresolvedResult.Instances, Is.Empty);
      Assert.That(unresolvedResult.Issues.Single().Status,
        Is.EqualTo(RideCarVisualHierarchyStaticInstanceIssueStatus.PartHierarchyUnresolved));
      Assert.That(foreignResult.Instances, Is.Empty);
      Assert.That(foreignResult.Issues.Single().Status,
        Is.EqualTo(RideCarVisualHierarchyStaticInstanceIssueStatus.HierarchyUnavailable));
    }
  }

  [Test]
  public void SceneBuilder_ClonesBindingsAndRollsBackTransactionally() {
    using var fixture = Fixture(wild: false);
    var instances = RideCarVisualHierarchyStaticInstanceRegistry.Build(
      fixture.Cars,
      Hierarchies(fixture.Hierarchy),
      fixture.Templates);
    var result = RideCarVisualHierarchySceneBuilder.Build(
      instances,
      (_, _) => new Flat());
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.SourcePartCount, Is.EqualTo(2));
        Assert.That(result.BuiltPartCount, Is.EqualTo(2));
        Assert.That(result.ModelCount, Is.EqualTo(2));
        Assert.That(result.Models[0].Mesh,
          Is.Not.SameAs(instances.Instances[0].MaterialBatches[0].Mesh));
        Assert.That(result.Models[0].Transform.Matrix,
          Is.EqualTo(instances.Instances[0].WorldTransform));
        Assert.That(result.ModelBindings[1].Instance,
          Is.SameAs(instances.Instances[1]));
        Assert.That(result.ClonedVertexCount, Is.EqualTo(6));
        Assert.That(result.ClonedIndexCount, Is.EqualTo(6));
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }

    var meshes = new List<Mesh>();
    var materials = new List<Material>();
    var models = new List<Model>();
    var modelCalls = 0;
    var operations = new RideCarStaticSceneBuilderOperations(
      (vertices, indices) => {
        var mesh = new Mesh(vertices, indices);
        meshes.Add(mesh);
        return mesh;
      },
      mesh => {
        modelCalls++;
        if (modelCalls == 2) throw new InvalidOperationException("stop");
        var model = new Model(mesh);
        models.Add(model);
        return model;
      },
      mesh => mesh.Dispose(),
      material => material.Dispose(),
      model => model.Dispose());

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVisualHierarchySceneBuilder.Build(
        instances,
        (_, _) => {
          var material = new Flat();
          materials.Add(material);
          return material;
        },
        RideCarVisualHierarchySceneBuilderLimits.Default,
        operations)));
    using (Assert.EnterMultipleScope()) {
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(models, Has.Count.EqualTo(1));
    }
  }

  private static FixtureData Fixture(
    bool wild,
    bool includeWheelTemplate = true,
    bool unresolvedWheel = false
  ) {
    var bodyRole = wild ? RideVisualRole.WildFlippedBody : RideVisualRole.Body;
    var body = BoneVisual(bodyRole, "Body", [Bone("AxleF", 1f, 2f, 3f)]);
    var axle = BoneVisual(
      RideVisualRole.FrontAxle,
      "Axle",
      [Bone("WheelR", 4f, 5f, 6f)]);
    var wheel = StaticVisual(RideVisualRole.FrontRightWheel, "Wheel");
    var visuals = new[] { body.Visual, axle.Visual, wheel.Visual };
    var carLink = new RideCarLink(RideTrainCarRole.Front, "car:ric", null, visuals);
    var train = new RideTrainLink("train:rit", null, [carLink]);
    var ride = new TrackedRideResourceLink(null!, [train], null);
    var bodyLink = Link(ride, train, carLink, body);
    var axleLink = Link(ride, train, carLink, axle);
    var wheelLink = Link(ride, train, carLink, wheel);
    var bodyTemplate = Template(bodyLink, body);
    var axleTemplate = Template(axleLink, axle);
    var wheelTemplate = Template(wheelLink, wheel);
    var allTemplates = includeWheelTemplate
      ? new[] { bodyTemplate, axleTemplate, wheelTemplate }
      : new[] { bodyTemplate, axleTemplate };
    var templates = TemplateRegistry(
      allTemplates,
      unresolvedVisualCount: includeWheelTemplate ? 0 : 1);
    var runtime = Runtime(carLink);
    var variant = wild ? RideCarVisualVariant.WildFlipped : RideCarVisualVariant.Normal;
    var movingRole = wild ? RideVisualRole.WildFlippedMoving : RideVisualRole.Moving;
    var selection = new RideCarVisualVariantSelectionEntry(
      0,
      runtime,
      wild ? 1 : 0,
      variant,
      bodyRole,
      movingRole,
      carLink,
      bodyLink,
      Moving: null,
      new RideCarBodyControlVisualFallback(carLink, bodyLink, movingRole),
      Issue: null);
    var templateEntry = new RideCarVariantVisualTemplateEntry(
      0,
      selection,
      bodyTemplate,
      Issue: null);
    var pose = new RideCarStaticPose(
      null!,
      default,
      default,
      Reversed: false,
      new(100f, 200f, 300f),
      Vector3.UnitX,
      Vector3.UnitY,
      Vector3.UnitZ,
      Quaternion.Identity,
      Matrix4x4.CreateTranslation(100f, 200f, 300f));
    var car = new RideCarVariantStaticInstanceEntry(
      0,
      runtime,
      new RideCarSavedWheelCursorEntry(0, runtime, null!, null!),
      selection,
      templateEntry,
      RideCarVariantStaticInstanceIssue.None,
      new RideCarLongitudinalGeometry(
        Vector3.UnitX,
        -Vector3.UnitX,
        Vector3.UnitX,
        -Vector3.UnitX,
        Vector3.UnitX,
        2f,
        0.5f,
        -0.5f,
        1f,
        1f,
        1f),
      pose,
      null,
      null);
    var frontAxle = new RideCarVisualHierarchyPart(
      RideVisualRole.FrontAxle,
      axle.Visual.Reference,
      71,
      axle.Visual,
      axleLink,
      RideCarVisualHierarchyPartStatus.Resolved,
      Anchor(RideCarVisualHierarchyAnchorSource.Body, bodyLink, body, 0));
    var frontWheel = new RideCarVisualHierarchyPart(
      RideVisualRole.FrontRightWheel,
      wheel.Visual.Reference,
      72,
      wheel.Visual,
      wheelLink,
      unresolvedWheel
        ? RideCarVisualHierarchyPartStatus.AnchorMissing
        : RideCarVisualHierarchyPartStatus.Resolved,
      unresolvedWheel
        ? null
        : Anchor(RideCarVisualHierarchyAnchorSource.FrontAxle, axleLink, axle, 0));
    var hierarchy = new RideCarVisualHierarchyResolution(
      ride,
      train,
      carLink,
      bodyRole,
      RideCarVisualHierarchyPartStatus.Resolved,
      body.Visual,
      bodyLink,
      frontAxle,
      Undeclared(RideVisualRole.RearAxle),
      frontWheel,
      Undeclared(RideVisualRole.FrontLeftWheel),
      Undeclared(RideVisualRole.BackRightWheel),
      Undeclared(RideVisualRole.BackLeftWheel));
    return new(
      VariantRegistry([car]),
      car,
      hierarchy,
      templates,
      [axleTemplate, wheelTemplate]);
  }

  private static VisualData BoneVisual(
    RideVisualRole role,
    string name,
    IReadOnlyList<BoneShapeBone> bones
  ) {
    var shape = new BoneShape(name, Vector3.Zero, Vector3.One, [], bones);
    var source = new RideBoneShapeResourceSource(
      new OvlFile(name, FileType.BoneShape, "shapes.unique.ovl"),
      shape);
    var lod = Lod(name, SvdLodType.BoneShape, null, $"{name}:bsh");
    return new(
      Visual(role, name, lod),
      new RideVisualShapeLodLink(lod, null, source),
      null,
      shape);
  }

  private static VisualData StaticVisual(RideVisualRole role, string name) {
    var shape = new StaticShape(name, Vector3.Zero, Vector3.One, [], []);
    var source = new RideStaticShapeResourceSource(
      new OvlFile(name, FileType.StaticShape, "shapes.unique.ovl"),
      shape);
    var lod = Lod(name, SvdLodType.StaticShape, $"{name}:shs", null);
    return new(
      Visual(role, name, lod),
      new RideVisualShapeLodLink(lod, source, null),
      shape,
      null);
  }

  private static RideVisualLink Visual(
    RideVisualRole role,
    string name,
    SceneryItemVisualLod lod
  ) => new(
    role,
    $"{name}:svd",
    new RideVisualResourceSource(
      new OvlFile(name, FileType.SceneryItemVisual, "visuals.unique.ovl"),
      new SceneryItemVisual(name, 0, 0f, 0f, 0f, 0f, [lod], null)));

  private static SceneryItemVisualLod Lod(
    string name,
    SvdLodType type,
    string? staticReference,
    string? boneReference
  ) => new(
    name,
    type,
    staticReference,
    boneReference,
    null,
    null,
    new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
    10f,
    []);

  private static RideCarVisualShapeLink Link(
    TrackedRideResourceLink ride,
    RideTrainLink train,
    RideCarLink car,
    VisualData visual
  ) => new(ride, train, car, visual.Visual, [visual.Lod]);

  private static RideCarVisualMeshTemplate Template(
    RideCarVisualShapeLink link,
    VisualData visual
  ) => new(
    link,
    visual.Lod,
    visual.BoneShape == null
      ? RideCarVisualTemplateShapeKind.StaticShape
      : RideCarVisualTemplateShapeKind.BoneShape,
    visual.StaticShape,
    visual.BoneShape,
    [Batch(link.Visual.Reference)]);

  private static StaticShapeMeshBatch Batch(string name) => new(
    0,
    name,
    0,
    null,
    null,
    0,
    0,
    1,
    new Mesh(
      [
        new Vertex {
          Position = Vector3.Zero,
          Normal = Vector3.UnitY,
          TexCoord = Vector2.Zero,
          Color = Vector4.One,
        },
        new Vertex {
          Position = Vector3.UnitX,
          Normal = Vector3.UnitY,
          TexCoord = Vector2.UnitX,
          Color = Vector4.One,
        },
        new Vertex {
          Position = Vector3.UnitZ,
          Normal = Vector3.UnitY,
          TexCoord = Vector2.UnitY,
          Color = Vector4.One,
        },
      ],
      [0, 1, 2]) { Name = name });

  private static BoneShapeBone Bone(string name, float x, float y, float z) => new(
    name,
    -1,
    Matrix4x4.Identity,
    Matrix4x4.CreateTranslation(x, y, z));

  private static RideCarVisualHierarchyAnchor Anchor(
    RideCarVisualHierarchyAnchorSource source,
    RideCarVisualShapeLink link,
    VisualData visual,
    int index
  ) => new(
    source,
    link,
    visual.Lod,
    visual.Lod.BoneShapeSource!,
    index,
    visual.BoneShape!.Bones[index]);

  private static RideCarVisualHierarchyPart Undeclared(RideVisualRole role) => new(
    role,
    null,
    0,
    null,
    null,
    RideCarVisualHierarchyPartStatus.VisualNotDeclared,
    null);

  private static RideCarInstanceRuntimeEntry Runtime(RideCarLink car) => new(
    0,
    0,
    null!,
    new DatRideCarInstanceData(
      100,
      1,
      0,
      0,
      1,
      1,
      0f,
      false,
      0f),
    RideTrainCarRole.Front,
    new RideCarTrackPieceRuntimeLink(
      1,
      RideCarTrackPieceRuntimeStatus.MissingSavedReference,
      null,
      null),
    new RideCarTrackPieceRuntimeLink(
      1,
      RideCarTrackPieceRuntimeStatus.MissingSavedReference,
      null,
      null),
    RideCarResourceRuntimeStatus.Resolved,
    car,
    null,
    null);

  private static RideCarVariantStaticInstanceRegistry VariantRegistry(
    RideCarVariantStaticInstanceEntry[] cars
  ) {
    var constructor = typeof(RideCarVariantStaticInstanceRegistry).GetConstructor(
      BindingFlags.Instance | BindingFlags.NonPublic,
      null,
      [typeof(RideCarVariantStaticInstanceEntry[])],
      null)!;
    return (RideCarVariantStaticInstanceRegistry)constructor.Invoke([cars]);
  }

  private static RideCarVisualTemplateRegistry TemplateRegistry(
    RideCarVisualMeshTemplate[] templates,
    int unresolvedVisualCount
  ) {
    var ownedMeshes = templates.SelectMany(template => template.Batches)
      .Select(batch => batch.Mesh)
      .ToArray();
    var bodyCount = templates.Count(template =>
      template.Link.Visual.Role == RideVisualRole.Body);
    return (RideCarVisualTemplateRegistry)Activator.CreateInstance(
      typeof(RideCarVisualTemplateRegistry),
      BindingFlags.Instance | BindingFlags.NonPublic,
      null,
      new object[] {
        Array.AsReadOnly(templates),
        ownedMeshes,
        (Action<Mesh>)(mesh => mesh.Dispose()),
        templates.Length + unresolvedVisualCount,
        unresolvedVisualCount,
        bodyCount,
        0,
        templates.Length,
        templates.Length,
        0ul,
        0ul,
      },
      null)!;
  }

  private static RideCarVisualHierarchyRegistry Hierarchies(
    RideCarVisualHierarchyResolution hierarchy
  ) => new(
    [hierarchy],
    hierarchy.Parts.Count(part => part.IsResolved),
    hierarchy.Parts.Count(part =>
      part.Status == RideCarVisualHierarchyPartStatus.AnchorAmbiguous),
    true,
    0);

  private sealed record VisualData(
    RideVisualLink Visual,
    RideVisualShapeLodLink Lod,
    StaticShape? StaticShape,
    BoneShape? BoneShape
  );

  private sealed record FixtureData(
    RideCarVariantStaticInstanceRegistry Cars,
    RideCarVariantStaticInstanceEntry Car,
    RideCarVisualHierarchyResolution Hierarchy,
    RideCarVisualTemplateRegistry Templates,
    IReadOnlyList<RideCarVisualMeshTemplate> PartTemplates
  ) : IDisposable {
    public void Dispose() => Templates.Dispose();
  }
}
