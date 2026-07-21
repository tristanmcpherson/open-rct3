// Wild Animal Frame-Zero Scene Loader Tests
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
public class WildAnimalFrameZeroSceneLoaderTests {
  [Test]
  public void Load_ResolvesDistinctWadsBuildsTypedSceneAndReleasesTemporaryOwners() {
    var fixture = new LoaderFixture([
      new PlacementSpec(
        0,
        [new DatWildAnimalAnimationData(8.5f, 0, 1f)]),
      new PlacementSpec(1, []),
      new PlacementSpec(2, [
        new DatWildAnimalAnimationData(1f, 0, 0.75f),
        new DatWildAnimalAnimationData(2f, 1, 0.25f),
      ]),
    ]);
    WildAnimalModelTemplateRegistry? template = null;
    var lease = new SyntheticMaterialLease(3, 4);
    var resolvedReferences = new List<string>();
    var operations = WildAnimalFrameZeroSceneLoaderOperations.Default with {
      BuildParkResources = (_, _) => fixture.Resources,
      BuildTemplates = resources =>
        template = WildAnimalModelTemplateRegistry.Build(resources),
      ResolveAnimationResources = (_, reference) => {
        resolvedReferences.Add(reference);
        return fixture.AnimationResources[reference];
      },
      CreateMaterialLease = _ => lease,
    };
    WildAnimalFrameZeroSceneLoadResult? loaded = null;
    Model? model = null;
    Material? sceneMaterial = null;

    try {
      loaded = WildAnimalFrameZeroSceneLoader.Load(
        fixture.Park,
        fixture.InstallRoot,
        operations);
      model = loaded.Scene.Models.Single();
      sceneMaterial = model.Material;
      var provenance = loaded.SpeciesProvenance.Single();

      using (Assert.EnterMultipleScope()) {
        Assert.That(resolvedReferences, Is.EqualTo(new[] {
          "ElephantAnimationA:wad",
          "ElephantAnimationB:wad",
        }));
        Assert.That(loaded.Resources, Is.SameAs(fixture.Resources));
        Assert.That(loaded.SpeciesCount, Is.EqualTo(1));
        Assert.That(loaded.DistinctModelCount, Is.EqualTo(1));
        Assert.That(loaded.DistinctAnimationDataCount, Is.EqualTo(2));
        Assert.That(loaded.ResolvedAnimationSlotCount, Is.EqualTo(8));
        Assert.That(loaded.PlaceholderAnimationSlotCount, Is.EqualTo(116));
        Assert.That(loaded.PoseCount, Is.EqualTo(2));
        Assert.That(loaded.MaterialCount, Is.EqualTo(3));
        Assert.That(loaded.MaterialBindingCount, Is.EqualTo(4));
        Assert.That(provenance.SpeciesResource,
          Is.SameAs(fixture.Resources.SpeciesResources[0]));
        Assert.That(provenance.AnimationResources,
          Is.EqualTo(new[] {
            fixture.AnimationResources["ElephantAnimationA:wad"],
            fixture.AnimationResources["ElephantAnimationB:wad"],
          }));
        Assert.That(loaded.Scene.BuiltPlacementCount, Is.EqualTo(1));
        Assert.That(loaded.Scene.SkippedPlacementCount, Is.EqualTo(2));
        Assert.That(loaded.Scene.SkippedPlacements.Select(skip => skip.Reason),
          Is.EqualTo(new[] {
            WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
            WildAnimalFrameZeroSceneSkipReason.WeightedState,
          }));
        Assert.That(loaded.Scene.ModelBindings.Single().Animation
          .ExactSingleClipEntry!.SavedEntry.Time, Is.EqualTo(8.5f));
        Assert.That(lease.ResolveCount, Is.EqualTo(1));
        Assert.That(lease.IsDisposed, Is.True);
        Assert.That(template, Is.Not.Null);
        Assert.That(template!.IsDisposed, Is.True);
        Assert.That(model.Mesh.State, Is.EqualTo(State.Uninitialized));
        Assert.That(model.Material, Is.TypeOf<Flat>());
        Assert.That(model.Material!.State, Is.Not.EqualTo(State.Disposed));
      }
    } finally {
      model?.Dispose();
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(model, Is.Not.Null);
      Assert.That(model.Mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(sceneMaterial, Is.Not.Null);
      Assert.That(sceneMaterial!.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Load_ReleasesTemplateWhenSecondDistinctWadResolutionFails() {
    var fixture = new LoaderFixture([
      new PlacementSpec(
        0,
        [new DatWildAnimalAnimationData(0f, 0, 1f)]),
    ]);
    WildAnimalModelTemplateRegistry? template = null;
    var materialCalls = 0;
    var operations = WildAnimalFrameZeroSceneLoaderOperations.Default with {
      BuildParkResources = (_, _) => fixture.Resources,
      BuildTemplates = resources =>
        template = WildAnimalModelTemplateRegistry.Build(resources),
      ResolveAnimationResources = (_, reference) => reference.EndsWith(
          "A:wad",
          StringComparison.Ordinal)
        ? fixture.AnimationResources[reference]
        : throw new InvalidOperationException("synthetic WAD failure"),
      CreateMaterialLease = _ => {
        materialCalls++;
        return new SyntheticMaterialLease(0, 0);
      },
    };

    var error = Assert.Throws<InvalidOperationException>(new Action(() =>
      WildAnimalFrameZeroSceneLoader.Load(
        fixture.Park,
        fixture.InstallRoot,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("synthetic WAD failure"));
      Assert.That(materialCalls, Is.Zero);
      Assert.That(template, Is.Not.Null);
      Assert.That(template!.IsDisposed, Is.True);
      Assert.That(template.Templates.SelectMany(item => item.Batches)
        .Select(batch => batch.Mesh.State), Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Load_DisposesCompletedSceneWhenTemporaryCleanupFails() {
    var fixture = new LoaderFixture([
      new PlacementSpec(
        0,
        [new DatWildAnimalAnimationData(0f, 0, 1f)]),
    ]);
    var lease = new SyntheticMaterialLease(1, 1, throwOnDispose: true);
    WildAnimalModelTemplateRegistry? template = null;
    WildAnimalFrameZeroSceneBuildResult? scene = null;
    Mesh? sceneMesh = null;
    Material? sceneMaterial = null;
    var operations = WildAnimalFrameZeroSceneLoaderOperations.Default with {
      BuildParkResources = (_, _) => fixture.Resources,
      BuildTemplates = resources =>
        template = WildAnimalModelTemplateRegistry.Build(resources),
      ResolveAnimationResources = (_, reference) =>
        fixture.AnimationResources[reference],
      CreateMaterialLease = _ => lease,
      BuildScene = (resources, templates, poses, createMaterial) => {
        scene = WildAnimalFrameZeroSceneBuilder.Build(
          resources,
          templates,
          poses,
          createMaterial);
        var model = scene.Models.Single();
        sceneMesh = model.Mesh;
        sceneMaterial = model.Material;
        return scene;
      },
    };

    var error = Assert.Throws<AggregateException>(new Action(() =>
      WildAnimalFrameZeroSceneLoader.Load(
        fixture.Park,
        fixture.InstallRoot,
        operations)));
    var model = scene!.Models.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message,
        Does.Contain("temporary resources could not be released"));
      Assert.That(lease.IsDisposed, Is.True);
      Assert.That(template, Is.Not.Null);
      Assert.That(template!.IsDisposed, Is.True);
      Assert.That(sceneMesh, Is.SameAs(model.Mesh));
      Assert.That(sceneMesh!.State, Is.EqualTo(State.Disposed));
      Assert.That(sceneMaterial, Is.Not.Null);
      Assert.That(sceneMaterial!.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the OstrichFarm DAT.")]
  public void OstrichFarm_LoadsOnlyProvableFrameZeroClipsAndRetainsTypedSkips() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = Path.Combine(
      installRoot!,
      "Campaigns",
      "Base",
      "Wild",
      "OstrichFarm.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);
    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    WildAnimalFrameZeroSceneLoadResult? loaded = null;

    try {
      var park = World.BuildPark(
        terrain,
        data.WaterManager,
        data.Paths,
        data.SceneryItems,
        data.SceneryItemPlacements,
        data.TrackPieces,
        data.RideTracks,
        data.TrackSegments,
        data.TrackedRideInstances,
        data.RideTrainInstances,
        data.RideCarInstances,
        data.WildAnimalSpeciesDatabaseEntries,
        data.WildAnimalVisuals,
        data.WildAnimalPlacements);
      loaded = WildAnimalFrameZeroSceneLoader.Load(park, installRoot);

      using (Assert.EnterMultipleScope()) {
        Assert.That(loaded.SpeciesCount, Is.GreaterThan(0));
        Assert.That(loaded.DistinctAnimationDataCount, Is.GreaterThan(0));
        Assert.That(loaded.PoseCount, Is.GreaterThan(0));
        Assert.That(loaded.MaterialCount, Is.GreaterThan(0));
        Assert.That(loaded.Scene.SourcePlacementCount, Is.EqualTo(16));
        Assert.That(loaded.Scene.VisiblePlacementCount, Is.EqualTo(16));
        Assert.That(loaded.Scene.HiddenPlacementCount, Is.Zero);
        Assert.That(loaded.Scene.BuiltPlacementCount, Is.EqualTo(15));
        Assert.That(loaded.Scene.SkippedPlacementCount, Is.EqualTo(1));
        Assert.That(loaded.Scene.NoActiveClipPlacementCount, Is.Zero);
        Assert.That(loaded.Scene.WeightedStatePlacementCount, Is.EqualTo(1));
        var weighted = loaded.Scene.SkippedPlacements.Single();
        Assert.That(weighted.Reason,
          Is.EqualTo(WildAnimalFrameZeroSceneSkipReason.WeightedState));
        Assert.That(weighted.Placement.Placement.Visual.EntryId, Is.EqualTo(9_137));
        Assert.That(loaded.Scene.Models.Select(model => model.Mesh.State),
          Is.All.EqualTo(State.Uninitialized));
      }
    } finally {
      if (loaded != null)
        foreach (var model in loaded.Scene.Models.Reverse()) model.Dispose();
      terrain.TextureCatalog?.Dispose();
    }
  }

  private sealed record PlacementSpec(
    int Type,
    IReadOnlyList<DatWildAnimalAnimationData> Animations
  );

  private sealed class LoaderFixture {
    public LoaderFixture(IReadOnlyList<PlacementSpec> placements) {
      InstallRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalFrameZeroLoader-{Guid.NewGuid():N}"));
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
      var variants = new[] {
        new WildAnimalSpeciesVariant("ElephantModel:mdl", "ElephantAnimationA:wad"),
        new WildAnimalSpeciesVariant("ElephantModel:mdl", "ElephantAnimationA:wad"),
        new WildAnimalSpeciesVariant("ElephantModel:mdl", "ElephantAnimationB:wad"),
        new WildAnimalSpeciesVariant("ElephantModel:mdl", "ElephantAnimationB:wad"),
      };
      var species = new WildAnimalSpeciesDefinition(
        "Elephant",
        @"WildAnimals\Elephant\Elephant_data",
        variants);
      var model = CreateModel("ElephantModel", modelCommonPath);
      var modelSource = new WildAnimalSpeciesModelResourceSource(
        new OvlFile("ElephantModel", FileType.Model, modelCommonPath),
        model);
      var variantLinks = variants.Select((variant, index) =>
        new WildAnimalSpeciesModelVariantLink(index, variant, modelSource)).ToArray();
      var bridge = new WildAnimalSpeciesModelResourceBridgeResult(
        "Elephant:was",
        speciesCommonPath,
        new OvlFile(
          "Elephant",
          FileType.WildAnimalSpecies,
          ToUniquePath(speciesCommonPath)),
        species,
        modelCommonPath,
        variantLinks);
      AnimationResources = new Dictionary<
        string,
        WildAnimalModelAnimationResourceBridgeResult>(StringComparer.OrdinalIgnoreCase) {
        ["ElephantAnimationA:wad"] = CreateAnimationBridge(
          "ElephantAnimationA",
          modelCommonPath,
          animationCommonPath),
        ["ElephantAnimationB:wad"] = CreateAnimationBridge(
          "ElephantAnimationB",
          modelCommonPath,
          animationCommonPath),
      };

      var datSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
        100,
        true,
        @"WildAnimals\WildAnimals",
        "Elephant");
      Park = new Park();
      Park.WildAnimalPlacements.AddRange(placements.Select((placement, index) => {
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
          true,
          Matrix4x4.Identity);
        return new DatWildAnimalPlacementData(
          animal,
          datSpecies,
          visual,
          DatWildAnimalVariantSelectionStatus.Unsupported);
      }));
      Resources = WildAnimalParkResourceRegistry.Build(
        Park,
        InstallRoot,
        new SingleResolver(bridge));
    }

    public string InstallRoot { get; }
    public Park Park { get; }
    public WildAnimalParkResourceRegistry Resources { get; }
    public IReadOnlyDictionary<
      string,
      WildAnimalModelAnimationResourceBridgeResult> AnimationResources { get; }

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
      string wadName,
      string modelCommonPath,
      string animationCommonPath
    ) {
      var animation = new ModelAnimationDefinition(
        $"{wadName}Idle",
        animationCommonPath,
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
      var source = new WildAnimalModelAnimationResourceSource(
        new OvlFile(animation.Name, FileType.ModelAnim, animationCommonPath),
        animation);
      var references = Enumerable.Range(0, 31)
        .Select(index => index is 0 or 1
          ? $"{animation.Name}:modelanim"
          : ":modelanim")
        .ToArray();
      var wad = new WildAnimalAnimationDataDefinition(
        wadName,
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
        Enumerable.Repeat(0f, 31).ToArray(),
        Enumerable.Repeat(0f, 31).ToArray());
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
        $"{wadName}:wad",
        modelCommonPath,
        animationCommonPath,
        new OvlFile(
          wadName,
          FileType.WildAnimalAnimData,
          ToUniquePath(animationCommonPath)),
        wad,
        slots);
    }

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private sealed class SyntheticMaterialLease(
    int materialCount,
    int materialBindingCount,
    bool throwOnDispose = false
  ) : IWildAnimalFrameZeroMaterialLease {
    public int MaterialCount { get; } = materialCount;
    public int MaterialBindingCount { get; } = materialBindingCount;
    public int ResolveCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public Material ResolveMaterial(
      WildAnimalSavedVariantSelection _,
      ModelDefinitionMeshBatch __
    ) {
      ObjectDisposedException.ThrowIf(IsDisposed, this);
      ResolveCount++;
      return new Flat();
    }

    public void Dispose() {
      if (IsDisposed) return;
      IsDisposed = true;
      if (throwOnDispose)
        throw new InvalidOperationException("synthetic material cleanup failure");
    }
  }

  private sealed class SingleResolver(WildAnimalSpeciesModelResourceBridgeResult result)
    : IWildAnimalParkSpeciesResourceResolver {
    public WildAnimalSpeciesModelResourceBridgeResult Resolve(string _, string __) => result;
  }
}
