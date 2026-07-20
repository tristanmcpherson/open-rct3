// Terrain Blend Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One exact terrain blend-layer model and its borrowed catalog texture identity.</summary>
public sealed record TerrainBlendSceneModelBinding(
  int BatchIndex,
  TerrainBlendLayerMeshBatch Batch,
  Texture AlbedoTexture,
  TerrainBlend Material,
  Model Model
) {
  public TerrainBlendLayerPassRole Role => Batch.Role;
  public byte SurfaceIndex => Batch.SurfaceIndex;
}

/// <summary>Caller-owned terrain blend models plus exact source bindings and role counts.</summary>
/// <remarks>
/// Every returned model owns its layer mesh and fresh material. Bindings and catalog textures are
/// borrowed identities. The caller must transfer the models to a <see cref="Scene"/> or dispose
/// them directly.
/// </remarks>
public sealed record TerrainBlendSceneBuildResult(
  IReadOnlyList<Model> Models,
  IReadOnlyList<TerrainBlendSceneModelBinding> ModelBindings,
  int SourceBatchCount,
  int BaseModelCount,
  int ContributionModelCount
) {
  public int ModelCount => Models.Count;
}

/// <summary>Allocation, lookup, and release seams used by focused ownership tests.</summary>
internal sealed record TerrainBlendSceneBuilderOperations(
  Func<Terrain, Vector4, string, IReadOnlyList<TerrainBlendLayerMeshBatch>> BuildMeshBatches,
  Func<Terrain, byte, Texture> ResolveSurfaceTexture,
  Func<TerrainBlendPass, Material> CreateMaterial,
  Func<Mesh, Model> CreateModel,
  Action<Mesh> DisposeMesh,
  Action<Material> DisposeMaterial,
  Action<Model> DisposeModel
) {
  public static TerrainBlendSceneBuilderOperations Default { get; } = new(
    TerrainBlendLayerMeshBuilder.BuildBatches,
    ResolveCatalogSurface,
    pass => new TerrainBlend(pass),
    mesh => new Model(mesh),
    mesh => mesh.Dispose(),
    material => material.Dispose(),
    model => model.Dispose());

  private static Texture ResolveCatalogSurface(Terrain terrain, byte surfaceIndex) {
    var catalog = terrain.TextureCatalog
      ?? throw new InvalidOperationException("Terrain has no texture catalog.");
    return catalog.GetSurface(surfaceIndex);
  }
}

/// <summary>Builds scene-ready models for exact GroundBlended terrain layer batches.</summary>
public static class TerrainBlendSceneBuilder {
  public static TerrainBlendSceneBuildResult Build(
    Terrain terrain,
    Vector4 color,
    string name = "Terrain Blend"
  ) => Build(terrain, color, name, TerrainBlendSceneBuilderOperations.Default);

  internal static TerrainBlendSceneBuildResult Build(
    Terrain terrain,
    Vector4 color,
    string name,
    TerrainBlendSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    if (!IsFinite(color))
      throw new ArgumentOutOfRangeException(nameof(color), "Terrain tint must be finite.");
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Terrain blend scene name cannot be empty.", nameof(name));

    var models = new List<Model>();
    var bindings = new List<TerrainBlendSceneModelBinding>();
    var materialSet = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var unownedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var unownedMeshOrder = new List<Mesh>();
    Material? pendingMaterial = null;
    Model? pendingModel = null;
    IReadOnlyList<TerrainBlendLayerMeshBatch>? batches = null;
    var baseModelCount = 0;
    var contributionModelCount = 0;

    try {
      batches = operations.BuildMeshBatches(terrain, color, name)
        ?? throw Invalid("mesh builder returned a null batch list");
      OwnBatchMeshes(batches, unownedMeshes, unownedMeshOrder);
      ValidateBatchOrder(batches);
      models.EnsureCapacity(batches.Count);
      bindings.EnsureCapacity(batches.Count);

      var batchIndex = 0;
      foreach (var batch in batches) {
        var texture = operations.ResolveSurfaceTexture(terrain, batch.SurfaceIndex)
          ?? throw Invalid($"batch {batchIndex} texture lookup returned null");
        if (texture.State == State.Disposed)
          throw Invalid($"batch {batchIndex} resolved a disposed catalog texture");

        var pass = ToMaterialPass(batch.Role);
        var createdMaterial = operations.CreateMaterial(pass)
          ?? throw Invalid($"batch {batchIndex} material factory returned null");
        if (createdMaterial.State == State.Disposed)
          throw Invalid($"batch {batchIndex} material factory returned a disposed material");
        if (!materialSet.Add(createdMaterial))
          throw Invalid($"batch {batchIndex} material factory reused a material");
        pendingMaterial = createdMaterial;
        var material = ValidateMaterial(createdMaterial, pass, batchIndex);
        material.AlbedoTexture = texture;
        if (!ReferenceEquals(material.AlbedoTexture, texture))
          throw Invalid($"batch {batchIndex} material changed exact albedo identity");

        var createdModel = operations.CreateModel(batch.Mesh)
          ?? throw Invalid($"batch {batchIndex} model factory returned null");
        if (!modelSet.Add(createdModel))
          throw Invalid($"batch {batchIndex} model factory reused a model");
        pendingModel = createdModel;
        if (!ReferenceEquals(createdModel.Mesh, batch.Mesh))
          throw Invalid($"batch {batchIndex} model did not adopt its layer mesh");
        if (batch.Mesh.State == State.Disposed)
          throw Invalid($"batch {batchIndex} model factory disposed its layer mesh");
        if (!unownedMeshes.Remove(batch.Mesh))
          throw Invalid($"batch {batchIndex} layer mesh ownership changed before transfer");
        if (createdModel.Material != null) {
          if (ReferenceEquals(createdModel.Material, material)) pendingMaterial = null;
          throw Invalid($"batch {batchIndex} model factory did not return a fresh model");
        }

        createdModel.Material = material;
        pendingMaterial = null;
        if (!ReferenceEquals(createdModel.Mesh, batch.Mesh) ||
            !ReferenceEquals(createdModel.Material, material))
          throw Invalid($"batch {batchIndex} changed exact mesh or material identity");

        var binding = new TerrainBlendSceneModelBinding(
          batchIndex, batch, texture, material, createdModel);
        models.Add(createdModel);
        pendingModel = null;
        bindings.Add(binding);
        if (batch.Role == TerrainBlendLayerPassRole.Base) baseModelCount++;
        else contributionModelCount++;
        batchIndex++;
      }

      if (unownedMeshes.Count != 0)
        throw Invalid("successful build retained unowned layer meshes");
      ValidateResult(
        batches,
        models,
        bindings,
        baseModelCount,
        contributionModelCount);
      return new TerrainBlendSceneBuildResult(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        batches.Count,
        baseModelCount,
        contributionModelCount);
    } catch (Exception primaryError) {
      var cleanupErrors = ReleasePending(
        ref pendingModel,
        ref pendingMaterial,
        operations);
      cleanupErrors.AddRange(ReleaseModels(models, operations.DisposeModel));
      cleanupErrors.AddRange(ReleaseUnownedMeshes(
        unownedMeshOrder,
        unownedMeshes,
        operations.DisposeMesh));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Terrain blend scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static void OwnBatchMeshes(
    IReadOnlyList<TerrainBlendLayerMeshBatch> batches,
    ISet<Mesh> unownedMeshes,
    ICollection<Mesh> unownedMeshOrder
  ) {
    InvalidDataException? firstError = null;
    var batchIndex = 0;
    foreach (var batch in batches) {
      if (batch?.Mesh == null) {
        firstError ??= Invalid($"batch {batchIndex} has no mesh");
        batchIndex++;
        continue;
      }
      if (batch.Mesh.State == State.Disposed) {
        firstError ??= Invalid($"batch {batchIndex} has a disposed mesh");
        batchIndex++;
        continue;
      }
      if (!unownedMeshes.Add(batch.Mesh)) {
        firstError ??= Invalid($"batch {batchIndex} reused a mesh");
        batchIndex++;
        continue;
      }
      unownedMeshOrder.Add(batch.Mesh);
      batchIndex++;
    }
    if (firstError != null) throw firstError;
  }

  private static void ValidateBatchOrder(
    IReadOnlyList<TerrainBlendLayerMeshBatch> batches
  ) {
    (TerrainBlendLayerPassRole Role, byte SurfaceIndex)? previous = null;
    var batchIndex = 0;
    foreach (var batch in batches) {
      ValidateRole(batch.Role, batchIndex);
      var key = (batch.Role, batch.SurfaceIndex);
      if (previous.HasValue && Compare(previous.Value, key) >= 0)
        throw Invalid(
          $"batch {batchIndex} is not in strict role and surface order");
      previous = key;
      batchIndex++;
    }
  }

  private static TerrainBlend ValidateMaterial(
    Material material,
    TerrainBlendPass pass,
    int batchIndex
  ) {
    if (material is not TerrainBlend terrainBlend)
      throw Invalid($"batch {batchIndex} material factory returned a non-terrain material");
    var expectedState = pass == TerrainBlendPass.Base
      ? MaterialRenderState.Opaque
      : MaterialRenderState.AdditiveContribution;
    if (terrainBlend.RenderState != expectedState)
      throw Invalid($"batch {batchIndex} material does not match its exact blend role");
    if (terrainBlend.AlbedoTexture != null || terrainBlend.NormalMap != null ||
        terrainBlend.SpecularMap != null || terrainBlend.EmissiveMap != null)
      throw Invalid($"batch {batchIndex} material factory did not return a fresh material");
    return terrainBlend;
  }

  private static void ValidateResult(
    IReadOnlyList<TerrainBlendLayerMeshBatch> batches,
    IReadOnlyList<Model> models,
    IReadOnlyList<TerrainBlendSceneModelBinding> bindings,
    int baseModelCount,
    int contributionModelCount
  ) {
    if (models.Count != batches.Count || bindings.Count != batches.Count ||
        baseModelCount + contributionModelCount != batches.Count)
      throw Invalid("model, binding, source batch, or role counts differ");

    foreach (var index in Enumerable.Range(0, batches.Count)) {
      var binding = bindings[index]
        ?? throw Invalid($"model binding {index} is null");
      if (binding.BatchIndex != index ||
          !ReferenceEquals(binding.Batch, batches[index]) ||
          !ReferenceEquals(binding.Model, models[index]) ||
          !ReferenceEquals(binding.Model.Mesh, binding.Batch.Mesh) ||
          !ReferenceEquals(binding.Model.Material, binding.Material) ||
          !ReferenceEquals(binding.Material.AlbedoTexture, binding.AlbedoTexture))
        throw Invalid($"model binding {index} changed exact resource identity");
      var pass = ToMaterialPass(binding.Role);
      var expectedState = pass == TerrainBlendPass.Base
        ? MaterialRenderState.Opaque
        : MaterialRenderState.AdditiveContribution;
      if (binding.Material.RenderState != expectedState)
        throw Invalid($"model binding {index} changed exact blend role");
    }
  }

  private static TerrainBlendPass ToMaterialPass(TerrainBlendLayerPassRole role) =>
    role switch {
      TerrainBlendLayerPassRole.Base => TerrainBlendPass.Base,
      TerrainBlendLayerPassRole.Contribution => TerrainBlendPass.Contribution,
      _ => throw new InvalidDataException($"Unsupported terrain blend-layer role {role}."),
    };

  private static void ValidateRole(TerrainBlendLayerPassRole role, int batchIndex) {
    if (role is TerrainBlendLayerPassRole.Base or TerrainBlendLayerPassRole.Contribution)
      return;
    throw Invalid($"batch {batchIndex} has unsupported role {role}");
  }

  private static int Compare(
    (TerrainBlendLayerPassRole Role, byte SurfaceIndex) first,
    (TerrainBlendLayerPassRole Role, byte SurfaceIndex) second
  ) {
    var roleComparison = first.Role.CompareTo(second.Role);
    return roleComparison != 0
      ? roleComparison
      : first.SurfaceIndex.CompareTo(second.SurfaceIndex);
  }

  private static void ValidateOperations(TerrainBlendSceneBuilderOperations operations) {
    if (operations.BuildMeshBatches == null || operations.ResolveSurfaceTexture == null ||
        operations.CreateMaterial == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException(
        "Terrain blend scene operations must all be configured.",
        nameof(operations));
  }

  private static List<Exception> ReleasePending(
    ref Model? model,
    ref Material? material,
    TerrainBlendSceneBuilderOperations operations
  ) {
    var errors = new List<Exception>();
    TryRelease(ref model, operations.DisposeModel, errors);
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

  private static List<Exception> ReleaseUnownedMeshes(
    IReadOnlyList<Mesh> order,
    ISet<Mesh> unownedMeshes,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    for (var index = order.Count - 1; index >= 0; index--) {
      var mesh = order[index];
      if (!unownedMeshes.Remove(mesh)) continue;
      try {
        disposeMesh(mesh);
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

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Terrain blend scene input is invalid: {message}.");
}
