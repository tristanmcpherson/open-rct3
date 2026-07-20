// Ride Car Visual Template Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OpenRCT3.Simulation;

/// <summary>The decoded shape kind retained by one ride-car visual template.</summary>
internal enum RideCarVisualTemplateShapeKind {
  StaticShape,
  BoneShape,
}

/// <summary>One exact RIC visual occurrence adapted into reusable material-preserving batches.</summary>
/// <remarks>
/// The link, LOD, and decoded shape records are borrowed. <see cref="Batches"/> and their meshes are
/// owned by the containing <see cref="RideCarVisualTemplateRegistry"/>.
/// </remarks>
internal sealed record RideCarVisualMeshTemplate(
  RideCarVisualShapeLink Link,
  RideVisualShapeLodLink Lod,
  RideCarVisualTemplateShapeKind ShapeKind,
  StaticShape? StaticShape,
  BoneShape? BoneShape,
  IReadOnlyList<StaticShapeMeshBatch> Batches
) {
  public object Shape => (object?)StaticShape ?? BoneShape!;
}

/// <summary>Owns reusable CPU mesh templates for exact resolved ride-car visual occurrences.</summary>
/// <remarks>
/// <para>
/// RCT3 serializes SVD LODs as an ordered array. Until camera-distance LOD selection is implemented
/// from executable-backed behavior, this registry deliberately selects the first serialized
/// supported LOD whose exact SHS or BSH target resolved. It never sorts or selects by
/// <see cref="SceneryItemVisualLod.Distance"/>.
/// </para>
/// <para>
/// Decoded resources and OVL provenance are borrowed managed records. The registry owns only the
/// adapted <see cref="Mesh"/> objects. Repeated decoded shape object identities share one batch array,
/// and disposal releases unique meshes once in reverse construction order.
/// </para>
/// </remarks>
internal sealed class RideCarVisualTemplateRegistry : IDisposable {
  private readonly Mesh[] ownedMeshes;
  private readonly Action<Mesh> disposeMesh;
  private bool disposed;

  private RideCarVisualTemplateRegistry(
    IReadOnlyList<RideCarVisualMeshTemplate> templates,
    Mesh[] ownedMeshes,
    Action<Mesh> disposeMesh,
    int visualOccurrenceCount,
    int unresolvedVisualCount,
    int bodyVisualOccurrenceCount,
    int unresolvedBodyVisualCount,
    int distinctShapeResourceCount,
    int batchCount,
    ulong vertexCount,
    ulong indexCount
  ) {
    Templates = templates;
    BodyTemplates = Array.AsReadOnly(templates
      .Where(template => template.Link.Visual.Role == RideVisualRole.Body)
      .ToArray());
    this.ownedMeshes = ownedMeshes;
    this.disposeMesh = disposeMesh;
    VisualOccurrenceCount = visualOccurrenceCount;
    UnresolvedVisualCount = unresolvedVisualCount;
    BodyVisualOccurrenceCount = bodyVisualOccurrenceCount;
    UnresolvedBodyVisualCount = unresolvedBodyVisualCount;
    DistinctShapeResourceCount = distinctShapeResourceCount;
    BatchCount = batchCount;
    VertexCount = vertexCount;
    IndexCount = indexCount;
  }

  public IReadOnlyList<RideCarVisualMeshTemplate> Templates { get; }
  public IReadOnlyList<RideCarVisualMeshTemplate> BodyTemplates { get; }
  public int VisualOccurrenceCount { get; }
  public int UnresolvedVisualCount { get; }
  public int BodyVisualOccurrenceCount { get; }
  public int UnresolvedBodyVisualCount { get; }
  public int DistinctShapeResourceCount { get; }
  public int BatchCount { get; }
  public ulong VertexCount { get; }
  public ulong IndexCount { get; }
  public bool IsDisposed => disposed;

  public static RideCarVisualTemplateRegistry Build(
    RideCarVisualResourceBridgeResult resources
  ) => Build(
    resources,
    StaticShapeMeshBuilder.BuildBatches,
    BoneShapeMeshBuilder.BuildBatches,
    mesh => mesh.Dispose(),
    RideCarVisualTemplateRegistryLimits.Default);

  internal static RideCarVisualTemplateRegistry Build(
    RideCarVisualResourceBridgeResult resources,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> adaptStaticShape,
    Func<BoneShape, IReadOnlyList<StaticShapeMeshBatch>> adaptBoneShape,
    Action<Mesh> disposeMesh,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(adaptStaticShape);
    ArgumentNullException.ThrowIfNull(adaptBoneShape);
    ArgumentNullException.ThrowIfNull(disposeMesh);
    ValidateLimits(limits);

    var selections = ValidateAndSelect(
      resources,
      limits,
      out var visualOccurrenceCount,
      out var unresolvedVisualCount,
      out var bodyVisualOccurrenceCount,
      out var unresolvedBodyVisualCount);
    var geometry = PreflightGeometry(selections, limits);
    var batchesByShape = new Dictionary<object, IReadOnlyList<StaticShapeMeshBatch>>(
      ReferenceEqualityComparer.Instance);
    var templates = new List<RideCarVisualMeshTemplate>(selections.Count);
    var ownedMeshes = new List<Mesh>(geometry.BatchCount);
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);

    try {
      foreach (var selection in selections) {
        if (!batchesByShape.TryGetValue(selection.Shape, out var batches)) {
          batches = Adapt(
            selection,
            adaptStaticShape,
            adaptBoneShape,
            ownedMeshes,
            ownedMeshSet);
          batchesByShape.Add(selection.Shape, batches);
        }
        templates.Add(new RideCarVisualMeshTemplate(
          selection.Link,
          selection.Lod,
          selection.Kind,
          selection.StaticShape,
          selection.BoneShape,
          batches));
      }

      return new RideCarVisualTemplateRegistry(
        Array.AsReadOnly(templates.ToArray()),
        ownedMeshes.ToArray(),
        disposeMesh,
        visualOccurrenceCount,
        unresolvedVisualCount,
        bodyVisualOccurrenceCount,
        unresolvedBodyVisualCount,
        batchesByShape.Count,
        geometry.BatchCount,
        geometry.VertexCount,
        geometry.IndexCount);
    } catch (Exception buildError) {
      var cleanupErrors = DisposeMeshes(ownedMeshes, disposeMesh);
      if (cleanupErrors.Count > 0)
        throw new AggregateException([buildError, .. cleanupErrors]);
      throw;
    }
  }

  /// <summary>Releases each unique adapted mesh once in reverse construction order.</summary>
  public void Dispose() {
    if (disposed) return;
    disposed = true;

    var errors = DisposeMeshes(ownedMeshes, disposeMesh);
    if (errors.Count > 0) throw new AggregateException(errors);
  }

  private static List<TemplateSelection> ValidateAndSelect(
    RideCarVisualResourceBridgeResult resources,
    RideCarVisualTemplateRegistryLimits limits,
    out int visualOccurrenceCount,
    out int unresolvedVisualCount,
    out int bodyVisualOccurrenceCount,
    out int unresolvedBodyVisualCount
  ) {
    if (resources.Visuals == null) throw Invalid("visual occurrence list is null");
    if (resources.UnresolvedShapeReferenceCount < 0)
      throw Invalid("unresolved shape-reference count is negative");
    if (Convert.ToUInt64(resources.Visuals.Count) > limits.MaximumVisualOccurrences)
      throw Invalid(
        $"visual occurrence count exceeds the limit {limits.MaximumVisualOccurrences}");

    var selections = new List<TemplateSelection>();
    var seenLinks = new HashSet<RideCarVisualShapeLink>(ReferenceEqualityComparer.Instance);
    var seenBodyOccurrences = new HashSet<CarOccurrence>(CarOccurrenceComparer.Instance);
    var closures = new Dictionary<RideCarResourceSource, IReadOnlySet<string>>(
      ReferenceEqualityComparer.Instance);
    var unresolvedLodCount = 0ul;
    visualOccurrenceCount = resources.Visuals.Count;
    unresolvedVisualCount = 0;
    bodyVisualOccurrenceCount = 0;
    unresolvedBodyVisualCount = 0;

    foreach (var link in resources.Visuals) {
      if (link == null) throw Invalid("visual occurrence list contains null");
      if (!seenLinks.Add(link))
        throw Invalid("visual occurrence list repeats the same link object");
      var selected = ValidateOccurrence(link, closures, limits, ref unresolvedLodCount);
      if (link.Visual.Role == RideVisualRole.Body) {
        var occurrence = new CarOccurrence(link.Ride, link.Train, link.Car);
        if (!seenBodyOccurrences.Add(occurrence))
          throw Invalid(
            $"RIC '{link.Car.Car!.Name}' has more than one resolved body visual in one occurrence");
        bodyVisualOccurrenceCount = checked(bodyVisualOccurrenceCount + 1);
      }
      if (selected == null) {
        unresolvedVisualCount = checked(unresolvedVisualCount + 1);
        if (link.Visual.Role == RideVisualRole.Body)
          unresolvedBodyVisualCount = checked(unresolvedBodyVisualCount + 1);
        continue;
      }
      if (Convert.ToUInt64(selections.Count) >= limits.MaximumTemplates)
        throw Invalid($"visual template count exceeds the limit {limits.MaximumTemplates}");
      selections.Add(selected);
    }

    if (unresolvedLodCount != Convert.ToUInt64(resources.UnresolvedShapeReferenceCount))
      throw Invalid(
        $"bridge reports {resources.UnresolvedShapeReferenceCount} unresolved shape references, " +
        $"but its exact LOD links contain {unresolvedLodCount}");
    return selections;
  }

  private static TemplateSelection? ValidateOccurrence(
    RideCarVisualShapeLink link,
    IDictionary<RideCarResourceSource, IReadOnlySet<string>> closures,
    RideCarVisualTemplateRegistryLimits limits,
    ref ulong unresolvedLodCount
  ) {
    if (link.Ride == null || link.Train == null || link.Car == null || link.Visual == null)
      throw Invalid("visual occurrence has a null graph link");
    if (link.Ride.Source == null || link.Ride.Trains == null)
      throw Invalid("visual occurrence has an invalid tracked-ride source");
    ValidateSource(
      link.Ride.Source.File,
      link.Ride.Source.Resource?.Name,
      FileType.TrackedRide,
      "TRR",
      limits);
    if (!ContainsReference(link.Ride.Trains, link.Train))
      throw Invalid("visual occurrence train is not owned by its exact ride link");

    if (!link.Train.IsResolved || link.Train.Source == null || link.Train.Cars == null)
      throw Invalid("visual occurrence has an unresolved or malformed RIT link");
    var trainResource = link.Train.Source.Resource ??
      throw Invalid("visual occurrence RIT source has no decoded resource");
    ValidateSource(
      link.Train.Source.File,
      trainResource.Name,
      FileType.RideTrain,
      "RIT",
      limits);
    ValidateIdentifier(link.Train.Reference, "RIT reference", limits);
    if (!string.Equals(
          link.Train.Reference,
          trainResource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"RIT reference '{link.Train.Reference}' does not match its exact decoded resource");
    if (!ContainsReference(link.Train.Cars, link.Car))
      throw Invalid("visual occurrence car is not owned by its exact train link");

    if (!link.Car.IsResolved || link.Car.Source == null || link.Car.Visuals == null)
      throw Invalid("visual occurrence has an unresolved or malformed RIC link");
    var carResource = link.Car.Source.Resource ??
      throw Invalid("visual occurrence RIC source has no decoded resource");
    if (!Enum.IsDefined(link.Car.Role))
      throw Invalid($"RIC occurrence has unsupported role {link.Car.Role}");
    ValidateSource(
      link.Car.Source.File,
      carResource.Name,
      FileType.RideCar,
      "RIC",
      limits);
    var carName = ParseTaggedReference(link.Car.Reference, "ric", "RIC reference", limits);
    if (!string.Equals(carName, carResource.Name, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"RIC reference '{link.Car.Reference}' does not match its exact decoded resource");
    if (!ContainsReference(link.Car.Visuals, link.Visual))
      throw Invalid("visual occurrence SVD is not owned by its exact car link");

    if (!link.Visual.IsResolved || link.Visual.Source == null)
      throw Invalid("visual occurrence has an unresolved SVD link");
    var visualResource = link.Visual.Source.Resource ??
      throw Invalid("visual occurrence SVD source has no decoded resource");
    if (!Enum.IsDefined(link.Visual.Role))
      throw Invalid($"visual occurrence has unsupported role {link.Visual.Role}");
    ValidateSource(
      link.Visual.Source.File,
      visualResource.Name,
      FileType.SceneryItemVisual,
      "SVD",
      limits);
    var visualName = ParseTaggedReference(
      link.Visual.Reference,
      "svd",
      "SVD reference",
      limits);
    if (!string.Equals(
          visualName,
          visualResource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"SVD reference '{link.Visual.Reference}' does not match its exact decoded resource");

    if (!closures.TryGetValue(link.Car.Source, out var allowedPaths)) {
      allowedPaths = ValidateClosure(link.Car.Source, limits);
      closures.Add(link.Car.Source, allowedPaths);
    }
    if (!allowedPaths.Contains(link.Visual.Source.File.Path))
      throw Invalid(
        $"SVD '{link.Visual.Source.Resource.Name}' is outside its exact RIC archive closure");

    return ValidateLods(link, allowedPaths, limits, ref unresolvedLodCount);
  }

  private static TemplateSelection? ValidateLods(
    RideCarVisualShapeLink link,
    IReadOnlySet<string> allowedPaths,
    RideCarVisualTemplateRegistryLimits limits,
    ref ulong unresolvedLodCount
  ) {
    if (link.Lods == null) throw Invalid("visual occurrence has a null linked LOD list");
    var visual = link.Visual.Source!.Resource;
    if (visual.Lods == null) throw Invalid($"SVD '{visual.Name}' has a null serialized LOD list");
    if (Convert.ToUInt64(visual.Lods.Count) > limits.MaximumLodsPerVisual ||
        Convert.ToUInt64(link.Lods.Count) > limits.MaximumLodsPerVisual)
      throw Invalid(
        $"SVD '{visual.Name}' LOD count exceeds the limit {limits.MaximumLodsPerVisual}");

    TemplateSelection? selected = null;
    var linkedIndex = 0;
    foreach (var serializedLod in visual.Lods) {
      if (serializedLod == null) throw Invalid($"SVD '{visual.Name}' has a null serialized LOD");
      ValidateIdentifier(serializedLod.Name, $"SVD '{visual.Name}' LOD name", limits);
      switch (serializedLod.Type) {
        case SvdLodType.StaticShape:
        case SvdLodType.BoneShape:
          if (linkedIndex >= link.Lods.Count)
            throw Invalid($"SVD '{visual.Name}' is missing a supported linked LOD");
          var linkedLod = link.Lods[linkedIndex++];
          if (linkedLod == null)
            throw Invalid($"SVD '{visual.Name}' linked LOD list contains null");
          if (!ReferenceEquals(linkedLod.Lod, serializedLod))
            throw Invalid(
              $"SVD '{visual.Name}' linked LOD order does not preserve serialized identity");
          ValidateLodTarget(linkedLod, allowedPaths, limits);
          if (!linkedLod.IsResolved) {
            Reserve(
              ref unresolvedLodCount,
              1,
              limits.MaximumUnresolvedShapeReferences,
              "unresolved shape references");
          } else {
            selected ??= CreateSelection(link, linkedLod);
          }
          break;
        case SvdLodType.Billboard:
          if (serializedLod.StaticShapeRef != null || serializedLod.BoneShapeRef != null)
            throw Invalid(
              $"SVD '{visual.Name}' billboard LOD '{serializedLod.Name}' has a shape reference");
          break;
        default:
          throw Invalid(
            $"SVD '{visual.Name}' LOD '{serializedLod.Name}' has unsupported type " +
            $"{serializedLod.Type}");
      }
    }
    if (linkedIndex != link.Lods.Count)
      throw Invalid($"SVD '{visual.Name}' exposes linked LODs outside its serialized array");
    return selected;
  }

  private static void ValidateLodTarget(
    RideVisualShapeLodLink linkedLod,
    IReadOnlySet<string> allowedPaths,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    var lod = linkedLod.Lod;
    if (linkedLod.StaticShapeSource != null && linkedLod.BoneShapeSource != null)
      throw Invalid($"SVD LOD '{lod.Name}' resolves both SHS and BSH targets");

    switch (lod.Type) {
      case SvdLodType.StaticShape:
        if (lod.BoneShapeRef != null || linkedLod.BoneShapeSource != null)
          throw Invalid($"SVD static LOD '{lod.Name}' contains a BSH target");
        var staticName = ParseTaggedReference(
          lod.StaticShapeRef,
          "shs",
          $"SVD static LOD '{lod.Name}'",
          limits);
        if (linkedLod.StaticShapeSource == null) return;
        ValidateShapeSource(
          linkedLod.StaticShapeSource.File,
          linkedLod.StaticShapeSource.Resource?.Name,
          FileType.StaticShape,
          "SHS",
          staticName,
          allowedPaths,
          limits);
        break;
      case SvdLodType.BoneShape:
        if (lod.StaticShapeRef != null || linkedLod.StaticShapeSource != null)
          throw Invalid($"SVD bone LOD '{lod.Name}' contains an SHS target");
        var boneName = ParseTaggedReference(
          lod.BoneShapeRef,
          "bsh",
          $"SVD bone LOD '{lod.Name}'",
          limits);
        if (linkedLod.BoneShapeSource == null) return;
        ValidateShapeSource(
          linkedLod.BoneShapeSource.File,
          linkedLod.BoneShapeSource.Resource?.Name,
          FileType.BoneShape,
          "BSH",
          boneName,
          allowedPaths,
          limits);
        break;
      default:
        throw Invalid(
          $"linked SVD LOD '{lod.Name}' has unsupported type {lod.Type} for shape adaptation");
    }
  }

  private static TemplateSelection CreateSelection(
    RideCarVisualShapeLink link,
    RideVisualShapeLodLink lod
  ) => lod.StaticShapeSource != null
    ? new TemplateSelection(
      link,
      lod,
      RideCarVisualTemplateShapeKind.StaticShape,
      lod.StaticShapeSource.Resource,
      null)
    : new TemplateSelection(
      link,
      lod,
      RideCarVisualTemplateShapeKind.BoneShape,
      null,
      lod.BoneShapeSource!.Resource);

  private static GeometryCounts PreflightGeometry(
    IReadOnlyList<TemplateSelection> selections,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    var shapes = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var batchCount = 0ul;
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var selection in selections) {
      if (!shapes.Add(selection.Shape)) continue;
      if (Convert.ToUInt64(shapes.Count) > limits.MaximumDistinctShapeResources)
        throw Invalid(
          $"distinct shape count exceeds the limit {limits.MaximumDistinctShapeResources}");
      switch (selection.Kind) {
        case RideCarVisualTemplateShapeKind.StaticShape:
          ReserveShape(
            selection.StaticShape!,
            ref batchCount,
            ref vertexCount,
            ref indexCount,
            limits);
          break;
        case RideCarVisualTemplateShapeKind.BoneShape:
          ReserveShape(
            selection.BoneShape!,
            ref batchCount,
            ref vertexCount,
            ref indexCount,
            limits);
          break;
        default:
          throw Invalid($"shape selection has unsupported kind {selection.Kind}");
      }
    }
    if (batchCount > int.MaxValue) throw Invalid("batch count exceeds the runtime range");
    return new GeometryCounts(Convert.ToInt32(batchCount), vertexCount, indexCount);
  }

  private static void ReserveShape(
    StaticShape shape,
    ref ulong batchCount,
    ref ulong vertexCount,
    ref ulong indexCount,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    if (shape == null || shape.Meshes == null || shape.Meshes.Count == 0)
      throw Invalid("selected SHS has no meshes");
    ValidateIdentifier(shape.Name, "selected SHS name", limits);
    Reserve(ref batchCount, Convert.ToUInt64(shape.Meshes.Count), limits.MaximumBatches, "batches");
    foreach (var mesh in shape.Meshes) {
      if (mesh == null || mesh.Vertices == null || mesh.Indices == null)
        throw Invalid($"selected SHS '{shape.Name}' has a null mesh payload");
      Reserve(
        ref vertexCount,
        Convert.ToUInt64(mesh.Vertices.Count),
        limits.MaximumVertices,
        "vertices");
      Reserve(
        ref indexCount,
        Convert.ToUInt64(mesh.Indices.Count),
        limits.MaximumIndices,
        "indices");
    }
  }

  private static void ReserveShape(
    BoneShape shape,
    ref ulong batchCount,
    ref ulong vertexCount,
    ref ulong indexCount,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    if (shape == null || shape.Meshes == null || shape.Meshes.Count == 0)
      throw Invalid("selected BSH has no meshes");
    ValidateIdentifier(shape.Name, "selected BSH name", limits);
    Reserve(ref batchCount, Convert.ToUInt64(shape.Meshes.Count), limits.MaximumBatches, "batches");
    foreach (var mesh in shape.Meshes) {
      if (mesh == null || mesh.Vertices == null || mesh.Indices == null)
        throw Invalid($"selected BSH '{shape.Name}' has a null mesh payload");
      Reserve(
        ref vertexCount,
        Convert.ToUInt64(mesh.Vertices.Count),
        limits.MaximumVertices,
        "vertices");
      Reserve(
        ref indexCount,
        Convert.ToUInt64(mesh.Indices.Count),
        limits.MaximumIndices,
        "indices");
    }
  }

  private static IReadOnlyList<StaticShapeMeshBatch> Adapt(
    TemplateSelection selection,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> adaptStaticShape,
    Func<BoneShape, IReadOnlyList<StaticShapeMeshBatch>> adaptBoneShape,
    ICollection<Mesh> ownedMeshes,
    ISet<Mesh> ownedMeshSet
  ) {
    var batches = selection.Kind switch {
      RideCarVisualTemplateShapeKind.StaticShape => adaptStaticShape(selection.StaticShape!),
      RideCarVisualTemplateShapeKind.BoneShape => adaptBoneShape(selection.BoneShape!),
      _ => throw Invalid($"shape selection has unsupported kind {selection.Kind}"),
    };
    if (batches == null) throw Invalid("shape adapter returned a null batch list");

    // The adapter has transferred every returned mesh to this transaction. Adopt the entire list
    // before validating counts or early batches so a later returned mesh cannot leak on failure.
    var retained = new StaticShapeMeshBatch[batches.Count];
    var hasDuplicateMesh = false;
    for (var index = 0; index < batches.Count; index++) {
      var batch = batches[index];
      retained[index] = batch;
      if (batch?.Mesh == null) continue;
      if (!ownedMeshSet.Add(batch.Mesh)) {
        hasDuplicateMesh = true;
        continue;
      }
      ownedMeshes.Add(batch.Mesh);
    }

    var expectedCount = selection.Kind switch {
      RideCarVisualTemplateShapeKind.StaticShape => selection.StaticShape!.Meshes.Count,
      RideCarVisualTemplateShapeKind.BoneShape => selection.BoneShape!.Meshes.Count,
      _ => 0,
    };
    if (batches.Count != expectedCount)
      throw Invalid(
        $"shape adapter returned {batches.Count} batches, expected {expectedCount}");
    if (hasDuplicateMesh)
      throw Invalid("shape adapter reused a mesh across distinct owned batches");

    for (var index = 0; index < retained.Length; index++) {
      var batch = retained[index];
      switch (selection.Kind) {
        case RideCarVisualTemplateShapeKind.StaticShape:
          ValidateBatch(selection.StaticShape!, index, batch);
          break;
        case RideCarVisualTemplateShapeKind.BoneShape:
          ValidateBatch(selection.BoneShape!, index, batch);
          break;
      }
    }
    return Array.AsReadOnly(retained);
  }

  private static void ValidateBatch(StaticShape shape, int index, StaticShapeMeshBatch? batch) {
    if (batch == null || batch.Mesh == null)
      throw Invalid($"SHS '{shape.Name}' adapter returned a null batch or mesh");
    var source = shape.Meshes[index];
    ValidateBatchMetadata(shape.Name, index, source.Name, source.SupportType, source.FtxRef,
      source.TxsRef, source.Transparency, source.TextureFlags, source.Sides, source.Vertices.Count,
      source.Indices.Count, batch);
  }

  private static void ValidateBatch(BoneShape shape, int index, StaticShapeMeshBatch? batch) {
    if (batch == null || batch.Mesh == null)
      throw Invalid($"BSH '{shape.Name}' adapter returned a null batch or mesh");
    var source = shape.Meshes[index];
    ValidateBatchMetadata(shape.Name, index, source.Name, source.SupportType, source.FtxRef,
      source.TxsRef, source.Transparency, source.TextureFlags, source.Sides, source.Vertices.Count,
      source.Indices.Count, batch);
  }

  private static void ValidateBatchMetadata(
    string shapeName,
    int expectedIndex,
    string expectedName,
    int expectedSupportType,
    string? expectedFtxRef,
    string? expectedTxsRef,
    uint expectedTransparency,
    uint expectedTextureFlags,
    uint expectedSides,
    int expectedVertexCount,
    int expectedIndexCount,
    StaticShapeMeshBatch batch
  ) {
    if (batch.SourceMeshIndex != expectedIndex ||
        !string.Equals(batch.SourceMeshName, expectedName, StringComparison.Ordinal) ||
        batch.SupportType != expectedSupportType ||
        !string.Equals(batch.FtxRef, expectedFtxRef, StringComparison.Ordinal) ||
        !string.Equals(batch.TxsRef, expectedTxsRef, StringComparison.Ordinal) ||
        batch.Transparency != expectedTransparency ||
        batch.TextureFlags != expectedTextureFlags ||
        batch.Sides != expectedSides)
      throw Invalid(
        $"shape '{shapeName}' adapter changed batch {expectedIndex} order or material metadata");
    if (!string.Equals(batch.Mesh.Name, expectedName, StringComparison.Ordinal))
      throw Invalid($"shape '{shapeName}' adapter changed mesh {expectedIndex} identity");
    if (batch.Mesh.Vertices == null || batch.Mesh.Vertices.Count != expectedVertexCount)
      throw Invalid($"shape '{shapeName}' adapter changed mesh {expectedIndex} vertex count");
    if (batch.Mesh.Indices == null || batch.Mesh.Indices.Count != expectedIndexCount ||
        batch.Mesh.Indices.Count == 0 || batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid($"shape '{shapeName}' adapter returned an invalid triangle list");

    var vertexIndex = 0;
    foreach (var vertex in batch.Mesh.Vertices) {
      if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) ||
          !IsFinite(vertex.TexCoord) || !IsFinite(vertex.Color))
        throw Invalid(
          $"shape '{shapeName}' batch {expectedIndex} vertex {vertexIndex} is non-finite");
      vertexIndex++;
    }
    var indexOffset = 0;
    foreach (var vertexIndexValue in batch.Mesh.Indices) {
      if (vertexIndexValue >= Convert.ToUInt32(batch.Mesh.Vertices.Count))
        throw Invalid(
          $"shape '{shapeName}' batch {expectedIndex} index {indexOffset} is out of range");
      indexOffset++;
    }
  }

  private static IReadOnlySet<string> ValidateClosure(
    RideCarResourceSource source,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    if (source.AllowedArchivePaths == null)
      throw Invalid($"RIC '{source.Resource.Name}' has a null archive closure");
    if (Convert.ToUInt64(source.AllowedArchivePaths.Count) > limits.MaximumArchivePathsPerCar)
      throw Invalid(
        $"RIC '{source.Resource.Name}' archive closure exceeds the limit " +
        $"{limits.MaximumArchivePathsPerCar}");
    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in source.AllowedArchivePaths) {
      ValidateIdentifier(path, $"RIC '{source.Resource.Name}' archive path", limits);
      if (!paths.Add(path))
        throw Invalid($"RIC '{source.Resource.Name}' repeats archive path '{path}'");
    }
    if (!paths.Contains(source.File.Path))
      throw Invalid($"RIC '{source.Resource.Name}' closure omits its source archive");
    return paths;
  }

  private static void ValidateShapeSource(
    OvlFile? file,
    string? resourceName,
    FileType expectedType,
    string tag,
    string referencedName,
    IReadOnlySet<string> allowedPaths,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    ValidateSource(file, resourceName, expectedType, tag, limits);
    if (!string.Equals(referencedName, resourceName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{tag} resource '{resourceName}' does not match referenced name '{referencedName}'");
    if (!allowedPaths.Contains(file!.Path))
      throw Invalid($"{tag} resource '{resourceName}' is outside its exact RIC archive closure");
  }

  private static void ValidateSource(
    OvlFile? file,
    string? resourceName,
    FileType expectedType,
    string tag,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    if (file == null || resourceName == null)
      throw Invalid($"{tag} source has a null OVL file or decoded resource");
    ValidateIdentifier(file.Name, $"{tag} OVL name", limits);
    ValidateIdentifier(file.Path, $"{tag} OVL path", limits);
    ValidateIdentifier(resourceName, $"decoded {tag} name", limits);
    if (file.Type != expectedType)
      throw Invalid($"{tag} source has OVL type {file.Type}, expected {expectedType}");
    if (!string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{tag} OVL name '{file.Name}' does not match decoded name '{resourceName}'");
  }

  private static string ParseTaggedReference(
    string? reference,
    string expectedTag,
    string description,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    ValidateIdentifier(reference, description, limits);
    var separator = reference!.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{reference}' is not an exact name:{expectedTag} key");
    var name = reference[..separator];
    ValidateIdentifier(name, $"{description} resource name", limits);
    return name;
  }

  private static void ValidateIdentifier(
    string? value,
    string description,
    RideCarVisualTemplateRegistryLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > limits.MaximumStringCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{limits.MaximumStringCharacters} characters");
  }

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values) {
      if (ReferenceEquals(value, target)) return true;
    }
    return false;
  }

  private static void Reserve(
    ref ulong total,
    ulong addition,
    ulong maximum,
    string description
  ) {
    if (addition > maximum || total > maximum - addition)
      throw Invalid($"aggregate {description} exceed the limit {maximum}");
    total += addition;
  }

  private static List<Exception> DisposeMeshes(
    IReadOnlyList<Mesh> meshes,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    for (var index = meshes.Count - 1; index >= 0; index--) {
      try {
        disposeMesh(meshes[index]);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void ValidateLimits(RideCarVisualTemplateRegistryLimits limits) {
    if (limits.MaximumVisualOccurrences == 0 ||
        limits.MaximumTemplates == 0 ||
        limits.MaximumLodsPerVisual == 0 ||
        limits.MaximumArchivePathsPerCar == 0 ||
        limits.MaximumUnresolvedShapeReferences == 0 ||
        limits.MaximumDistinctShapeResources == 0 ||
        limits.MaximumBatches == 0 ||
        limits.MaximumVertices == 0 ||
        limits.MaximumIndices == 0 ||
        limits.MaximumStringCharacters <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits), "Template limits must be positive.");
  }

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-car visual template registry: {message}.");

  private sealed record TemplateSelection(
    RideCarVisualShapeLink Link,
    RideVisualShapeLodLink Lod,
    RideCarVisualTemplateShapeKind Kind,
    StaticShape? StaticShape,
    BoneShape? BoneShape
  ) {
    public object Shape => (object?)StaticShape ?? BoneShape!;
  }

  private readonly record struct GeometryCounts(
    int BatchCount,
    ulong VertexCount,
    ulong IndexCount);

  private readonly record struct CarOccurrence(
    TrackedRideResourceLink Ride,
    RideTrainLink Train,
    RideCarLink Car);

  private sealed class CarOccurrenceComparer : IEqualityComparer<CarOccurrence> {
    public static CarOccurrenceComparer Instance { get; } = new();

    public bool Equals(CarOccurrence x, CarOccurrence y) =>
      ReferenceEquals(x.Ride, y.Ride) &&
      ReferenceEquals(x.Train, y.Train) &&
      ReferenceEquals(x.Car, y.Car);

    public int GetHashCode(CarOccurrence value) => HashCode.Combine(
      RuntimeHelpers.GetHashCode(value.Ride),
      RuntimeHelpers.GetHashCode(value.Train),
      RuntimeHelpers.GetHashCode(value.Car));
  }
}

/// <summary>Allocation and traversal ceilings for ride-car visual template construction.</summary>
internal readonly record struct RideCarVisualTemplateRegistryLimits(
  ulong MaximumVisualOccurrences,
  ulong MaximumTemplates,
  ulong MaximumLodsPerVisual,
  ulong MaximumArchivePathsPerCar,
  ulong MaximumUnresolvedShapeReferences,
  ulong MaximumDistinctShapeResources,
  ulong MaximumBatches,
  ulong MaximumVertices,
  ulong MaximumIndices,
  int MaximumStringCharacters
) {
  public static RideCarVisualTemplateRegistryLimits Default { get; } = new(
    MaximumVisualOccurrences: 256 * 1024,
    MaximumTemplates: 256 * 1024,
    MaximumLodsPerVisual: 4 * 1024,
    MaximumArchivePathsPerCar: 4 * 1024,
    MaximumUnresolvedShapeReferences: 4_000_000,
    MaximumDistinctShapeResources: 256 * 1024,
    MaximumBatches: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000,
    MaximumStringCharacters: 4 * 1024);
}
