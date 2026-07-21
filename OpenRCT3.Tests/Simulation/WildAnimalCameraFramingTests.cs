// Wild Animal Camera Framing Tests
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

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalCameraFramingTests {
  private const float Epsilon = 0.001f;

  [Test]
  public void Calculate_UsesOneRenderedTranslationPerBuiltPlacement() {
    var first = new Vector3(10f, 20f, 30f);
    var second = new Vector3(30f, 40f, 50f);
    using var fixture = new SceneFixture(
      new PlacementInput(0, first, 2),
      new PlacementInput(1, second, 1));

    var framing = WildAnimalCameraFraming.Calculate(fixture.Scene);

    Assert.That(framing.HasValue, Is.True);
    var expectedTarget = (first + second) * 0.5f;
    var expectedRadius = Vector3.Distance(expectedTarget, first);
    using (Assert.EnterMultipleScope()) {
      Assert.That(framing!.Value.PlacementCount, Is.EqualTo(2));
      Assert.That(framing.Value.Target.X, Is.EqualTo(expectedTarget.X).Within(Epsilon));
      Assert.That(framing.Value.Target.Y, Is.EqualTo(expectedTarget.Y).Within(Epsilon));
      Assert.That(framing.Value.Target.Z, Is.EqualTo(expectedTarget.Z).Within(Epsilon));
      Assert.That(framing.Value.Target,
        Is.Not.EqualTo(((first * 2f) + second) / 3f));
      Assert.That(framing.Value.Radius, Is.EqualTo(expectedRadius).Within(Epsilon));
      Assert.That(
        framing.Value.Distance,
        Is.EqualTo((expectedRadius * 2f) + 8f).Within(Epsilon));
      Assert.That(framing.Value.MinimumDistance,
        Is.EqualTo(Camera.NearPlaneDistance * 2f));
      Assert.That(float.IsFinite(framing.Value.Target.X), Is.True);
      Assert.That(float.IsFinite(framing.Value.Target.Y), Is.True);
      Assert.That(float.IsFinite(framing.Value.Target.Z), Is.True);
      Assert.That(float.IsFinite(framing.Value.Radius), Is.True);
    }
  }

  [Test]
  public void Calculate_ClampsNearAndFarDiagnosticDistances() {
    using var near = new SceneFixture(
      new PlacementInput(0, new Vector3(5f, 6f, 7f), 1));
    using var far = new SceneFixture(
      new PlacementInput(0, new Vector3(-100f, 0f, 0f), 1),
      new PlacementInput(1, new Vector3(100f, 0f, 0f), 1));

    var nearFraming = WildAnimalCameraFraming.Calculate(near.Scene);
    var farFraming = WildAnimalCameraFraming.Calculate(far.Scene);

    using (Assert.EnterMultipleScope()) {
      Assert.That(nearFraming!.Value.Radius, Is.Zero);
      Assert.That(nearFraming.Value.Distance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumDiagnosticDistance));
      Assert.That(farFraming!.Value.Radius, Is.EqualTo(100f).Within(Epsilon));
      Assert.That(farFraming.Value.Distance,
        Is.EqualTo(WildAnimalCameraFraming.MaximumDiagnosticDistance));
      Assert.That(nearFraming.Value.MinimumDistance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumCameraDistance));
      Assert.That(farFraming.Value.MinimumDistance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumCameraDistance));
    }
  }

  [Test]
  public void Calculate_FrameZeroMatchesStaticFraming() {
    using var fixture = new SceneFixture(
      new PlacementInput(0, new Vector3(10f, 20f, 30f), 2),
      new PlacementInput(1, new Vector3(30f, 40f, 50f), 1));

    var staticFraming = WildAnimalCameraFraming.Calculate(fixture.Scene);
    var frameZeroFraming = WildAnimalCameraFraming.Calculate(fixture.FrameZeroScene);

    Assert.That(frameZeroFraming, Is.EqualTo(staticFraming));
  }

  [Test]
  public void Calculate_FrameZeroExcludesNoClipAndWeightedPlacementSkips() {
    var first = new Vector3(10f, 20f, 30f);
    var second = new Vector3(30f, 40f, 50f);
    using var mixed = new SceneFixture(
      new PlacementInput(0, first, 1),
      new PlacementInput(
        1,
        new Vector3(1_000f, 2_000f, 3_000f),
        1,
        FrameZeroState.NoActiveClip),
      new PlacementInput(2, second, 1),
      new PlacementInput(
        3,
        new Vector3(-1_000f, -2_000f, -3_000f),
        1,
        FrameZeroState.Weighted));
    using var onlySkipped = new SceneFixture(
      new PlacementInput(
        0,
        Vector3.Zero,
        1,
        FrameZeroState.NoActiveClip),
      new PlacementInput(
        1,
        Vector3.One,
        1,
        FrameZeroState.Weighted));

    var framing = WildAnimalCameraFraming.Calculate(mixed.FrameZeroScene);

    using (Assert.EnterMultipleScope()) {
      Assert.That(framing!.Value.PlacementCount, Is.EqualTo(2));
      Assert.That(framing.Value.Target, Is.EqualTo((first + second) * 0.5f));
      Assert.That(mixed.FrameZeroScene.SkippedPlacements.Select(item => item.Reason),
        Is.EqualTo(new[] {
          WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
          WildAnimalFrameZeroSceneSkipReason.WeightedState,
        }));
      Assert.That(WildAnimalCameraFraming.Calculate(onlySkipped.FrameZeroScene), Is.Null);
    }
  }

  [Test]
  public void Calculate_FrameZeroCountsMultiBatchPlacementOnce() {
    var position = new Vector3(10f, 20f, 30f);
    using var fixture = new SceneFixture(new PlacementInput(0, position, 3));

    var framing = WildAnimalCameraFraming.Calculate(fixture.FrameZeroScene);

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.FrameZeroScene.ModelCount, Is.EqualTo(3));
      Assert.That(framing!.Value.PlacementCount, Is.EqualTo(1));
      Assert.That(framing.Value.Target, Is.EqualTo(position));
      Assert.That(framing.Value.Radius, Is.Zero);
      Assert.That(framing.Value.Distance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumDiagnosticDistance));
    }
  }

  [TestCase(MalformedFrameZeroScene.SourceIdentity)]
  [TestCase(MalformedFrameZeroScene.NonFiniteTransform)]
  public void Calculate_FrameZeroRejectsChangedLiveTransformOrSourceIdentity(
    MalformedFrameZeroScene malformed
  ) {
    using var fixture = new SceneFixture(new PlacementInput(0, Vector3.One, 2));
    var scene = fixture.FrameZeroScene;
    switch (malformed) {
      case MalformedFrameZeroScene.SourceIdentity:
        scene = scene with {
          ModelBindings = ReplaceBinding(
            scene,
            0,
            scene.ModelBindings[0] with {
              MaterialSourceBatch = scene.ModelBindings[1].MaterialSourceBatch,
            }),
        };
        break;
      case MalformedFrameZeroScene.NonFiniteTransform:
        var nonFinite = scene.Models[0].Transform.Matrix;
        nonFinite.M41 = float.PositiveInfinity;
        scene.Models[0].Transform.Matrix = nonFinite;
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed));
    }

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalCameraFraming.Calculate(scene)));
  }

  [Test]
  public void Calculate_ReturnsNoFrameForValidSceneWithoutBuiltPlacements() {
    var scene = new WildAnimalStaticSceneBuildResult(
      Array.AsReadOnly(Array.Empty<Model>()),
      Array.AsReadOnly(Array.Empty<WildAnimalStaticSceneModelBinding>()),
      SourcePlacementCount: 2,
      VisiblePlacementCount: 1,
      HiddenPlacementCount: 1,
      BuiltPlacementCount: 0,
      SkippedPlacementCount: 2,
      ModelCount: 0,
      MissingMaterialBatchCount: 1,
      ClonedVertexCount: 0,
      ClonedIndexCount: 0);

    Assert.That(WildAnimalCameraFraming.Calculate(scene), Is.Null);
  }

  [TestCase(MalformedCameraScene.SummaryCounts)]
  [TestCase(MalformedCameraScene.ModelIdentity)]
  [TestCase(MalformedCameraScene.BatchIdentity)]
  [TestCase(MalformedCameraScene.NonFiniteTransform)]
  [TestCase(MalformedCameraScene.ChangedSavedTransform)]
  public void Calculate_RejectsMalformedSceneOrBindingBeforeReturningAFrame(
    MalformedCameraScene malformed
  ) {
    using var fixture = new SceneFixture(
      new PlacementInput(0, new Vector3(1f, 2f, 3f), 2),
      new PlacementInput(1, new Vector3(4f, 5f, 6f), 1));
    var scene = fixture.Scene;
    switch (malformed) {
      case MalformedCameraScene.SummaryCounts:
        scene = scene with { BuiltPlacementCount = 1 };
        break;
      case MalformedCameraScene.ModelIdentity:
        scene = scene with {
          ModelBindings = ReplaceBinding(
            scene,
            0,
            scene.ModelBindings[0] with { Model = scene.Models[1] }),
        };
        break;
      case MalformedCameraScene.BatchIdentity:
        scene = scene with {
          ModelBindings = ReplaceBinding(
            scene,
            0,
            scene.ModelBindings[0] with { Batch = scene.ModelBindings[1].Batch }),
        };
        break;
      case MalformedCameraScene.NonFiniteTransform:
        var nonFinite = scene.Models[0].Transform.Matrix;
        nonFinite.M41 = float.NaN;
        scene.Models[0].Transform.Matrix = nonFinite;
        break;
      case MalformedCameraScene.ChangedSavedTransform:
        scene.Models[0].Transform.Matrix *= Matrix4x4.CreateTranslation(1f, 0f, 0f);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed));
    }

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalCameraFraming.Calculate(scene)));
  }

  private static IReadOnlyList<WildAnimalStaticSceneModelBinding> ReplaceBinding(
    WildAnimalStaticSceneBuildResult scene,
    int index,
    WildAnimalStaticSceneModelBinding replacement
  ) {
    var bindings = scene.ModelBindings.ToArray();
    bindings[index] = replacement;
    return Array.AsReadOnly(bindings);
  }

  private static IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> ReplaceBinding(
    WildAnimalFrameZeroSceneBuildResult scene,
    int index,
    WildAnimalFrameZeroSceneModelBinding replacement
  ) {
    var bindings = scene.ModelBindings.ToArray();
    bindings[index] = replacement;
    return Array.AsReadOnly(bindings);
  }

  public enum MalformedCameraScene {
    SummaryCounts,
    ModelIdentity,
    BatchIdentity,
    NonFiniteTransform,
    ChangedSavedTransform,
  }

  public enum MalformedFrameZeroScene {
    SourceIdentity,
    NonFiniteTransform,
  }

  private enum FrameZeroState {
    Exact,
    NoActiveClip,
    Weighted,
  }

  private sealed record PlacementInput(
    int Type,
    Vector3 Position,
    int BatchCount,
    FrameZeroState FrameZeroState = FrameZeroState.Exact
  );

  private sealed class SceneFixture : IDisposable {
    private readonly List<Mesh> templateMeshes = [];

    public SceneFixture(params PlacementInput[] placements) {
      if (placements.Length == 0)
        throw new ArgumentException("The fixture needs at least one placement.");
      var batchCounts = Enumerable.Range(0, 4)
        .Select(type => BatchCount(placements, type))
        .ToArray();
      var root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalCameraFraming-{Guid.NewGuid():N}"));
      var speciesCommonPath = Path.Combine(root, "WildAnimals.common.ovl");
      var modelCommonPath = Path.Combine(root, "Models.common.ovl");
      var decodedVariants = Enumerable.Range(0, 4)
        .Select(type => new WildAnimalSpeciesVariant(
          $"Animal{type}:mdl",
          $"Animal{type}:wad"))
        .ToArray();
      var decodedSpecies = new WildAnimalSpeciesDefinition(
        "Species",
        @"WildAnimals\Species_data",
        decodedVariants);
      var variantTemplates = new WildAnimalModelVariantTemplateLink[4];
      var variantLinks = new WildAnimalSpeciesModelVariantLink[4];
      foreach (var type in Enumerable.Range(0, 4)) {
        var definition = Definition(
          $"Animal{type}",
          modelCommonPath,
          batchCounts[type]);
        var source = new WildAnimalSpeciesModelResourceSource(
          new OvlFile(definition.Name, FileType.Model, modelCommonPath),
          definition);
        var link = new WildAnimalSpeciesModelVariantLink(
          type,
          decodedVariants[type],
          source);
        var batches = ModelDefinitionMeshBuilder.BuildBatches(definition);
        templateMeshes.AddRange(batches.Select(batch => batch.Mesh));
        variantLinks[type] = link;
        variantTemplates[type] = new(
          link,
          new WildAnimalModelTemplate(source, batches));
      }
      var bridge = new WildAnimalSpeciesModelResourceBridgeResult(
        "Species:was",
        speciesCommonPath,
        new OvlFile("Species", FileType.WildAnimalSpecies, speciesCommonPath),
        decodedSpecies,
        modelCommonPath,
        Array.AsReadOnly(variantLinks));
      var datSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
        100,
        true,
        @"WildAnimals\WildAnimals",
        "Species");
      var speciesResource = new WildAnimalParkSpeciesResource(
        0,
        datSpecies,
        bridge,
        Array.AsReadOnly(Enumerable.Range(0, placements.Length).ToArray()));
      var poseVariants = variantLinks
        .Select((link, type) => PoseVariant(link, root, type))
        .ToArray();
      var models = new List<Model>();
      var bindings = new List<WildAnimalStaticSceneModelBinding>();
      var frameZeroModels = new List<Model>();
      var frameZeroBindings = new List<WildAnimalFrameZeroSceneModelBinding>();
      var frameZeroSkips = new List<WildAnimalFrameZeroScenePlacementSkip>();
      var frameZeroBuiltPlacements = new HashSet<int>();
      var skinnedVertexCount = 0ul;
      var skinnedIndexCount = 0ul;
      foreach (var placementIndex in Enumerable.Range(0, placements.Length)) {
        var input = placements[placementIndex];
        var animal = new DatWildAnimalData(
          Convert.ToUInt64(1_000 + placementIndex),
          datSpecies.EntryId,
          Convert.ToUInt64(2_000 + placementIndex),
          input.Type is 0 or 1,
          input.Type is 0 or 2,
          input.Type);
        var nativeTransform = Matrix4x4.CreateTranslation(
          input.Position.X,
          input.Position.Z,
          input.Position.Y);
        var visual = new DatWildAnimalVisualData(
          animal.VisualEntryId,
          Animations(input.FrameZeroState),
          true,
          true,
          nativeTransform);
        var placement = new WildAnimalParkPlacementResource(
          placementIndex,
          new DatWildAnimalPlacementData(
            animal,
            datSpecies,
            visual,
            DatWildAnimalVariantSelectionStatus.Unsupported),
          speciesResource);
        var selection = new WildAnimalSavedVariantSelection(
          animal,
          input.Type,
          variantTemplates[input.Type]);
        var poseVariant = poseVariants[input.Type];
        var animation = WildAnimalSavedAnimationResolver.Resolve(
          visual,
          poseVariant.AnimationResources);
        var isExact = animation.Status ==
          WildAnimalSavedAnimationResolutionStatus.ExactSingleClip;
        if (!isExact) {
          var reason = animation.Status switch {
            WildAnimalSavedAnimationResolutionStatus.NoActiveClip =>
              WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
            WildAnimalSavedAnimationResolutionStatus.WeightedState =>
              WildAnimalFrameZeroSceneSkipReason.WeightedState,
            _ => throw new InvalidOperationException("Fixture animation state is not a skip."),
          };
          frameZeroSkips.Add(new(placement, selection, animation, reason));
        }
        var transform = WildAnimalWorldTransform.ToPark(nativeTransform);
        foreach (var batchIndex in Enumerable.Range(
                   0,
                   selection.Variant.Template.Batches.Count)) {
          var batch = selection.Variant.Template.Batches[batchIndex];
          var mesh = new Mesh(
            new List<Vertex>(batch.Mesh.Vertices),
            new List<uint>(batch.Mesh.Indices));
          var model = new Model(mesh) { Material = new Flat() };
          model.Transform.Matrix = transform;
          models.Add(model);
          bindings.Add(new(
            model,
            placement,
            selection,
            batchIndex,
            batch));
          if (!isExact) continue;
          var skinnedBatch = new ModelDefinitionMeshBatch(
            batch.SourceGroupIndex,
            batch.SourceMeshIndex,
            batch.SourceMeshName,
            model.Mesh);
          var poseSlot = poseVariant.Slots[
            animation.ExactSingleClipEntry!.SavedEntry.Type];
          frameZeroModels.Add(model);
          frameZeroBindings.Add(new(
            model,
            placement,
            selection,
            animation,
            poseVariant,
            poseSlot,
            batchIndex,
            skinnedBatch,
            batch));
          frameZeroBuiltPlacements.Add(placementIndex);
          skinnedVertexCount += Convert.ToUInt64(model.Mesh.Vertices.Count);
          skinnedIndexCount += Convert.ToUInt64(model.Mesh.Indices.Count);
        }
      }
      Scene = new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        placements.Length,
        placements.Length,
        0,
        placements.Length,
        0,
        models.Count,
        0,
        Convert.ToUInt64(models.Count * 3),
        Convert.ToUInt64(models.Count * 3));
      FrameZeroScene = new(
        Array.AsReadOnly(frameZeroModels.ToArray()),
        Array.AsReadOnly(frameZeroBindings.ToArray()),
        Array.AsReadOnly(frameZeroSkips.ToArray()),
        placements.Length,
        placements.Length,
        0,
        frameZeroBuiltPlacements.Count,
        frameZeroSkips.Count,
        frameZeroSkips.Count(skip =>
          skip.Reason == WildAnimalFrameZeroSceneSkipReason.NoActiveClip),
        frameZeroSkips.Count(skip =>
          skip.Reason == WildAnimalFrameZeroSceneSkipReason.WeightedState),
        frameZeroModels.Count,
        skinnedVertexCount,
        skinnedIndexCount);
    }

    public WildAnimalStaticSceneBuildResult Scene { get; }
    public WildAnimalFrameZeroSceneBuildResult FrameZeroScene { get; }

    public void Dispose() {
      foreach (var model in Scene.Models.Reverse()) model.Dispose();
      foreach (var mesh in templateMeshes.AsEnumerable().Reverse()) mesh.Dispose();
    }

    private static int BatchCount(IReadOnlyList<PlacementInput> placements, int type) {
      var counts = placements
        .Where(placement => placement.Type == type)
        .Select(placement => placement.BatchCount)
        .Distinct()
        .ToArray();
      if (counts.Length > 1 || counts.FirstOrDefault(1) <= 0)
        throw new ArgumentException("One variant needs one positive fixture batch count.");
      return counts.FirstOrDefault(1);
    }

    private static ModelDefinition Definition(string name, string path, int batchCount) => new(
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
      Groups = [Group(Enumerable.Range(0, batchCount).Select(Mesh).ToArray())],
    };

    private static ModelGroup Group(params ModelMesh[] meshes) => new(
      0,
      Convert.ToUInt16(meshes.Length),
      0,
      0,
      [],
      0,
      meshes,
      []);

    private static ModelMesh Mesh(int index) {
      var offset = Convert.ToSingle(index * 10);
      var vertices = new[] {
        Vertex(new Vector3(offset, 0f, 0f)),
        Vertex(new Vector3(offset, 0f, -1f)),
        Vertex(new Vector3(offset + 1f, 0f, 0f)),
      };
      var indices = new uint[] { 0, 2, 1 };
      return new(
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

    private static BoneShapeVertex Vertex(Vector3 position) => new(
      position,
      Vector3.UnitY,
      Vector2.Zero,
      Vector4.One,
      new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0));

    private static IReadOnlyList<DatWildAnimalAnimationData> Animations(
      FrameZeroState state
    ) => state switch {
      FrameZeroState.Exact => [new DatWildAnimalAnimationData(0f, 0, 1f)],
      FrameZeroState.NoActiveClip => [],
      FrameZeroState.Weighted => [new DatWildAnimalAnimationData(0f, 0, 0.5f)],
      _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static WildAnimalFrameZeroPoseVariantLink PoseVariant(
      WildAnimalSpeciesModelVariantLink variant,
      string root,
      int type
    ) {
      var animationCommonPath = Path.Combine(root, $"Animal{type}Anims.common.ovl");
      var animationName = $"Animal{type}Idle";
      var animation = AnimationDefinition(animationName, animationCommonPath);
      var source = new WildAnimalModelAnimationResourceSource(
        new OvlFile(animationName, FileType.ModelAnim, animationCommonPath),
        animation);
      var references = Enumerable.Range(0, 31)
        .Select(index => index == 0 ? $"{animationName}:modelanim" : ":modelanim")
        .ToArray();
      var slots = references.Select((reference, index) => index == 0
        ? new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Resolved,
          source)
        : new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Placeholder,
          null)).ToArray();
      var wadName = $"Animal{type}";
      var resources = new WildAnimalModelAnimationResourceBridgeResult(
        $"{wadName}:wad",
        variant.ModelSource.File.Path,
        animationCommonPath,
        new OvlFile(
          wadName,
          FileType.WildAnimalAnimData,
          ToUniquePath(animationCommonPath)),
        AnimationData(wadName, animationCommonPath, references),
        Array.AsReadOnly(slots));
      var pose = ModelAnimationFrameZeroPoseEvaluator.Evaluate(
        variant.ModelSource.Resource,
        animation);
      var poseSlots = slots.Select(slot =>
        new WildAnimalFrameZeroPoseSlotLink(slot, slot.IsResolved ? pose : null))
        .ToArray();
      return new(variant, resources, Array.AsReadOnly(poseSlots));
    }

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
      [new ModelAnimationTriple(0f, 0f, 0f)],
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
}
