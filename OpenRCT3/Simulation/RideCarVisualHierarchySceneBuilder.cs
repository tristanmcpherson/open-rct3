// Ride Car Visual Hierarchy Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One exact source binding for a scene-owned axle or wheel model.</summary>
internal sealed record RideCarVisualHierarchySceneModelBinding(
  Model Model,
  RideCarVisualHierarchyStaticPartInstance Instance,
  int PartRegistryIndex,
  int MaterialBatchIndex
);

/// <summary>Caller-owned hierarchy models plus immutable source bindings and counts.</summary>
internal sealed record RideCarVisualHierarchySceneBuildResult(
  IReadOnlyList<Model> Models,
  IReadOnlyList<RideCarVisualHierarchySceneModelBinding> ModelBindings,
  int SourceCarCount,
  int PlannedCarCount,
  int SourcePartCount,
  int BuiltPartCount,
  int SkippedPartCount,
  int ModelCount,
  int MissingMaterialBatchCount,
  int UpstreamIssueCount,
  int TemplateUnavailableCount,
  ulong ClonedVertexCount,
  ulong ClonedIndexCount
);

/// <summary>Transactionally clones exact axle and wheel batches into scene-ready models.</summary>
internal static class RideCarVisualHierarchySceneBuilder {
  public static RideCarVisualHierarchySceneBuildResult Build(
    RideCarVisualHierarchyStaticInstanceRegistry instances,
    Func<RideCarVisualHierarchyStaticPartInstance, StaticShapeMeshBatch, Material?>
      createMaterial
  ) => Build(
    instances,
    createMaterial,
    RideCarVisualHierarchySceneBuilderLimits.Default,
    RideCarStaticSceneBuilderOperations.Default);

  internal static RideCarVisualHierarchySceneBuildResult Build(
    RideCarVisualHierarchyStaticInstanceRegistry instances,
    Func<RideCarVisualHierarchyStaticPartInstance, StaticShapeMeshBatch, Material?>
      createMaterial,
    RideCarVisualHierarchySceneBuilderLimits limits,
    RideCarStaticSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(instances);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateLimits(limits);
    ValidateOperations(operations);
    var plan = Preflight(instances, limits);
    var models = new List<Model>(plan.Batches.Count);
    var bindings = new List<RideCarVisualHierarchySceneModelBinding>(plan.Batches.Count);
    var clonedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var builtParts = new HashSet<int>();
    var missingMaterialBatchCount = 0;
    var clonedVertexCount = 0ul;
    var clonedIndexCount = 0ul;
    Mesh? pendingMesh = null;
    Material? pendingMaterial = null;
    Model? pendingModel = null;

    try {
      foreach (var item in plan.Batches) {
        var material = createMaterial(item.Instance, item.Batch);
        if (material == null) {
          missingMaterialBatchCount++;
          continue;
        }
        pendingMaterial = material;
        if (material.State == State.Disposed || !materials.Add(material))
          throw Invalid(
            $"part {item.Instance.RegistryIndex} batch {item.BatchIndex} material is " +
            "disposed or reused");

        var vertices = new List<Vertex>(item.Batch.Mesh.Vertices);
        var indices = new List<uint>(item.Batch.Mesh.Indices);
        var mesh = operations.CreateMesh(vertices, indices)
          ?? throw Invalid("mesh factory returned null");
        pendingMesh = mesh;
        if (plan.TemplateMeshes.Contains(mesh) || !clonedMeshes.Add(mesh))
          throw Invalid(
            $"part {item.Instance.RegistryIndex} batch {item.BatchIndex} reused a mesh");
        ValidateClone(item, vertices, indices, mesh);

        var model = operations.CreateModel(mesh)
          ?? throw Invalid("model factory returned null");
        pendingModel = model;
        if (!ReferenceEquals(model.Mesh, mesh))
          throw Invalid("model did not adopt its cloned mesh");
        pendingMesh = null;
        if (model.Material != null || !modelSet.Add(model))
          throw Invalid("model factory returned a reused or non-fresh model");
        model.Material = material;
        pendingMaterial = null;
        model.Transform.Matrix = item.Instance.WorldTransform;
        if (!ReferenceEquals(model.Mesh, mesh) || !ReferenceEquals(model.Material, material) ||
            model.Transform.Matrix != item.Instance.WorldTransform)
          throw Invalid("model changed exact mesh, material, or transform identity");

        models.Add(model);
        pendingModel = null;
        bindings.Add(new(
          model,
          item.Instance,
          item.Instance.RegistryIndex,
          item.BatchIndex));
        builtParts.Add(item.Instance.RegistryIndex);
        clonedVertexCount += Convert.ToUInt64(mesh.Vertices.Count);
        clonedIndexCount += Convert.ToUInt64(mesh.Indices.Count);
      }

      ValidateBindings(plan, models, bindings);
      return new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        instances.SourceCarCount,
        instances.PlannedCarCount,
        instances.PartCount,
        builtParts.Count,
        instances.PartCount - builtParts.Count,
        models.Count,
        missingMaterialBatchCount,
        instances.Issues.Count,
        instances.TemplateUnavailableCount,
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
        "Ride-car hierarchy scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static BuildPlan Preflight(
    RideCarVisualHierarchyStaticInstanceRegistry instances,
    RideCarVisualHierarchySceneBuilderLimits limits
  ) {
    if (instances.Instances == null || instances.Issues == null ||
        instances.PartCount != instances.Instances.Count ||
        instances.EligibleCarCount > instances.SourceCarCount ||
        instances.PlannedCarCount > instances.EligibleCarCount ||
        instances.UnavailableCarCount != instances.SourceCarCount - instances.PlannedCarCount ||
        instances.TemplateUnavailableCount != instances.Issues.Count(issue =>
          issue.Status ==
            RideCarVisualHierarchyStaticInstanceIssueStatus.TemplateUnavailable))
      throw Invalid("static hierarchy registry counts have drifted");
    if (instances.SourceCarCount > limits.MaximumCarCount)
      throw Limit("car", Convert.ToUInt64(limits.MaximumCarCount));
    if (instances.PartCount > limits.MaximumPartCount)
      throw Limit("part", Convert.ToUInt64(limits.MaximumPartCount));

    var batches = new List<PlannedBatch>();
    var templateMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var index in Enumerable.Range(0, instances.Instances.Count)) {
      var instance = instances.Instances[index]
        ?? throw Invalid($"part instance {index} is null");
      ValidateInstance(instance, index);
      foreach (var batchIndex in Enumerable.Range(0, instance.MaterialBatches.Count)) {
        var batch = instance.MaterialBatches[batchIndex]
          ?? throw Invalid($"part {index} batch {batchIndex} is null");
        ValidateBatch(instance, batch, batchIndex);
        templateMeshes.Add(batch.Mesh);
        Reserve(
          ref vertexCount,
          Convert.ToUInt64(batch.Mesh.Vertices.Count),
          limits.MaximumVertices,
          "vertex");
        Reserve(
          ref indexCount,
          Convert.ToUInt64(batch.Mesh.Indices.Count),
          limits.MaximumIndices,
          "index");
        if (Convert.ToUInt64(batches.Count) >= limits.MaximumModels)
          throw Limit("model", limits.MaximumModels);
        batches.Add(new(instance, batchIndex, batch));
      }
    }
    return new(Array.AsReadOnly(batches.ToArray()), templateMeshes);
  }

  private static void ValidateInstance(
    RideCarVisualHierarchyStaticPartInstance instance,
    int index
  ) {
    if (instance.RegistryIndex != index || instance.SavedCar == null ||
        instance.Hierarchy == null || instance.Part == null || instance.Visual == null ||
        instance.Template == null || instance.MaterialBatches == null ||
        !instance.SavedCar.IsResolved || instance.Role != instance.Part.Role ||
        instance.Type != instance.Part.Type ||
        !ReferenceEquals(instance.Part.ShapeVisual, instance.Visual) ||
        !ReferenceEquals(instance.Template.Link, instance.Visual) ||
        !ReferenceEquals(instance.MaterialBatches, instance.Template.Batches) ||
        !ContainsReference(instance.Hierarchy.Parts, instance.Part) ||
        !TrackMath.IsFinite(instance.WorldTransform))
      throw Invalid($"part {index} changed exact static hierarchy identity");
    if (instance.MaterialBatches.Count == 0)
      throw Invalid($"part {index} has no material batches");
  }

  private static void ValidateBatch(
    RideCarVisualHierarchyStaticPartInstance instance,
    StaticShapeMeshBatch batch,
    int batchIndex
  ) {
    if (!ReferenceEquals(instance.MaterialBatches[batchIndex], batch) || batch.Mesh == null ||
        batch.Mesh.Vertices == null || batch.Mesh.Indices == null ||
        batch.Mesh.State == State.Disposed || batch.SourceMeshIndex < 0 ||
        string.IsNullOrWhiteSpace(batch.SourceMeshName) ||
        batch.Mesh.Vertices.Count == 0 || batch.Mesh.Indices.Count == 0 ||
        batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid($"part {instance.RegistryIndex} batch {batchIndex} has invalid geometry");
    foreach (var vertex in batch.Mesh.Vertices)
      if (!IsFinite(vertex))
        throw Invalid(
          $"part {instance.RegistryIndex} batch {batchIndex} has a non-finite vertex");
    foreach (var vertexIndex in batch.Mesh.Indices)
      if (vertexIndex >= Convert.ToUInt32(batch.Mesh.Vertices.Count))
        throw Invalid(
          $"part {instance.RegistryIndex} batch {batchIndex} has an out-of-range index");
  }

  private static void ValidateClone(
    PlannedBatch item,
    List<Vertex> vertices,
    List<uint> indices,
    Mesh mesh
  ) {
    if (mesh.State == State.Disposed || !ReferenceEquals(mesh.Vertices, vertices) ||
        !ReferenceEquals(mesh.Indices, indices) ||
        ReferenceEquals(mesh.Vertices, item.Batch.Mesh.Vertices) ||
        ReferenceEquals(mesh.Indices, item.Batch.Mesh.Indices) ||
        !mesh.Vertices.SequenceEqual(item.Batch.Mesh.Vertices) ||
        !mesh.Indices.SequenceEqual(item.Batch.Mesh.Indices))
      throw Invalid(
        $"part {item.Instance.RegistryIndex} batch {item.BatchIndex} changed cloned geometry");
    mesh.Name = string.IsNullOrWhiteSpace(item.Batch.Mesh.Name)
      ? $"{item.Batch.SourceMeshName} [ride car {item.Instance.SavedCar.RegistryIndex} " +
        $"{item.Instance.Role}]"
      : $"{item.Batch.Mesh.Name} [ride car {item.Instance.SavedCar.RegistryIndex} " +
        $"{item.Instance.Role}]";
  }

  private static void ValidateBindings(
    BuildPlan plan,
    IReadOnlyList<Model> models,
    IReadOnlyList<RideCarVisualHierarchySceneModelBinding> bindings
  ) {
    if (bindings.Count != models.Count)
      throw Invalid("model and binding counts differ");
    foreach (var index in Enumerable.Range(0, models.Count)) {
      var binding = bindings[index];
      if (binding == null || !ReferenceEquals(binding.Model, models[index]) ||
          binding.PartRegistryIndex != binding.Instance.RegistryIndex ||
          binding.MaterialBatchIndex < 0 ||
          binding.MaterialBatchIndex >= binding.Instance.MaterialBatches.Count ||
          !ReferenceEquals(
            binding.Instance.MaterialBatches[binding.MaterialBatchIndex],
            binding.Model.Mesh == null ? null :
              plan.Batches.Single(item =>
                ReferenceEquals(item.Instance, binding.Instance) &&
                item.BatchIndex == binding.MaterialBatchIndex).Batch) ||
          binding.Model.Material == null || binding.Model.Mesh == null)
        throw Invalid($"model binding {index} changed exact part identity");
    }
  }

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
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

  private static void ValidateOperations(RideCarStaticSceneBuilderOperations operations) {
    if (operations.CreateMesh == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException("Hierarchy scene operations must all be configured.",
        nameof(operations));
  }

  private static void ValidateLimits(RideCarVisualHierarchySceneBuilderLimits limits) {
    if (limits.MaximumCarCount < 0 || limits.MaximumPartCount < 0 ||
        limits.MaximumModels == 0 || limits.MaximumVertices == 0 ||
        limits.MaximumIndices == 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void Reserve(
    ref ulong total,
    ulong addition,
    ulong maximum,
    string resource
  ) {
    if (addition > maximum || total > maximum - addition)
      throw Limit(resource, maximum);
    total += addition;
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

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car hierarchy scene input is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Ride-car hierarchy scene {resource} count exceeds the limit {maximum}.");

  private sealed record PlannedBatch(
    RideCarVisualHierarchyStaticPartInstance Instance,
    int BatchIndex,
    StaticShapeMeshBatch Batch
  );

  private sealed record BuildPlan(
    IReadOnlyList<PlannedBatch> Batches,
    IReadOnlySet<Mesh> TemplateMeshes
  );
}

/// <summary>Allocation ceilings for cloned axle and wheel scene resources.</summary>
internal readonly record struct RideCarVisualHierarchySceneBuilderLimits(
  int MaximumCarCount,
  int MaximumPartCount,
  ulong MaximumModels,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static RideCarVisualHierarchySceneBuilderLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumPartCount: 6_000_000,
    MaximumModels: 24_000_000,
    MaximumVertices: 100_000_000,
    MaximumIndices: 300_000_000);
}
