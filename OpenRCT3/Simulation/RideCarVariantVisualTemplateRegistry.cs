// Ride Car Variant Visual Template Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>The typed outcome of adapting one saved car's selected body visual.</summary>
internal enum RideCarVariantVisualTemplateStatus {
  Resolved,
  UpstreamSelectionFailed,
  ChangedSelectionIdentity,
  UnavailableRequiredLod,
}

/// <summary>Typed evidence explaining why one selected car has no body template.</summary>
internal sealed record RideCarVariantVisualTemplateIssue(
  RideCarVariantVisualTemplateStatus Status,
  RideCarVisualVariantSelectionStatus SelectionStatus,
  string Detail
);

/// <summary>One saved-car selection paired with its exact reusable body mesh template.</summary>
/// <remarks>
/// The selection, graph, LOD, and decoded shape records are borrowed. The containing registry owns
/// the meshes exposed by <see cref="Template"/>.
/// </remarks>
internal sealed record RideCarVariantVisualTemplateEntry(
  int RegistryIndex,
  RideCarVisualVariantSelectionEntry Selection,
  RideCarVisualMeshTemplate? Template,
  RideCarVariantVisualTemplateIssue? Issue
) {
  public ulong CarInstanceEntryId => Selection.CarInstanceEntryId;
  public RideCarVariantVisualTemplateStatus Status =>
    Issue?.Status ?? RideCarVariantVisualTemplateStatus.Resolved;
  public bool IsResolved => Issue == null;
}

/// <summary>
/// Owns exact normal or Wild-flipped body templates in saved-car registry order.
/// </summary>
/// <remarks>
/// <para>
/// Each resolved selector entry contributes exactly one body template. RCT3 serializes SVD LODs in
/// order, so this registry selects the first serialized supported LOD and never sorts by distance.
/// A failed selector entry remains an explicit typed entry and never acquires render resources.
/// </para>
/// <para>
/// Decoded resources and graph identities are borrowed. The registry owns only meshes returned by
/// the SHS/BSH adapters. Repeated decoded shape object identities share one immutable batch array;
/// unique meshes are disposed once in reverse construction order.
/// </para>
/// </remarks>
internal sealed class RideCarVariantVisualTemplateRegistry : IDisposable {
  private readonly Mesh[] ownedMeshes;
  private readonly Action<Mesh> disposeMesh;
  private bool disposed;

  private RideCarVariantVisualTemplateRegistry(
    IReadOnlyList<RideCarVariantVisualTemplateEntry> entries,
    Mesh[] ownedMeshes,
    Action<Mesh> disposeMesh,
    int distinctShapeResourceCount,
    int batchCount,
    ulong vertexCount,
    ulong indexCount
  ) {
    Entries = entries;
    this.ownedMeshes = ownedMeshes;
    this.disposeMesh = disposeMesh;
    ResolvedCount = entries.Count(entry => entry.IsResolved);
    UpstreamSelectionFailureCount = entries.Count(entry =>
      entry.Status == RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed);
    DistinctShapeResourceCount = distinctShapeResourceCount;
    BatchCount = batchCount;
    VertexCount = vertexCount;
    IndexCount = indexCount;
  }

  public IReadOnlyList<RideCarVariantVisualTemplateEntry> Entries { get; }
  public int ResolvedCount { get; }
  public int FailedCount => Entries.Count - ResolvedCount;
  public int UpstreamSelectionFailureCount { get; }
  public int DistinctShapeResourceCount { get; }
  public int BatchCount { get; }
  public ulong VertexCount { get; }
  public ulong IndexCount { get; }
  public bool IsDisposed => disposed;

  public static RideCarVariantVisualTemplateRegistry Build(
    RideCarVisualVariantSelectionRegistry selections
  ) => Build(
    selections,
    StaticShapeMeshBuilder.BuildBatches,
    BoneShapeMeshBuilder.BuildBatches,
    mesh => mesh.Dispose(),
    RideCarVariantVisualTemplateRegistryLimits.Default);

  internal static RideCarVariantVisualTemplateRegistry Build(
    RideCarVisualVariantSelectionRegistry selections,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> adaptStaticShape,
    Func<BoneShape, IReadOnlyList<StaticShapeMeshBatch>> adaptBoneShape,
    Action<Mesh> disposeMesh,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(selections);
    ArgumentNullException.ThrowIfNull(adaptStaticShape);
    ArgumentNullException.ThrowIfNull(adaptBoneShape);
    ArgumentNullException.ThrowIfNull(disposeMesh);
    ValidateLimits(limits);

    var pending = ValidateAndSelect(selections, limits);
    var geometry = PreflightGeometry(pending, limits);
    var batchesByShape = new Dictionary<object, IReadOnlyList<StaticShapeMeshBatch>>(
      ReferenceEqualityComparer.Instance);
    var entries = new List<RideCarVariantVisualTemplateEntry>(pending.Count);
    var ownedMeshes = new List<Mesh>(geometry.BatchCount);
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);

    try {
      foreach (var item in pending) {
        if (item.Selection == null) {
          entries.Add(new(
            item.Entry.RegistryIndex,
            item.Entry,
            Template: null,
            new(
              item.Status,
              item.Entry.Status,
              item.Detail!)));
          continue;
        }

        if (!batchesByShape.TryGetValue(item.Selection.Shape, out var batches)) {
          batches = Adapt(
            item.Selection,
            adaptStaticShape,
            adaptBoneShape,
            ownedMeshes,
            ownedMeshSet);
          batchesByShape.Add(item.Selection.Shape, batches);
        }
        var template = new RideCarVisualMeshTemplate(
          item.Selection.Link,
          item.Selection.Lod,
          item.Selection.Kind,
          item.Selection.StaticShape,
          item.Selection.BoneShape,
          batches);
        entries.Add(new(
          item.Entry.RegistryIndex,
          item.Entry,
          template,
          Issue: null));
      }

      return new RideCarVariantVisualTemplateRegistry(
        Array.AsReadOnly(entries.ToArray()),
        ownedMeshes.ToArray(),
        disposeMesh,
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

  /// <summary>Releases every unique adapted mesh once in reverse construction order.</summary>
  public void Dispose() {
    if (disposed) return;
    disposed = true;

    var errors = DisposeMeshes(ownedMeshes, disposeMesh);
    if (errors.Count > 0) throw new AggregateException(errors);
  }

  private static List<PendingEntry> ValidateAndSelect(
    RideCarVisualVariantSelectionRegistry selections,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) {
    if (selections.Entries == null)
      throw Invalid("selection entry list is null");
    if (Convert.ToUInt64(selections.Entries.Count) > limits.MaximumEntries)
      throw Invalid($"selection entry count exceeds the limit {limits.MaximumEntries}");
    if (Convert.ToUInt64(selections.SelectedCount) > limits.MaximumTemplates)
      throw Invalid($"resolved template count exceeds the limit {limits.MaximumTemplates}");

    var result = new List<PendingEntry>(selections.Entries.Count);
    var closures = new Dictionary<RideCarResourceSource, IReadOnlySet<string>>(
      ReferenceEqualityComparer.Instance);
    var aggregateLodCount = 0ul;
    foreach (var index in Enumerable.Range(0, selections.Entries.Count)) {
      var entry = selections.Entries[index];
      if (entry == null || entry.CarRuntime == null)
        throw Invalid($"selection entry {index} is null or incomplete");
      if (entry.RegistryIndex != index || entry.CarRuntime.RegistryIndex != index)
        throw Invalid($"selection entry {index} changed exact saved-car registry order");

      if (!entry.IsSelected) {
        result.Add(ValidateUpstreamFailure(entry));
        continue;
      }
      result.Add(ValidateSelected(
        selections,
        entry,
        closures,
        limits,
        ref aggregateLodCount));
    }
    return result;
  }

  private static PendingEntry ValidateUpstreamFailure(
    RideCarVisualVariantSelectionEntry entry
  ) {
    if (entry.Issue == null ||
        entry.Issue.Status == RideCarVisualVariantSelectionStatus.Selected ||
        !Enum.IsDefined(entry.Issue.Status) ||
        entry.Body != null || entry.Moving != null || entry.BodyControlFallback != null)
      return Changed(entry, "failed selection carries invalid status or resolved visual payload");
    return new(
      entry,
      RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed,
      $"visual selection failed with {entry.Issue.Status}",
      Selection: null);
  }

  private static PendingEntry ValidateSelected(
    RideCarVisualVariantSelectionRegistry selections,
    RideCarVisualVariantSelectionEntry entry,
    IDictionary<RideCarResourceSource, IReadOnlySet<string>> closures,
    RideCarVariantVisualTemplateRegistryLimits limits,
    ref ulong aggregateLodCount
  ) {
    if (entry.Issue != null || entry.SelectedVariant == null ||
        entry.RequiredBodyRole == null || entry.RequiredMovingRole == null ||
        entry.Car == null || entry.Body == null)
      return Changed(entry, "selected entry has incomplete variant, car, or body evidence");
    if (!selections.IsAuthorizedOccurrence(entry.Body) ||
        entry.Moving != null && !selections.IsAuthorizedOccurrence(entry.Moving))
      return Changed(
        entry,
        "selected body or moving visual is not an authorized bridge occurrence");

    var expectedBodyRole = entry.SelectedVariant == RideCarVisualVariant.Normal
      ? RideVisualRole.Body
      : RideVisualRole.WildFlippedBody;
    var expectedMovingRole = entry.SelectedVariant == RideCarVisualVariant.Normal
      ? RideVisualRole.Moving
      : RideVisualRole.WildFlippedMoving;
    if (!Enum.IsDefined(entry.SelectedVariant.Value) ||
        entry.RequiredBodyRole != expectedBodyRole ||
        entry.RequiredMovingRole != expectedMovingRole)
      return Changed(entry, "selected variant does not match its required visual roles");

    var runtime = entry.CarRuntime;
    if (runtime.TrainRuntime == null ||
        runtime.TrainRuntime.SavedVisualVariant != entry.SavedVisualVariant ||
        !SavedVariantMatches(entry.SelectedVariant.Value, entry.SavedVisualVariant) ||
        runtime.ResourceStatus != RideCarResourceRuntimeStatus.Resolved ||
        !ReferenceEquals(runtime.CarResource, entry.Car) ||
        runtime.ConsistCar == null ||
        !ReferenceEquals(runtime.ConsistCar.CarResource, entry.Car) ||
        runtime.TrainConsist?.IsResolved != true ||
        runtime.TrainConsist.RideGraph == null ||
        runtime.TrainConsist.TrainGraph == null)
      return Changed(entry, "selected entry changed exact saved-car or consist identity");

    var car = entry.Car;
    if (car.Source?.Resource == null || car.Visuals == null)
      return Changed(entry, "selected entry has an invalid RIC graph");
    if (!TryValidateGraph(
          runtime.TrainConsist.RideGraph,
          runtime.TrainConsist.TrainGraph,
          car,
          limits,
          out var graphDetail))
      return Changed(entry, graphDetail ?? "selected entry has an invalid RIC graph");

    if (!closures.TryGetValue(car.Source, out var closure)) {
      if (!TryValidateClosure(car.Source, limits, out closure, out var closureDetail))
        return Changed(entry, closureDetail!);
      closures.Add(car.Source, closure);
    }

    var decodedCar = car.Source.Resource;
    string? serializedBody;
    string? serializedMoving;
    if (entry.SelectedVariant == RideCarVisualVariant.Normal) {
      serializedBody = decodedCar.Visual;
      serializedMoving = decodedCar.MovingVisual;
    } else {
      if (decodedCar.Version != RideCarVersion.Wild || decodedCar.Wild == null)
        return Changed(entry, "Wild-flipped selection is not backed by a Wild RIC extension");
      serializedBody = decodedCar.Wild.FlippedVisual;
      serializedMoving = decodedCar.Wild.FlippedMovingVisual;
    }
    if (serializedBody == null)
      return Changed(entry, "selected variant has no serialized body reference");
    if (!TryValidateVisualOccurrence(
          entry.Body,
          car,
          runtime.TrainConsist.RideGraph,
          runtime.TrainConsist.TrainGraph,
          expectedBodyRole,
          serializedBody,
          closure,
          limits,
          ref aggregateLodCount,
          out var bodySelection,
          out var bodyStatus,
          out var bodyDetail))
      return Failure(entry, bodyStatus, bodyDetail ?? "required body visual is unavailable");

    if (entry.Moving == null) {
      if (serializedMoving != null || entry.BodyControlFallback == null ||
          !ReferenceEquals(entry.BodyControlFallback.Car, car) ||
          !ReferenceEquals(entry.BodyControlFallback.Body, entry.Body) ||
          entry.BodyControlFallback.MissingMovingRole != expectedMovingRole ||
          car.Visuals.Any(visual => visual != null && visual.Role == expectedMovingRole))
        return Changed(entry, "body-control fallback changed exact missing-moving evidence");
    } else {
      if (serializedMoving == null || entry.BodyControlFallback != null)
        return Changed(entry, "selected moving visual changed serialized or fallback identity");
      if (!TryValidateVisualOccurrence(
            entry.Moving,
            car,
            runtime.TrainConsist.RideGraph,
            runtime.TrainConsist.TrainGraph,
            expectedMovingRole,
            serializedMoving,
            closure,
            limits,
            ref aggregateLodCount,
            out _,
            out var movingStatus,
            out var movingDetail))
        return Failure(entry, movingStatus, movingDetail ?? "required moving visual is unavailable");
    }

    return new(
      entry,
      RideCarVariantVisualTemplateStatus.Resolved,
      Detail: null,
      bodySelection);
  }

  private static bool TryValidateGraph(
    TrackedRideResourceLink ride,
    RideTrainLink train,
    RideCarLink car,
    RideCarVariantVisualTemplateRegistryLimits limits,
    out string? detail
  ) {
    detail = null;
    if (ride.Source?.Resource == null || ride.Trains == null ||
        !ValidSource(
          ride.Source.File,
          ride.Source.Resource.Name,
          FileType.TrackedRide,
          limits) ||
        !ContainsReference(ride.Trains, train)) {
      detail = "selected RIT is not owned by its exact TRR graph";
      return false;
    }
    if (train.Source?.Resource == null || train.Cars == null ||
        !ValidSource(
          train.Source.File,
          train.Source.Resource.Name,
          FileType.RideTrain,
          limits) ||
        !string.Equals(
          train.Reference,
          train.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase) ||
        !ContainsReference(train.Cars, car)) {
      detail = "selected RIC is not owned by its exact RIT graph";
      return false;
    }
    if (car.Source?.Resource == null ||
        !ValidSource(
          car.Source.File,
          car.Source.Resource.Name,
          FileType.RideCar,
          limits) ||
        !TryParseTaggedReference(car.Reference, "ric", limits, out var carName) ||
        !string.Equals(
          carName,
          car.Source.Resource.Name,
          StringComparison.OrdinalIgnoreCase)) {
      detail = "selected RIC source or tagged reference changed identity";
      return false;
    }
    return true;
  }

  private static bool TryValidateClosure(
    RideCarResourceSource source,
    RideCarVariantVisualTemplateRegistryLimits limits,
    out IReadOnlySet<string> closure,
    out string? detail
  ) {
    closure = null!;
    detail = null;
    if (source.AllowedArchivePaths == null) {
      detail = "selected RIC has a null archive closure";
      return false;
    }
    if (Convert.ToUInt64(source.AllowedArchivePaths.Count) >
        limits.MaximumArchivePathsPerCar)
      throw Invalid(
        $"RIC archive path count exceeds the limit {limits.MaximumArchivePathsPerCar}");

    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in source.AllowedArchivePaths) {
      if (!ValidIdentifier(path, limits) || !paths.Add(path)) {
        detail = "selected RIC archive closure contains an invalid or duplicate path";
        return false;
      }
    }
    if (!paths.Contains(source.File.Path)) {
      detail = "selected RIC archive closure omits its own source";
      return false;
    }
    closure = paths;
    return true;
  }

  private static bool TryValidateVisualOccurrence(
    RideCarVisualShapeLink link,
    RideCarLink car,
    TrackedRideResourceLink ride,
    RideTrainLink train,
    RideVisualRole expectedRole,
    string serializedReference,
    IReadOnlySet<string> closure,
    RideCarVariantVisualTemplateRegistryLimits limits,
    ref ulong aggregateLodCount,
    out TemplateSelection selection,
    out RideCarVariantVisualTemplateStatus failureStatus,
    out string? detail
  ) {
    selection = null!;
    failureStatus = RideCarVariantVisualTemplateStatus.ChangedSelectionIdentity;
    detail = null;
    if (link == null || link.Visual == null || link.Lods == null ||
        !ReferenceEquals(link.Ride, ride) ||
        !ReferenceEquals(link.Train, train) ||
        !ReferenceEquals(link.Car, car) ||
        link.Visual.Role != expectedRole ||
        CountReferences(car.Visuals, link.Visual) != 1 ||
        car.Visuals.Count(visual => visual != null && visual.Role == expectedRole) != 1 ||
        !string.Equals(
          link.Visual.Reference,
          serializedReference,
          StringComparison.OrdinalIgnoreCase)) {
      detail = $"selected {expectedRole} occurrence changed graph or serialized identity";
      return false;
    }

    var visualSource = link.Visual.Source;
    var visual = visualSource?.Resource;
    if (visualSource == null || visual == null ||
        !ValidSource(
          visualSource.File,
          visual.Name,
          FileType.SceneryItemVisual,
          limits) ||
        !TryParseTaggedReference(link.Visual.Reference, "svd", limits, out var visualName) ||
        !string.Equals(visualName, visual.Name, StringComparison.OrdinalIgnoreCase) ||
        !closure.Contains(visualSource.File.Path)) {
      detail = $"selected {expectedRole} SVD source changed exact identity";
      return false;
    }
    if (visual.Lods == null) {
      detail = $"selected {expectedRole} SVD has a null serialized LOD list";
      return false;
    }
    if (Convert.ToUInt64(visual.Lods.Count) > limits.MaximumLodsPerVisual ||
        Convert.ToUInt64(link.Lods.Count) > limits.MaximumLodsPerVisual)
      throw Invalid(
        $"selected {expectedRole} LOD count exceeds the limit " +
        $"{limits.MaximumLodsPerVisual}");
    Reserve(
      ref aggregateLodCount,
      Convert.ToUInt64(visual.Lods.Count),
      limits.MaximumAggregateLods,
      "serialized visual LODs");
    Reserve(
      ref aggregateLodCount,
      Convert.ToUInt64(link.Lods.Count),
      limits.MaximumAggregateLods,
      "linked visual LODs");

    TemplateSelection? first = null;
    var linkedIndex = 0;
    var supportedCount = 0;
    var unavailable = false;
    foreach (var serializedLod in visual.Lods) {
      if (serializedLod == null) {
        detail = $"selected {expectedRole} SVD contains a null serialized LOD";
        return false;
      }
      switch (serializedLod.Type) {
        case SvdLodType.StaticShape:
        case SvdLodType.BoneShape:
          supportedCount = checked(supportedCount + 1);
          if (linkedIndex >= link.Lods.Count) {
            detail = $"selected {expectedRole} SVD is missing a supported linked LOD";
            return false;
          }
          var linked = link.Lods[linkedIndex++];
          if (linked == null || !ReferenceEquals(linked.Lod, serializedLod)) {
            detail = $"selected {expectedRole} LOD order changed serialized object identity";
            return false;
          }
          var target = ValidateLodTarget(linked, closure, limits, out var targetSelection);
          if (target == LodTargetStatus.Changed) {
            detail = $"selected {expectedRole} LOD changed exact shape-source identity";
            return false;
          }
          if (target == LodTargetStatus.Unavailable) {
            unavailable = true;
          } else {
            first ??= targetSelection with { Link = link };
          }
          break;
        case SvdLodType.Billboard:
          if (serializedLod.StaticShapeRef != null || serializedLod.BoneShapeRef != null) {
            detail = $"selected {expectedRole} billboard LOD carries a shape target";
            return false;
          }
          break;
        default:
          detail = $"selected {expectedRole} SVD contains unsupported LOD type " +
            $"{serializedLod.Type}";
          return false;
      }
    }
    if (linkedIndex != link.Lods.Count) {
      detail = $"selected {expectedRole} exposes linked LODs outside serialized order";
      return false;
    }
    if (supportedCount == 0 || unavailable || first == null) {
      failureStatus = RideCarVariantVisualTemplateStatus.UnavailableRequiredLod;
      detail = $"selected {expectedRole} has no complete resolved supported LOD set";
      return false;
    }
    selection = first;
    return true;
  }

  private static LodTargetStatus ValidateLodTarget(
    RideVisualShapeLodLink linked,
    IReadOnlySet<string> closure,
    RideCarVariantVisualTemplateRegistryLimits limits,
    out TemplateSelection selection
  ) {
    selection = null!;
    var lod = linked.Lod;
    if (linked.StaticShapeSource != null && linked.BoneShapeSource != null)
      return LodTargetStatus.Changed;
    switch (lod.Type) {
      case SvdLodType.StaticShape:
        if (lod.BoneShapeRef != null || linked.BoneShapeSource != null ||
            !TryParseTaggedReference(lod.StaticShapeRef, "shs", limits, out var staticName))
          return LodTargetStatus.Changed;
        if (linked.StaticShapeSource == null) return LodTargetStatus.Unavailable;
        var staticSource = linked.StaticShapeSource;
        if (!ValidShapeSource(
              staticSource.File,
              staticSource.Resource?.Name,
              FileType.StaticShape,
              staticName,
              closure,
              limits))
          return LodTargetStatus.Changed;
        selection = new(
          Link: null!,
          linked,
          RideCarVisualTemplateShapeKind.StaticShape,
          staticSource.Resource,
          BoneShape: null);
        return LodTargetStatus.Resolved;
      case SvdLodType.BoneShape:
        if (lod.StaticShapeRef != null || linked.StaticShapeSource != null ||
            !TryParseTaggedReference(lod.BoneShapeRef, "bsh", limits, out var boneName))
          return LodTargetStatus.Changed;
        if (linked.BoneShapeSource == null) return LodTargetStatus.Unavailable;
        var boneSource = linked.BoneShapeSource;
        if (!ValidShapeSource(
              boneSource.File,
              boneSource.Resource?.Name,
              FileType.BoneShape,
              boneName,
              closure,
              limits))
          return LodTargetStatus.Changed;
        selection = new(
          Link: null!,
          linked,
          RideCarVisualTemplateShapeKind.BoneShape,
          StaticShape: null,
          boneSource.Resource);
        return LodTargetStatus.Resolved;
      default:
        return LodTargetStatus.Changed;
    }
  }

  private static bool ValidShapeSource(
    OvlFile? file,
    string? resourceName,
    FileType expectedType,
    string referencedName,
    IReadOnlySet<string> closure,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) => ValidSource(file, resourceName, expectedType, limits) &&
    string.Equals(referencedName, resourceName, StringComparison.OrdinalIgnoreCase) &&
    closure.Contains(file!.Path);

  private static bool ValidSource(
    OvlFile? file,
    string? resourceName,
    FileType expectedType,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) => file != null &&
    ValidIdentifier(file.Name, limits) &&
    ValidIdentifier(file.Path, limits) &&
    ValidIdentifier(resourceName, limits) &&
    file.Type == expectedType &&
    string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase);

  private static bool SavedVariantMatches(
    RideCarVisualVariant variant,
    int? savedVariant
  ) => variant switch {
    RideCarVisualVariant.Normal => savedVariant is null or 0,
    RideCarVisualVariant.WildFlipped => savedVariant == 1,
    _ => false,
  };

  private static GeometryCounts PreflightGeometry(
    IReadOnlyList<PendingEntry> pending,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) {
    var shapes = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var batchCount = 0ul;
    var vertexCount = 0ul;
    var indexCount = 0ul;
    foreach (var item in pending) {
      var selection = item.Selection;
      if (selection == null || !shapes.Add(selection.Shape)) continue;
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
    return new(Convert.ToInt32(batchCount), vertexCount, indexCount);
  }

  private static void ReserveShape(
    StaticShape shape,
    ref ulong batchCount,
    ref ulong vertexCount,
    ref ulong indexCount,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) {
    if (shape == null || shape.Meshes == null || shape.Meshes.Count == 0)
      throw Invalid("selected SHS has no meshes");
    if (!ValidIdentifier(shape.Name, limits))
      throw Invalid("selected SHS has an invalid name");
    Reserve(
      ref batchCount,
      Convert.ToUInt64(shape.Meshes.Count),
      limits.MaximumBatches,
      "mesh batches");
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
    RideCarVariantVisualTemplateRegistryLimits limits
  ) {
    if (shape == null || shape.Meshes == null || shape.Meshes.Count == 0)
      throw Invalid("selected BSH has no meshes");
    if (!ValidIdentifier(shape.Name, limits))
      throw Invalid("selected BSH has an invalid name");
    Reserve(
      ref batchCount,
      Convert.ToUInt64(shape.Meshes.Count),
      limits.MaximumBatches,
      "mesh batches");
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

    // Every returned mesh is now part of this build transaction. Adopt the full list before
    // validation so a malformed early batch cannot leak a valid later mesh.
    var retained = new StaticShapeMeshBatch[batches.Count];
    var duplicateMesh = false;
    foreach (var index in Enumerable.Range(0, batches.Count)) {
      var batch = batches[index];
      retained[index] = batch;
      if (batch?.Mesh == null) continue;
      if (!ownedMeshSet.Add(batch.Mesh)) {
        duplicateMesh = true;
        continue;
      }
      ownedMeshes.Add(batch.Mesh);
    }

    var expectedCount = selection.Kind switch {
      RideCarVisualTemplateShapeKind.StaticShape => selection.StaticShape!.Meshes.Count,
      RideCarVisualTemplateShapeKind.BoneShape => selection.BoneShape!.Meshes.Count,
      _ => 0,
    };
    if (retained.Length != expectedCount)
      throw Invalid(
        $"shape adapter returned {retained.Length} batches, expected {expectedCount}");
    if (duplicateMesh)
      throw Invalid("shape adapter reused a mesh across distinct owned batches");

    foreach (var index in Enumerable.Range(0, retained.Length)) {
      switch (selection.Kind) {
        case RideCarVisualTemplateShapeKind.StaticShape:
          ValidateBatch(selection.StaticShape!, index, retained[index]);
          break;
        case RideCarVisualTemplateShapeKind.BoneShape:
          ValidateBatch(selection.BoneShape!, index, retained[index]);
          break;
      }
    }
    return Array.AsReadOnly(retained);
  }

  private static void ValidateBatch(
    StaticShape shape,
    int index,
    StaticShapeMeshBatch? batch
  ) {
    if (batch == null || batch.Mesh == null)
      throw Invalid($"SHS '{shape.Name}' adapter returned a null batch or mesh");
    var source = shape.Meshes[index];
    ValidateBatchMetadata(
      shape.Name,
      index,
      source.Name,
      source.SupportType,
      source.FtxRef,
      source.TxsRef,
      source.Transparency,
      source.TextureFlags,
      source.Sides,
      source.Vertices.Count,
      source.Indices.Count,
      batch);
  }

  private static void ValidateBatch(
    BoneShape shape,
    int index,
    StaticShapeMeshBatch? batch
  ) {
    if (batch == null || batch.Mesh == null)
      throw Invalid($"BSH '{shape.Name}' adapter returned a null batch or mesh");
    var source = shape.Meshes[index];
    ValidateBatchMetadata(
      shape.Name,
      index,
      source.Name,
      source.SupportType,
      source.FtxRef,
      source.TxsRef,
      source.Transparency,
      source.TextureFlags,
      source.Sides,
      source.Vertices.Count,
      source.Indices.Count,
      batch);
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

  private static PendingEntry Failure(
    RideCarVisualVariantSelectionEntry entry,
    RideCarVariantVisualTemplateStatus status,
    string detail
  ) => new(entry, status, detail, Selection: null);

  private static PendingEntry Changed(
    RideCarVisualVariantSelectionEntry entry,
    string detail
  ) => Failure(
    entry,
    RideCarVariantVisualTemplateStatus.ChangedSelectionIdentity,
    detail);

  private static bool TryParseTaggedReference(
    string? reference,
    string expectedTag,
    RideCarVariantVisualTemplateRegistryLimits limits,
    out string name
  ) {
    name = string.Empty;
    if (!ValidIdentifier(reference, limits)) return false;
    var separator = reference!.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      return false;
    name = reference[..separator];
    return ValidIdentifier(name, limits);
  }

  private static bool ValidIdentifier(
    string? value,
    RideCarVariantVisualTemplateRegistryLimits limits
  ) => !string.IsNullOrWhiteSpace(value) &&
    value.Length <= limits.MaximumStringCharacters &&
    string.Equals(value, value.Trim(), StringComparison.Ordinal);

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static int CountReferences<T>(IReadOnlyList<T> values, T target)
    where T : class {
    var count = 0;
    foreach (var value in values)
      if (ReferenceEquals(value, target)) count++;
    return count;
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

  private static void ValidateLimits(RideCarVariantVisualTemplateRegistryLimits limits) {
    if (limits.MaximumEntries == 0 ||
        limits.MaximumTemplates == 0 ||
        limits.MaximumLodsPerVisual == 0 ||
        limits.MaximumAggregateLods == 0 ||
        limits.MaximumArchivePathsPerCar == 0 ||
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
    new($"Invalid ride-car variant visual-template registry: {message}.");

  private sealed record PendingEntry(
    RideCarVisualVariantSelectionEntry Entry,
    RideCarVariantVisualTemplateStatus Status,
    string? Detail,
    TemplateSelection? Selection);

  private sealed record TemplateSelection(
    RideCarVisualShapeLink Link,
    RideVisualShapeLodLink Lod,
    RideCarVisualTemplateShapeKind Kind,
    StaticShape? StaticShape,
    BoneShape? BoneShape
  ) {
    public object Shape => (object?)StaticShape ?? BoneShape!;
  }

  private enum LodTargetStatus {
    Resolved,
    Unavailable,
    Changed,
  }

  private readonly record struct GeometryCounts(
    int BatchCount,
    ulong VertexCount,
    ulong IndexCount);
}

/// <summary>Allocation and traversal ceilings for selected ride-car body templates.</summary>
internal readonly record struct RideCarVariantVisualTemplateRegistryLimits(
  ulong MaximumEntries,
  ulong MaximumTemplates,
  ulong MaximumLodsPerVisual,
  ulong MaximumAggregateLods,
  ulong MaximumArchivePathsPerCar,
  ulong MaximumDistinctShapeResources,
  ulong MaximumBatches,
  ulong MaximumVertices,
  ulong MaximumIndices,
  int MaximumStringCharacters
) {
  public static RideCarVariantVisualTemplateRegistryLimits Default { get; } = new(
    MaximumEntries: 1_000_000,
    MaximumTemplates: 1_000_000,
    MaximumLodsPerVisual: 4 * 1024,
    MaximumAggregateLods: 8_000_000,
    MaximumArchivePathsPerCar: 4 * 1024,
    MaximumDistinctShapeResources: 256 * 1024,
    MaximumBatches: 4_000_000,
    MaximumVertices: 50_000_000,
    MaximumIndices: 150_000_000,
    MaximumStringCharacters: 4 * 1024);
}
