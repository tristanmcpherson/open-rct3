// Wild Animal Frame-Zero Scene Builder
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>A visible saved animation state that cannot be represented by one frame-zero pose.</summary>
internal enum WildAnimalFrameZeroSceneSkipReason {
  NoActiveClip,
  WeightedState,
}

/// <summary>One typed visible-placement skip retaining its exact saved animation evidence.</summary>
internal sealed record WildAnimalFrameZeroScenePlacementSkip(
  WildAnimalParkPlacementResource Placement,
  WildAnimalSavedVariantSelection Selection,
  WildAnimalSavedAnimationResolution Animation,
  WildAnimalFrameZeroSceneSkipReason Reason
);

/// <summary>One scene-owned skinned model and all exact saved and decoded source bindings.</summary>
internal sealed record WildAnimalFrameZeroSceneModelBinding(
  Model Model,
  WildAnimalParkPlacementResource Placement,
  WildAnimalSavedVariantSelection Selection,
  WildAnimalSavedAnimationResolution Animation,
  WildAnimalFrameZeroPoseVariantLink PoseVariant,
  WildAnimalFrameZeroPoseSlotLink PoseSlot,
  int MaterialBatchIndex,
  ModelDefinitionMeshBatch SkinnedBatch,
  ModelDefinitionMeshBatch MaterialSourceBatch
);

/// <summary>Caller-owned frame-zero models plus exact build and typed-skip evidence.</summary>
/// <remarks>
/// Each model owns its fresh CPU-skinned mesh and fresh material. Material factories receive the
/// selected neutral template batch because that is the exact MDL group/mesh identity understood by
/// <see cref="WildAnimalModelMaterialResolver"/>. The corresponding skinned batch retains the same
/// source indices and belongs to the returned model.
/// </remarks>
internal sealed record WildAnimalFrameZeroSceneBuildResult(
  IReadOnlyList<Model> Models,
  IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> ModelBindings,
  IReadOnlyList<WildAnimalFrameZeroScenePlacementSkip> SkippedPlacements,
  int SourcePlacementCount,
  int VisiblePlacementCount,
  int HiddenPlacementCount,
  int BuiltPlacementCount,
  int SkippedPlacementCount,
  int NoActiveClipPlacementCount,
  int WeightedStatePlacementCount,
  int ModelCount,
  ulong SkinnedVertexCount,
  ulong SkinnedIndexCount
);

/// <summary>Allocation, adaptation, and release seams used by focused ownership tests.</summary>
internal sealed record WildAnimalFrameZeroSceneBuilderOperations(
  Func<
    OpenRCT3.Serialization.DatWildAnimalVisualData,
    WildAnimalModelAnimationResourceBridgeResult,
    WildAnimalSavedAnimationResolution> ResolveSavedAnimation,
  Func<
    ModelAnimationFrameZeroPose,
    IReadOnlyList<ModelDefinitionMeshBatch>> BuildSkinnedBatches,
  Func<Mesh, Model> CreateModel,
  Action<Mesh> DisposeMesh,
  Action<Material> DisposeMaterial,
  Action<Model> DisposeModel
) {
  public static WildAnimalFrameZeroSceneBuilderOperations Default { get; } = new(
    WildAnimalSavedAnimationResolver.Resolve,
    ModelAnimationFrameZeroMeshBuilder.BuildBatches,
    mesh => new Model(mesh),
    mesh => mesh.Dispose(),
    material => material.Dispose(),
    model => model.Dispose());
}

/// <summary>
/// Builds frame-zero scene models only for visible saved animals with one exact full-weight clip.
/// </summary>
internal static class WildAnimalFrameZeroSceneBuilder {
  public static WildAnimalFrameZeroSceneBuildResult Build(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    IReadOnlyList<WildAnimalFrameZeroPoseRegistry> speciesPoses,
    Func<
      WildAnimalParkPlacementResource,
      WildAnimalSavedVariantSelection,
      ModelDefinitionMeshBatch,
      Material?> createMaterial
  ) => Build(
    resources,
    speciesTemplates,
    speciesPoses,
    createMaterial,
    WildAnimalFrameZeroSceneBuilderLimits.Default,
    WildAnimalFrameZeroSceneBuilderOperations.Default);

  internal static WildAnimalFrameZeroSceneBuildResult Build(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    IReadOnlyList<WildAnimalFrameZeroPoseRegistry> speciesPoses,
    Func<
      WildAnimalParkPlacementResource,
      WildAnimalSavedVariantSelection,
      ModelDefinitionMeshBatch,
      Material?> createMaterial,
    WildAnimalFrameZeroSceneBuilderLimits limits,
    WildAnimalFrameZeroSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(speciesTemplates);
    ArgumentNullException.ThrowIfNull(speciesPoses);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateLimits(limits);
    ValidateOperations(operations);

    var species = PreflightSpecies(resources, speciesTemplates, speciesPoses, limits);
    var templateMeshes = CollectTemplateMeshes(speciesTemplates);
    var skinnedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var unownedMeshes = new List<Mesh>();
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var models = new List<Model>();
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var bindings = new List<WildAnimalFrameZeroSceneModelBinding>();
    var skipped = new List<WildAnimalFrameZeroScenePlacementSkip>();
    var builtPlacements = new HashSet<int>();
    var visiblePlacementCount = 0;
    var hiddenPlacementCount = 0;
    var noActiveClipPlacementCount = 0;
    var weightedStatePlacementCount = 0;
    var skinnedVertexCount = 0ul;
    var skinnedIndexCount = 0ul;
    Material? pendingMaterial = null;
    Model? pendingModel = null;

    try {
      foreach (var index in Enumerable.Range(0, resources.Placements.Count)) {
        var placement = resources.Placements[index]
          ?? throw Invalid($"park placement {index} is null");
        ValidatePlacement(resources, placement, index);
        if (!placement.Placement.Visual.Visible) {
          hiddenPlacementCount++;
          continue;
        }
        visiblePlacementCount++;

        var speciesIndex = placement.SpeciesResource.RegistryIndex;
        var speciesItem = species[speciesIndex];
        var selection = WildAnimalSavedVariantSelector.Select(
          placement.Placement.Animal,
          speciesItem.Template);
        var poseVariant = speciesItem.Poses.Variants[selection.SerializedVariantIndex];
        ValidateSelectedVariant(placement, selection, poseVariant);

        var animation = operations.ResolveSavedAnimation(
          placement.Placement.Visual,
          poseVariant.AnimationResources)
          ?? throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} animation resolver returned null");
        ValidateResolution(placement, poseVariant, animation);
        if (animation.Status !=
            WildAnimalSavedAnimationResolutionStatus.ExactSingleClip) {
          var reason = animation.Status switch {
            WildAnimalSavedAnimationResolutionStatus.NoActiveClip =>
              WildAnimalFrameZeroSceneSkipReason.NoActiveClip,
            WildAnimalSavedAnimationResolutionStatus.WeightedState =>
              WildAnimalFrameZeroSceneSkipReason.WeightedState,
            _ => throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} has an unknown animation status"),
          };
          skipped.Add(new(placement, selection, animation, reason));
          if (reason == WildAnimalFrameZeroSceneSkipReason.NoActiveClip)
            noActiveClipPlacementCount++;
          else
            weightedStatePlacementCount++;
          continue;
        }

        var exact = animation.ExactSingleClipEntry!;
        if (exact.SavedEntry.Type < 0 || exact.SavedEntry.Type >= poseVariant.Slots.Count)
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} exact clip slot is out of range");
        var poseSlot = poseVariant.Slots[exact.SavedEntry.Type];
        ValidateExactPose(placement, poseVariant, exact, poseSlot);
        var pose = poseSlot.Pose!;
        var skinnedBatches = operations.BuildSkinnedBatches(pose)
          ?? throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} mesh builder returned null");
        var retained = AdoptSkinnedBatches(
          placement,
          skinnedBatches,
          templateMeshes,
          skinnedMeshes,
          unownedMeshes);
        var materialBatches = selection.Variant.Template.Batches;
        ValidateBatchMapping(placement, selection, pose, retained, materialBatches);
        var transform = WildAnimalWorldTransform.ToPark(
          placement.Placement.Visual.WorldMatrix);

        foreach (var batchIndex in Enumerable.Range(0, retained.Count)) {
          var skinnedBatch = retained[batchIndex];
          var materialBatch = materialBatches[batchIndex];
          Reserve(
            ref skinnedVertexCount,
            Convert.ToUInt64(skinnedBatch.Mesh.Vertices.Count),
            limits.MaximumVertices,
            "vertex");
          Reserve(
            ref skinnedIndexCount,
            Convert.ToUInt64(skinnedBatch.Mesh.Indices.Count),
            limits.MaximumIndices,
            "index");
          if (Convert.ToUInt64(models.Count) >= limits.MaximumModels)
            throw Limit("model", limits.MaximumModels);

          var material = createMaterial(placement, selection, materialBatch)
            ?? throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "material factory returned null");
          if (materials.Contains(material))
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "material factory reused a material");
          pendingMaterial = material;
          if (material.State == State.Disposed)
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "material factory returned a disposed material");
          materials.Add(material);

          var model = operations.CreateModel(skinnedBatch.Mesh)
            ?? throw Invalid("model factory returned null");
          if (modelSet.Contains(model))
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "model factory returned a reused model");
          pendingModel = model;
          if (!ReferenceEquals(model.Mesh, skinnedBatch.Mesh))
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "model did not adopt its skinned mesh");
          RemoveExactMesh(unownedMeshes, skinnedBatch.Mesh);
          if (model.Material != null)
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "model factory returned a non-fresh model");
          modelSet.Add(model);

          model.Material = material;
          pendingMaterial = null;
          model.Transform.Matrix = transform;
          if (!ReferenceEquals(model.Mesh, skinnedBatch.Mesh) ||
              !ReferenceEquals(model.Material, material) ||
              model.Transform.Matrix != transform)
            throw Invalid(
              $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} " +
              "changed exact mesh, material, or transform identity");

          models.Add(model);
          pendingModel = null;
          bindings.Add(new(
            model,
            placement,
            selection,
            animation,
            poseVariant,
            poseSlot,
            batchIndex,
            skinnedBatch,
            materialBatch));
        }
        builtPlacements.Add(placement.PlacementIndex);
      }

      ValidatePlacementGroups(resources);
      if (unownedMeshes.Count != 0)
        throw Invalid("successful construction retained unowned skinned meshes");
      ValidateResults(models, bindings, skipped, visiblePlacementCount, builtPlacements.Count);
      return new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        Array.AsReadOnly(skipped.ToArray()),
        resources.Placements.Count,
        visiblePlacementCount,
        hiddenPlacementCount,
        builtPlacements.Count,
        skipped.Count,
        noActiveClipPlacementCount,
        weightedStatePlacementCount,
        models.Count,
        skinnedVertexCount,
        skinnedIndexCount);
    } catch (Exception primaryError) {
      var cleanupErrors = new List<Exception>();
      TryRelease(ref pendingModel, operations.DisposeModel, cleanupErrors);
      TryRelease(ref pendingMaterial, operations.DisposeMaterial, cleanupErrors);
      cleanupErrors.AddRange(ReleaseMeshes(unownedMeshes, operations.DisposeMesh));
      cleanupErrors.AddRange(ReleaseModels(models, operations.DisposeModel));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Frame-zero Wild-animal scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static SpeciesPlan[] PreflightSpecies(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    IReadOnlyList<WildAnimalFrameZeroPoseRegistry> speciesPoses,
    WildAnimalFrameZeroSceneBuilderLimits limits
  ) {
    if (resources.SpeciesResources == null || resources.Placements == null)
      throw Invalid("park resource registry has a null species or placement list");
    if (resources.Placements.Count > limits.MaximumPlacementCount)
      throw Limit("placement", Convert.ToUInt64(limits.MaximumPlacementCount));
    if (resources.SpeciesResources.Count != speciesTemplates.Count ||
        resources.SpeciesResources.Count != speciesPoses.Count)
      throw Invalid(
        "species, neutral-template, and frame-zero-pose registry counts differ");

    var templates = new HashSet<WildAnimalModelTemplateRegistry>(
      ReferenceEqualityComparer.Instance);
    var poses = new HashSet<WildAnimalFrameZeroPoseRegistry>(
      ReferenceEqualityComparer.Instance);
    var result = new SpeciesPlan[resources.SpeciesResources.Count];
    foreach (var index in Enumerable.Range(0, result.Length)) {
      var species = resources.SpeciesResources[index]
        ?? throw Invalid($"species resource {index} is null");
      var template = speciesTemplates[index]
        ?? throw Invalid($"species template {index} is null");
      var pose = speciesPoses[index]
        ?? throw Invalid($"species pose registry {index} is null");
      if (!templates.Add(template) || !poses.Add(pose))
        throw Invalid($"species resource {index} reused a caller-owned registry");
      ValidateSpecies(species, template, pose, index);
      result[index] = new(species, template, pose);
    }
    return result;
  }

  private static void ValidateSpecies(
    WildAnimalParkSpeciesResource species,
    WildAnimalModelTemplateRegistry template,
    WildAnimalFrameZeroPoseRegistry poses,
    int index
  ) {
    if (species.RegistryIndex != index || species.Species == null ||
        species.Bridge == null || species.Bridge.Species == null ||
        !string.Equals(
          species.Species.SymbolName,
          species.Bridge.Species.Name,
          StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          species.Bridge.SpeciesReference,
          $"{species.Species.SymbolName}:was",
          StringComparison.Ordinal))
      throw Invalid($"species resource {index} changed exact DAT-to-WAS identity");
    if (template.IsDisposed)
      throw Invalid($"species template {index} is disposed");
    if (template.Variants == null || template.Variants.Count != 4 ||
        poses.Variants == null || poses.Variants.Count != 4 ||
        species.Bridge.Variants == null || species.Bridge.Variants.Count != 4)
      throw Invalid($"species resource {index} does not retain four exact variants");

    foreach (var variantIndex in Enumerable.Range(0, 4)) {
      var bridgeVariant = species.Bridge.Variants[variantIndex];
      var templateVariant = template.Variants[variantIndex];
      var poseVariant = poses.Variants[variantIndex];
      if (bridgeVariant == null || templateVariant == null || poseVariant == null ||
          !ReferenceEquals(templateVariant.VariantLink, bridgeVariant) ||
          !ReferenceEquals(poseVariant.VariantLink, bridgeVariant) ||
          !ReferenceEquals(templateVariant.Template.ModelSource, bridgeVariant.ModelSource))
        throw Invalid(
          $"species resource {index} variant {variantIndex} changed bridge identity");
      if (poseVariant.AnimationResources == null ||
          !string.Equals(
            poseVariant.AnimationResources.AnimationDataReference,
            bridgeVariant.Variant.AnimationDataReference,
            StringComparison.OrdinalIgnoreCase) ||
          poseVariant.AnimationResources.Slots == null ||
          poseVariant.AnimationResources.Slots.Count != 31 ||
          poseVariant.Slots == null || poseVariant.Slots.Count != 31)
        throw Invalid(
          $"species resource {index} variant {variantIndex} changed WAD identity");
      foreach (var slotIndex in Enumerable.Range(0, 31)) {
        var slot = poseVariant.Slots[slotIndex];
        if (slot == null || slot.AnimationSlotLink == null ||
            slot.SerializedIndex != slotIndex ||
            !ReferenceEquals(
              slot.AnimationSlotLink,
              poseVariant.AnimationResources.Slots[slotIndex]))
          throw Invalid(
            $"species resource {index} variant {variantIndex} slot {slotIndex} " +
            "changed exact WAD identity");
      }
    }
  }

  private static HashSet<Mesh> CollectTemplateMeshes(
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates
  ) {
    var result = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    foreach (var template in speciesTemplates) {
      if (template.Templates == null)
        throw Invalid("species template has a null distinct-template list");
      foreach (var model in template.Templates) {
        if (model?.Batches == null)
          throw Invalid("species template has a null model template or batch list");
        foreach (var batch in model.Batches) {
          if (batch?.Mesh == null || !result.Add(batch.Mesh))
            throw Invalid("species templates contain a null or reused neutral mesh");
        }
      }
    }
    return result;
  }

  private static void ValidatePlacement(
    WildAnimalParkResourceRegistry resources,
    WildAnimalParkPlacementResource placement,
    int index
  ) {
    if (placement.PlacementIndex != index || placement.Placement == null ||
        placement.SpeciesResource == null || placement.Placement.Animal == null ||
        placement.Placement.Species == null || placement.Placement.Visual == null)
      throw Invalid($"park placement {index} changed saved order or identity");
    if (placement.Placement.Animal.SpeciesDatabaseEntryId !=
          placement.Placement.Species.EntryId ||
        placement.Placement.Animal.VisualEntryId != placement.Placement.Visual.EntryId ||
        placement.VariantSelectionStatus !=
          OpenRCT3.Serialization.DatWildAnimalVariantSelectionStatus.Unsupported)
      throw Invalid($"park placement {index} changed exact saved DAT links");
    var speciesIndex = placement.SpeciesResource.RegistryIndex;
    if (speciesIndex < 0 || speciesIndex >= resources.SpeciesResources.Count ||
        !ReferenceEquals(
          placement.SpeciesResource,
          resources.SpeciesResources[speciesIndex]) ||
        !ReferenceEquals(
          placement.Placement.Species,
          placement.SpeciesResource.Species))
      throw Invalid($"park placement {index} changed exact species-resource identity");
  }

  private static void ValidateSelectedVariant(
    WildAnimalParkPlacementResource placement,
    WildAnimalSavedVariantSelection selection,
    WildAnimalFrameZeroPoseVariantLink poseVariant
  ) {
    var variantIndex = selection.SerializedVariantIndex;
    if (!ReferenceEquals(selection.Animal, placement.Placement.Animal) ||
        variantIndex != placement.Placement.Animal.Type ||
        !ReferenceEquals(selection.Variant.VariantLink, poseVariant.VariantLink) ||
        !ReferenceEquals(
          selection.Variant.Template.ModelSource,
          poseVariant.VariantLink.ModelSource))
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed exact variant identity");
  }

  private static void ValidateResolution(
    WildAnimalParkPlacementResource placement,
    WildAnimalFrameZeroPoseVariantLink poseVariant,
    WildAnimalSavedAnimationResolution animation
  ) {
    if (!ReferenceEquals(animation.Visual, placement.Placement.Visual) ||
        !ReferenceEquals(animation.Resources, poseVariant.AnimationResources) ||
        animation.Entries == null)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed saved animation identity");
    var activeEntries = new List<WildAnimalSavedAnimationEntryResolution>();
    foreach (var index in Enumerable.Range(0, animation.Entries.Count)) {
      var entry = animation.Entries[index];
      if (entry == null || entry.SavedIndex != index ||
          entry.SavedEntry.Type < 0 ||
          entry.SavedEntry.Type >= poseVariant.AnimationResources.Slots.Count ||
          !ReferenceEquals(
            entry.Slot,
            poseVariant.AnimationResources.Slots[entry.SavedEntry.Type]))
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} saved entry {index} drifted");
      if (entry.IsActive) activeEntries.Add(entry);
    }
    var active = activeEntries.Count;
    if (active != animation.ActiveEntryCount)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed active animation count");
    switch (animation.Status) {
      case WildAnimalSavedAnimationResolutionStatus.NoActiveClip:
        if (active != 0 || animation.ExactSingleClipEntry != null)
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} changed no-active-clip evidence");
        break;
      case WildAnimalSavedAnimationResolutionStatus.ExactSingleClip:
        if (active != 1 || animation.ExactSingleClipEntry == null ||
            !ReferenceEquals(animation.ExactSingleClipEntry, activeEntries[0]) ||
            animation.ExactSingleClipEntry.SavedEntry.Weight != 1f)
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} changed exact-clip evidence");
        break;
      case WildAnimalSavedAnimationResolutionStatus.WeightedState:
        if (active == 0 || animation.ExactSingleClipEntry != null ||
            active == 1 && activeEntries[0].SavedEntry.Weight == 1f)
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} changed weighted-state evidence");
        break;
      default:
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} has an unknown animation status");
    }
  }

  private static void ValidateExactPose(
    WildAnimalParkPlacementResource placement,
    WildAnimalFrameZeroPoseVariantLink poseVariant,
    WildAnimalSavedAnimationEntryResolution exact,
    WildAnimalFrameZeroPoseSlotLink poseSlot
  ) {
    var slotIndex = exact.SavedEntry.Type;
    if (slotIndex < 0 || slotIndex >= poseVariant.Slots.Count ||
        !ReferenceEquals(exact.Slot, poseVariant.AnimationResources.Slots[slotIndex]) ||
        !ReferenceEquals(poseSlot.AnimationSlotLink, exact.Slot) ||
        !poseSlot.IsResolved || poseSlot.Pose == null || exact.SavedEntry.Weight != 1f)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed exact clip-slot identity");
    var pose = poseSlot.Pose;
    var model = poseVariant.VariantLink.ModelSource.Resource;
    var animation = exact.Slot.Source?.Resource;
    if (!ReferenceEquals(pose.Model, model) ||
        !ReferenceEquals(pose.Animation, animation) ||
        pose.Bones == null || model.Bones == null ||
        pose.Bones.Count != model.Bones.Count ||
        pose.TranslatedBoneCount != Convert.ToInt32(animation!.AnimatedBoneCountAt18) ||
        pose.RotatedBoneCount != Convert.ToInt32(animation.FullBoneCountAt1C))
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed pose skeleton identity");
    foreach (var index in Enumerable.Range(0, pose.Bones.Count)) {
      var bone = pose.Bones[index];
      if (bone == null || bone.ModelBoneIndex != index ||
          !ReferenceEquals(bone.Bone, model.Bones[index]) ||
          !IsFinite(bone.LocalTransform) || !IsFinite(bone.WorldTransform) ||
          !IsFinite(bone.SkinTransform))
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} pose bone {index} drifted");
    }
  }

  private static IReadOnlyList<ModelDefinitionMeshBatch> AdoptSkinnedBatches(
    WildAnimalParkPlacementResource placement,
    IReadOnlyList<ModelDefinitionMeshBatch> batches,
    ISet<Mesh> templateMeshes,
    ISet<Mesh> skinnedMeshes,
    ICollection<Mesh> unownedMeshes
  ) {
    if (batches.Count == 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} mesh builder returned no batches");
    var retained = new ModelDefinitionMeshBatch[batches.Count];
    var nullBatchIndex = -1;
    var disposedBatchIndex = -1;
    var templateBatchIndex = -1;
    var reusedBatchIndex = -1;
    foreach (var index in Enumerable.Range(0, batches.Count)) {
      var batch = batches[index];
      retained[index] = batch;
      if (batch?.Mesh == null) {
        if (nullBatchIndex < 0) nullBatchIndex = index;
        continue;
      }
      if (batch.Mesh.State == State.Disposed) {
        if (disposedBatchIndex < 0) disposedBatchIndex = index;
        continue;
      }
      if (templateMeshes.Contains(batch.Mesh)) {
        if (templateBatchIndex < 0) templateBatchIndex = index;
        continue;
      }
      if (!skinnedMeshes.Add(batch.Mesh)) {
        if (reusedBatchIndex < 0) reusedBatchIndex = index;
        continue;
      }
      unownedMeshes.Add(batch.Mesh);
    }
    if (nullBatchIndex >= 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} skinned batch " +
        $"{nullBatchIndex} is null");
    if (disposedBatchIndex >= 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} skinned batch " +
        $"{disposedBatchIndex} is disposed");
    if (templateBatchIndex >= 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} skinned batch " +
        $"{templateBatchIndex} reused a neutral template mesh");
    if (reusedBatchIndex >= 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} skinned batch " +
        $"{reusedBatchIndex} reused an owned mesh");
    return Array.AsReadOnly(retained);
  }

  private static void ValidateBatchMapping(
    WildAnimalParkPlacementResource placement,
    WildAnimalSavedVariantSelection selection,
    ModelAnimationFrameZeroPose pose,
    IReadOnlyList<ModelDefinitionMeshBatch> skinned,
    IReadOnlyList<ModelDefinitionMeshBatch> material
  ) {
    if (!ReferenceEquals(pose.Model, selection.Variant.Template.ModelSource.Resource) ||
        material == null || skinned.Count != material.Count)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed skinned/material batch count");
    foreach (var index in Enumerable.Range(0, skinned.Count)) {
      var skinnedBatch = skinned[index];
      var materialBatch = material[index];
      if (materialBatch?.Mesh == null || materialBatch.Mesh.State == State.Disposed ||
          skinnedBatch.SourceGroupIndex != materialBatch.SourceGroupIndex ||
          skinnedBatch.SourceMeshIndex != materialBatch.SourceMeshIndex ||
          !string.Equals(
            skinnedBatch.SourceMeshName,
            materialBatch.SourceMeshName,
            StringComparison.Ordinal) ||
          !string.Equals(
            skinnedBatch.Mesh.Name,
            skinnedBatch.SourceMeshName,
            StringComparison.Ordinal) ||
          skinnedBatch.Mesh == null || skinnedBatch.Mesh.Vertices == null ||
          skinnedBatch.Mesh.Indices == null || skinnedBatch.Mesh.Vertices.Count == 0 ||
          skinnedBatch.Mesh.Indices.Count == 0 || skinnedBatch.Mesh.Indices.Count % 3 != 0 ||
          skinnedBatch.Mesh.Vertices.Count != materialBatch.Mesh.Vertices.Count ||
          !skinnedBatch.Mesh.Indices.SequenceEqual(materialBatch.Mesh.Indices))
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} skinned batch {index} " +
          "changed exact MDL group/mesh identity");
      foreach (var vertex in skinnedBatch.Mesh.Vertices)
        if (!IsFinite(vertex))
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} skinned batch {index} " +
            "contains a non-finite vertex");
      foreach (var vertexIndex in skinnedBatch.Mesh.Indices)
        if (vertexIndex >= Convert.ToUInt32(skinnedBatch.Mesh.Vertices.Count))
          throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} skinned batch {index} " +
            "contains an out-of-range index");
    }
  }

  private static void ValidatePlacementGroups(WildAnimalParkResourceRegistry resources) {
    var placementsBySpecies = Enumerable.Range(0, resources.SpeciesResources.Count)
      .Select(_ => new List<int>())
      .ToArray();
    foreach (var placement in resources.Placements)
      placementsBySpecies[placement.SpeciesResource.RegistryIndex]
        .Add(placement.PlacementIndex);
    foreach (var speciesIndex in Enumerable.Range(0, resources.SpeciesResources.Count)) {
      var species = resources.SpeciesResources[speciesIndex];
      if (species.PlacementIndices == null ||
          !species.PlacementIndices.SequenceEqual(placementsBySpecies[speciesIndex]))
        throw Invalid(
          $"species resource {speciesIndex} changed placement membership or order");
    }
  }

  private static void ValidateResults(
    IReadOnlyList<Model> models,
    IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> bindings,
    IReadOnlyList<WildAnimalFrameZeroScenePlacementSkip> skipped,
    int visiblePlacementCount,
    int builtPlacementCount
  ) {
    if (models.Count != bindings.Count ||
        visiblePlacementCount != builtPlacementCount + skipped.Count)
      throw Invalid("result counts disagree with exact model and skip bindings");
    foreach (var index in Enumerable.Range(0, models.Count)) {
      var binding = bindings[index];
      if (binding == null || !ReferenceEquals(binding.Model, models[index]) ||
          !ReferenceEquals(binding.Model.Mesh, binding.SkinnedBatch.Mesh) ||
          binding.Model.Material == null ||
          !ReferenceEquals(binding.Selection.Animal, binding.Placement.Placement.Animal) ||
          !ReferenceEquals(
            binding.PoseSlot,
            binding.PoseVariant.Slots[
              binding.Animation.ExactSingleClipEntry!.SavedEntry.Type]) ||
          binding.MaterialBatchIndex < 0 ||
          binding.MaterialBatchIndex >= binding.Selection.Variant.Template.Batches.Count ||
          !ReferenceEquals(
            binding.MaterialSourceBatch,
            binding.Selection.Variant.Template.Batches[binding.MaterialBatchIndex]))
        throw Invalid($"model binding {index} changed exact source or ownership identity");
    }
  }

  private static void ValidateOperations(
    WildAnimalFrameZeroSceneBuilderOperations operations
  ) {
    if (operations.ResolveSavedAnimation == null ||
        operations.BuildSkinnedBatches == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException(
        "Frame-zero Wild-animal scene operations must all be configured.",
        nameof(operations));
  }

  private static void ValidateLimits(WildAnimalFrameZeroSceneBuilderLimits limits) {
    if (limits.MaximumPlacementCount < 0 || limits.MaximumModels == 0 ||
        limits.MaximumVertices == 0 || limits.MaximumIndices == 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void Reserve(
    ref ulong current,
    ulong addition,
    ulong maximum,
    string resource
  ) {
    if (addition > maximum || current > maximum - addition)
      throw Limit(resource, maximum);
    current += addition;
  }

  private static void RemoveExactMesh(ICollection<Mesh> meshes, Mesh mesh) {
    if (!meshes.Remove(mesh))
      throw Invalid("model adopted a mesh outside the current build transaction");
  }

  private static List<Exception> ReleaseMeshes(
    List<Mesh> meshes,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    while (meshes.Count > 0) {
      var index = meshes.Count - 1;
      var mesh = meshes[index];
      meshes.RemoveAt(index);
      try {
        disposeMesh(mesh);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static List<Exception> ReleaseModels(
    List<Model> models,
    Action<Model> disposeModel
  ) {
    var errors = new List<Exception>();
    while (models.Count > 0) {
      var index = models.Count - 1;
      var model = models[index];
      models.RemoveAt(index);
      try {
        disposeModel(model);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void TryRelease<T>(
    ref T? resource,
    Action<T> release,
    ICollection<Exception> errors
  ) where T : class {
    if (resource == null) return;
    var owned = resource;
    resource = null;
    try {
      release(owned);
    } catch (Exception error) {
      errors.Add(error);
    }
  }

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static bool IsFinite(Vertex value) =>
    IsFinite(value.Position) && IsFinite(value.Normal) &&
    IsFinite(value.TexCoord) && IsFinite(value.Color);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Frame-zero Wild-animal scene input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Frame-zero Wild-animal scene {resource} count exceeds the limit {maximum}.");

  private sealed record SpeciesPlan(
    WildAnimalParkSpeciesResource Species,
    WildAnimalModelTemplateRegistry Template,
    WildAnimalFrameZeroPoseRegistry Poses
  );
}

/// <summary>Allocation ceilings for caller-owned frame-zero Wild-animal scene resources.</summary>
internal readonly record struct WildAnimalFrameZeroSceneBuilderLimits(
  int MaximumPlacementCount,
  ulong MaximumModels,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static WildAnimalFrameZeroSceneBuilderLimits Default { get; } = new(
    MaximumPlacementCount: 100_000,
    MaximumModels: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000);
}
