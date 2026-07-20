// Wild Animal Static Scene Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;
using System.Reflection;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalStaticSceneBuilderTests {
  [Test]
  public void Build_ClonesAllFourExactVariantsInPlacementThenBatchOrder() {
    var nativeMatrices = new[] {
      new Matrix4x4(
        1f, 2f, 3f, 4f,
        5f, 6f, 7f, 8f,
        9f, 10f, 11f, 12f,
        13f, 14f, 15f, 16f),
      Matrix4x4.CreateRotationX(0.25f) * Matrix4x4.CreateTranslation(20f, 21f, 22f),
      Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(30f, 31f, 32f),
      Matrix4x4.CreateRotationZ(0.75f) * Matrix4x4.CreateTranslation(40f, 41f, 42f),
    };
    using var fixture = new SceneFixture(
      nativeMatrices.Select((matrix, type) => new PlacementSpec(type, true, matrix)).ToArray(),
      [2, 1, 2, 1]);
    var calls = new List<(
      WildAnimalParkPlacementResource Placement,
      WildAnimalSavedVariantSelection Selection,
      ModelDefinitionMeshBatch Batch)>();
    var materials = new List<Material>();

    var result = WildAnimalStaticSceneBuilder.Build(
      fixture.Resources,
      [fixture.Template],
      (placement, selection, batch) => {
        calls.Add((placement, selection, batch));
        var material = new Flat();
        materials.Add(material);
        return material;
      });
    var clones = result.Models.Select(model => model.Mesh).ToArray();
    var expectedPlacements = new[] { 0, 0, 1, 2, 2, 3 };
    var expectedVariants = new[] { 0, 0, 1, 2, 2, 3 };
    var expectedBatches = new[] { 0, 1, 0, 0, 1, 0 };
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.SourcePlacementCount, Is.EqualTo(4));
        Assert.That(result.VisiblePlacementCount, Is.EqualTo(4));
        Assert.That(result.HiddenPlacementCount, Is.Zero);
        Assert.That(result.BuiltPlacementCount, Is.EqualTo(4));
        Assert.That(result.SkippedPlacementCount, Is.Zero);
        Assert.That(result.ModelCount, Is.EqualTo(6));
        Assert.That(result.MissingMaterialBatchCount, Is.Zero);
        Assert.That(result.ClonedVertexCount, Is.EqualTo(18));
        Assert.That(result.ClonedIndexCount, Is.EqualTo(18));
        Assert.That(((IList<Model>)result.Models).IsReadOnly, Is.True);
        Assert.That(
          ((IList<WildAnimalStaticSceneModelBinding>)result.ModelBindings).IsReadOnly,
          Is.True);
        Assert.That(
          calls.Select(call => call.Placement.PlacementIndex),
          Is.EqualTo(expectedPlacements));
        Assert.That(
          calls.Select(call => call.Selection.SerializedVariantIndex),
          Is.EqualTo(expectedVariants));
        Assert.That(
          calls.Select(call => call.Batch.SourceMeshIndex),
          Is.EqualTo(expectedBatches));
        Assert.That(result.Models.Distinct().Count(), Is.EqualTo(6));
        Assert.That(clones.Distinct().Count(), Is.EqualTo(6));
        Assert.That(materials.Distinct().Count(), Is.EqualTo(6));
        Assert.That(ExpectedTransform(nativeMatrices[0]), Is.Not.EqualTo(nativeMatrices[0]));
      }
      foreach (var index in Enumerable.Range(0, result.Models.Count)) {
        var placementIndex = expectedPlacements[index];
        var variantIndex = expectedVariants[index];
        var batchIndex = expectedBatches[index];
        var binding = result.ModelBindings[index];
        var sourceBatch = fixture.Template.Variants[variantIndex]
          .Template.Batches[batchIndex];
        using (Assert.EnterMultipleScope()) {
          Assert.That(binding.Model, Is.SameAs(result.Models[index]));
          Assert.That(binding.Placement,
            Is.SameAs(fixture.Resources.Placements[placementIndex]));
          Assert.That(binding.Selection, Is.SameAs(calls[index].Selection));
          Assert.That(binding.Selection.Animal,
            Is.SameAs(binding.Placement.Placement.Animal));
          Assert.That(binding.Selection.Variant,
            Is.SameAs(fixture.Template.Variants[variantIndex]));
          Assert.That(binding.MaterialBatchIndex, Is.EqualTo(batchIndex));
          Assert.That(binding.Batch, Is.SameAs(sourceBatch));
          Assert.That(binding.Model.Material, Is.SameAs(materials[index]));
          Assert.That(binding.Model.Transform.Matrix,
            Is.EqualTo(ExpectedTransform(nativeMatrices[placementIndex])));
          Assert.That(binding.Model.Transform.Matrix,
            Is.Not.EqualTo(nativeMatrices[placementIndex]));
          Assert.That(binding.Model.Mesh, Is.Not.SameAs(sourceBatch.Mesh));
          Assert.That(binding.Model.Mesh.Vertices, Is.Not.SameAs(sourceBatch.Mesh.Vertices));
          Assert.That(binding.Model.Mesh.Indices, Is.Not.SameAs(sourceBatch.Mesh.Indices));
          Assert.That(binding.Model.Mesh.Vertices, Is.EqualTo(sourceBatch.Mesh.Vertices));
          Assert.That(binding.Model.Mesh.Indices, Is.EqualTo(sourceBatch.Mesh.Indices));
        }
      }
    } finally {
      DisposeModels(result.Models);
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(clones.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(TemplateMeshes(fixture.Template).Select(mesh => mesh.State),
        Is.All.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_HiddenPlacementSkipsInvalidSelectorAndTransformState() {
    var invalidMatrix = Matrix4x4.Identity;
    invalidMatrix.M23 = float.NaN;
    using var fixture = new SceneFixture([
      new PlacementSpec(99, false, invalidMatrix),
      new PlacementSpec(3, true, Matrix4x4.CreateTranslation(1f, 2f, 3f)),
    ]);
    var calls = new List<int>();

    var result = WildAnimalStaticSceneBuilder.Build(
      fixture.Resources,
      [fixture.Template],
      (placement, _, _) => {
        calls.Add(placement.PlacementIndex);
        return new Flat();
      });
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(calls, Is.EqualTo(new[] { 1 }));
        Assert.That(result.SourcePlacementCount, Is.EqualTo(2));
        Assert.That(result.VisiblePlacementCount, Is.EqualTo(1));
        Assert.That(result.HiddenPlacementCount, Is.EqualTo(1));
        Assert.That(result.BuiltPlacementCount, Is.EqualTo(1));
        Assert.That(result.SkippedPlacementCount, Is.EqualTo(1));
        Assert.That(result.ModelCount, Is.EqualTo(1));
        Assert.That(result.ModelBindings[0].Selection.SerializedVariantIndex, Is.EqualTo(3));
      }
    } finally {
      DisposeModels(result.Models);
    }
  }

  [Test]
  public void Build_MissingMaterialsSkipOnlyTheirExactBatchesAndFullyMissingPlacement() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
      new PlacementSpec(1, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    var meshCalls = 0;
    var modelCalls = 0;
    var operations = WildAnimalStaticSceneBuilderOperations.Default with {
      CreateMesh = (vertices, indices) => {
        meshCalls++;
        return new Mesh(vertices, indices);
      },
      CreateModel = mesh => {
        modelCalls++;
        return new Model(mesh);
      },
    };

    var result = WildAnimalStaticSceneBuilder.Build(
      fixture.Resources,
      [fixture.Template],
      (placement, _, batch) =>
        placement.PlacementIndex == 0 && batch.SourceMeshIndex == 1
          ? new Flat()
          : null,
      WildAnimalStaticSceneBuilderLimits.Default,
      operations);
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.BuiltPlacementCount, Is.EqualTo(1));
        Assert.That(result.SkippedPlacementCount, Is.EqualTo(1));
        Assert.That(result.ModelCount, Is.EqualTo(1));
        Assert.That(result.MissingMaterialBatchCount, Is.EqualTo(2));
        Assert.That(result.ClonedVertexCount, Is.EqualTo(3));
        Assert.That(result.ClonedIndexCount, Is.EqualTo(3));
        Assert.That(meshCalls, Is.EqualTo(1));
        Assert.That(modelCalls, Is.EqualTo(1));
        Assert.That(result.ModelBindings[0].Placement,
          Is.SameAs(fixture.Resources.Placements[0]));
        Assert.That(result.ModelBindings[0].MaterialBatchIndex, Is.EqualTo(1));
      }
    } finally {
      DisposeModels(result.Models);
    }
  }

  [Test]
  public void Build_RejectsChangedParkSpeciesTemplateAndBatchIdentityBeforeFactories() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ]);
    using var unrelated = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], symbol: "Ostrich");
    var calls = 0;
    Material Factory(
      WildAnimalParkPlacementResource placement,
      WildAnimalSavedVariantSelection selection,
      ModelDefinitionMeshBatch batch
    ) {
      _ = placement;
      _ = selection;
      _ = batch;
      calls++;
      return new Flat();
    }

    var templateError = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [unrelated.Template],
        Factory)));
    var membershipError = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        RegistryWithChangedPlacementMembership(fixture.Resources),
        [fixture.Template],
        Factory)));
    fixture.Template.Templates[0].Batches[0].Mesh.Name = "changed-batch-name";
    var batchError = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        Factory)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(templateError!.Message, Does.Contain("exact bridge identity"));
      Assert.That(membershipError!.Message,
        Does.Contain("placement membership or order"));
      Assert.That(batchError!.Message, Does.Contain("changed exact MDL identity"));
      Assert.That(calls, Is.Zero);
    }
  }

  [TestCase(SceneResourceLimit.Placement, "placement")]
  [TestCase(SceneResourceLimit.Model, "model")]
  [TestCase(SceneResourceLimit.Vertex, "vertex")]
  [TestCase(SceneResourceLimit.Index, "index")]
  public void Build_RejectsResourceLimitsBeforeMaterialCreation(
    SceneResourceLimit resource,
    string description
  ) {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    var limits = resource switch {
      SceneResourceLimit.Placement =>
        WildAnimalStaticSceneBuilderLimits.Default with { MaximumPlacementCount = 0 },
      SceneResourceLimit.Model =>
        WildAnimalStaticSceneBuilderLimits.Default with { MaximumModels = 1 },
      SceneResourceLimit.Vertex =>
        WildAnimalStaticSceneBuilderLimits.Default with { MaximumVertices = 5 },
      SceneResourceLimit.Index =>
        WildAnimalStaticSceneBuilderLimits.Default with { MaximumIndices = 5 },
      _ => throw new ArgumentOutOfRangeException(nameof(resource)),
    };
    var calls = 0;

    var error = Assert.Throws<InvalidOperationException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => {
          calls++;
          return new Flat();
        },
        limits,
        WildAnimalStaticSceneBuilderOperations.Default)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain($"{description} count exceeds the limit"));
      Assert.That(calls, Is.Zero);
      Assert.That(TemplateMeshes(fixture.Template).Select(mesh => mesh.State),
        Is.All.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_ReleasesPendingResourcesThenPriorModelsOnPartialFailure() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    var createdMeshes = new List<Mesh>();
    var createdMaterials = new List<Material>();
    var createdModels = new List<Model>();
    var cleanup = new List<string>();
    var modelCalls = 0;
    var operations = new WildAnimalStaticSceneBuilderOperations(
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
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => {
          var material = new Flat();
          createdMaterials.Add(material);
          return material;
        },
        WildAnimalStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("forced model failure"));
      Assert.That(cleanup, Is.EqualTo(new[] { "mesh-1", "material-1", "model-0" }));
      Assert.That(createdMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(createdMaterials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(TemplateMeshes(fixture.Template).Select(mesh => mesh.State),
        Is.All.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_ReusedMaterialIsReleasedOnlyThroughItsPriorModel() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    var shared = new Flat();
    var modelReleases = 0;
    var pendingMaterialReleases = 0;
    var operations = WildAnimalStaticSceneBuilderOperations.Default with {
      DisposeMaterial = material => {
        pendingMaterialReleases++;
        material.Dispose();
      },
      DisposeModel = model => {
        modelReleases++;
        model.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => shared,
        WildAnimalStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("reused a material"));
      Assert.That(modelReleases, Is.EqualTo(1));
      Assert.That(pendingMaterialReleases, Is.Zero);
      Assert.That(shared.State, Is.EqualTo(State.Disposed));
      Assert.That(TemplateMeshes(fixture.Template).Select(mesh => mesh.State),
        Is.All.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_ReusedCloneIsReleasedOnlyThroughItsPriorModel() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    Mesh? shared = null;
    var modelReleases = 0;
    var pendingMeshReleases = 0;
    var pendingMaterialReleases = 0;
    var materials = new List<Material>();
    var operations = WildAnimalStaticSceneBuilderOperations.Default with {
      CreateMesh = (vertices, indices) => shared ??= new Mesh(vertices, indices),
      DisposeMesh = mesh => {
        pendingMeshReleases++;
        mesh.Dispose();
      },
      DisposeMaterial = material => {
        pendingMaterialReleases++;
        material.Dispose();
      },
      DisposeModel = model => {
        modelReleases++;
        model.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => {
          var material = new Flat();
          materials.Add(material);
          return material;
        },
        WildAnimalStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("reused a cloned mesh"));
      Assert.That(modelReleases, Is.EqualTo(1));
      Assert.That(pendingMeshReleases, Is.Zero);
      Assert.That(pendingMaterialReleases, Is.EqualTo(1));
      Assert.That(shared!.State, Is.EqualTo(State.Disposed));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_ReusedModelLeavesItsCurrentMeshAndMaterialPending() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ], [2, 1, 1, 1]);
    var createdMeshes = new List<Mesh>();
    var materials = new List<Material>();
    Model? shared = null;
    var modelReleases = 0;
    var pendingMeshReleases = 0;
    var pendingMaterialReleases = 0;
    var operations = WildAnimalStaticSceneBuilderOperations.Default with {
      CreateMesh = (vertices, indices) => {
        var mesh = new Mesh(vertices, indices);
        createdMeshes.Add(mesh);
        return mesh;
      },
      CreateModel = mesh => shared ??= new Model(mesh),
      DisposeMesh = mesh => {
        pendingMeshReleases++;
        mesh.Dispose();
      },
      DisposeMaterial = material => {
        pendingMaterialReleases++;
        material.Dispose();
      },
      DisposeModel = model => {
        modelReleases++;
        model.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => {
          var material = new Flat();
          materials.Add(material);
          return material;
        },
        WildAnimalStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("reused model"));
      Assert.That(modelReleases, Is.EqualTo(1));
      Assert.That(pendingMeshReleases, Is.EqualTo(1));
      Assert.That(pendingMaterialReleases, Is.EqualTo(1));
      Assert.That(createdMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_TemplateMeshReturnedByFactoryRemainsBorrowed() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity),
    ]);
    var templateMesh = fixture.Template.Variants[0].Template.Batches[0].Mesh;
    var pendingMeshReleases = 0;
    var pendingMaterialReleases = 0;
    var material = new Flat();
    var operations = WildAnimalStaticSceneBuilderOperations.Default with {
      CreateMesh = (_, _) => templateMesh,
      DisposeMesh = mesh => {
        pendingMeshReleases++;
        mesh.Dispose();
      },
      DisposeMaterial = pending => {
        pendingMaterialReleases++;
        pending.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalStaticSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        (_, _, _) => material,
        WildAnimalStaticSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("reused a template mesh"));
      Assert.That(pendingMeshReleases, Is.Zero);
      Assert.That(pendingMaterialReleases, Is.EqualTo(1));
      Assert.That(material.State, Is.EqualTo(State.Disposed));
      Assert.That(templateMesh.State, Is.EqualTo(State.Uninitialized));
    }
  }

  private static WildAnimalParkResourceRegistry RegistryWithChangedPlacementMembership(
    WildAnimalParkResourceRegistry source
  ) {
    var changedSpecies = source.SpeciesResources[0] with {
      PlacementIndices = Array.AsReadOnly(new[] { 1 }),
    };
    var placements = source.Placements
      .Select(placement => placement with { SpeciesResource = changedSpecies })
      .ToArray();
    var constructor = typeof(WildAnimalParkResourceRegistry).GetConstructor(
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      [
        typeof(IReadOnlyList<WildAnimalParkSpeciesResource>),
        typeof(IReadOnlyList<WildAnimalParkPlacementResource>),
      ],
      modifiers: null);
    Assert.That(constructor, Is.Not.Null);
    return (WildAnimalParkResourceRegistry)constructor!.Invoke([
      new WildAnimalParkSpeciesResource[] { changedSpecies },
      placements,
    ]);
  }

  private static Matrix4x4 ExpectedTransform(Matrix4x4 native) {
    var swapYAndZ = new Matrix4x4(
      1f, 0f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 1f, 0f, 0f,
      0f, 0f, 0f, 1f);
    return swapYAndZ * native * swapYAndZ;
  }

  private static IReadOnlyList<Mesh> TemplateMeshes(
    WildAnimalModelTemplateRegistry registry
  ) => registry.Templates
    .SelectMany(template => template.Batches)
    .Select(batch => batch.Mesh)
    .ToArray();

  private static void DisposeModels(IReadOnlyList<Model> models) {
    foreach (var model in models.Reverse()) model.Dispose();
  }

  private sealed record PlacementSpec(int Type, bool Visible, Matrix4x4 NativeMatrix);

  public enum SceneResourceLimit {
    Placement,
    Model,
    Vertex,
    Index,
  }

  private sealed class SceneFixture : IDisposable {
    private readonly string installRoot;

    public SceneFixture(
      IReadOnlyList<PlacementSpec> placements,
      IReadOnlyList<int>? batchCounts = null,
      string symbol = "Elephant"
    ) {
      installRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalStaticSceneBuilder-{Guid.NewGuid():N}"));
      var counts = batchCounts?.ToArray() ?? new[] { 1, 1, 1, 1 };
      if (counts.Length != 4 || counts.Any(count => count <= 0))
        throw new ArgumentException("The fixture needs four nonempty variant templates.");
      var speciesCommonPath = Path.Combine(
        installRoot,
        "WildAnimals",
        "WildAnimals.common.ovl");
      var modelCommonPath = Path.Combine(
        installRoot,
        "WildAnimals",
        symbol,
        $"{symbol}_data.common.ovl");
      var decodedVariants = Enumerable.Range(0, 4)
        .Select(index => new WildAnimalSpeciesVariant(
          $"{symbol}Model{index}:mdl",
          $"{symbol}Animation{index}:wad"))
        .ToArray();
      var decodedSpecies = new WildAnimalSpeciesDefinition(
        symbol,
        $@"WildAnimals\{symbol}\{symbol}_data",
        decodedVariants);
      var links = decodedVariants.Select((variant, variantIndex) => {
        var modelName = $"{symbol}Model{variantIndex}";
        var meshes = Enumerable.Range(0, counts[variantIndex])
          .Select(meshIndex => SourceMesh((variantIndex * 100f) + (meshIndex * 10f)))
          .ToArray();
        var definition = new ModelDefinition(
          modelName,
          modelCommonPath,
          100,
          200,
          0,
          0,
          0,
          0) {
          Bones = [],
          Groups = [SourceGroup(meshes)],
        };
        var source = new WildAnimalSpeciesModelResourceSource(
          new OvlFile(modelName, FileType.Model, modelCommonPath),
          definition);
        return new WildAnimalSpeciesModelVariantLink(variantIndex, variant, source);
      }).ToArray();
      Bridge = new WildAnimalSpeciesModelResourceBridgeResult(
        $"{symbol}:was",
        speciesCommonPath,
        new OvlFile(
          symbol,
          FileType.WildAnimalSpecies,
          ToUniquePath(speciesCommonPath)),
        decodedSpecies,
        modelCommonPath,
        Array.AsReadOnly(links));
      Template = WildAnimalModelTemplateRegistry.Build(Bridge);

      var datSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
        100,
        true,
        @"WildAnimals\WildAnimals",
        symbol);
      var park = new Park();
      park.WildAnimalPlacements.AddRange(placements.Select((placement, index) => {
        var animalEntryId = Convert.ToUInt64(1_000 + index);
        var visualEntryId = Convert.ToUInt64(2_000 + index);
        var animal = new DatWildAnimalData(
          animalEntryId,
          datSpecies.EntryId,
          visualEntryId,
          placement.Type is 0 or 1,
          placement.Type is 0 or 2,
          placement.Type);
        var visual = new DatWildAnimalVisualData(
          visualEntryId,
          [],
          true,
          placement.Visible,
          placement.NativeMatrix);
        return new DatWildAnimalPlacementData(
          animal,
          datSpecies,
          visual,
          DatWildAnimalVariantSelectionStatus.Unsupported);
      }));
      Resources = WildAnimalParkResourceRegistry.Build(
        park,
        installRoot,
        new SingleResolver(Bridge));
    }

    public WildAnimalSpeciesModelResourceBridgeResult Bridge { get; }
    public WildAnimalModelTemplateRegistry Template { get; }
    public WildAnimalParkResourceRegistry Resources { get; }

    public void Dispose() => Template.Dispose();

    private static ModelGroup SourceGroup(params ModelMesh[] meshes) => new(
      0,
      Convert.ToUInt16(meshes.Length),
      0,
      0,
      [],
      0,
      meshes,
      []);

    private static ModelMesh SourceMesh(float offset) {
      var vertices = new[] {
        SourceVertex(new Vector3(offset, 0f, 0f)),
        SourceVertex(new Vector3(offset, 0f, -1f)),
        SourceVertex(new Vector3(offset + 1f, 0f, 0f)),
      };
      var indices = new uint[] { 0, 2, 1 };
      return new ModelMesh(
        0x1305,
        Convert.ToUInt32(indices.Length),
        1,
        Convert.ToUInt16(vertices.Length),
        0,
        0,
        0,
        0,
        vertices,
        indices);
    }

    private static BoneShapeVertex SourceVertex(Vector3 position) => new(
      position,
      Vector3.UnitY,
      Vector2.Zero,
      Vector4.One,
      new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0));

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private sealed class SingleResolver(WildAnimalSpeciesModelResourceBridgeResult result)
    : IWildAnimalParkSpeciesResourceResolver {
    public WildAnimalSpeciesModelResourceBridgeResult Resolve(
      string _,
      string __
    ) => result;
  }
}
