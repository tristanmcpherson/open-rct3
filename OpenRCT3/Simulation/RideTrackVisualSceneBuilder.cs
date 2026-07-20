// Ride Track Visual Scene Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Caller-owned ride-track models plus exact placement outcome counts.</summary>
/// <remarks>
/// Every returned model owns the mesh adapted for one placement and its fresh material. The caller
/// must transfer the models to a <see cref="Scene"/> or dispose them directly.
/// </remarks>
internal sealed record RideTrackVisualSceneBuildResult(
  IReadOnlyList<Model> Models,
  int PlacementCount,
  int RenderedPlacementCount,
  int SkippedPlacementCount,
  int MissingMaterialBatchCount
) {
  public int ModelCount => Models.Count;
}

/// <summary>Renderer construction and release seams used by focused ownership tests.</summary>
internal sealed record RideTrackVisualSceneBuilderOperations(
  Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> AdaptStaticShape,
  Func<BoneShape, IReadOnlyList<StaticShapeMeshBatch>> AdaptBoneShape,
  Func<RideTrackPlacement, SceneryItem, Terrain, Matrix4x4> CreatePlacementTransform,
  Func<Mesh, Model> CreateModel,
  Action<Mesh> DisposeMesh,
  Action<Material> DisposeMaterial,
  Action<Model> DisposeModel
) {
  public static RideTrackVisualSceneBuilderOperations Default { get; } = new(
    StaticShapeMeshBuilder.BuildBatches,
    BoneShapeMeshBuilder.BuildBatches,
    RideTrackPlacementTransform.Create,
    mesh => new Model(mesh),
    mesh => mesh.Dispose(),
    material => material.Dispose(),
    model => model.Dispose());
}

/// <summary>Builds exact TKS visual models for decoded DAT ride-track placements.</summary>
/// <remarks>
/// Selection deliberately mirrors <see cref="SceneryGeometryBuilder"/>: SID SVD references are
/// ordered alternatives, not additive parts. The first declared alternative with a supported shape
/// wins, then the first serialized SHS or BSH LOD wins without distance sorting. Bone shapes remain
/// in their serialized rest pose; this layer invents no animation or dynamic LOD policy.
/// </remarks>
internal static class RideTrackVisualSceneBuilder {
  public static RideTrackVisualSceneBuildResult Build(
    RideTrackResourceResolution placements,
    RideTrackVisualResourceBridgeResult visuals,
    Terrain terrain,
    RideCarVisualMaterialResolver materials
  ) {
    ArgumentNullException.ThrowIfNull(materials);
    return Build(
      placements,
      visuals,
      terrain,
      (link, placement, batch) => materials.Resolve(
        batch,
        link.AllowedArchivePaths,
        SceneryFlexiColours.FromSerialized(
          placement.FlexiColour0,
          placement.FlexiColour1,
          placement.FlexiColour2)).Material,
      RideTrackVisualSceneBuilderLimits.Default,
      RideTrackVisualSceneBuilderOperations.Default);
  }

  internal static RideTrackVisualSceneBuildResult Build(
    RideTrackResourceResolution placements,
    RideTrackVisualResourceBridgeResult visuals,
    Terrain terrain,
    Func<RideTrackVisualLink, RideTrackPlacement, StaticShapeMeshBatch, Material?>
      createMaterial,
    RideTrackVisualSceneBuilderLimits limits,
    RideTrackVisualSceneBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(visuals);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(createMaterial);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    ValidateLimits(limits);
    if (placements.Placements == null) throw Invalid("placement list is null");
    if (visuals.Visuals == null) throw Invalid("visual-link list is null");
    if (placements.Placements.Count > limits.MaximumPlacements)
      throw Limit("placement", Convert.ToUInt64(limits.MaximumPlacements));

    var visualIndex = IndexVisuals(visuals.Visuals);
    var models = new List<Model>();
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var adaptedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var unownedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var unownedMeshOrder = new List<Mesh>();
    var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var renderedPlacements = 0;
    var unresolvedPlacements = 0;
    var missingMaterialBatches = 0;
    var modelBudget = 0ul;
    var vertexBudget = 0ul;
    var indexBudget = 0ul;
    Model? pendingModel = null;
    Material? pendingMaterial = null;

    try {
      foreach (var placementLink in placements.Placements) {
        if (placementLink == null) throw Invalid("placement list contains null");
        ValidatePlacementLink(placementLink);
        if (!placementLink.IsResolved) {
          unresolvedPlacements++;
          continue;
        }

        var section = placementLink.Section!;
        if (!visualIndex.TryGetValue(section, out var alternatives))
          throw Invalid(
            $"resolved placement {placementLink.Placement.SourceEntryId} has no exact TKS " +
            "visual set");
        var selected = alternatives.FirstOrDefault(link => link.Lods.Count > 0);
        if (selected == null) continue;
        var selectedLod = selected.Lods[0];
        var sceneryItem = selected.Scenery.Source!.Resource;
        var transform = operations.CreatePlacementTransform(
          placementLink.Placement,
          sceneryItem,
          terrain);
        if (!IsFinite(transform))
          throw Invalid(
            $"placement {placementLink.Placement.SourceEntryId} produced a non-finite " +
            "world transform");

        var batches = Adapt(selectedLod, operations);
        if (batches == null)
          throw Invalid(
            $"selected LOD '{selectedLod.Lod.Name}' adapter returned a null batch list");
        OwnAdaptedBatches(
          selectedLod,
          batches,
          adaptedMeshes,
          unownedMeshes,
          unownedMeshOrder);
        if (batches.Count != selectedLod.Materials.Count)
          throw Invalid(
            $"selected LOD '{selectedLod.Lod.Name}' adapted {batches.Count} batches for " +
            $"{selectedLod.Materials.Count} exact material identities");

        foreach (var batchIndex in Enumerable.Range(0, batches.Count)) {
          var batch = batches[batchIndex];
          ValidateAdaptedBatch(selectedLod, batch, batchIndex);
          Reserve(ref modelBudget, 1, limits.MaximumModels, "model");
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
        }

        var placementModelCount = 0;
        foreach (var batchIndex in Enumerable.Range(0, batches.Count)) {
          var batch = batches[batchIndex];
          var material = createMaterial(selected, placementLink.Placement, batch);
          if (material == null) {
            missingMaterialBatches++;
            ReleaseUnowned(batch.Mesh, unownedMeshes, operations.DisposeMesh);
            continue;
          }
          if (material.State == State.Disposed)
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} " +
              "material factory returned a disposed material");
          if (!materials.Add(material))
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} " +
              "material factory reused a material");
          pendingMaterial = material;

          var model = operations.CreateModel(batch.Mesh)
            ?? throw Invalid("model factory returned null");
          if (!modelSet.Add(model))
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} " +
              "model factory reused a model");
          pendingModel = model;
          if (!ReferenceEquals(model.Mesh, batch.Mesh))
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} model " +
              "did not adopt its adapted mesh");
          if (!unownedMeshes.Remove(batch.Mesh))
            throw Invalid("adapted mesh ownership changed before model adoption");
          if (model.Material != null)
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} model " +
              "was not fresh");

          model.Material = material;
          pendingMaterial = null;
          model.Transform.Matrix = transform;
          if (!ReferenceEquals(model.Mesh, batch.Mesh) ||
              !ReferenceEquals(model.Material, material) ||
              model.Transform.Matrix != transform)
            throw Invalid(
              $"placement {placementLink.Placement.SourceEntryId} batch {batchIndex} changed " +
              "exact mesh, material, or transform identity");
          models.Add(model);
          pendingModel = null;
          placementModelCount++;
        }
        if (placementModelCount > 0) renderedPlacements++;
      }

      if (unresolvedPlacements != placements.UnresolvedPlacementCount)
        throw Invalid(
          $"advertised unresolved count {placements.UnresolvedPlacementCount} differs from " +
          $"the {unresolvedPlacements} exact placement outcomes");
      if (unownedMeshes.Count != 0)
        throw Invalid("successful build retains unowned adapted meshes");
      return new RideTrackVisualSceneBuildResult(
        Array.AsReadOnly(models.ToArray()),
        placements.Placements.Count,
        renderedPlacements,
        placements.Placements.Count - renderedPlacements,
        missingMaterialBatches);
    } catch (Exception primaryError) {
      var cleanupErrors = ReleasePending(
        ref pendingModel,
        ref pendingMaterial,
        operations);
      cleanupErrors.AddRange(ReleaseUnownedMeshes(
        unownedMeshOrder,
        unownedMeshes,
        operations.DisposeMesh));
      cleanupErrors.AddRange(ReleaseModels(models, operations.DisposeModel));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Ride-track visual scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static IReadOnlyDictionary<
    TrackSectionResourceLink,
    IReadOnlyList<RideTrackVisualLink>> IndexVisuals(
      IReadOnlyList<RideTrackVisualLink> visuals
    ) {
    var grouped = new Dictionary<TrackSectionResourceLink, List<RideTrackVisualLink>>(
      ReferenceEqualityComparer.Instance);
    foreach (var link in visuals) {
      if (link?.Section == null || link.Scenery == null || link.VisualSource == null ||
          link.Lods == null || link.AllowedArchivePaths == null)
        throw Invalid("visual-link list contains an incomplete link");
      if (!ReferenceEquals(link.Scenery, link.Section.Scenery))
        throw Invalid(
          $"TKS '{link.Section.Source.Resource.Name}' visual changed exact SID-link identity");
      if (!grouped.TryGetValue(link.Section, out var values))
        grouped.Add(link.Section, values = []);
      values.Add(link);
    }

    var result = new Dictionary<
      TrackSectionResourceLink,
      IReadOnlyList<RideTrackVisualLink>>(ReferenceEqualityComparer.Instance);
    foreach (var pair in grouped) {
      var section = pair.Key;
      var scenery = section.Scenery.Source?.Resource
        ?? throw Invalid($"TKS '{section.Source.Resource.Name}' has no exact SID source");
      if (scenery.VisualRefs == null || scenery.VisualRefs.Count == 0)
        throw Invalid($"SID '{scenery.Name}' has no declared SVD alternatives");
      if (pair.Value.Count != scenery.VisualRefs.Count)
        throw Invalid(
          $"SID '{scenery.Name}' has {pair.Value.Count} visual links for " +
          $"{scenery.VisualRefs.Count} declared alternatives");

      var ordered = new RideTrackVisualLink[scenery.VisualRefs.Count];
      var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var index in Enumerable.Range(0, scenery.VisualRefs.Count)) {
        var name = ParseTaggedReference(
          scenery.VisualRefs[index],
          "svd",
          $"SID '{scenery.Name}' visual");
        if (!seenNames.Add(name))
          throw Invalid($"SID '{scenery.Name}' repeats visual '{name}:svd'");
        var matches = pair.Value.Where(link => string.Equals(
          link.VisualSource.Resource.Name,
          name,
          StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
          throw Invalid(
            $"SID '{scenery.Name}' visual '{name}:svd' has {matches.Length} exact links");
        ValidateVisualLink(matches[0], section, scenery);
        ordered[index] = matches[0];
      }
      result.Add(section, Array.AsReadOnly(ordered));
    }
    return result;
  }

  private static void ValidateVisualLink(
    RideTrackVisualLink link,
    TrackSectionResourceLink section,
    SceneryItem scenery
  ) {
    var visual = link.VisualSource.Resource;
    if (visual.Lods == null) throw Invalid($"SVD '{visual.Name}' has a null LOD list");
    if (link.AllowedArchivePaths.Count == 0)
      throw Invalid($"SVD '{visual.Name}' has an empty archive closure");
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in link.AllowedArchivePaths) {
      if (string.IsNullOrWhiteSpace(path) || !allowed.Add(path))
        throw Invalid($"SVD '{visual.Name}' has an invalid or repeated archive path");
    }
    if (!allowed.Contains(section.Source.File.Path) ||
        !allowed.Contains(link.Scenery.Source!.File.Path) ||
        !allowed.Contains(link.VisualSource.File.Path))
      throw Invalid($"SVD '{visual.Name}' archive closure omits its exact TKS/SID/SVD chain");

    var serialized = visual.Lods.Where(lod =>
      lod.Type is SvdLodType.StaticShape or SvdLodType.BoneShape).ToArray();
    if (serialized.Length != link.Lods.Count)
      throw Invalid(
        $"SVD '{visual.Name}' has {link.Lods.Count} linked shape LODs for " +
        $"{serialized.Length} serialized supported LODs");
    foreach (var index in Enumerable.Range(0, serialized.Length)) {
      var linked = link.Lods[index]
        ?? throw Invalid($"SVD '{visual.Name}' shape LOD list contains null");
      if (!ReferenceEquals(linked.Lod, serialized[index]))
        throw Invalid($"SVD '{visual.Name}' changed serialized supported LOD order");
      var shapePath = linked.Lod.Type switch {
        SvdLodType.StaticShape when linked.StaticShapeSource != null &&
          linked.BoneShapeSource == null => linked.StaticShapeSource.File.Path,
        SvdLodType.BoneShape when linked.BoneShapeSource != null &&
          linked.StaticShapeSource == null => linked.BoneShapeSource.File.Path,
        _ => throw Invalid(
          $"SVD '{visual.Name}' LOD '{linked.Lod.Name}' changed exact shape type"),
      };
      if (!allowed.Contains(shapePath))
        throw Invalid(
          $"SVD '{visual.Name}' LOD '{linked.Lod.Name}' shape leaves its archive closure");
    }
  }

  private static void ValidatePlacementLink(RideTrackResourceLink link) {
    if (link.PlacementSection == null || link.PlacementSection.Placement == null)
      throw Invalid("placement resource link has no exact DAT placement");
    if (!ReferenceEquals(link.Placement, link.PlacementSection.Placement))
      throw Invalid("placement resource link changed exact DAT identity");
    if (!link.IsResolved) {
      if (link.PlacementSection.Source != null)
        throw Invalid(
          $"unresolved placement {link.Placement.SourceEntryId} retains a TKS source");
      return;
    }

    var source = link.PlacementSection.Source
      ?? throw Invalid(
        $"resolved placement {link.Placement.SourceEntryId} has no overlay TKS source");
    var section = link.Section!;
    if (!ReferenceEquals(source.Resource, section.Source.Resource) ||
        !ReferenceEquals(source.File, section.Source.File))
      throw Invalid(
        $"resolved placement {link.Placement.SourceEntryId} changed exact TKS identity");
  }

  private static IReadOnlyList<StaticShapeMeshBatch> Adapt(
    RideTrackVisualShapeLodLink selected,
    RideTrackVisualSceneBuilderOperations operations
  ) => selected.Lod.Type switch {
    SvdLodType.StaticShape when selected.StaticShape != null &&
      selected.BoneShape == null => operations.AdaptStaticShape(selected.StaticShape),
    SvdLodType.BoneShape when selected.BoneShape != null &&
      selected.StaticShape == null => operations.AdaptBoneShape(selected.BoneShape),
    _ => throw Invalid(
      $"selected LOD '{selected.Lod.Name}' has no exact supported shape"),
  };

  private static void OwnAdaptedBatches(
    RideTrackVisualShapeLodLink selected,
    IReadOnlyList<StaticShapeMeshBatch> batches,
    ISet<Mesh> adaptedMeshes,
    ISet<Mesh> unownedMeshes,
    ICollection<Mesh> unownedMeshOrder
  ) {
    InvalidDataException? firstError = null;
    foreach (var batchIndex in Enumerable.Range(0, batches.Count)) {
      var batch = batches[batchIndex];
      if (batch?.Mesh == null) {
        firstError ??= Invalid(
          $"selected LOD '{selected.Lod.Name}' batch {batchIndex} has no mesh data");
        continue;
      }
      if (batch.Mesh.State == State.Disposed) {
        firstError ??= Invalid(
          $"selected LOD '{selected.Lod.Name}' batch {batchIndex} is disposed");
        continue;
      }
      if (!adaptedMeshes.Add(batch.Mesh)) {
        firstError ??= Invalid(
          $"selected LOD '{selected.Lod.Name}' batch {batchIndex} reused a mesh");
        continue;
      }
      if (!unownedMeshes.Add(batch.Mesh)) {
        firstError ??= Invalid("adapted mesh ownership index rejected a fresh mesh");
        continue;
      }
      unownedMeshOrder.Add(batch.Mesh);
    }
    if (firstError != null) throw firstError;
  }

  private static void ValidateAdaptedBatch(
    RideTrackVisualShapeLodLink selected,
    StaticShapeMeshBatch batch,
    int batchIndex
  ) {
    if (batch.Mesh.Vertices == null || batch.Mesh.Indices == null)
      throw Invalid($"selected LOD '{selected.Lod.Name}' batch {batchIndex} has no mesh data");

    var material = selected.Materials[batchIndex];
    if (material.MeshIndex != batchIndex || batch.SourceMeshIndex != batchIndex ||
        !string.Equals(
          material.MeshName,
          batch.SourceMeshName,
          StringComparison.Ordinal) ||
        !string.Equals(
          material.FlexibleTextureReference,
          batch.FtxRef,
          StringComparison.Ordinal) ||
        !string.Equals(
          material.TextureStyleReference,
          batch.TxsRef,
          StringComparison.Ordinal))
      throw Invalid(
        $"selected LOD '{selected.Lod.Name}' batch {batchIndex} changed exact mesh or " +
        "material identity");
    if (batch.Mesh.Vertices.Count == 0 || batch.Mesh.Indices.Count == 0 ||
        batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid(
        $"selected LOD '{selected.Lod.Name}' batch {batchIndex} has invalid triangle geometry");
  }

  private static string ParseTaggedReference(
    string? reference,
    string expectedTag,
    string description
  ) {
    if (string.IsNullOrWhiteSpace(reference))
      throw Invalid($"{description} reference is empty");
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1)
      throw Invalid($"{description} reference '{reference}' is malformed");
    var tag = reference[(separator + 1)..];
    if (!tag.Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} reference '{reference}' identifies '{tag}' instead of " +
        $"'{expectedTag}'");
    return reference[..separator];
  }

  private static void ValidateOperations(RideTrackVisualSceneBuilderOperations operations) {
    if (operations.AdaptStaticShape == null || operations.AdaptBoneShape == null ||
        operations.CreatePlacementTransform == null || operations.CreateModel == null ||
        operations.DisposeMesh == null || operations.DisposeMaterial == null ||
        operations.DisposeModel == null)
      throw new ArgumentException(
        "Ride-track visual scene operations must all be configured.",
        nameof(operations));
  }

  private static void ValidateLimits(RideTrackVisualSceneBuilderLimits limits) {
    if (limits.MaximumPlacements < 0 || limits.MaximumModels == 0 ||
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

  private static void ReleaseUnowned(
    Mesh mesh,
    ISet<Mesh> unownedMeshes,
    Action<Mesh> disposeMesh
  ) {
    if (!unownedMeshes.Remove(mesh))
      throw Invalid("adapted mesh was not caller-owned before release");
    disposeMesh(mesh);
  }

  private static List<Exception> ReleasePending(
    ref Model? model,
    ref Material? material,
    RideTrackVisualSceneBuilderOperations operations
  ) {
    var errors = new List<Exception>();
    TryRelease(ref model, operations.DisposeModel, errors);
    TryRelease(ref material, operations.DisposeMaterial, errors);
    return errors;
  }

  private static List<Exception> ReleaseUnownedMeshes(
    IReadOnlyList<Mesh> order,
    ISet<Mesh> unowned,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    foreach (var mesh in order.Reverse()) {
      if (!unowned.Remove(mesh)) continue;
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

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-track visual scene input: {message}.");

  private static InvalidOperationException Limit(string resource, ulong maximum) =>
    new($"Ride-track visual scene {resource} count exceeds the limit {maximum}.");
}

/// <summary>Allocation ceilings for ride-track visual scene resources.</summary>
internal readonly record struct RideTrackVisualSceneBuilderLimits(
  int MaximumPlacements,
  ulong MaximumModels,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static RideTrackVisualSceneBuilderLimits Default { get; } = new(
    MaximumPlacements: 1_000_000,
    MaximumModels: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000);
}
