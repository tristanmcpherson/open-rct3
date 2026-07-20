// Wild Animal Ostrich Installed Pipeline Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

using RenderTexture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalOstrichInstalledPipelineTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the OstrichFarm DAT.")]
  public void OstrichFarm_DatThroughInstalledWasModelsAndStaticScenePreservesAllSixteenAnimals() {
    var installRoot = RequireInstallRoot();
    var fixture = LoadOstrichFarm(installRoot);
    var data = fixture.Data;
    var terrain = fixture.Terrain;
    var park = fixture.Park;
    var templates = new List<WildAnimalModelTemplateRegistry>();
    WildAnimalStaticSceneBuildResult? scene = null;
    Mesh[] sceneMeshes = [];
    Material[] sceneMaterials = [];
    Mesh[] templateMeshes = [];
    try {
      var resources = WildAnimalParkResourceRegistry.Build(park, installRoot);
      foreach (var species in resources.SpeciesResources)
        templates.Add(WildAnimalModelTemplateRegistry.Build(species.Bridge));

      var speciesResource = resources.SpeciesResources.Single();
      var bridge = speciesResource.Bridge;
      var template = templates.Single();
      var expectedVariantModels = new[] {
        "MaleOstrich:mdl",
        "FemaleOstrich:mdl",
        "BabyOstrich:mdl",
        "BabyOstrich:mdl",
      };
      var expectedVariantAnimations = new[] {
        "MaleOstrich:wad",
        "MaleOstrich:wad",
        "BabyOstrich:wad",
        "BabyOstrich:wad",
      };
      var expectedModelCommonPath = Path.Combine(
        Path.GetFullPath(installRoot),
        "WildAnimals",
        "Ostrich",
        "Ostrich_data.common.ovl");

      using (Assert.EnterMultipleScope()) {
        Assert.That(data.WildAnimalSpeciesDatabaseEntries, Has.Count.EqualTo(21));
        Assert.That(data.WildAnimals, Has.Count.EqualTo(16));
        Assert.That(data.WildAnimalVisuals, Has.Count.EqualTo(16));
        Assert.That(data.WildAnimalPlacements, Has.Count.EqualTo(16));
        Assert.That(park.WildAnimalPlacements, Has.Count.EqualTo(16));
        Assert.That(resources.SpeciesResources, Has.Count.EqualTo(1));
        Assert.That(resources.Placements, Has.Count.EqualTo(16));
        Assert.That(speciesResource.RegistryIndex, Is.Zero);
        Assert.That(speciesResource.Species.EntryId, Is.EqualTo(8_115));
        Assert.That(speciesResource.Species.OverlayFilename,
          Is.EqualTo(@"WildAnimals\WildAnimals"));
        Assert.That(speciesResource.Species.SymbolName, Is.EqualTo("Ostrich"));
        Assert.That(speciesResource.PlacementIndices,
          Is.EqualTo(Enumerable.Range(0, 16)));
        Assert.That(resources.Placements.Select(placement => placement.PlacementIndex),
          Is.EqualTo(Enumerable.Range(0, 16)));
        Assert.That(resources.Placements.Select(placement =>
          placement.Placement.Animal.EntryId),
          Is.EqualTo(data.WildAnimals.Select(animal => animal.EntryId)));
        Assert.That(resources.Placements.Select(placement =>
          placement.VariantSelectionStatus),
          Is.All.EqualTo(DatWildAnimalVariantSelectionStatus.Unsupported));
        Assert.That(resources.Placements.Select(placement =>
          placement.Placement.Visual.Visible), Is.All.True);
        Assert.That(bridge.SpeciesReference, Is.EqualTo("Ostrich:was"));
        Assert.That(bridge.SpeciesFile.Type, Is.EqualTo(FileType.WildAnimalSpecies));
        Assert.That(bridge.SpeciesFile.Name, Is.EqualTo("Ostrich"));
        Assert.That(bridge.Species.Name, Is.EqualTo("Ostrich"));
        Assert.That(bridge.Species.PackagePath,
          Is.EqualTo(@"WildAnimals\Ostrich\Ostrich_data"));
        Assert.That(bridge.ModelPackageCommonPath,
          Is.EqualTo(expectedModelCommonPath).IgnoreCase);
        Assert.That(bridge.Variants.Select(variant => variant.SerializedIndex),
          Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(bridge.Variants.Select(variant => variant.Variant.ModelReference),
          Is.EqualTo(expectedVariantModels));
        Assert.That(bridge.Variants.Select(variant =>
          variant.Variant.AnimationDataReference),
          Is.EqualTo(expectedVariantAnimations));
        Assert.That(template.VariantCount, Is.EqualTo(4));
        Assert.That(template.DistinctModelSourceCount, Is.EqualTo(3));
        Assert.That(template.Templates.Select(model => model.ModelSource.Identity),
          Is.EqualTo(new[] {
            "MaleOstrich:mdl",
            "FemaleOstrich:mdl",
            "BabyOstrich:mdl",
          }));
        Assert.That(template.BatchCount, Is.GreaterThan(0));
        Assert.That(template.VertexCount, Is.GreaterThan(0));
        Assert.That(template.IndexCount, Is.GreaterThan(0));
      }
      foreach (var index in Enumerable.Range(0, resources.Placements.Count)) {
        Assert.That(
          resources.Placements[index].Placement,
          Is.SameAs(data.WildAnimalPlacements[index]));
        Assert.That(
          resources.Placements[index].SpeciesResource,
          Is.SameAs(speciesResource));
      }

      var selections = resources.Placements.Select(placement =>
        WildAnimalSavedVariantSelector.Select(
          placement.Placement.Animal,
          templates[placement.SpeciesResource.RegistryIndex])).ToArray();
      var transforms = resources.Placements.Select(placement =>
        WildAnimalWorldTransform.ToPark(
          placement.Placement.Visual.WorldMatrix)).ToArray();
      using (Assert.EnterMultipleScope()) {
        Assert.That(selections, Has.Length.EqualTo(16));
        Assert.That(selections.Select(selection => selection.Animal.EntryId),
          Is.EqualTo(data.WildAnimals.Select(animal => animal.EntryId)));
        Assert.That(selections.Select(selection => selection.SerializedVariantIndex),
          Is.EqualTo(data.WildAnimals.Select(animal => animal.Type)));
        Assert.That(selections.Select(selection => selection.SerializedVariantIndex).Distinct(),
          Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        Assert.That(selections.Select(selection =>
          selection.Variant.VariantLink.Variant.ModelReference),
          Is.EqualTo(data.WildAnimals.Select(animal => expectedVariantModels[animal.Type])));
        Assert.That(transforms, Has.Length.EqualTo(16));
        Assert.That(transforms.All(IsFinite), Is.True);
      }
      foreach (var index in Enumerable.Range(0, selections.Length)) {
        Assert.That(selections[index].Animal,
          Is.SameAs(resources.Placements[index].Placement.Animal));
        Assert.That(selections[index].Variant,
          Is.SameAs(template.Variants[selections[index].SerializedVariantIndex]));
      }
      var firstOstrichIndex = Enumerable.Range(0, resources.Placements.Count).Single(index =>
        resources.Placements[index].Placement.Animal.EntryId == 8_733);
      using (Assert.EnterMultipleScope()) {
        Assert.That(transforms[firstOstrichIndex].M41,
          Is.EqualTo(-60.26788f).Within(0.00001f));
        Assert.That(transforms[firstOstrichIndex].M42,
          Is.EqualTo(103.0577f).Within(0.0001f));
        Assert.That(transforms[firstOstrichIndex].M43,
          Is.EqualTo(-1.406913f).Within(0.00001f));
      }

      var expectedBatches = selections.SelectMany((selection, placementIndex) =>
        selection.Variant.Template.Batches.Select((batch, batchIndex) => new {
          PlacementIndex = placementIndex,
          Selection = selection,
          BatchIndex = batchIndex,
          Batch = batch,
          Transform = transforms[placementIndex],
        })).ToArray();
      var materialRequests = new List<(
        WildAnimalParkPlacementResource Placement,
        WildAnimalSavedVariantSelection Selection,
        ModelDefinitionMeshBatch Batch,
        Flat Material)>();
      scene = WildAnimalStaticSceneBuilder.Build(
        resources,
        templates,
        (placement, selection, batch) => {
          var material = new Flat();
          materialRequests.Add((placement, selection, batch, material));
          return material;
        });
      sceneMeshes = scene.Models.Select(model => model.Mesh).ToArray();
      sceneMaterials = scene.Models.Select(model => model.Material!).ToArray();
      templateMeshes = templates.SelectMany(species =>
        species.Templates.SelectMany(model =>
          model.Batches.Select(batch => batch.Mesh))).ToArray();

      var expectedVertexCount = expectedBatches.Aggregate(
        0ul,
        (total, item) => checked(
          total + Convert.ToUInt64(item.Batch.Mesh.Vertices.Count)));
      var expectedIndexCount = expectedBatches.Aggregate(
        0ul,
        (total, item) => checked(
          total + Convert.ToUInt64(item.Batch.Mesh.Indices.Count)));
      using (Assert.EnterMultipleScope()) {
        Assert.That(scene.SourcePlacementCount, Is.EqualTo(16));
        Assert.That(scene.VisiblePlacementCount, Is.EqualTo(16));
        Assert.That(scene.HiddenPlacementCount, Is.Zero);
        Assert.That(scene.BuiltPlacementCount, Is.EqualTo(16));
        Assert.That(scene.SkippedPlacementCount, Is.Zero);
        Assert.That(scene.MissingMaterialBatchCount, Is.Zero);
        Assert.That(scene.ModelCount, Is.EqualTo(expectedBatches.Length));
        Assert.That(scene.Models, Has.Count.EqualTo(expectedBatches.Length));
        Assert.That(scene.ModelBindings, Has.Count.EqualTo(expectedBatches.Length));
        Assert.That(materialRequests, Has.Count.EqualTo(expectedBatches.Length));
        Assert.That(scene.ClonedVertexCount, Is.EqualTo(expectedVertexCount));
        Assert.That(scene.ClonedIndexCount, Is.EqualTo(expectedIndexCount));
        Assert.That(sceneMeshes.Distinct(ReferenceEqualityComparer.Instance).Count(),
          Is.EqualTo(sceneMeshes.Length));
        Assert.That(sceneMaterials.Distinct(ReferenceEqualityComparer.Instance).Count(),
          Is.EqualTo(sceneMaterials.Length));
        Assert.That(sceneMaterials, Is.All.TypeOf<Flat>());
        Assert.That(sceneMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Ready));
        Assert.That(sceneMaterials.Select(material => material.State),
          Is.All.EqualTo(State.Ready));
        Assert.That(scene.Models.All(model => IsFinite(model.Transform.Matrix)), Is.True);
        Assert.That(sceneMeshes.Any(mesh => templateMeshes.Contains(
          mesh,
          ReferenceEqualityComparer.Instance)), Is.False);
      }
      foreach (var index in Enumerable.Range(0, expectedBatches.Length)) {
        var expected = expectedBatches[index];
        var binding = scene.ModelBindings[index];
        var request = materialRequests[index];
        using (Assert.EnterMultipleScope()) {
          Assert.That(binding.Model, Is.SameAs(scene.Models[index]));
          Assert.That(binding.Placement,
            Is.SameAs(resources.Placements[expected.PlacementIndex]));
          Assert.That(binding.Selection.Animal, Is.SameAs(expected.Selection.Animal));
          Assert.That(binding.Selection.Variant, Is.SameAs(expected.Selection.Variant));
          Assert.That(binding.MaterialBatchIndex, Is.EqualTo(expected.BatchIndex));
          Assert.That(binding.Batch, Is.SameAs(expected.Batch));
          Assert.That(binding.Model.Transform.Matrix, Is.EqualTo(expected.Transform));
          Assert.That(request.Placement, Is.SameAs(binding.Placement));
          Assert.That(request.Selection, Is.SameAs(binding.Selection));
          Assert.That(request.Batch, Is.SameAs(binding.Batch));
          Assert.That(binding.Model.Material, Is.SameAs(request.Material));
        }
      }
    } finally {
      if (scene != null)
        foreach (var model in scene.Models.Reverse()) model.Dispose();
      foreach (var template in templates.AsEnumerable().Reverse()) template.Dispose();
      terrain.TextureCatalog?.Dispose();
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(templates.Select(template => template.IsDisposed), Is.All.True);
      Assert.That(templateMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(sceneMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(sceneMaterials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the OstrichFarm DAT.")]
  public void OstrichFarm_ProductionSceneLoaderBuildsExactTexturedSceneAndTransfersLeases() {
    var installRoot = RequireInstallRoot();
    var fixture = LoadOstrichFarm(installRoot);
    WildAnimalSceneLoadResult? loaded = null;
    Model[] sceneModels = [];
    Mesh[] sceneMeshes = [];
    Material[] sceneMaterials = [];
    RenderTexture[] sceneTextures = [];
    try {
      loaded = WildAnimalSceneLoader.Load(fixture.Park, installRoot);
      sceneModels = loaded.Scene.Models.ToArray();
      sceneMeshes = sceneModels.Select(model => model.Mesh).ToArray();
      sceneMaterials = sceneModels.Select(model => model.Material!).ToArray();
      Assert.That(sceneMaterials, Is.All.TypeOf<Textured>());
      var textured = sceneMaterials.Cast<Textured>().ToArray();
      Assert.That(textured.Select(material => material.AlbedoTexture), Is.All.Not.Null);
      sceneTextures = textured.Select(material => material.AlbedoTexture!).ToArray();
      var distinctTextures = sceneTextures
        .Distinct(ReferenceEqualityComparer.Instance)
        .Cast<RenderTexture>()
        .ToArray();

      var species = loaded.Resources.SpeciesResources.Single();
      using var evidence = WildAnimalModelMaterialResourceBridge.ResolveInstalled(
        species.Bridge);
      var evidenceStyles = evidence.Materials
        .Select(material => material.TextureStyleReference)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(style => style, StringComparer.OrdinalIgnoreCase)
        .ToArray();
      var expectedStates = evidenceStyles
        .Select(ExpectedMaterialState)
        .Distinct()
        .ToArray();
      var actualStates = textured
        .Select(MaterialState)
        .Distinct()
        .ToArray();
      var expectedBlendModes = expectedStates
        .Select(state => state.BlendMode)
        .Distinct()
        .ToArray();
      var expectedCulling = expectedStates
        .Select(state => state.CullBackFaces)
        .Distinct()
        .ToArray();

      using (Assert.EnterMultipleScope()) {
        Assert.That(loaded.SpeciesCount, Is.EqualTo(1));
        Assert.That(loaded.DistinctModelCount, Is.EqualTo(3));
        Assert.That(loaded.MaterialCount, Is.EqualTo(evidence.Materials.Count));
        Assert.That(loaded.MaterialCount, Is.GreaterThan(0));
        Assert.That(loaded.MaterialBindingCount, Is.EqualTo(evidence.Bindings.Count));
        Assert.That(loaded.MaterialBindingCount, Is.GreaterThan(0));
        Assert.That(loaded.Scene.SourcePlacementCount, Is.EqualTo(16));
        Assert.That(loaded.Scene.VisiblePlacementCount, Is.EqualTo(16));
        Assert.That(loaded.Scene.HiddenPlacementCount, Is.Zero);
        Assert.That(loaded.Scene.BuiltPlacementCount, Is.EqualTo(16));
        Assert.That(loaded.Scene.SkippedPlacementCount, Is.Zero);
        Assert.That(loaded.Scene.MissingMaterialBatchCount, Is.Zero);
        Assert.That(loaded.Scene.ModelCount, Is.EqualTo(sceneModels.Length));
        Assert.That(loaded.Scene.ModelBindings, Has.Count.EqualTo(sceneModels.Length));
        Assert.That(loaded.Resources.SpeciesResources, Has.Count.EqualTo(1));
        Assert.That(loaded.Resources.Placements, Has.Count.EqualTo(16));
        Assert.That(sceneModels, Is.Not.Empty);
        Assert.That(sceneMaterials
          .Distinct(ReferenceEqualityComparer.Instance).Count(),
          Is.EqualTo(sceneMaterials.Length));
        Assert.That(sceneMeshes
          .Distinct(ReferenceEqualityComparer.Instance).Count(),
          Is.EqualTo(sceneMeshes.Length));
        Assert.That(distinctTextures, Has.Length.EqualTo(loaded.MaterialCount));
        Assert.That(distinctTextures.Length, Is.LessThan(sceneTextures.Length));
        Assert.That(distinctTextures.Select(texture => texture.State),
          Is.All.EqualTo(State.Uninitialized));
        Assert.That(textured.Select(material => material.State),
          Is.All.EqualTo(State.Uninitialized));
        Assert.That(sceneMeshes.All(mesh => mesh.State != State.Disposed), Is.True);
        Assert.That(evidenceStyles, Is.Not.Empty);
        Assert.That(expectedStates.Select(state => state.BlendMode).All(mode =>
          mode is MaterialBlendMode.Opaque or MaterialBlendMode.AlphaMask), Is.True);
        Assert.That(actualStates, Is.EquivalentTo(expectedStates),
          $"Installed TXS evidence: {string.Join(", ", evidenceStyles)}");
        Assert.That(actualStates.Select(state => state.BlendMode).Distinct(),
          Is.EquivalentTo(expectedBlendModes));
        Assert.That(actualStates.Select(state => state.CullBackFaces).Distinct(),
          Is.EquivalentTo(expectedCulling));
        Assert.That(actualStates.Any(state =>
          state.BlendMode == MaterialBlendMode.Opaque),
          Is.EqualTo(expectedStates.Any(state =>
            state.BlendMode == MaterialBlendMode.Opaque)));
        Assert.That(actualStates.Any(state =>
          state.BlendMode == MaterialBlendMode.AlphaMask),
          Is.EqualTo(expectedStates.Any(state =>
            state.BlendMode == MaterialBlendMode.AlphaMask)));
        Assert.That(actualStates.Any(state => state.CullBackFaces),
          Is.EqualTo(expectedStates.Any(state => state.CullBackFaces)));
        Assert.That(actualStates.Any(state => !state.CullBackFaces),
          Is.EqualTo(expectedStates.Any(state => !state.CullBackFaces)));
        Assert.That(sceneModels.All(model => IsFinite(model.Transform.Matrix)), Is.True);
      }

      foreach (var index in Enumerable.Range(0, loaded.Scene.ModelBindings.Count)) {
        var binding = loaded.Scene.ModelBindings[index];
        var placementIndex = binding.Placement.PlacementIndex;
        var expectedPlacement = loaded.Resources.Placements[placementIndex];
        var expectedVariant = species.Bridge.Variants[
          binding.Selection.SerializedVariantIndex];
        var expectedBatch = binding.Selection.Variant.Template.Batches[
          binding.MaterialBatchIndex];
        var expectedBinding = evidence.Bindings.Single(candidate =>
          ReferenceEquals(
            candidate.ModelSource,
            binding.Selection.Variant.Template.ModelSource) &&
          candidate.SourceGroupIndex == binding.Batch.SourceGroupIndex &&
          candidate.SourceMeshIndex == binding.Batch.SourceMeshIndex);
        var expectedMaterialState = ExpectedMaterialState(
          expectedBinding.Material.TextureStyleReference);
        var material = (Textured)binding.Model.Material!;
        var expectedTransform = WildAnimalWorldTransform.ToPark(
          binding.Placement.Placement.Visual.WorldMatrix);

        using (Assert.EnterMultipleScope()) {
          Assert.That(binding.Model, Is.SameAs(sceneModels[index]));
          Assert.That(binding.Placement, Is.SameAs(expectedPlacement));
          Assert.That(binding.Selection.Animal,
            Is.SameAs(binding.Placement.Placement.Animal));
          Assert.That(binding.Selection.SerializedVariantIndex,
            Is.EqualTo(binding.Placement.Placement.Animal.Type));
          Assert.That(binding.Selection.Variant.VariantLink, Is.SameAs(expectedVariant));
          Assert.That(binding.Selection.Variant.Template.ModelSource,
            Is.SameAs(expectedVariant.ModelSource));
          Assert.That(binding.Batch, Is.SameAs(expectedBatch));
          Assert.That(binding.Batch.Mesh.State, Is.EqualTo(State.Disposed));
          Assert.That(binding.Model.Mesh, Is.Not.SameAs(binding.Batch.Mesh));
          Assert.That(binding.Model.Material, Is.SameAs(sceneMaterials[index]));
          Assert.That(material.AlbedoTexture!.Name,
            Is.EqualTo(expectedBinding.Material.TextureReference));
          Assert.That(MaterialState(material), Is.EqualTo(expectedMaterialState));
          Assert.That(binding.Model.Transform.Matrix, Is.EqualTo(expectedTransform));
          Assert.That(IsFinite(binding.Model.Transform.Matrix), Is.True);
        }
      }

      foreach (var expectedBinding in evidence.Bindings) {
        Assert.That(loaded.Scene.ModelBindings.Any(binding =>
          ReferenceEquals(
            binding.Selection.Variant.Template.ModelSource,
            expectedBinding.ModelSource) &&
          binding.Batch.SourceGroupIndex == expectedBinding.SourceGroupIndex &&
          binding.Batch.SourceMeshIndex == expectedBinding.SourceMeshIndex), Is.True);
      }
    } finally {
      foreach (var model in sceneModels.Reverse()) model.Dispose();
      fixture.Terrain.TextureCatalog?.Dispose();
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(sceneMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(sceneMaterials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
      Assert.That(sceneTextures.Select(texture => texture.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  private static string RequireInstallRoot() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    return installRoot!;
  }

  private static OstrichFarmFixture LoadOstrichFarm(string installRoot) {
    var mapPath = Path.Combine(
      installRoot,
      "Campaigns",
      "Base",
      "Wild",
      "OstrichFarm.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);
    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
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
      return new(data, terrain, park);
    } catch {
      terrain.TextureCatalog?.Dispose();
      throw;
    }
  }

  private static InstalledMaterialState ExpectedMaterialState(string reference) {
    const string tag = ":txs";
    if (!reference.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
      throw new AssertionException($"Installed TXS evidence is malformed: '{reference}'.");
    var style = reference[..^tag.Length];
    var cullBackFaces = !style.EndsWith("DS", StringComparison.OrdinalIgnoreCase);
    if (style.StartsWith("SIOpaque", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.Opaque, null, cullBackFaces);
    if (style.StartsWith("SIAlphaMaskLow", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.AlphaMask, 100, cullBackFaces);
    if (style.StartsWith("SIAlphaMask", StringComparison.OrdinalIgnoreCase))
      return new(
        MaterialBlendMode.AlphaMask,
        Textured.DefaultAlphaMaskReference,
        cullBackFaces);
    if (style.StartsWith("SIAlpha", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.AlphaMask, 8, cullBackFaces);
    throw new AssertionException($"Installed TXS evidence is unsupported: '{reference}'.");
  }

  private static InstalledMaterialState MaterialState(Textured material) => new(
    material.RenderState.BlendMode,
    material.AlphaReference,
    material.CullBackFaces);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private sealed record OstrichFarmFixture(
    DatTerrainData Data,
    Terrain Terrain,
    Park Park
  );

  private readonly record struct InstalledMaterialState(
    MaterialBlendMode BlendMode,
    byte? AlphaReference,
    bool CullBackFaces
  );
}
