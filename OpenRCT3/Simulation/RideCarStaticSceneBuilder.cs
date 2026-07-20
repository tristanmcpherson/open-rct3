// Ride Car Static Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>The exact body source used for one static ride-car scene model.</summary>
internal enum RideCarStaticSceneModelSource {
  BaseWithoutVariantSelection,
  SelectedVariant,
}

/// <summary>The registry policy used to build one static ride-car scene.</summary>
internal enum RideCarStaticSceneBuildMode {
  BaseWithoutVariantSelection,
  SelectedVariants,
}

/// <summary>One exact, borrowed source binding for a scene-owned static ride-car model.</summary>
/// <remarks>
/// The binding owns and disposes nothing. <see cref="Model"/> remains owned by the receiving scene
/// or caller. <see cref="Entry"/> is either the exact legacy entry or an immutable managed snapshot
/// of the selected variant entry. <see cref="VariantEntry"/> retains the exact selector evidence for
/// variant and fallback models. All referenced templates and renderer resources remain borrowed.
/// </remarks>
internal sealed record RideCarStaticSceneModelBinding(
  Model Model,
  RideCarStaticInstanceEntry Entry,
  int RegistryIndex,
  int MaterialBatchIndex,
  RideCarStaticSceneModelSource Source =
    RideCarStaticSceneModelSource.BaseWithoutVariantSelection,
  RideCarVariantStaticInstanceEntry? VariantEntry = null
);

/// <summary>Caller-owned static ride-car models plus exact source bindings and counts.</summary>
/// <remarks>
/// Models preserve saved-car order followed by source material-batch order. Every returned model
/// owns a fresh CPU mesh clone and the fresh material produced for that exact car and batch. The
/// caller may transfer the models to a <see cref="Scene"/> or must dispose them directly. Template
/// meshes remain owned by <see cref="RideCarVisualTemplateRegistry"/> and are never transferred.
/// Model bindings are immutable borrowed identity records and own no resources.
/// </remarks>
internal sealed record RideCarStaticSceneBuildResult(
  IReadOnlyList<Model> Models,
  IReadOnlyList<RideCarStaticSceneModelBinding> ModelBindings,
  int SourceCarCount,
  int BuiltCarCount,
  int SkippedCarCount,
  int ModelCount,
  int MissingMaterialBatchCount,
  ulong ClonedVertexCount,
  ulong ClonedIndexCount,
  int UnresolvedCarResourceCount,
  int UnresolvedSavedCursorCount,
  int MissingBodyTemplateCount,
  int UnavailableModelGeometryCount,
  int UnavailableStaticPoseCount,
  RideCarStaticSceneBuildMode BuildMode =
    RideCarStaticSceneBuildMode.BaseWithoutVariantSelection,
  int VariantBuiltCarCount = 0,
  int BaseFallbackBuiltCarCount = 0,
  int UnavailableVisualSelectionCount = 0
);

/// <summary>Resource construction and release seams used to prove transactional ownership.</summary>
/// <remarks>The default operations create ordinary renderer resources. Alternate operations are
/// internal so focused tests can observe partial construction and cleanup without a GL context.</remarks>
internal sealed record RideCarStaticSceneBuilderOperations(
  Func<List<Vertex>, List<uint>, Mesh> CreateMesh,
  Func<Mesh, Model> CreateModel,
  Action<Mesh> DisposeMesh,
  Action<Material> DisposeMaterial,
  Action<Model> DisposeModel
) {
  public static RideCarStaticSceneBuilderOperations Default { get; } = new(
    (vertices, indices) => new Mesh(vertices, indices),
    mesh => new Model(mesh),
    mesh => mesh.Dispose(),
    material => material.Dispose(),
    model => model.Dispose());
}

/// <summary>Builds independent scene-ready models from resolved saved ride-car instances.</summary>
/// <remarks>A null material is the typed missing-FTX outcome and skips that batch before cloning.</remarks>
internal static class RideCarStaticSceneBuilder {
  private const RideCarStaticInstanceIssue KnownIssues =
    RideCarStaticInstanceIssue.UnresolvedCarResource |
    RideCarStaticInstanceIssue.UnresolvedSavedCursor |
    RideCarStaticInstanceIssue.MissingBodyTemplate |
    RideCarStaticInstanceIssue.UnavailableModelGeometry |
    RideCarStaticInstanceIssue.UnavailableStaticPose;
  private const RideCarVariantStaticInstanceIssue KnownVariantIssues =
    RideCarVariantStaticInstanceIssue.UnresolvedCarResource |
    RideCarVariantStaticInstanceIssue.UnresolvedSavedCursor |
    RideCarVariantStaticInstanceIssue.UnavailableVisualSelection |
    RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate |
    RideCarVariantStaticInstanceIssue.UnavailableModelGeometry |
    RideCarVariantStaticInstanceIssue.UnavailableStaticPose;

  public static RideCarStaticSceneBuildResult Build(
    RideCarStaticInstanceRegistry instances,
    Func<RideCarStaticInstanceEntry, StaticShapeMeshBatch, Material?> createMaterial
  ) => Build(
    instances,
    createMaterial,
    RideCarStaticSceneBuilderLimits.Default,
    RideCarStaticSceneBuilderOperations.Default);

  internal static RideCarStaticSceneBuildResult Build(
    RideCarStaticInstanceRegistry instances,
    Func<RideCarStaticInstanceEntry, StaticShapeMeshBatch, Material?> createMaterial,
    RideCarStaticSceneBuilderLimits limits,
    RideCarStaticSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(instances);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    ValidateLimits(limits);

    var plan = Preflight(instances, limits);
    var summary = new SceneSummary(
      RideCarStaticSceneBuildMode.BaseWithoutVariantSelection,
      instances.CarCount,
      instances.UnresolvedCarResourceCount,
      instances.UnresolvedSavedCursorCount,
      instances.MissingBodyTemplateCount,
      instances.UnavailableModelGeometryCount,
      instances.UnavailableStaticPoseCount,
      UnavailableVisualSelectionCount: 0);
    return Build(
      plan,
      item => createMaterial(item.Entry, item.Batch),
      summary,
      operations);
  }

  /// <summary>
  /// Builds selected normal or Wild bodies directly from the exact variant registry.
  /// </summary>
  public static RideCarStaticSceneBuildResult Build(
    RideCarVariantStaticInstanceRegistry variants,
    Func<RideCarVariantStaticInstanceEntry, StaticShapeMeshBatch, Material?>
      createMaterial
  ) => Build(
    variants,
    createMaterial,
    RideCarStaticSceneBuilderLimits.Default,
    RideCarStaticSceneBuilderOperations.Default);

  internal static RideCarStaticSceneBuildResult Build(
    RideCarVariantStaticInstanceRegistry variants,
    Func<RideCarVariantStaticInstanceEntry, StaticShapeMeshBatch, Material?>
      createMaterial,
    RideCarStaticSceneBuilderLimits limits,
    RideCarStaticSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(variants);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    ValidateLimits(limits);

    var plan = PreflightVariants(variants, limits);
    var summary = new SceneSummary(
      RideCarStaticSceneBuildMode.SelectedVariants,
      variants.CarCount,
      variants.UnresolvedCarResourceCount,
      variants.UnresolvedSavedCursorCount,
      variants.UnavailableVisualSelectionCount + variants.UnavailableBodyTemplateCount,
      variants.UnavailableModelGeometryCount,
      variants.UnavailableStaticPoseCount,
      variants.UnavailableVisualSelectionCount);
    return Build(
      plan,
      item => createMaterial(item.VariantEntry!, item.Batch),
      summary,
      operations);
  }

  private static RideCarStaticSceneBuildResult Build(
    BuildPlan plan,
    Func<PlannedBatch, Material?> createMaterial,
    SceneSummary summary,
    RideCarStaticSceneBuilderOperations operations
  ) {
    var models = new List<Model>(plan.Batches.Count);
    var bindings = new List<RideCarStaticSceneModelBinding>(plan.Batches.Count);
    var clonedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var builtCars = new HashSet<int>();
    var variantBuiltCars = new HashSet<int>();
    var fallbackBuiltCars = new HashSet<int>();
    var missingMaterialBatchCount = 0;
    var clonedVertexCount = 0ul;
    var clonedIndexCount = 0ul;
    Mesh? pendingMesh = null;
    Material? pendingMaterial = null;
    Model? pendingModel = null;

    try {
      foreach (var item in plan.Batches) {
        var createdMaterial = createMaterial(item);
        if (createdMaterial == null) {
          missingMaterialBatchCount++;
          continue;
        }
        pendingMaterial = createdMaterial;
        if (createdMaterial.State == State.Disposed)
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} material factory " +
            "returned a disposed material");
        if (!materials.Add(createdMaterial))
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} material factory " +
            "reused a material");

        var vertices = new List<Vertex>(item.Batch.Mesh.Vertices);
        var indices = new List<uint>(item.Batch.Mesh.Indices);
        var createdMesh = operations.CreateMesh(vertices, indices)
          ?? throw Invalid("mesh factory returned null");
        if (plan.TemplateMeshes.Contains(createdMesh))
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} reused a template mesh");
        if (!clonedMeshes.Add(createdMesh))
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} reused a cloned mesh");
        pendingMesh = createdMesh;
        ValidateClone(item, vertices, indices, createdMesh);

        var createdModel = operations.CreateModel(createdMesh)
          ?? throw Invalid("model factory returned null");
        pendingModel = createdModel;
        if (!ReferenceEquals(createdModel.Mesh, createdMesh))
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} model did not adopt " +
            "its cloned mesh");
        pendingMesh = null;
        if (createdModel.Material != null)
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} model was not fresh");
        if (!modelSet.Add(createdModel))
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} reused a model");

        createdModel.Material = createdMaterial;
        pendingMaterial = null;
        createdModel.Transform.Matrix = item.Transform;
        if (!ReferenceEquals(createdModel.Material, createdMaterial) ||
            !ReferenceEquals(createdModel.Mesh, createdMesh) ||
            createdModel.Transform.Matrix != item.Transform)
          throw Invalid(
            $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} model changed exact " +
            "resource or transform identity");
        var binding = new RideCarStaticSceneModelBinding(
          createdModel,
          item.Entry,
          item.Entry.RegistryIndex,
          item.BatchIndex,
          item.Source,
          item.VariantEntry);
        models.Add(createdModel);
        pendingModel = null;
        bindings.Add(binding);
        builtCars.Add(item.Entry.RegistryIndex);
        if (item.Source == RideCarStaticSceneModelSource.SelectedVariant)
          variantBuiltCars.Add(item.Entry.RegistryIndex);
        else
          fallbackBuiltCars.Add(item.Entry.RegistryIndex);
        clonedVertexCount += Convert.ToUInt64(createdMesh.Vertices.Count);
        clonedIndexCount += Convert.ToUInt64(createdMesh.Indices.Count);
      }

      ValidateBindings(plan, models, bindings);
      if (summary.BuildMode == RideCarStaticSceneBuildMode.SelectedVariants &&
          builtCars.Count != variantBuiltCars.Count)
        throw Invalid("variant built-car count does not cover the rendered cars");
      return new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        summary.SourceCarCount,
        builtCars.Count,
        summary.SourceCarCount - builtCars.Count,
        models.Count,
        missingMaterialBatchCount,
        clonedVertexCount,
        clonedIndexCount,
        summary.UnresolvedCarResourceCount,
        summary.UnresolvedSavedCursorCount,
        summary.MissingBodyTemplateCount,
        summary.UnavailableModelGeometryCount,
        summary.UnavailableStaticPoseCount,
        summary.BuildMode,
        variantBuiltCars.Count,
        fallbackBuiltCars.Count,
        summary.UnavailableVisualSelectionCount);
    } catch (Exception primaryError) {
      var cleanupErrors = ReleasePending(
        ref pendingModel,
        ref pendingMaterial,
        ref pendingMesh,
        operations);
      cleanupErrors.AddRange(ReleaseModels(models, operations.DisposeModel));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Static ride-car scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static BuildPlan Preflight(
    RideCarStaticInstanceRegistry instances,
    RideCarStaticSceneBuilderLimits limits
  ) {
    if (instances.Entries == null)
      throw Invalid("instance list is null");
    if (instances.CarCount != instances.Entries.Count)
      throw Invalid("source car count changed from its immutable entry list");
    if (instances.CarCount > limits.MaximumCarCount)
      throw Limit("car", Convert.ToUInt64(limits.MaximumCarCount));

    var batches = new List<PlannedBatch>();
    var templateMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var resolvedCount = 0;
    var unresolvedCarResourceCount = 0;
    var unresolvedSavedCursorCount = 0;
    var missingBodyTemplateCount = 0;
    var unavailableModelGeometryCount = 0;
    var unavailableStaticPoseCount = 0;
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var index in Enumerable.Range(0, instances.Entries.Count)) {
      var entry = instances.Entries[index]
        ?? throw Invalid($"instance list contains null at index {index}");
      if (entry.RegistryIndex != index)
        throw Invalid($"car {index} changed exact saved registry order");
      if ((entry.Issues & ~KnownIssues) != 0)
        throw Invalid($"car {index} contains unknown issue flags {entry.Issues}");

      if ((entry.Issues & RideCarStaticInstanceIssue.UnresolvedCarResource) != 0)
        unresolvedCarResourceCount++;
      if ((entry.Issues & RideCarStaticInstanceIssue.UnresolvedSavedCursor) != 0)
        unresolvedSavedCursorCount++;
      if ((entry.Issues & RideCarStaticInstanceIssue.MissingBodyTemplate) != 0)
        missingBodyTemplateCount++;
      if ((entry.Issues & RideCarStaticInstanceIssue.UnavailableModelGeometry) != 0)
        unavailableModelGeometryCount++;
      if ((entry.Issues & RideCarStaticInstanceIssue.UnavailableStaticPose) != 0)
        unavailableStaticPoseCount++;
      if (!entry.IsResolved) {
        if (entry.Pose != null)
          throw Invalid($"skipped car {index} unexpectedly retains a static pose");
        continue;
      }

      resolvedCount++;
      if (entry.BodyTemplate == null || entry.Pose == null || entry.MaterialBatches == null)
        throw Invalid($"resolved car {index} is missing its template, batches, or pose");
      if (!ReferenceEquals(entry.MaterialBatches, entry.BodyTemplate.Batches))
        throw Invalid($"resolved car {index} changed exact template-batch identity");
      if (!IsFinite(entry.Pose.Transform))
        throw Invalid($"resolved car {index} has a non-finite transform");
      if (entry.MaterialBatches.Count == 0)
        throw Invalid($"resolved car {index} has no material batches");

      foreach (var batchIndex in Enumerable.Range(0, entry.MaterialBatches.Count)) {
        var batch = entry.MaterialBatches[batchIndex]
          ?? throw Invalid($"resolved car {index} batch {batchIndex} is null");
        ValidateTemplateBatch(entry, batch, batchIndex);
        templateMeshes.Add(batch.Mesh);
        Reserve(ref vertexCount, Convert.ToUInt64(batch.Mesh.Vertices.Count),
          limits.MaximumVertices, "vertex");
        Reserve(ref indexCount, Convert.ToUInt64(batch.Mesh.Indices.Count),
          limits.MaximumIndices, "index");
        if (Convert.ToUInt64(batches.Count) >= limits.MaximumModels)
          throw Limit("model", limits.MaximumModels);
        batches.Add(new(entry, batchIndex, batch, entry.Pose.Transform));
      }
    }

    ValidateAdvertisedCounts(
      instances,
      resolvedCount,
      unresolvedCarResourceCount,
      unresolvedSavedCursorCount,
      missingBodyTemplateCount,
      unavailableModelGeometryCount,
      unavailableStaticPoseCount);
    return new(
      Array.AsReadOnly(batches.ToArray()),
      templateMeshes,
      Array.AsReadOnly(
        instances.Entries.Cast<RideCarStaticInstanceEntry?>().ToArray()));
  }

  private static BuildPlan PreflightVariants(
    RideCarVariantStaticInstanceRegistry variants,
    RideCarStaticSceneBuilderLimits limits
  ) {
    if (variants.Entries == null || variants.CarCount != variants.Entries.Count)
      throw Invalid("variant car count changed from its immutable entry list");
    if (variants.CarCount > limits.MaximumCarCount)
      throw Limit("car", Convert.ToUInt64(limits.MaximumCarCount));

    var batches = new List<PlannedBatch>();
    var templateMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var bindingEntries = new RideCarStaticInstanceEntry?[variants.CarCount];
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var index in Enumerable.Range(0, variants.CarCount)) {
      var variant = variants.Entries[index]
        ?? throw Invalid($"variant instance list contains null at index {index}");
      ValidateVariantEntry(variant, index);
      if (!variant.IsResolved) continue;

      var entry = SnapshotVariantEntry(variant);
      bindingEntries[index] = entry;
      foreach (var batchIndex in Enumerable.Range(0, entry.MaterialBatches!.Count)) {
        var batch = entry.MaterialBatches[batchIndex]
          ?? throw Invalid($"resolved variant car {index} batch {batchIndex} is null");
        ValidateTemplateBatch(entry, batch, batchIndex);
        templateMeshes.Add(batch.Mesh);
        Reserve(ref vertexCount, Convert.ToUInt64(batch.Mesh.Vertices.Count),
          limits.MaximumVertices, "vertex");
        Reserve(ref indexCount, Convert.ToUInt64(batch.Mesh.Indices.Count),
          limits.MaximumIndices, "index");
        if (Convert.ToUInt64(batches.Count) >= limits.MaximumModels)
          throw Limit("model", limits.MaximumModels);
        batches.Add(new(
          entry,
          batchIndex,
          batch,
          entry.Pose!.Transform,
          RideCarStaticSceneModelSource.SelectedVariant,
          variant));
      }
    }
    return new(
      Array.AsReadOnly(batches.ToArray()),
      templateMeshes,
      Array.AsReadOnly(bindingEntries));
  }

  private static void ValidateVariantEntry(
    RideCarVariantStaticInstanceEntry entry,
    int index
  ) {
    if (entry.RegistryIndex != index || (entry.Issues & ~KnownVariantIssues) != 0)
      throw Invalid($"variant car {index} changed order or contains unknown issue flags");
    if (entry.CarRuntime == null || entry.SavedCursor == null ||
        entry.VisualSelection == null || entry.VisualTemplate == null ||
        entry.VisualSelection.RegistryIndex != index ||
        entry.VisualTemplate.RegistryIndex != index ||
        !ReferenceEquals(entry.VisualSelection.CarRuntime, entry.CarRuntime) ||
        !ReferenceEquals(entry.VisualTemplate.Selection, entry.VisualSelection) ||
        !ReferenceEquals(entry.SavedCursor.CarRuntime, entry.CarRuntime))
      throw Invalid($"variant car {index} changed exact runtime, selector, or template identity");
    if (!entry.IsResolved) {
      if (entry.Pose != null)
        throw Invalid($"skipped variant car {index} unexpectedly retains a static pose");
      return;
    }
    if (!entry.VisualSelection.IsSelected || !entry.VisualTemplate.IsResolved ||
        entry.SelectedVariant == null || entry.VisualSelection.Body == null ||
        entry.BodyTemplate == null || entry.Geometry == null || entry.Pose == null ||
        entry.MaterialBatches == null || entry.MaterialBatches.Count == 0 ||
        !ReferenceEquals(entry.VisualTemplate.Template, entry.BodyTemplate) ||
        !ReferenceEquals(entry.BodyTemplate.Link, entry.VisualSelection.Body) ||
        !ReferenceEquals(entry.MaterialBatches, entry.BodyTemplate.Batches) ||
        !IsFinite(entry.Pose.Transform))
      throw Invalid($"resolved variant car {index} changed exact selected render evidence");
  }

  private static RideCarStaticInstanceEntry SnapshotVariantEntry(
    RideCarVariantStaticInstanceEntry variant
  ) => new(
    variant.RegistryIndex,
    variant.CarRuntime,
    variant.SavedCursor,
    RideCarStaticInstanceIssue.None,
    variant.BodyTemplate,
    variant.Geometry,
    variant.Pose,
    variant.GeometryUnavailableDetail,
    variant.StaticPoseUnavailableDetail);

  private static void ValidateTemplateBatch(
    RideCarStaticInstanceEntry entry,
    StaticShapeMeshBatch batch,
    int batchIndex
  ) {
    if (batch.Mesh == null || batch.Mesh.Vertices == null || batch.Mesh.Indices == null)
      throw Invalid(
        $"resolved car {entry.RegistryIndex} batch {batchIndex} has no mesh data");
    if (batch.Mesh.State == State.Disposed)
      throw Invalid(
        $"resolved car {entry.RegistryIndex} batch {batchIndex} uses a disposed template mesh");
    if (batch.SourceMeshIndex < 0 || string.IsNullOrWhiteSpace(batch.SourceMeshName))
      throw Invalid(
        $"resolved car {entry.RegistryIndex} batch {batchIndex} has invalid source identity");
    if (batch.Mesh.Vertices.Count == 0 || batch.Mesh.Indices.Count == 0 ||
        batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid(
        $"resolved car {entry.RegistryIndex} batch {batchIndex} has invalid triangle geometry");

    foreach (var vertex in batch.Mesh.Vertices)
      if (!IsFinite(vertex))
        throw Invalid(
          $"resolved car {entry.RegistryIndex} batch {batchIndex} has a non-finite vertex");
    foreach (var vertexIndex in batch.Mesh.Indices)
      if (vertexIndex >= Convert.ToUInt32(batch.Mesh.Vertices.Count))
        throw Invalid(
          $"resolved car {entry.RegistryIndex} batch {batchIndex} has an out-of-range index");
  }

  private static void ValidateClone(
    PlannedBatch item,
    List<Vertex> vertices,
    List<uint> indices,
    Mesh mesh
  ) {
    if (mesh.State == State.Disposed)
      throw Invalid(
        $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} produced a disposed mesh");
    if (!ReferenceEquals(mesh.Vertices, vertices) ||
        !ReferenceEquals(mesh.Indices, indices))
      throw Invalid(
        $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} mesh did not adopt its " +
        "cloned CPU lists");
    if (ReferenceEquals(mesh.Vertices, item.Batch.Mesh.Vertices) ||
        ReferenceEquals(mesh.Indices, item.Batch.Mesh.Indices))
      throw Invalid(
        $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} retained template CPU lists");
    if (mesh.Vertices.Count != item.Batch.Mesh.Vertices.Count ||
        mesh.Indices.Count != item.Batch.Mesh.Indices.Count ||
        !mesh.Vertices.SequenceEqual(item.Batch.Mesh.Vertices) ||
        !mesh.Indices.SequenceEqual(item.Batch.Mesh.Indices))
      throw Invalid(
        $"car {item.Entry.RegistryIndex} batch {item.BatchIndex} changed cloned geometry");

    mesh.Name = string.IsNullOrWhiteSpace(item.Batch.Mesh.Name)
      ? $"{item.Batch.SourceMeshName} [ride car {item.Entry.RegistryIndex}]"
      : $"{item.Batch.Mesh.Name} [ride car {item.Entry.RegistryIndex}]";
  }

  private static void ValidateAdvertisedCounts(
    RideCarStaticInstanceRegistry instances,
    int resolvedCount,
    int unresolvedCarResourceCount,
    int unresolvedSavedCursorCount,
    int missingBodyTemplateCount,
    int unavailableModelGeometryCount,
    int unavailableStaticPoseCount
  ) {
    if (instances.ResolvedCount != resolvedCount ||
        instances.UnresolvedCount != instances.CarCount - resolvedCount)
      throw Invalid("resolved or skipped car counts changed from their entry outcomes");
    if (instances.UnresolvedCarResourceCount != unresolvedCarResourceCount ||
        instances.UnresolvedSavedCursorCount != unresolvedSavedCursorCount ||
        instances.MissingBodyTemplateCount != missingBodyTemplateCount ||
        instances.UnavailableModelGeometryCount != unavailableModelGeometryCount ||
        instances.UnavailableStaticPoseCount != unavailableStaticPoseCount)
      throw Invalid("typed skipped-car counts changed from their entry outcomes");
  }

  private static void ValidateBindings(
    BuildPlan plan,
    IReadOnlyList<Model> models,
    IReadOnlyList<RideCarStaticSceneModelBinding> bindings
  ) {
    if (bindings.Count != models.Count)
      throw Invalid("model and exact source-binding counts differ");

    foreach (var index in Enumerable.Range(0, models.Count)) {
      var binding = bindings[index]
        ?? throw Invalid($"model binding {index} is null");
      if (!ReferenceEquals(binding.Model, models[index]))
        throw Invalid($"model binding {index} changed exact model identity");
      if (binding.RegistryIndex < 0 || binding.RegistryIndex >= plan.BindingEntries.Count)
        throw Invalid($"model binding {index} has an out-of-range registry index");
      if (!ReferenceEquals(binding.Entry, plan.BindingEntries[binding.RegistryIndex]) ||
          binding.Entry.RegistryIndex != binding.RegistryIndex)
        throw Invalid($"model binding {index} changed exact saved-car identity");
      if (binding.Source == RideCarStaticSceneModelSource.SelectedVariant) {
        if (binding.VariantEntry == null || !binding.VariantEntry.IsResolved ||
            !ReferenceEquals(binding.Entry.CarRuntime, binding.VariantEntry.CarRuntime) ||
            !ReferenceEquals(binding.Entry.BodyTemplate, binding.VariantEntry.BodyTemplate) ||
            !ReferenceEquals(binding.Entry.Pose, binding.VariantEntry.Pose))
          throw Invalid($"model binding {index} changed exact selected-variant identity");
      } else if (binding.VariantEntry != null) {
        throw Invalid($"base model binding {index} unexpectedly retains variant evidence");
      }
      if (!binding.Entry.IsResolved || binding.Entry.MaterialBatches == null)
        throw Invalid($"model binding {index} references an unresolved saved car");
      if (binding.MaterialBatchIndex < 0 ||
          binding.MaterialBatchIndex >= binding.Entry.MaterialBatches.Count)
        throw Invalid($"model binding {index} has an out-of-range material-batch index");
      if (binding.Model.Material == null || binding.Model.Mesh == null)
        throw Invalid($"model binding {index} references an incomplete model");
    }
  }

  private static void ValidateOperations(RideCarStaticSceneBuilderOperations operations) {
    if (operations.CreateMesh == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException(
        "Static ride-car scene operations must all be configured.",
        nameof(operations));
  }

  private static void ValidateLimits(RideCarStaticSceneBuilderLimits limits) {
    if (limits.MaximumCarCount < 0 || limits.MaximumModels == 0 ||
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
    RideCarStaticSceneBuilderOperations operations
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
      var lastIndex = models.Count - 1;
      var model = models[lastIndex];
      models.RemoveAt(lastIndex);
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

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(string message) =>
    new($"Static ride-car scene input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Static ride-car scene {resource} count exceeds the limit {maximum}.");

  private sealed record PlannedBatch(
    RideCarStaticInstanceEntry Entry,
    int BatchIndex,
    StaticShapeMeshBatch Batch,
    Matrix4x4 Transform,
    RideCarStaticSceneModelSource Source =
      RideCarStaticSceneModelSource.BaseWithoutVariantSelection,
    RideCarVariantStaticInstanceEntry? VariantEntry = null
  );

  private sealed record BuildPlan(
    IReadOnlyList<PlannedBatch> Batches,
    IReadOnlySet<Mesh> TemplateMeshes,
    IReadOnlyList<RideCarStaticInstanceEntry?> BindingEntries
  );

  private sealed record SceneSummary(
    RideCarStaticSceneBuildMode BuildMode,
    int SourceCarCount,
    int UnresolvedCarResourceCount,
    int UnresolvedSavedCursorCount,
    int MissingBodyTemplateCount,
    int UnavailableModelGeometryCount,
    int UnavailableStaticPoseCount,
    int UnavailableVisualSelectionCount
  );
}

/// <summary>Allocation ceilings for cloned static ride-car scene resources.</summary>
internal readonly record struct RideCarStaticSceneBuilderLimits(
  int MaximumCarCount,
  ulong MaximumModels,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static RideCarStaticSceneBuilderLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumModels: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000);
}
