// Wild Animal Scene Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Exact saved Wild-animal resources and caller-owned static scene models.</summary>
internal sealed record WildAnimalSceneLoadResult(
  WildAnimalParkResourceRegistry Resources,
  WildAnimalStaticSceneBuildResult Scene,
  int SpeciesCount,
  int DistinctModelCount,
  int MaterialCount,
  int MaterialBindingCount
);

/// <summary>Loads exact installed WAS, MDL, TEX, and TXS evidence into static scene models.</summary>
internal static class WildAnimalSceneLoader {
  public static WildAnimalSceneLoadResult Load(Park park, string installRoot) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    var resources = WildAnimalParkResourceRegistry.Build(park, installRoot);
    var templates = new List<WildAnimalModelTemplateRegistry>(
      resources.SpeciesResources.Count);
    var materialResolvers = new List<WildAnimalModelMaterialResolver>(
      resources.SpeciesResources.Count);
    var owned = new List<IDisposable>();
    var distinctModelCount = 0;
    var materialCount = 0;
    var materialBindingCount = 0;
    WildAnimalStaticSceneBuildResult scene;

    try {
      foreach (var species in resources.SpeciesResources) {
        var template = WildAnimalModelTemplateRegistry.Build(species.Bridge);
        owned.Add(template);
        templates.Add(template);
        distinctModelCount = checked(
          distinctModelCount + template.DistinctModelSourceCount);

        var materialResources = WildAnimalModelMaterialResourceBridge.ResolveInstalled(
          species.Bridge);
        owned.Add(materialResources);
        materialCount = checked(materialCount + materialResources.Materials.Count);
        materialBindingCount = checked(
          materialBindingCount + materialResources.Bindings.Count);

        var materialResolver = new WildAnimalModelMaterialResolver(materialResources);
        owned.Add(materialResolver);
        materialResolvers.Add(materialResolver);
      }

      scene = WildAnimalStaticSceneBuilder.Build(
        resources,
        templates,
        (placement, selection, batch) => {
          var speciesIndex = placement.SpeciesResource.RegistryIndex;
          if (speciesIndex < 0 || speciesIndex >= materialResolvers.Count)
            throw Invalid(
              $"placement {placement.PlacementIndex} has an out-of-range species index " +
              $"{speciesIndex}");
          return materialResolvers[speciesIndex].ResolveMaterial(selection, batch);
        });
    } catch (Exception primaryError) {
      var cleanupErrors = DisposeOwned(owned);
      if (cleanupErrors.Count != 0)
        throw new AggregateException(
          "Wild-animal scene loading failed and resource cleanup also reported errors.",
          [primaryError, .. cleanupErrors]);
      throw;
    }

    var successCleanupErrors = DisposeOwned(owned);
    if (successCleanupErrors.Count != 0) {
      successCleanupErrors.AddRange(DisposeModels(scene.Models));
      throw new AggregateException(
        "Wild-animal scene resources could not be released after construction.",
        successCleanupErrors);
    }
    return new(
      resources,
      scene,
      resources.SpeciesResources.Count,
      distinctModelCount,
      materialCount,
      materialBindingCount);
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

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild-animal scene load: {message}.");
}
