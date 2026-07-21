// Wild Animal Frame-Zero Scene Loader
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Borrowed exact installed provenance retained for one used saved species.</summary>
internal sealed record WildAnimalFrameZeroSpeciesLoadProvenance(
  WildAnimalParkSpeciesResource SpeciesResource,
  IReadOnlyList<WildAnimalModelAnimationResourceBridgeResult> AnimationResources,
  int DistinctModelCount,
  int ResolvedAnimationSlotCount,
  int PlaceholderAnimationSlotCount,
  int PoseCount,
  int MaterialCount,
  int MaterialBindingCount
) {
  public int DistinctAnimationDataCount => AnimationResources.Count;
}

/// <summary>Scene-owned posed models plus borrowed exact installed source evidence.</summary>
/// <remarks>
/// The returned scene owns its models, skinned meshes, materials, and retained texture leases.
/// Park, WAS, MDL, WAD, ModelAnim, and saved-animation records are borrowed diagnostic evidence.
/// All neutral template meshes, decoded TEX owners, and material resolvers have already been
/// released before this result is returned.
/// </remarks>
internal sealed record WildAnimalFrameZeroSceneLoadResult(
  WildAnimalParkResourceRegistry Resources,
  WildAnimalFrameZeroSceneBuildResult Scene,
  IReadOnlyList<WildAnimalFrameZeroSpeciesLoadProvenance> SpeciesProvenance,
  int SpeciesCount,
  int DistinctModelCount,
  int DistinctAnimationDataCount,
  int ResolvedAnimationSlotCount,
  int PlaceholderAnimationSlotCount,
  int PoseCount,
  int MaterialCount,
  int MaterialBindingCount
);

/// <summary>Temporary exact TEX/material ownership used while posed models are constructed.</summary>
internal interface IWildAnimalFrameZeroMaterialLease : IDisposable {
  int MaterialCount { get; }
  int MaterialBindingCount { get; }

  Material ResolveMaterial(
    WildAnimalSavedVariantSelection selection,
    ModelDefinitionMeshBatch batch);
}

internal delegate WildAnimalFrameZeroSceneBuildResult
  WildAnimalFrameZeroSceneBuildOperation(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> templates,
    IReadOnlyList<WildAnimalFrameZeroPoseRegistry> poses,
    Func<
      WildAnimalParkPlacementResource,
      WildAnimalSavedVariantSelection,
      ModelDefinitionMeshBatch,
      Material?> createMaterial);

/// <summary>Installed-resource and construction seams used by focused orchestration tests.</summary>
internal sealed record WildAnimalFrameZeroSceneLoaderOperations(
  Func<Park, string, WildAnimalParkResourceRegistry> BuildParkResources,
  Func<
    WildAnimalSpeciesModelResourceBridgeResult,
    WildAnimalModelTemplateRegistry> BuildTemplates,
  Func<
    WildAnimalSpeciesModelResourceBridgeResult,
    string,
    WildAnimalModelAnimationResourceBridgeResult> ResolveAnimationResources,
  Func<
    WildAnimalSpeciesModelResourceBridgeResult,
    IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>,
    WildAnimalFrameZeroPoseRegistry> BuildPoses,
  Func<
    WildAnimalSpeciesModelResourceBridgeResult,
    IWildAnimalFrameZeroMaterialLease> CreateMaterialLease,
  WildAnimalFrameZeroSceneBuildOperation BuildScene
) {
  public static WildAnimalFrameZeroSceneLoaderOperations Default { get; } = new(
    (park, installRoot) => WildAnimalParkResourceRegistry.Build(park, installRoot),
    resources => WildAnimalModelTemplateRegistry.Build(resources),
    (resources, reference) =>
      WildAnimalModelAnimationResourceBridge.ResolveInstalled(resources, reference),
    (resources, animations) =>
      WildAnimalFrameZeroPoseRegistry.Build(resources, animations),
    InstalledWildAnimalFrameZeroMaterialLease.Create,
    (resources, templates, poses, createMaterial) =>
      WildAnimalFrameZeroSceneBuilder.Build(
        resources,
        templates,
        poses,
        createMaterial));
}

/// <summary>
/// Loads every exact installed resource needed to pose provable saved clips at frame zero.
/// </summary>
/// <remarks>
/// Saved states with no active clip or a weighted blend remain typed scene skips. Animation time,
/// interpolation, and weighted blending are deliberately not guessed here.
/// </remarks>
internal static class WildAnimalFrameZeroSceneLoader {
  public static WildAnimalFrameZeroSceneLoadResult Load(
    Park park,
    string installRoot
  ) => Load(
    park,
    installRoot,
    WildAnimalFrameZeroSceneLoaderOperations.Default);

  internal static WildAnimalFrameZeroSceneLoadResult Load(
    Park park,
    string installRoot,
    WildAnimalFrameZeroSceneLoaderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);

    var resources = operations.BuildParkResources(park, installRoot) ??
      throw Invalid("park resource builder returned null");
    if (resources.SpeciesResources == null || resources.Placements == null)
      throw Invalid("park resource registry has a null species or placement list");

    var templates = new List<WildAnimalModelTemplateRegistry>(
      resources.SpeciesResources.Count);
    var poses = new List<WildAnimalFrameZeroPoseRegistry>(
      resources.SpeciesResources.Count);
    var materials = new List<IWildAnimalFrameZeroMaterialLease>(
      resources.SpeciesResources.Count);
    var provenance = new List<WildAnimalFrameZeroSpeciesLoadProvenance>(
      resources.SpeciesResources.Count);
    var owned = new List<IDisposable>(resources.SpeciesResources.Count * 2);
    var distinctModelCount = 0;
    var distinctAnimationDataCount = 0;
    var resolvedAnimationSlotCount = 0;
    var placeholderAnimationSlotCount = 0;
    var poseCount = 0;
    var materialCount = 0;
    var materialBindingCount = 0;
    WildAnimalFrameZeroSceneBuildResult? scene = null;

    try {
      foreach (var index in Enumerable.Range(0, resources.SpeciesResources.Count)) {
        var species = resources.SpeciesResources[index] ??
          throw Invalid($"species resource {index} is null");
        if (species.RegistryIndex != index || species.Bridge == null)
          throw Invalid($"species resource {index} changed registry or bridge identity");

        var template = operations.BuildTemplates(species.Bridge) ??
          throw Invalid($"species resource {index} template builder returned null");
        owned.Add(template);
        templates.Add(template);

        var animationMap = ResolveDistinctAnimations(species, operations);
        var animationList = Array.AsReadOnly(animationMap.Values.ToArray());
        var pose = operations.BuildPoses(species.Bridge, animationMap) ??
          throw Invalid($"species resource {index} pose builder returned null");
        poses.Add(pose);

        var material = operations.CreateMaterialLease(species.Bridge) ??
          throw Invalid($"species resource {index} material lease builder returned null");
        owned.Add(material);
        materials.Add(material);
        if (material.MaterialCount < 0 || material.MaterialBindingCount < 0)
          throw Invalid($"species resource {index} material lease returned negative counts");

        var speciesProvenance = new WildAnimalFrameZeroSpeciesLoadProvenance(
          species,
          animationList,
          template.DistinctModelSourceCount,
          pose.ResolvedSlotCount,
          pose.PlaceholderSlotCount,
          pose.PoseCount,
          material.MaterialCount,
          material.MaterialBindingCount);
        provenance.Add(speciesProvenance);
        distinctModelCount = checked(
          distinctModelCount + speciesProvenance.DistinctModelCount);
        distinctAnimationDataCount = checked(
          distinctAnimationDataCount + speciesProvenance.DistinctAnimationDataCount);
        resolvedAnimationSlotCount = checked(
          resolvedAnimationSlotCount + speciesProvenance.ResolvedAnimationSlotCount);
        placeholderAnimationSlotCount = checked(
          placeholderAnimationSlotCount + speciesProvenance.PlaceholderAnimationSlotCount);
        poseCount = checked(poseCount + speciesProvenance.PoseCount);
        materialCount = checked(materialCount + speciesProvenance.MaterialCount);
        materialBindingCount = checked(
          materialBindingCount + speciesProvenance.MaterialBindingCount);
      }

      scene = operations.BuildScene(
        resources,
        Array.AsReadOnly(templates.ToArray()),
        Array.AsReadOnly(poses.ToArray()),
        (placement, selection, batch) => {
          var speciesIndex = placement.SpeciesResource.RegistryIndex;
          if (speciesIndex < 0 || speciesIndex >= materials.Count)
            throw Invalid(
              $"placement {placement.PlacementIndex} has out-of-range species index " +
              $"{speciesIndex}");
          return materials[speciesIndex].ResolveMaterial(selection, batch);
        }) ?? throw Invalid("scene builder returned null");
      if (scene.Models == null || scene.ModelBindings == null ||
          scene.SkippedPlacements == null)
        throw Invalid("scene builder returned incomplete ownership or diagnostic lists");
    } catch (Exception primaryError) {
      var cleanupErrors = DisposeOwned(owned);
      if (scene?.Models != null)
        cleanupErrors.AddRange(DisposeModels(scene.Models));
      if (cleanupErrors.Count != 0)
        throw new AggregateException(
          "Frame-zero Wild-animal scene loading failed and temporary cleanup also failed.",
          [primaryError, .. cleanupErrors]);
      throw;
    }

    var completedScene = scene!;
    var successCleanupErrors = DisposeOwned(owned);
    if (successCleanupErrors.Count != 0) {
      successCleanupErrors.AddRange(DisposeModels(completedScene.Models));
      throw new AggregateException(
        "Frame-zero Wild-animal temporary resources could not be released after construction.",
        successCleanupErrors);
    }

    return new(
      resources,
      completedScene,
      Array.AsReadOnly(provenance.ToArray()),
      resources.SpeciesResources.Count,
      distinctModelCount,
      distinctAnimationDataCount,
      resolvedAnimationSlotCount,
      placeholderAnimationSlotCount,
      poseCount,
      materialCount,
      materialBindingCount);
  }

  private static IReadOnlyDictionary<
    string,
    WildAnimalModelAnimationResourceBridgeResult> ResolveDistinctAnimations(
      WildAnimalParkSpeciesResource species,
      WildAnimalFrameZeroSceneLoaderOperations operations
    ) {
    if (species.Bridge.Variants == null || species.Bridge.Variants.Count != 4)
      throw Invalid(
        $"species resource {species.RegistryIndex} does not retain four exact variants");
    var animations = new Dictionary<
      string,
      WildAnimalModelAnimationResourceBridgeResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var variant in species.Bridge.Variants) {
      if (variant?.Variant == null)
        throw Invalid($"species resource {species.RegistryIndex} has a null variant link");
      var reference = variant.Variant.AnimationDataReference;
      if (string.IsNullOrWhiteSpace(reference) ||
          !string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
        throw Invalid(
          $"species resource {species.RegistryIndex} has an empty or padded WAD reference");
      if (animations.ContainsKey(reference)) continue;
      var animation = operations.ResolveAnimationResources(species.Bridge, reference) ??
        throw Invalid(
          $"species resource {species.RegistryIndex} animation resolver returned null for " +
          $"'{reference}'");
      animations.Add(reference, animation);
    }
    return new ReadOnlyDictionary<
      string,
      WildAnimalModelAnimationResourceBridgeResult>(animations);
  }

  private static List<Exception> DisposeOwned(List<IDisposable> resources) {
    var errors = new List<Exception>();
    while (resources.Count > 0) {
      var index = resources.Count - 1;
      var resource = resources[index];
      resources.RemoveAt(index);
      try {
        resource.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static List<Exception> DisposeModels(IReadOnlyList<Model> models) {
    var errors = new List<Exception>();
    foreach (var model in models.Reverse()) {
      try {
        model.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void ValidateOperations(
    WildAnimalFrameZeroSceneLoaderOperations operations
  ) {
    if (operations.BuildParkResources == null || operations.BuildTemplates == null ||
        operations.ResolveAnimationResources == null || operations.BuildPoses == null ||
        operations.CreateMaterialLease == null || operations.BuildScene == null)
      throw new ArgumentException(
        "Frame-zero Wild-animal scene loader operations must all be configured.",
        nameof(operations));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid frame-zero Wild-animal scene load: {message}.");
}

/// <summary>Owns exact decoded TEX sources and renderer texture handles during scene creation.</summary>
internal sealed class InstalledWildAnimalFrameZeroMaterialLease
  : IWildAnimalFrameZeroMaterialLease {
  private readonly WildAnimalModelMaterialResourceBridgeResult resources;
  private readonly WildAnimalModelMaterialResolver resolver;
  private bool disposed;

  private InstalledWildAnimalFrameZeroMaterialLease(
    WildAnimalModelMaterialResourceBridgeResult resources,
    WildAnimalModelMaterialResolver resolver
  ) {
    this.resources = resources;
    this.resolver = resolver;
  }

  public int MaterialCount => resources.Materials.Count;
  public int MaterialBindingCount => resources.Bindings.Count;

  public static IWildAnimalFrameZeroMaterialLease Create(
    WildAnimalSpeciesModelResourceBridgeResult species
  ) {
    ArgumentNullException.ThrowIfNull(species);
    var resources = WildAnimalModelMaterialResourceBridge.ResolveInstalled(species);
    try {
      return new InstalledWildAnimalFrameZeroMaterialLease(
        resources,
        new WildAnimalModelMaterialResolver(resources));
    } catch (Exception primaryError) {
      try {
        resources.Dispose();
      } catch (Exception cleanupError) {
        throw new AggregateException(primaryError, cleanupError);
      }
      throw;
    }
  }

  public Material ResolveMaterial(
    WildAnimalSavedVariantSelection selection,
    ModelDefinitionMeshBatch batch
  ) {
    ObjectDisposedException.ThrowIf(disposed, this);
    return resolver.ResolveMaterial(selection, batch);
  }

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    var errors = new List<Exception>();
    try {
      resolver.Dispose();
    } catch (Exception error) {
      errors.Add(error);
    }
    try {
      resources.Dispose();
    } catch (Exception error) {
      errors.Add(error);
    }
    if (errors.Count != 0)
      throw new AggregateException(
        "Installed frame-zero Wild-animal material cleanup reported errors.",
        errors);
  }
}
