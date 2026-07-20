// Ride Car Static Scene Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Simulation;
using System.Numerics;
using System.Reflection;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarStaticSceneBuilderTests {
  [Test]
  public void Build_ClonesEveryBatchIntoOrderedCallerOwnedModels() {
    var firstTemplate = Batch(0, "body-a", 0f);
    var secondTemplate = Batch(1, "body-b", 10f);
    var transform = Matrix4x4.CreateTranslation(3f, 4f, 5f);
    var resolved = Entry(0, RideCarStaticInstanceIssue.None,
      [firstTemplate, secondTemplate], transform);
    var skipped = Entry(
      1,
      RideCarStaticInstanceIssue.UnresolvedSavedCursor,
      null,
      Matrix4x4.Identity);
    var registry = Registry([resolved, skipped]);
    var calls = new List<(RideCarStaticInstanceEntry Entry, StaticShapeMeshBatch Batch)>();
    var materials = new List<Material>();

    var result = RideCarStaticSceneBuilder.Build(registry, (entry, batch) => {
      calls.Add((entry, batch));
      var material = new Flat();
      materials.Add(material);
      return material;
    });
    var clones = result.Models.Select(model => model.Mesh).ToArray();
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.SourceCarCount, Is.EqualTo(2));
        Assert.That(result.BuiltCarCount, Is.EqualTo(1));
        Assert.That(result.SkippedCarCount, Is.EqualTo(1));
        Assert.That(result.ModelCount, Is.EqualTo(2));
        Assert.That(result.MissingMaterialBatchCount, Is.Zero);
        Assert.That(result.ClonedVertexCount, Is.EqualTo(6));
        Assert.That(result.ClonedIndexCount, Is.EqualTo(6));
        Assert.That(result.UnresolvedSavedCursorCount, Is.EqualTo(1));
        Assert.That(calls.Select(call => call.Entry), Is.EqualTo([resolved, resolved]));
        Assert.That(calls.Select(call => call.Batch),
          Is.EqualTo([firstTemplate, secondTemplate]));
        Assert.That(result.Models.Select(model => model.Material),
          Is.EqualTo(materials));
        Assert.That(result.Models.All(model => model.Transform.Matrix == transform), Is.True);
        Assert.That(clones[0], Is.Not.SameAs(firstTemplate.Mesh));
        Assert.That(clones[1], Is.Not.SameAs(secondTemplate.Mesh));
        Assert.That(clones[0], Is.Not.SameAs(clones[1]));
        Assert.That(clones[0].Vertices, Is.Not.SameAs(firstTemplate.Mesh.Vertices));
        Assert.That(clones[0].Indices, Is.Not.SameAs(firstTemplate.Mesh.Indices));
        Assert.That(clones[0].Vertices, Is.EqualTo(firstTemplate.Mesh.Vertices));
        Assert.That(clones[0].Indices, Is.EqualTo(firstTemplate.Mesh.Indices));
        Assert.That(firstTemplate.Mesh.State, Is.EqualTo(State.Uninitialized));
        Assert.That(secondTemplate.Mesh.State, Is.EqualTo(State.Uninitialized));
      }
    } finally {
      foreach (var model in result.Models.Reverse()) model.Dispose();
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(clones.All(mesh => mesh.State == State.Disposed), Is.True);
      Assert.That(materials.All(material => material.State == State.Disposed), Is.True);
      Assert.That(firstTemplate.Mesh.State, Is.EqualTo(State.Uninitialized));
      Assert.That(secondTemplate.Mesh.State, Is.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_SkipsOneMissingMaterialWithoutSkippingTheRenderableCar() {
    var missing = Batch(0, "missing", 0f);
    var visible = Batch(1, "visible", 10f);
    var registry = Registry([
      Entry(0, RideCarStaticInstanceIssue.None,
        [missing, visible], Matrix4x4.Identity),
    ]);

    var result = RideCarStaticSceneBuilder.Build(
      registry,
      (_, batch) => ReferenceEquals(batch, missing) ? null : new Flat());
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.BuiltCarCount, Is.EqualTo(1));
        Assert.That(result.SkippedCarCount, Is.Zero);
        Assert.That(result.ModelCount, Is.EqualTo(1));
        Assert.That(result.MissingMaterialBatchCount, Is.EqualTo(1));
        Assert.That(result.ClonedVertexCount, Is.EqualTo(3));
        Assert.That(result.ClonedIndexCount, Is.EqualTo(3));
        Assert.That(result.Models.Single().Mesh.Vertices,
          Is.EqualTo(visible.Mesh.Vertices));
        Assert.That(missing.Mesh.State, Is.EqualTo(State.Uninitialized));
      }
    } finally {
      foreach (var model in result.Models.Reverse()) model.Dispose();
    }
  }

  [Test]
  public void Build_SkipsCarWhoseEveryMaterialBatchIsMissingWithoutAllocatingModels() {
    var templates = new[] { Batch(0, "missing-a", 0f), Batch(1, "missing-b", 10f) };
    var registry = Registry([
      Entry(0, RideCarStaticInstanceIssue.None, templates, Matrix4x4.Identity),
    ]);
    var meshCalls = 0;
    var modelCalls = 0;
    var operations = RideCarStaticSceneBuilderOperations.Default with {
      CreateMesh = (vertices, indices) => {
        meshCalls++;
        return new Mesh(vertices, indices);
      },
      CreateModel = mesh => {
        modelCalls++;
        return new Model(mesh);
      },
    };

    var result = RideCarStaticSceneBuilder.Build(
      registry,
      (_, _) => null,
      RideCarStaticSceneBuilderLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.BuiltCarCount, Is.Zero);
      Assert.That(result.SkippedCarCount, Is.EqualTo(1));
      Assert.That(result.ModelCount, Is.Zero);
      Assert.That(result.MissingMaterialBatchCount, Is.EqualTo(2));
      Assert.That(result.ClonedVertexCount, Is.Zero);
      Assert.That(result.ClonedIndexCount, Is.Zero);
      Assert.That(result.Models, Is.Empty);
      Assert.That(meshCalls, Is.Zero);
      Assert.That(modelCalls, Is.Zero);
      Assert.That(templates.All(batch => batch.Mesh.State == State.Uninitialized), Is.True);
    }
  }

  [Test]
  public void Build_ReleasesPendingResourcesThenPriorModelsWhenConstructionFails() {
    var templates = new[] { Batch(0, "body-a", 0f), Batch(1, "body-b", 10f) };
    var registry = Registry([
      Entry(0, RideCarStaticInstanceIssue.None, templates, Matrix4x4.Identity),
    ]);
    var createdMeshes = new List<Mesh>();
    var createdMaterials = new List<Material>();
    var createdModels = new List<Model>();
    var cleanup = new List<string>();
    var modelCalls = 0;
    var operations = new RideCarStaticSceneBuilderOperations(
      (vertices, indices) => {
        var mesh = new Mesh(vertices, indices);
        createdMeshes.Add(mesh);
        return mesh;
      },
      mesh => {
        modelCalls++;
        if (modelCalls == 2) throw new InvalidOperationException("forced model failure");
        var model = new Model(mesh);
        createdModels.Add(model);
        return model;
      },
      mesh => {
        cleanup.Add($"mesh-{createdMeshes.IndexOf(mesh)}");
        mesh.Dispose();
      },
      material => {
        cleanup.Add($"material-{createdMaterials.IndexOf(material)}");
        material.Dispose();
      },
      model => {
        cleanup.Add($"model-{createdModels.IndexOf(model)}");
        model.Dispose();
      });

    var error = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarStaticSceneBuilder.Build(
        registry,
        (_, _) => {
          var material = new Flat();
          createdMaterials.Add(material);
          return material;
        },
        RideCarStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("forced model failure"));
      Assert.That(cleanup, Is.EqualTo(["mesh-1", "material-1", "model-0"]));
      Assert.That(createdMeshes.All(mesh => mesh.State == State.Disposed), Is.True);
      Assert.That(createdMaterials.All(material => material.State == State.Disposed), Is.True);
      Assert.That(templates.All(batch => batch.Mesh.State == State.Uninitialized), Is.True);
    }
  }

  [Test]
  public void Build_RejectsReusedOrDisposedMaterialOutputsTransactionally() {
    var templates = new[] { Batch(0, "body-a", 0f), Batch(1, "body-b", 10f) };
    var registry = Registry([
      Entry(0, RideCarStaticInstanceIssue.None, templates, Matrix4x4.Identity),
    ]);
    var reused = new Flat();
    var reuseError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticSceneBuilder.Build(registry, (_, _) => reused)));
    var disposed = new Flat();
    disposed.Dispose();
    var disposedError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticSceneBuilder.Build(registry, (_, _) => disposed)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(reuseError!.Message, Does.Contain("reused a material"));
      Assert.That(reused.State, Is.EqualTo(State.Disposed));
      Assert.That(disposedError!.Message, Does.Contain("disposed material"));
      Assert.That(templates.All(batch => batch.Mesh.State == State.Uninitialized), Is.True);
    }
  }

  [Test]
  public void Build_RejectsNonFiniteTransformsAndBoundsBeforeCallingFactories() {
    var batch = Batch(0, "body", 0f);
    var transform = Matrix4x4.Identity with { M41 = float.NaN };
    var malformed = Registry([
      Entry(0, RideCarStaticInstanceIssue.None, [batch], transform),
    ]);
    var bounded = Registry([
      Entry(0, RideCarStaticInstanceIssue.None,
        [batch, Batch(1, "second-body", 10f)], Matrix4x4.Identity),
    ]);
    var calls = 0;

    var transformError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarStaticSceneBuilder.Build(malformed, (_, _) => {
        calls++;
        return new Flat();
      })));
    var limitError = Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarStaticSceneBuilder.Build(
        bounded,
        (_, _) => {
          calls++;
          return new Flat();
        },
        RideCarStaticSceneBuilderLimits.Default with { MaximumModels = 1 },
        RideCarStaticSceneBuilderOperations.Default)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(transformError!.Message, Does.Contain("non-finite transform"));
      Assert.That(limitError!.Message, Does.Contain("model count exceeds the limit 1"));
      Assert.That(calls, Is.Zero);
      Assert.That(batch.Mesh.State, Is.EqualTo(State.Uninitialized));
    }
  }

  private static RideCarStaticInstanceRegistry Registry(
    RideCarStaticInstanceEntry[] entries
  ) {
    var constructor = typeof(RideCarStaticInstanceRegistry).GetConstructor(
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      [typeof(RideCarStaticInstanceEntry[])],
      modifiers: null);
    Assert.That(constructor, Is.Not.Null);
    return (RideCarStaticInstanceRegistry)constructor!.Invoke([entries]);
  }

  private static RideCarStaticInstanceEntry Entry(
    int index,
    RideCarStaticInstanceIssue issues,
    IReadOnlyList<StaticShapeMeshBatch>? batches,
    Matrix4x4 transform
  ) {
    RideCarVisualMeshTemplate? template = null;
    RideCarStaticPose? pose = null;
    if (batches != null) {
      template = new(
        Link: null!,
        Lod: null!,
        RideCarVisualTemplateShapeKind.BoneShape,
        StaticShape: null,
        BoneShape: null,
        batches);
    }
    if (issues == RideCarStaticInstanceIssue.None) {
      pose = new(
        Circuit: null!,
        FrontContact: default,
        RearContact: default,
        Reversed: false,
        ContactMidpoint: Vector3.Zero,
        Forward: Vector3.UnitX,
        Right: Vector3.UnitY,
        Up: Vector3.UnitZ,
        Orientation: Quaternion.Identity,
        transform);
    }
    return new(
      index,
      CarRuntime: null!,
      SavedCursor: null!,
      issues,
      template,
      Geometry: null,
      pose,
      GeometryUnavailableDetail: null,
      StaticPoseUnavailableDetail: null);
  }

  private static StaticShapeMeshBatch Batch(int index, string name, float offset) {
    var vertices = new List<Vertex> {
      new() {
        Position = new(offset, 0f, 0f),
        Normal = Vector3.UnitZ,
        TexCoord = Vector2.Zero,
        Color = Vector4.One,
      },
      new() {
        Position = new(offset + 1f, 0f, 0f),
        Normal = Vector3.UnitZ,
        TexCoord = Vector2.UnitX,
        Color = Vector4.One,
      },
      new() {
        Position = new(offset, 1f, 0f),
        Normal = Vector3.UnitZ,
        TexCoord = Vector2.UnitY,
        Color = Vector4.One,
      },
    };
    var mesh = new Mesh(vertices, new List<uint> { 0, 1, 2 }) { Name = name };
    return new(index, name, 0, null, null, 0, 0, 1, mesh);
  }
}
