// Wild Animal Frame-Zero Scene Builder Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalFrameZeroSceneBuilderTests {
  [Test]
  public void Build_CreatesExactSkinnedModelWithNeutralMaterialBindingAndSavedTransform() {
    var nativeTransform = Matrix4x4.CreateRotationY(0.4f) *
      Matrix4x4.CreateTranslation(10f, 20f, 30f);
    using var fixture = new SceneFixture([
      new PlacementSpec(
        0,
        true,
        nativeTransform,
        [new DatWildAnimalAnimationData(8.5f, 0, 1f)]),
    ]);
    var calls = new List<(
      WildAnimalParkPlacementResource Placement,
      WildAnimalSavedVariantSelection Selection,
      ModelDefinitionMeshBatch Batch)>();
    var material = new Flat();

    var result = WildAnimalFrameZeroSceneBuilder.Build(
      fixture.Resources,
      [fixture.Template],
      [fixture.Poses],
      (placement, selection, batch) => {
        calls.Add((placement, selection, batch));
        return material;
      });
    var model = result.Models.Single();
    var binding = result.ModelBindings.Single();
    var skinnedMesh = model.Mesh;
    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.SourcePlacementCount, Is.EqualTo(1));
        Assert.That(result.VisiblePlacementCount, Is.EqualTo(1));
        Assert.That(result.HiddenPlacementCount, Is.Zero);
        Assert.That(result.BuiltPlacementCount, Is.EqualTo(1));
        Assert.That(result.SkippedPlacementCount, Is.Zero);
        Assert.That(result.ModelCount, Is.EqualTo(1));
        Assert.That(result.SkinnedVertexCount, Is.EqualTo(3));
        Assert.That(result.SkinnedIndexCount, Is.EqualTo(3));
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].Placement, Is.SameAs(fixture.Resources.Placements[0]));
        Assert.That(calls[0].Selection.SerializedVariantIndex, Is.Zero);
        Assert.That(calls[0].Batch,
          Is.SameAs(fixture.Template.Variants[0].Template.Batches[0]));
        Assert.That(binding.Model, Is.SameAs(model));
        Assert.That(binding.SkinnedBatch.Mesh, Is.SameAs(model.Mesh));
        Assert.That(binding.MaterialSourceBatch, Is.SameAs(calls[0].Batch));
        Assert.That(binding.PoseVariant, Is.SameAs(fixture.Poses.Variants[0]));
        Assert.That(binding.PoseSlot, Is.SameAs(fixture.Poses.Variants[0].Slots[0]));
        Assert.That(binding.Animation.ExactSingleClipEntry!.SavedEntry.Time,
          Is.EqualTo(8.5f));
        Assert.That(model.Material, Is.SameAs(material));
        Assert.That(model.Transform.Matrix, Is.EqualTo(ExpectedTransform(nativeTransform)));
        Assert.That(model.Mesh, Is.Not.SameAs(calls[0].Batch.Mesh));
        Assert.That(model.Mesh.Vertices[0].Position, Is.EqualTo(new Vector3(2f, 4f, 3f)));
        Assert.That(((IList<Model>)result.Models).IsReadOnly, Is.True);
        Assert.That(((IList<WildAnimalFrameZeroSceneModelBinding>)result.ModelBindings)
          .IsReadOnly, Is.True);
      }
    } finally {
      model.Dispose();
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(skinnedMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(material.State, Is.EqualTo(State.Disposed));
      Assert.That(fixture.Template.Variants[0].Template.Batches[0].Mesh.State,
        Is.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_RetainsTypedNoClipAndWeightedSkipsWithoutBuildingMeshes() {
    using var fixture = new SceneFixture([
      new PlacementSpec(0, true, Matrix4x4.Identity, []),
      new PlacementSpec(0, true, Matrix4x4.Identity, [
        new DatWildAnimalAnimationData(1f, 0, 0.75f),
        new DatWildAnimalAnimationData(2f, 1, 0.25f),
      ]),
    ]);
    var meshCalls = 0;
    var materialCalls = 0;
    var operations = WildAnimalFrameZeroSceneBuilderOperations.Default with {
      BuildSkinnedBatches = pose => {
        meshCalls++;
        return ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose);
      },
    };

    var result = WildAnimalFrameZeroSceneBuilder.Build(
      fixture.Resources,
      [fixture.Template],
      [fixture.Poses],
      (_, _, _) => {
        materialCalls++;
        return new Flat();
      },
      WildAnimalFrameZeroSceneBuilderLimits.Default,
      operations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Models, Is.Empty);
      Assert.That(result.ModelBindings, Is.Empty);
      Assert.That(result.VisiblePlacementCount, Is.EqualTo(2));
      Assert.That(result.BuiltPlacementCount, Is.Zero);
      Assert.That(result.SkippedPlacementCount, Is.EqualTo(2));
      Assert.That(result.NoActiveClipPlacementCount, Is.EqualTo(1));
      Assert.That(result.WeightedStatePlacementCount, Is.EqualTo(1));
      Assert.That(result.SkippedPlacements.Select(item => item.Reason),
        Is.EqualTo(new[] {
          WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
          WildAnimalFrameZeroSceneSkipReason.WeightedState,
        }));
      Assert.That(result.SkippedPlacements.Select(item => item.Placement.PlacementIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(result.SkippedPlacements.Select(item => item.Animation.Status),
        Is.EqualTo(new[] {
          WildAnimalSavedAnimationResolutionStatus.NoActiveClip,
          WildAnimalSavedAnimationResolutionStatus.WeightedState,
        }));
      Assert.That(meshCalls, Is.Zero);
      Assert.That(materialCalls, Is.Zero);
    }
  }

  [Test]
  public void Build_RejectsPoseRegistryFromAnotherExactSpeciesBeforeFactories() {
    using var fixture = new SceneFixture([
      new PlacementSpec(
        0,
        true,
        Matrix4x4.Identity,
        [new DatWildAnimalAnimationData(0f, 0, 1f)]),
    ]);
    using var unrelated = new SceneFixture([
      new PlacementSpec(
        0,
        true,
        Matrix4x4.Identity,
        [new DatWildAnimalAnimationData(0f, 0, 1f)]),
    ], symbol: "Giraffe");
    var materialCalls = 0;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalFrameZeroSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        [unrelated.Poses],
        (_, _, _) => {
          materialCalls++;
          return new Flat();
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("changed bridge identity"));
      Assert.That(materialCalls, Is.Zero);
      Assert.That(fixture.Template.Variants[0].Template.Batches[0].Mesh.State,
        Is.EqualTo(State.Uninitialized));
      Assert.That(unrelated.Template.Variants[0].Template.Batches[0].Mesh.State,
        Is.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_RollsBackModelsMaterialsAndUnadoptedSkinnedMeshesOnFailure() {
    using var fixture = new SceneFixture([
      new PlacementSpec(
        0,
        true,
        Matrix4x4.Identity,
        [new DatWildAnimalAnimationData(0f, 0, 1f)]),
    ], batchCount: 2);
    var createdMeshes = new List<Mesh>();
    var materials = new List<Material>();
    var modelCalls = 0;
    var modelReleases = 0;
    var meshReleases = 0;
    var materialReleases = 0;
    var operations = WildAnimalFrameZeroSceneBuilderOperations.Default with {
      BuildSkinnedBatches = pose => {
        var batches = ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose);
        createdMeshes.AddRange(batches.Select(batch => batch.Mesh));
        return batches;
      },
      CreateModel = mesh => {
        modelCalls++;
        if (modelCalls == 2) throw new InvalidOperationException("synthetic model failure");
        return new Model(mesh);
      },
      DisposeMesh = mesh => {
        meshReleases++;
        mesh.Dispose();
      },
      DisposeMaterial = material => {
        materialReleases++;
        material.Dispose();
      },
      DisposeModel = model => {
        modelReleases++;
        model.Dispose();
      },
    };

    var error = Assert.Throws<InvalidOperationException>(new Action(() =>
      WildAnimalFrameZeroSceneBuilder.Build(
        fixture.Resources,
        [fixture.Template],
        [fixture.Poses],
        (_, _, _) => {
          var material = new Flat();
          materials.Add(material);
          return material;
        },
        WildAnimalFrameZeroSceneBuilderLimits.Default,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("synthetic model failure"));
      Assert.That(modelCalls, Is.EqualTo(2));
      Assert.That(modelReleases, Is.EqualTo(1));
      Assert.That(meshReleases, Is.EqualTo(1));
      Assert.That(materialReleases, Is.EqualTo(1));
      Assert.That(createdMeshes, Has.Count.EqualTo(2));
      Assert.That(createdMeshes.Select(mesh => mesh.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(materials, Has.Count.EqualTo(2));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(fixture.Template.Variants[0].Template.Batches
        .Select(batch => batch.Mesh.State), Is.All.EqualTo(State.Uninitialized));
    }
  }

  private static Matrix4x4 ExpectedTransform(Matrix4x4 native) {
    var swapYAndZ = new Matrix4x4(
      1f, 0f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 1f, 0f, 0f,
      0f, 0f, 0f, 1f);
    return swapYAndZ * native * swapYAndZ;
  }

  private sealed record PlacementSpec(
    int Type,
    bool Visible,
    Matrix4x4 NativeMatrix,
    IReadOnlyList<DatWildAnimalAnimationData> Animations
  );

  private sealed class SceneFixture : IDisposable {
    private readonly string installRoot;

    public SceneFixture(
      IReadOnlyList<PlacementSpec> placements,
      int batchCount = 1,
      string symbol = "Elephant"
    ) {
      installRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalFrameZeroScene-{Guid.NewGuid():N}"));
      var speciesCommonPath = Path.Combine(
        installRoot,
        "WildAnimals",
        "WildAnimals.common.ovl");
      var modelCommonPath = Path.Combine(
        installRoot,
        "WildAnimals",
        symbol,
        $"{symbol}_data.common.ovl");
      var animationCommonPath = Path.Combine(
        installRoot,
        "WildAnimals",
        symbol,
        $"{symbol}_anims.common.ovl");
      var modelName = $"{symbol}Model";
      var wadName = $"{symbol}Animation";
      var variants = Enumerable.Range(0, 4)
        .Select(_ => new WildAnimalSpeciesVariant(
          $"{modelName}:mdl",
          $"{wadName}:wad"))
        .ToArray();
      var species = new WildAnimalSpeciesDefinition(
        symbol,
        $@"WildAnimals\{symbol}\{symbol}_data",
        variants);
      var model = Model(modelName, modelCommonPath, batchCount);
      var modelSource = new WildAnimalSpeciesModelResourceSource(
        new OvlFile(modelName, FileType.Model, modelCommonPath),
        model);
      var links = variants.Select((variant, index) =>
        new WildAnimalSpeciesModelVariantLink(index, variant, modelSource)).ToArray();
      Bridge = new(
        $"{symbol}:was",
        speciesCommonPath,
        new OvlFile(
          symbol,
          FileType.WildAnimalSpecies,
          ToUniquePath(speciesCommonPath)),
        species,
        modelCommonPath,
        links);

      var animation = AnimationDefinition("Idle", animationCommonPath);
      var animationSource = new WildAnimalModelAnimationResourceSource(
        new OvlFile("Idle", FileType.ModelAnim, animationCommonPath),
        animation);
      var references = Enumerable.Range(0, 31)
        .Select(index => index is 0 or 1 ? "Idle:modelanim" : ":modelanim")
        .ToArray();
      var wad = AnimationData(wadName, animationCommonPath, references);
      var slots = references.Select((reference, index) =>
        reference == ":modelanim"
          ? new WildAnimalModelAnimationSlotLink(
            index,
            reference,
            WildAnimalModelAnimationSlotStatus.Placeholder,
            null)
          : new WildAnimalModelAnimationSlotLink(
            index,
            reference,
            WildAnimalModelAnimationSlotStatus.Resolved,
            animationSource)).ToArray();
      var animationBridge = new WildAnimalModelAnimationResourceBridgeResult(
        $"{wadName}:wad",
        modelCommonPath,
        animationCommonPath,
        new OvlFile(
          wadName,
          FileType.WildAnimalAnimData,
          ToUniquePath(animationCommonPath)),
        wad,
        slots);
      var animationBridges = new Dictionary<
        string,
        WildAnimalModelAnimationResourceBridgeResult>(StringComparer.OrdinalIgnoreCase) {
        [$"{wadName}:wad"] = animationBridge,
      };
      Poses = WildAnimalFrameZeroPoseRegistry.Build(Bridge, animationBridges);
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
          placement.Animations,
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
    public WildAnimalFrameZeroPoseRegistry Poses { get; }
    public WildAnimalParkResourceRegistry Resources { get; }

    public void Dispose() => Template.Dispose();

    private static ModelDefinition Model(string name, string path, int batchCount) {
      var meshes = Enumerable.Range(0, batchCount)
        .Select(index => SourceMesh(Convert.ToSingle(index * 10)))
        .ToArray();
      return new ModelDefinition(
        name,
        path,
        100,
        200,
        1,
        0,
        0,
        0) {
        Bones = [new ModelBone(
          "Root",
          new Vector4(0f, 0f, 0f, 1f),
          new Vector4(0f, 0f, 0f, 1f),
          Matrix4x4.Identity,
          ushort.MaxValue,
          1)],
        Groups = [new ModelGroup(
          0,
          Convert.ToUInt16(meshes.Length),
          0,
          0,
          [],
          0,
          meshes,
          [])],
      };
    }

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
      new BoneShapeSkinning(0, 255, 255, 255, 255, 0, 0, 0));

    private static ModelAnimationDefinition AnimationDefinition(
      string name,
      string path
    ) => new(
      name,
      path,
      300,
      0,
      1,
      400,
      500,
      1,
      1,
      600,
      700,
      new uint[] { 0 },
      new uint[] { 0 },
      [new ModelAnimationTriple(2f, 3f, 4f)],
      [new ModelAnimationFourTuple(0f, 0f, 0f, 1f)],
      ["Root"],
      ["Root"]);

    private static WildAnimalAnimationDataDefinition AnimationData(
      string name,
      string commonPath,
      IReadOnlyList<string> references
    ) => new(
      name,
      ToUniquePath(commonPath),
      1_000,
      0,
      0,
      31,
      1_056,
      2_000,
      2_124,
      0,
      0,
      0,
      0,
      0,
      0,
      0,
      0,
      references,
      Enumerable.Repeat(0f, 31).ToArray(),
      Enumerable.Repeat(0f, 31).ToArray());

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private sealed class SingleResolver(WildAnimalSpeciesModelResourceBridgeResult result)
    : IWildAnimalParkSpeciesResourceResolver {
    public WildAnimalSpeciesModelResourceBridgeResult Resolve(string _, string __) => result;
  }
}
