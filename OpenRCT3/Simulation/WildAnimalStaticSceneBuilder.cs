// Wild Animal Static Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One scene-owned model bound to its exact saved animal and WAS/MDL batch.</summary>
internal sealed record WildAnimalStaticSceneModelBinding(
  Model Model,
  WildAnimalParkPlacementResource Placement,
  WildAnimalSavedVariantSelection Selection,
  int MaterialBatchIndex,
  ModelDefinitionMeshBatch Batch
);

/// <summary>Caller-owned static Wild-animal models and exact saved-source bindings.</summary>
/// <remarks>
/// Models preserve saved placement order followed by source MDL batch order. Each model owns a
/// fresh CPU mesh clone and the fresh material produced for that exact placement and batch. The
/// template registries retain their neutral meshes and may be disposed after this build completes.
/// </remarks>
internal sealed record WildAnimalStaticSceneBuildResult(
  IReadOnlyList<Model> Models,
  IReadOnlyList<WildAnimalStaticSceneModelBinding> ModelBindings,
  int SourcePlacementCount,
  int VisiblePlacementCount,
  int HiddenPlacementCount,
  int BuiltPlacementCount,
  int SkippedPlacementCount,
  int ModelCount,
  int MissingMaterialBatchCount,
  ulong ClonedVertexCount,
  ulong ClonedIndexCount
);

/// <summary>Renderer allocation and release seams used by focused ownership tests.</summary>
internal sealed record WildAnimalStaticSceneBuilderOperations(
  Func<List<Vertex>, List<uint>, Mesh> CreateMesh,
  Func<Mesh, Model> CreateModel,
  Action<Mesh> DisposeMesh,
  Action<Material> DisposeMaterial,
  Action<Model> DisposeModel
) {
  public static WildAnimalStaticSceneBuilderOperations Default { get; } = new(
    (vertices, indices) => new Mesh(vertices, indices),
    mesh => new Model(mesh),
    mesh => mesh.Dispose(),
    material => material.Dispose(),
    model => model.Dispose());
}

/// <summary>Builds static scene models from exact saved Wild-animal variant selections.</summary>
internal static class WildAnimalStaticSceneBuilder {
  public static WildAnimalStaticSceneBuildResult Build(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    Func<
      WildAnimalParkPlacementResource,
      WildAnimalSavedVariantSelection,
      ModelDefinitionMeshBatch,
      Material?> createMaterial
  ) => Build(
    resources,
    speciesTemplates,
    createMaterial,
    WildAnimalStaticSceneBuilderLimits.Default,
    WildAnimalStaticSceneBuilderOperations.Default);

  internal static WildAnimalStaticSceneBuildResult Build(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    Func<
      WildAnimalParkPlacementResource,
      WildAnimalSavedVariantSelection,
      ModelDefinitionMeshBatch,
      Material?> createMaterial,
    WildAnimalStaticSceneBuilderLimits limits,
    WildAnimalStaticSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(speciesTemplates);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    ValidateLimits(limits);

    var plan = Preflight(resources, speciesTemplates, limits);
    var models = new List<Model>(plan.Batches.Count);
    var bindings = new List<WildAnimalStaticSceneModelBinding>(plan.Batches.Count);
    var templateMeshes = plan.TemplateMeshes;
    var clonedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var builtPlacements = new HashSet<int>();
    var missingMaterialBatchCount = 0;
    var clonedVertexCount = 0ul;
    var clonedIndexCount = 0ul;
    Mesh? pendingMesh = null;
    Material? pendingMaterial = null;
    Model? pendingModel = null;

    try {
      foreach (var item in plan.Batches) {
        var material = createMaterial(item.Placement, item.Selection, item.Batch);
        if (material == null) {
          missingMaterialBatchCount++;
          continue;
        }
        if (materials.Contains(material))
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "material factory reused a material");
        pendingMaterial = material;
        if (material.State == State.Disposed)
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "material factory returned a disposed material");
        materials.Add(material);

        var vertices = new List<Vertex>(item.Batch.Mesh.Vertices);
        var indices = new List<uint>(item.Batch.Mesh.Indices);
        var mesh = operations.CreateMesh(vertices, indices)
          ?? throw Invalid("mesh factory returned null");
        if (templateMeshes.Contains(mesh))
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "reused a template mesh");
        if (clonedMeshes.Contains(mesh))
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "reused a cloned mesh");
        pendingMesh = mesh;
        clonedMeshes.Add(mesh);
        ValidateClone(item, vertices, indices, mesh);

        var model = operations.CreateModel(mesh)
          ?? throw Invalid("model factory returned null");
        if (modelSet.Contains(model))
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "model factory returned a reused model");
        pendingModel = model;
        if (!ReferenceEquals(model.Mesh, mesh))
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "model did not adopt its cloned mesh");
        pendingMesh = null;
        if (model.Material != null)
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "model factory returned a non-fresh model");
        modelSet.Add(model);

        model.Material = material;
        pendingMaterial = null;
        model.Transform.Matrix = item.Transform;
        if (!ReferenceEquals(model.Mesh, mesh) ||
            !ReferenceEquals(model.Material, material) ||
            model.Transform.Matrix != item.Transform)
          throw Invalid(
            $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
            "changed exact mesh, material, or transform identity");

        models.Add(model);
        pendingModel = null;
        bindings.Add(new(
          model,
          item.Placement,
          item.Selection,
          item.BatchIndex,
          item.Batch));
        builtPlacements.Add(item.Placement.PlacementIndex);
        clonedVertexCount = checked(
          clonedVertexCount + Convert.ToUInt64(mesh.Vertices.Count));
        clonedIndexCount = checked(
          clonedIndexCount + Convert.ToUInt64(mesh.Indices.Count));
      }

      ValidateBindings(models, bindings);
      return new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        plan.SourcePlacementCount,
        plan.VisiblePlacementCount,
        plan.HiddenPlacementCount,
        builtPlacements.Count,
        plan.SourcePlacementCount - builtPlacements.Count,
        models.Count,
        missingMaterialBatchCount,
        clonedVertexCount,
        clonedIndexCount);
    } catch (Exception primaryError) {
      var cleanupErrors = ReleasePending(
        ref pendingModel,
        ref pendingMaterial,
        ref pendingMesh,
        operations);
      cleanupErrors.AddRange(ReleaseModels(models, operations.DisposeModel));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Static Wild-animal scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static BuildPlan Preflight(
    WildAnimalParkResourceRegistry resources,
    IReadOnlyList<WildAnimalModelTemplateRegistry> speciesTemplates,
    WildAnimalStaticSceneBuilderLimits limits
  ) {
    if (resources.SpeciesResources == null || resources.Placements == null)
      throw Invalid("park resource registry has a null species or placement list");
    if (resources.SpeciesResources.Count != speciesTemplates.Count)
      throw Invalid(
        $"template count {speciesTemplates.Count} differs from species count " +
        $"{resources.SpeciesResources.Count}");
    if (resources.Placements.Count > limits.MaximumPlacementCount)
      throw Limit("placement", Convert.ToUInt64(limits.MaximumPlacementCount));

    var templates = new WildAnimalModelTemplateRegistry[speciesTemplates.Count];
    var templateMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    foreach (var index in Enumerable.Range(0, speciesTemplates.Count)) {
      var species = resources.SpeciesResources[index]
        ?? throw Invalid($"species resource {index} is null");
      var template = speciesTemplates[index]
        ?? throw Invalid($"species template {index} is null");
      ValidateSpeciesTemplate(species, template, index, templateMeshes);
      templates[index] = template;
    }

    var batches = new List<PlannedBatch>();
    var visiblePlacementCount = 0;
    var hiddenPlacementCount = 0;
    var vertexBudget = 0ul;
    var indexBudget = 0ul;
    foreach (var index in Enumerable.Range(0, resources.Placements.Count)) {
      var placement = resources.Placements[index]
        ?? throw Invalid($"park placement {index} is null");
      ValidatePlacement(resources, placement, index);
      if (!placement.Placement.Visual.Visible) {
        hiddenPlacementCount++;
        continue;
      }
      visiblePlacementCount++;

      var template = templates[placement.SpeciesResource.RegistryIndex];
      var selection = WildAnimalSavedVariantSelector.Select(
        placement.Placement.Animal,
        template);
      var transform = WildAnimalWorldTransform.ToPark(placement.Placement.Visual.WorldMatrix);
      var selectedBatches = selection.Variant.Template.Batches;
      if (selectedBatches == null || selectedBatches.Count == 0)
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} selected an empty MDL template");
      foreach (var batchIndex in Enumerable.Range(0, selectedBatches.Count)) {
        var batch = selectedBatches[batchIndex]
          ?? throw Invalid(
            $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} is null");
        ValidateTemplateBatch(placement, selection, batch, batchIndex);
        Reserve(
          ref vertexBudget,
          Convert.ToUInt64(batch.Mesh.Vertices.Count),
          limits.MaximumVertices,
          "vertex");
        Reserve(
          ref indexBudget,
          Convert.ToUInt64(batch.Mesh.Indices.Count),
          limits.MaximumIndices,
          "index");
        if (Convert.ToUInt64(batches.Count) >= limits.MaximumModels)
          throw Limit("model", limits.MaximumModels);
        batches.Add(new(placement, selection, batchIndex, batch, transform));
      }
    }
    ValidatePlacementGroups(resources);

    return new(
      Array.AsReadOnly(batches.ToArray()),
      templateMeshes,
      resources.Placements.Count,
      visiblePlacementCount,
      hiddenPlacementCount);
  }

  private static void ValidateSpeciesTemplate(
    WildAnimalParkSpeciesResource species,
    WildAnimalModelTemplateRegistry template,
    int index,
    ISet<Mesh> templateMeshes
  ) {
    if (species.RegistryIndex != index)
      throw Invalid($"species resource {index} changed exact registry order");
    if (species.Species == null || species.Bridge == null ||
        species.Bridge.Species == null || species.Bridge.Variants == null ||
        !string.Equals(
          species.Bridge.SpeciesReference,
          $"{species.Species.SymbolName}:was",
          StringComparison.Ordinal) ||
        !string.Equals(
          species.Bridge.Species.Name,
          species.Species.SymbolName,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid($"species resource {index} changed exact DAT-to-WAS identity");
    if (template.IsDisposed)
      throw Invalid($"species template {index} is disposed");
    if (template.Variants == null || template.Variants.Count != 4 ||
        species.Bridge?.Variants == null || species.Bridge.Variants.Count != 4)
      throw Invalid($"species resource {index} does not retain four exact WAS variants");
    var expectedTemplates = new List<WildAnimalModelTemplate>();
    var templatesBySource = new Dictionary<
      WildAnimalSpeciesModelResourceSource,
      WildAnimalModelTemplate>(ReferenceEqualityComparer.Instance);
    foreach (var variantIndex in Enumerable.Range(0, 4)) {
      var bridgeVariant = species.Bridge.Variants[variantIndex];
      var variant = template.Variants[variantIndex];
      if (bridgeVariant == null || bridgeVariant.ModelSource == null ||
          variant == null || variant.VariantLink == null ||
          !ReferenceEquals(variant.VariantLink, bridgeVariant))
        throw Invalid(
          $"species resource {index} variant {variantIndex} changed exact bridge identity");
      if (variant.Template == null ||
          !ReferenceEquals(
            variant.Template.ModelSource,
            variant.VariantLink.ModelSource))
        throw Invalid(
          $"species resource {index} variant {variantIndex} changed exact MDL template");
      if (templatesBySource.TryGetValue(
            variant.VariantLink.ModelSource,
            out var expectedTemplate)) {
        if (!ReferenceEquals(variant.Template, expectedTemplate))
          throw Invalid(
            $"species resource {index} variant {variantIndex} split one MDL template");
      } else {
        templatesBySource.Add(variant.VariantLink.ModelSource, variant.Template);
        expectedTemplates.Add(variant.Template);
      }
    }
    if (template.Templates == null || template.Templates.Count != expectedTemplates.Count)
      throw Invalid($"species template {index} changed exact distinct MDL templates");
    foreach (var templateIndex in Enumerable.Range(0, expectedTemplates.Count)) {
      var modelTemplate = template.Templates[templateIndex];
      if (modelTemplate == null ||
          !ReferenceEquals(modelTemplate, expectedTemplates[templateIndex]) ||
          modelTemplate.Batches == null)
        throw Invalid(
          $"species template {index} changed model template {templateIndex} identity");
      ValidateModelTemplateBatches(species, modelTemplate, index, templateIndex);
      foreach (var batch in modelTemplate.Batches) {
        if (batch?.Mesh == null || !templateMeshes.Add(batch.Mesh))
          throw Invalid(
            $"species template {index} contains a null or reused mesh identity");
      }
    }
  }

  private static void ValidatePlacement(
    WildAnimalParkResourceRegistry resources,
    WildAnimalParkPlacementResource placement,
    int index
  ) {
    if (placement.PlacementIndex != index || placement.Placement == null ||
        placement.SpeciesResource == null)
      throw Invalid($"park placement {index} changed exact saved order or identity");
    if (placement.Placement.Animal == null || placement.Placement.Species == null ||
        placement.Placement.Visual == null ||
        placement.Placement.Animal.SpeciesDatabaseEntryId !=
          placement.Placement.Species.EntryId ||
        placement.Placement.Animal.VisualEntryId != placement.Placement.Visual.EntryId ||
        placement.VariantSelectionStatus !=
          DatWildAnimalVariantSelectionStatus.Unsupported)
      throw Invalid($"park placement {index} changed exact saved DAT links");
    var speciesIndex = placement.SpeciesResource.RegistryIndex;
    if (speciesIndex < 0 || speciesIndex >= resources.SpeciesResources.Count ||
        !ReferenceEquals(
          placement.SpeciesResource,
          resources.SpeciesResources[speciesIndex]))
      throw Invalid($"park placement {index} changed exact species-resource identity");
    if (!ReferenceEquals(placement.Placement.Species, placement.SpeciesResource.Species))
      throw Invalid($"park placement {index} changed exact DAT species identity");
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
          $"species resource {speciesIndex} changed exact placement membership or order");
    }
  }

  private static void ValidateModelTemplateBatches(
    WildAnimalParkSpeciesResource species,
    WildAnimalModelTemplate template,
    int speciesIndex,
    int templateIndex
  ) {
    if (template.ModelSource?.Resource?.Groups == null)
      throw Invalid(
        $"species template {speciesIndex} model template {templateIndex} lost its MDL source");
    var expected = template.ModelSource.Resource.Groups
      .SelectMany((group, groupIndex) => {
        if (group?.Meshes == null)
          throw Invalid(
            $"species template {speciesIndex} model template {templateIndex} " +
            $"has a null MDL group {groupIndex}");
        return group.Meshes.Select((mesh, meshIndex) => (groupIndex, meshIndex, mesh));
      })
      .ToArray();
    if (template.Batches.Count != expected.Length)
      throw Invalid(
        $"species template {speciesIndex} model template {templateIndex} changed batch count");
    foreach (var batchIndex in Enumerable.Range(0, expected.Length)) {
      var source = expected[batchIndex];
      var batch = template.Batches[batchIndex];
      var expectedName =
        $"{template.ModelSource.Resource.Name}/group-{source.groupIndex}/" +
        $"mesh-{source.meshIndex}";
      if (source.mesh == null || source.mesh.Vertices == null ||
          source.mesh.Indices == null || batch == null || batch.Mesh == null ||
          batch.Mesh.Vertices == null || batch.Mesh.Indices == null ||
          batch.Mesh.State == State.Disposed ||
          batch.SourceGroupIndex != source.groupIndex ||
          batch.SourceMeshIndex != source.meshIndex ||
          !string.Equals(batch.SourceMeshName, expectedName, StringComparison.Ordinal) ||
          !string.Equals(batch.Mesh.Name, expectedName, StringComparison.Ordinal) ||
          batch.Mesh.Vertices.Count != source.mesh.Vertices.Count ||
          batch.Mesh.Indices.Count != source.mesh.Indices.Count)
        throw Invalid(
          $"species resource {species.RegistryIndex} template {templateIndex} batch " +
          $"{batchIndex} changed exact MDL identity");
    }
  }

  private static void ValidateTemplateBatch(
    WildAnimalParkPlacementResource placement,
    WildAnimalSavedVariantSelection selection,
    ModelDefinitionMeshBatch batch,
    int batchIndex
  ) {
    if (!ReferenceEquals(selection.Animal, placement.Placement.Animal) ||
        selection.SerializedVariantIndex != placement.Placement.Animal.Type ||
        !ReferenceEquals(
          selection.Variant.VariantLink,
          placement.SpeciesResource.Bridge.Variants[selection.SerializedVariantIndex]) ||
        batchIndex >= selection.Variant.Template.Batches.Count ||
        !ReferenceEquals(batch, selection.Variant.Template.Batches[batchIndex]))
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} changed exact selector identity");
    if (batch.Mesh == null || batch.Mesh.Vertices == null || batch.Mesh.Indices == null ||
        batch.Mesh.State == State.Disposed)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} has no live mesh");
    if (batch.SourceGroupIndex < 0 || batch.SourceMeshIndex < 0 ||
        string.IsNullOrWhiteSpace(batch.SourceMeshName) ||
        batch.Mesh.Vertices.Count == 0 || batch.Mesh.Indices.Count == 0 ||
        batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid(
        $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} has invalid geometry");
    foreach (var vertex in batch.Mesh.Vertices)
      if (!IsFinite(vertex))
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} has a " +
          "non-finite vertex");
    foreach (var vertexIndex in batch.Mesh.Indices)
      if (vertexIndex >= Convert.ToUInt32(batch.Mesh.Vertices.Count))
        throw Invalid(
          $"animal {placement.Placement.Animal.EntryId} batch {batchIndex} has an " +
          "out-of-range index");
  }

  private static void ValidateClone(
    PlannedBatch item,
    List<Vertex> vertices,
    List<uint> indices,
    Mesh mesh
  ) {
    if (mesh.State == State.Disposed ||
        !ReferenceEquals(mesh.Vertices, vertices) ||
        !ReferenceEquals(mesh.Indices, indices) ||
        ReferenceEquals(mesh.Vertices, item.Batch.Mesh.Vertices) ||
        ReferenceEquals(mesh.Indices, item.Batch.Mesh.Indices) ||
        !mesh.Vertices.SequenceEqual(item.Batch.Mesh.Vertices) ||
        !mesh.Indices.SequenceEqual(item.Batch.Mesh.Indices))
      throw Invalid(
        $"animal {item.Placement.Placement.Animal.EntryId} batch {item.BatchIndex} " +
        "changed cloned geometry");
    mesh.Name = string.IsNullOrWhiteSpace(item.Batch.Mesh.Name)
      ? $"{item.Batch.SourceMeshName} [Wild animal " +
        $"{item.Placement.Placement.Animal.EntryId}]"
      : $"{item.Batch.Mesh.Name} [Wild animal {item.Placement.Placement.Animal.EntryId}]";
  }

  private static void ValidateBindings(
    IReadOnlyList<Model> models,
    IReadOnlyList<WildAnimalStaticSceneModelBinding> bindings
  ) {
    if (models.Count != bindings.Count)
      throw Invalid("model and exact source-binding counts differ");
    foreach (var index in Enumerable.Range(0, models.Count)) {
      var binding = bindings[index]
        ?? throw Invalid($"model binding {index} is null");
      if (!ReferenceEquals(binding.Model, models[index]) ||
          binding.Model.Mesh == null || binding.Model.Material == null ||
          !ReferenceEquals(binding.Selection.Animal, binding.Placement.Placement.Animal) ||
          binding.MaterialBatchIndex < 0 ||
          binding.MaterialBatchIndex >= binding.Selection.Variant.Template.Batches.Count ||
          !ReferenceEquals(
            binding.Batch,
            binding.Selection.Variant.Template.Batches[binding.MaterialBatchIndex]))
        throw Invalid($"model binding {index} changed exact saved or MDL identity");
    }
  }

  private static void ValidateOperations(WildAnimalStaticSceneBuilderOperations operations) {
    if (operations.CreateMesh == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException(
        "Static Wild-animal scene operations must all be configured.",
        nameof(operations));
  }

  private static void ValidateLimits(WildAnimalStaticSceneBuilderLimits limits) {
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

  private static List<Exception> ReleasePending(
    ref Model? model,
    ref Material? material,
    ref Mesh? mesh,
    WildAnimalStaticSceneBuilderOperations operations
  ) {
    var errors = new List<Exception>();
    TryRelease(ref model, operations.DisposeModel, errors);
    TryRelease(ref mesh, operations.DisposeMesh, errors);
    TryRelease(ref material, operations.DisposeMaterial, errors);
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

  private static bool IsFinite(Vertex vertex) =>
    IsFinite(vertex.Position) && IsFinite(vertex.Normal) &&
    IsFinite(vertex.TexCoord) && IsFinite(vertex.Color);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Static Wild-animal scene input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Static Wild-animal scene {resource} count exceeds the limit {maximum}.");

  private sealed record PlannedBatch(
    WildAnimalParkPlacementResource Placement,
    WildAnimalSavedVariantSelection Selection,
    int BatchIndex,
    ModelDefinitionMeshBatch Batch,
    Matrix4x4 Transform
  );

  private sealed record BuildPlan(
    IReadOnlyList<PlannedBatch> Batches,
    IReadOnlySet<Mesh> TemplateMeshes,
    int SourcePlacementCount,
    int VisiblePlacementCount,
    int HiddenPlacementCount
  );
}

/// <summary>Allocation ceilings for cloned static Wild-animal scene resources.</summary>
internal readonly record struct WildAnimalStaticSceneBuilderLimits(
  int MaximumPlacementCount,
  ulong MaximumModels,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static WildAnimalStaticSceneBuilderLimits Default { get; } = new(
    MaximumPlacementCount: 100_000,
    MaximumModels: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000);
}
