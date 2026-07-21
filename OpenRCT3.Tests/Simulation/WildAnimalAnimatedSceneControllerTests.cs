// Wild Animal Animated Scene Controller Tests
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
public class WildAnimalAnimatedSceneControllerTests {
  private const float Epsilon = 0.0001f;

  [Test]
  public void BuildAndUpdate_SamplesSavedTimeLoopsByWadPeriodAndPreservesTypedSkips() {
    var fixture = new AnimatedFixture([
      new PlacementSpec(0, [new DatWildAnimalAnimationData(2f, 0, 1f)]),
      new PlacementSpec(1, []),
      new PlacementSpec(2, [
        new DatWildAnimalAnimationData(1f, 0, 0.75f),
        new DatWildAnimalAnimationData(2f, 1, 0.25f),
      ]),
    ]);
    var loaded = fixture.Load();
    using var scene = WildAnimalAnimatedScene.Adopt(loaded);
    var model = scene.Models.Single();
    var originalMaterial = model.Material;

    var controller = WildAnimalAnimatedSceneController.Build(scene);
    var entry = scene.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(scene.AnimatedPlacementCount, Is.EqualTo(1));
      Assert.That(scene.AnimatedModelCount, Is.EqualTo(1));
      Assert.That(scene.SkippedPlacementCount, Is.EqualTo(2));
      Assert.That(scene.SkippedPlacements.Select(skip => skip.Reason),
        Is.EqualTo(new[] {
          WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
          WildAnimalFrameZeroSceneSkipReason.WeightedState,
        }));
      Assert.That(entry.Period, Is.EqualTo(4f));
      Assert.That(entry.Looping, Is.True);
      Assert.That(entry.Forward, Is.True);
      Assert.That(entry.PoseVariant.AnimationResources.AnimationData.ValuesAt14[0],
        Is.EqualTo(-123f));
      Assert.That(entry.CurrentSavedTime, Is.EqualTo(2f).Within(Epsilon));
      Assert.That(entry.CurrentPose, Is.Not.Null);
      Assert.That(entry.CurrentPose!.NormalizedTime, Is.EqualTo(0.5f).Within(Epsilon));
      Assert.That(entry.CurrentPose.FrameFraction, Is.EqualTo(0.5f).Within(Epsilon));
      Assert.That(model.Mesh.Vertices[0].Position.X, Is.EqualTo(4f).Within(Epsilon));
      Assert.That(model.Material, Is.SameAs(originalMaterial));
    }

    var update = controller.Update(TimeSpan.FromSeconds(3));

    using (Assert.EnterMultipleScope()) {
      Assert.That(update.AnimatedPlacementCount, Is.EqualTo(1));
      Assert.That(update.AnimatedModelCount, Is.EqualTo(1));
      Assert.That(update.UpdatedPlacementCount, Is.EqualTo(1));
      Assert.That(update.UpdatedModelCount, Is.EqualTo(1));
      Assert.That(update.UpdatedVertexCount, Is.EqualTo(3));
      Assert.That(entry.CurrentSavedTime, Is.EqualTo(1f).Within(Epsilon));
      Assert.That(entry.CurrentPose!.SavedTime, Is.EqualTo(1f).Within(Epsilon));
      Assert.That(entry.CurrentPose.NormalizedTime, Is.EqualTo(0.25f).Within(Epsilon));
      Assert.That(model.Mesh.Vertices[0].Position.X, Is.EqualTo(2f).Within(Epsilon));
      Assert.That(model.Material, Is.SameAs(originalMaterial));
      Assert.That(model.Mesh.State, Is.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void TryUpdate_DoesNotPublishEarlierPoseWhenLaterEvaluationFails() {
    var fixture = new AnimatedFixture([
      new PlacementSpec(0, [new DatWildAnimalAnimationData(1f, 0, 1f)]),
      new PlacementSpec(0, [new DatWildAnimalAnimationData(2f, 0, 1f)]),
    ]);
    using var scene = WildAnimalAnimatedScene.Adopt(fixture.Load());
    var defaults = WildAnimalAnimatedSceneControllerOperations.Default;
    var evaluateCalls = 0;
    var resetCalls = 0;
    var disposedTemporaryMeshes = 0;
    var failOnCall = int.MaxValue;
    var operations = defaults with {
      EvaluatePose = (model, animation, time, period, loop, forward) => {
        evaluateCalls++;
        if (evaluateCalls == failOnCall)
          throw new InvalidOperationException("synthetic second-animal failure");
        return defaults.EvaluatePose(model, animation, time, period, loop, forward);
      },
      ResetMeshUpload = mesh => {
        resetCalls++;
        defaults.ResetMeshUpload(mesh);
      },
      DisposeTemporaryMesh = mesh => {
        disposedTemporaryMeshes++;
        defaults.DisposeTemporaryMesh(mesh);
      },
    };
    var controller = WildAnimalAnimatedSceneController.Build(scene, operations);
    var initialResetCalls = resetCalls;
    var initialDisposedMeshes = disposedTemporaryMeshes;
    var initialTimes = scene.Entries.Select(entry => entry.CurrentSavedTime).ToArray();
    var initialVertices = scene.Models
      .Select(model => model.Mesh.Vertices.ToArray())
      .ToArray();
    failOnCall = evaluateCalls + 2;

    var succeeded = controller.TryUpdate(
      TimeSpan.FromSeconds(0.5),
      out var result,
      out var error);

    using (Assert.EnterMultipleScope()) {
      Assert.That(succeeded, Is.False);
      Assert.That(result, Is.EqualTo(default(WildAnimalAnimatedSceneUpdateResult)));
      Assert.That(error, Is.TypeOf<InvalidOperationException>());
      Assert.That(error!.Message, Is.EqualTo("synthetic second-animal failure"));
      Assert.That(resetCalls, Is.EqualTo(initialResetCalls));
      Assert.That(disposedTemporaryMeshes, Is.EqualTo(initialDisposedMeshes + 1));
      Assert.That(scene.Entries.Select(entry => entry.CurrentSavedTime),
        Is.EqualTo(initialTimes));
    }
    foreach (var index in Enumerable.Range(0, scene.Models.Count))
      Assert.That(scene.Models[index].Mesh.Vertices, Is.EqualTo(initialVertices[index]));
  }

  [Test]
  public void TryUpdate_RejectsSceneOwnedTemporaryMeshWithoutChangingSceneOrTime() {
    var fixture = new AnimatedFixture([
      new PlacementSpec(0, [new DatWildAnimalAnimationData(1f, 0, 1f)]),
    ]);
    using var scene = WildAnimalAnimatedScene.Adopt(fixture.Load());
    var defaults = WildAnimalAnimatedSceneControllerOperations.Default;
    var targetModel = scene.Models.Single();
    var targetMesh = targetModel.Mesh;
    var targetMaterial = targetModel.Material;
    var returnSceneMesh = false;
    var disposedSceneMesh = false;
    var operations = defaults with {
      BuildBatches = pose => {
        if (!returnSceneMesh) return defaults.BuildBatches(pose);
        var source = scene.Entries.Single().Bindings.Single().SkinnedBatch;
        return [new ModelDefinitionMeshBatch(
          source.SourceGroupIndex,
          source.SourceMeshIndex,
          source.SourceMeshName,
          targetMesh)];
      },
      DisposeTemporaryMesh = mesh => {
        if (ReferenceEquals(mesh, targetMesh)) disposedSceneMesh = true;
        defaults.DisposeTemporaryMesh(mesh);
      },
    };
    var controller = WildAnimalAnimatedSceneController.Build(scene, operations);
    var entry = scene.Entries.Single();
    var initialVertices = targetMesh.Vertices.ToArray();
    var initialState = targetMesh.State;
    var initialTime = entry.CurrentSavedTime;
    var initialPose = entry.CurrentPose;
    returnSceneMesh = true;

    var succeeded = controller.TryUpdate(
      TimeSpan.FromSeconds(0.5),
      out var result,
      out var error);

    using (Assert.EnterMultipleScope()) {
      Assert.That(succeeded, Is.False);
      Assert.That(result, Is.EqualTo(default(WildAnimalAnimatedSceneUpdateResult)));
      Assert.That(error, Is.TypeOf<InvalidDataException>());
      Assert.That(error!.Message, Does.Contain("scene-owned"));
      Assert.That(disposedSceneMesh, Is.False);
      Assert.That(targetMesh.State, Is.EqualTo(initialState));
      Assert.That(targetMesh.Vertices, Is.EqualTo(initialVertices));
      Assert.That(entry.CurrentSavedTime, Is.EqualTo(initialTime));
      Assert.That(entry.CurrentPose, Is.SameAs(initialPose));
      Assert.That(targetModel.Material, Is.SameAs(targetMaterial));
    }
  }

  [TestCase(MalformedAnimatedSkip.DetachedPlacementIdentity)]
  [TestCase(MalformedAnimatedSkip.BuiltPlacementOverlap)]
  [TestCase(MalformedAnimatedSkip.WrongSelectionEvidence)]
  [TestCase(MalformedAnimatedSkip.WrongVisualEvidence)]
  public void Adopt_RejectsMalformedTypedSkipAndReleasesTransferredModels(
    MalformedAnimatedSkip malformed
  ) {
    var fixture = new AnimatedFixture([
      new PlacementSpec(0, [new DatWildAnimalAnimationData(1f, 0, 1f)]),
      new PlacementSpec(1, []),
    ]);
    var loaded = fixture.Load();
    var original = loaded.Scene.SkippedPlacements.Single();
    var changed = malformed switch {
      MalformedAnimatedSkip.DetachedPlacementIdentity => original with {
        Placement = new WildAnimalParkPlacementResource(
          original.Placement.PlacementIndex,
          original.Placement.Placement,
          original.Placement.SpeciesResource),
      },
      MalformedAnimatedSkip.BuiltPlacementOverlap => original with {
        Placement = loaded.Resources.Placements[0],
      },
      MalformedAnimatedSkip.WrongSelectionEvidence => original with {
        Selection = loaded.Scene.ModelBindings.Single().Selection,
      },
      MalformedAnimatedSkip.WrongVisualEvidence => original with {
        Animation = original.Animation with {
          Visual = loaded.Resources.Placements[0].Placement.Visual,
        },
      },
      _ => throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null),
    };
    var changedScene = loaded.Scene with {
      SkippedPlacements = Array.AsReadOnly(new[] { changed }),
    };
    var changedLoad = loaded with { Scene = changedScene };
    var model = loaded.Scene.Models.Single();
    var mesh = model.Mesh;
    var material = model.Material!;

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalAnimatedScene.Adopt(changedLoad)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(material.State, Is.EqualTo(State.Disposed));
      Assert.That(model.Material, Is.Null);
    }
  }

  [Test]
  public void Adopt_InvalidWadPeriodReleasesTransferredSceneModels() {
    var fixture = new AnimatedFixture([
      new PlacementSpec(0, [new DatWildAnimalAnimationData(1f, 0, 1f)]),
    ], period: 0f);
    var loaded = fixture.Load();
    var model = loaded.Scene.Models.Single();
    var mesh = model.Mesh;
    var material = model.Material!;

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalAnimatedScene.Adopt(loaded)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("period"));
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(material.State, Is.EqualTo(State.Disposed));
      Assert.That(model.Material, Is.Null);
    }
  }

  private sealed record PlacementSpec(
    int Type,
    IReadOnlyList<DatWildAnimalAnimationData> Animations
  );

  private sealed class AnimatedFixture {
    private readonly WildAnimalParkResourceRegistry resources;
    private readonly WildAnimalModelAnimationResourceBridgeResult animationResources;

    public AnimatedFixture(
      IReadOnlyList<PlacementSpec> placements,
      float period = 4f
    ) {
      InstallRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalAnimatedScene-{Guid.NewGuid():N}"));
      var speciesCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "WildAnimals.common.ovl");
      var modelCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "Elephant",
        "Elephant_data.common.ovl");
      var animationCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "Elephant",
        "Elephant_anims.common.ovl");
      var variants = Enumerable.Range(0, 4)
        .Select(_ => new WildAnimalSpeciesVariant(
          "ElephantModel:mdl",
          "ElephantAnimation:wad"))
        .ToArray();
      var species = new WildAnimalSpeciesDefinition(
        "Elephant",
        @"WildAnimals\Elephant\Elephant_data",
        variants);
      var model = CreateModel("ElephantModel", modelCommonPath);
      var modelSource = new WildAnimalSpeciesModelResourceSource(
        new OvlFile("ElephantModel", FileType.Model, modelCommonPath),
        model);
      var bridge = new WildAnimalSpeciesModelResourceBridgeResult(
        "Elephant:was",
        speciesCommonPath,
        new OvlFile(
          "Elephant",
          FileType.WildAnimalSpecies,
          ToUniquePath(speciesCommonPath)),
        species,
        modelCommonPath,
        variants.Select((variant, index) =>
          new WildAnimalSpeciesModelVariantLink(index, variant, modelSource)).ToArray());
      animationResources = CreateAnimationBridge(
        modelCommonPath,
        animationCommonPath,
        period);

      var datSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
        100,
        true,
        @"WildAnimals\WildAnimals",
        "Elephant");
      Park = new Park();
      Park.WildAnimalPlacements.AddRange(placements.Select((placement, index) => {
        var animalEntryId = Convert.ToUInt64(1_000 + index);
        var visualEntryId = Convert.ToUInt64(2_000 + index);
        return new DatWildAnimalPlacementData(
          new DatWildAnimalData(
            animalEntryId,
            datSpecies.EntryId,
            visualEntryId,
            placement.Type is 0 or 1,
            placement.Type is 0 or 2,
            placement.Type),
          datSpecies,
          new DatWildAnimalVisualData(
            visualEntryId,
            placement.Animations,
            true,
            true,
            Matrix4x4.Identity),
          DatWildAnimalVariantSelectionStatus.Unsupported);
      }));
      resources = WildAnimalParkResourceRegistry.Build(
        Park,
        InstallRoot,
        new SingleResolver(bridge));
    }

    public string InstallRoot { get; }
    public Park Park { get; }

    public WildAnimalFrameZeroSceneLoadResult Load() {
      var operations = WildAnimalFrameZeroSceneLoaderOperations.Default with {
        BuildParkResources = (_, _) => resources,
        ResolveAnimationResources = (_, _) => animationResources,
        CreateMaterialLease = _ => new SyntheticMaterialLease(),
      };
      return WildAnimalFrameZeroSceneLoader.Load(Park, InstallRoot, operations);
    }

    private static ModelDefinition CreateModel(string name, string path) => new(
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
        1,
        0,
        0,
        [],
        0,
        [CreateMesh()],
        [])],
    };

    private static ModelMesh CreateMesh() {
      var vertices = new[] {
        CreateVertex(new Vector3(0f, 0f, 0f)),
        CreateVertex(new Vector3(0f, 0f, -1f)),
        CreateVertex(new Vector3(1f, 0f, 0f)),
      };
      return new ModelMesh(
        0x1305,
        3,
        1,
        3,
        0,
        0,
        0,
        0,
        vertices,
        new uint[] { 0, 2, 1 });
    }

    private static BoneShapeVertex CreateVertex(Vector3 position) => new(
      position,
      Vector3.UnitY,
      Vector2.Zero,
      Vector4.One,
      new BoneShapeSkinning(0, 255, 255, 255, 255, 0, 0, 0));

    private static WildAnimalModelAnimationResourceBridgeResult CreateAnimationBridge(
      string modelCommonPath,
      string animationCommonPath,
      float period
    ) {
      var animation = new ModelAnimationDefinition(
        "ElephantWalk",
        animationCommonPath,
        300,
        4f,
        2,
        400,
        500,
        1,
        1,
        600,
        700,
        new uint[] { 0 },
        new uint[] { 0 },
        [new ModelAnimationTriple(0f, 0f, 0f),
          new ModelAnimationTriple(8f, 0f, 0f)],
        [new ModelAnimationFourTuple(0f, 0f, 0f, 1f),
          new ModelAnimationFourTuple(0f, 0f, 0f, 1f)],
        ["Root"],
        ["Root"]);
      var source = new WildAnimalModelAnimationResourceSource(
        new OvlFile("ElephantWalk", FileType.ModelAnim, animationCommonPath),
        animation);
      var references = Enumerable.Range(0, 31)
        .Select(index => index is 0 or 1 ? "ElephantWalk:modelanim" : ":modelanim")
        .ToArray();
      var wad = new WildAnimalAnimationDataDefinition(
        "ElephantAnimation",
        ToUniquePath(animationCommonPath),
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
        Enumerable.Repeat(period, 31).ToArray(),
        Enumerable.Repeat(-123f, 31).ToArray());
      var slots = references.Select((reference, index) => reference == ":modelanim"
        ? new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Placeholder,
          null)
        : new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Resolved,
          source)).ToArray();
      return new(
        "ElephantAnimation:wad",
        modelCommonPath,
        animationCommonPath,
        new OvlFile(
          "ElephantAnimation",
          FileType.WildAnimalAnimData,
          ToUniquePath(animationCommonPath)),
        wad,
        slots);
    }

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private sealed class SyntheticMaterialLease : IWildAnimalFrameZeroMaterialLease {
    public int MaterialCount => 1;
    public int MaterialBindingCount => 1;

    public Material ResolveMaterial(
      WildAnimalSavedVariantSelection _,
      ModelDefinitionMeshBatch __
    ) => new Flat();

    public void Dispose() { }
  }

  private sealed class SingleResolver(WildAnimalSpeciesModelResourceBridgeResult result)
    : IWildAnimalParkSpeciesResourceResolver {
    public WildAnimalSpeciesModelResourceBridgeResult Resolve(string _, string __) => result;
  }

  public enum MalformedAnimatedSkip {
    DetachedPlacementIdentity,
    BuiltPlacementOverlap,
    WrongSelectionEvidence,
    WrongVisualEvidence,
  }
}
