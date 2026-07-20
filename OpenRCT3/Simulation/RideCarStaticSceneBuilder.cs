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

/// <summary>Caller-owned static ride-car models plus explicit source and skip counts.</summary>
/// <remarks>
/// Models preserve saved-car order followed by source material-batch order. Every returned model
/// owns a fresh CPU mesh clone and the fresh material produced for that exact car and batch. The
/// caller may transfer the models to a <see cref="Scene"/> or must dispose them directly. Template
/// meshes remain owned by <see cref="RideCarVisualTemplateRegistry"/> and are never transferred.
/// </remarks>
internal sealed record RideCarStaticSceneBuildResult(
  IReadOnlyList<Model> Models,
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
  int UnavailableStaticPoseCount
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
    var models = new List<Model>(plan.Batches.Count);
    var clonedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var builtCars = new HashSet<int>();
    var missingMaterialBatchCount = 0;
    var clonedVertexCount = 0ul;
    var clonedIndexCount = 0ul;
    Mesh? pendingMesh = null;
    Material? pendingMaterial = null;
    Model? pendingModel = null;

    try {
      foreach (var item in plan.Batches) {
        var createdMaterial = createMaterial(item.Entry, item.Batch);
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
        models.Add(createdModel);
        builtCars.Add(item.Entry.RegistryIndex);
        clonedVertexCount += Convert.ToUInt64(createdMesh.Vertices.Count);
        clonedIndexCount += Convert.ToUInt64(createdMesh.Indices.Count);
        pendingModel = null;
      }

      return new(
        Array.AsReadOnly(models.ToArray()),
        instances.CarCount,
        builtCars.Count,
        instances.CarCount - builtCars.Count,
        models.Count,
        missingMaterialBatchCount,
        clonedVertexCount,
        clonedIndexCount,
        instances.UnresolvedCarResourceCount,
        instances.UnresolvedSavedCursorCount,
        instances.MissingBodyTemplateCount,
        instances.UnavailableModelGeometryCount,
        instances.UnavailableStaticPoseCount);
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
      templateMeshes);
  }

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
    Matrix4x4 Transform
  );

  private sealed record BuildPlan(
    IReadOnlyList<PlannedBatch> Batches,
    IReadOnlySet<Mesh> TemplateMeshes
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
