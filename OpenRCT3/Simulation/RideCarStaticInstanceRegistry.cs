// Ride Car Static Instance Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed blockers that prevent one saved ride car from becoming a static instance.</summary>
[Flags]
internal enum RideCarStaticInstanceIssue {
  None = 0,
  UnresolvedCarResource = 1,
  UnresolvedSavedCursor = 2,
  MissingBodyTemplate = 4,
  UnavailableModelGeometry = 8,
  UnavailableStaticPose = 16,
}

/// <summary>One saved ride car composed into an optional finite, material-preserving static pose.</summary>
/// <remarks>
/// Every retained object is borrowed from an upstream immutable registry. In particular,
/// <see cref="MaterialBatches"/> and their meshes remain owned by
/// <see cref="RideCarVisualTemplateRegistry"/>.
/// </remarks>
internal sealed record RideCarStaticInstanceEntry(
  int RegistryIndex,
  RideCarInstanceRuntimeEntry CarRuntime,
  RideCarSavedWheelCursorEntry SavedCursor,
  RideCarStaticInstanceIssue Issues,
  RideCarVisualMeshTemplate? BodyTemplate,
  RideCarLongitudinalGeometry? Geometry,
  RideCarStaticPose? Pose,
  string? GeometryUnavailableDetail,
  string? StaticPoseUnavailableDetail
) {
  public ulong CarInstanceEntryId => CarRuntime.CarInstanceEntryId;
  public RideTrainCarRole SavedRole => CarRuntime.SavedRole;
  public IReadOnlyList<StaticShapeMeshBatch>? MaterialBatches => BodyTemplate?.Batches;
  public bool IsResolved => Issues == RideCarStaticInstanceIssue.None;
}

/// <summary>
/// Bounded immutable composition of exact saved cars, wheel cursors, visual templates, geometry,
/// and static poses.
/// </summary>
/// <remarks>
/// Entries preserve authoritative saved-car order. This registry borrows every decoded resource,
/// mesh, material batch, cursor, and pose input. It creates no renderer model, GPU object, mesh,
/// or material and owns or disposes nothing.
/// </remarks>
internal sealed class RideCarStaticInstanceRegistry {
  private const RideCarStaticInstanceIssue KnownIssues =
    RideCarStaticInstanceIssue.UnresolvedCarResource |
    RideCarStaticInstanceIssue.UnresolvedSavedCursor |
    RideCarStaticInstanceIssue.MissingBodyTemplate |
    RideCarStaticInstanceIssue.UnavailableModelGeometry |
    RideCarStaticInstanceIssue.UnavailableStaticPose;

  private RideCarStaticInstanceRegistry(RideCarStaticInstanceEntry[] entries) {
    Entries = Array.AsReadOnly(entries);
    ResolvedCount = entries.Count(entry => entry.IsResolved);
    UnresolvedCarResourceCount = Count(
      entries,
      RideCarStaticInstanceIssue.UnresolvedCarResource);
    UnresolvedSavedCursorCount = Count(
      entries,
      RideCarStaticInstanceIssue.UnresolvedSavedCursor);
    MissingBodyTemplateCount = Count(
      entries,
      RideCarStaticInstanceIssue.MissingBodyTemplate);
    UnavailableModelGeometryCount = Count(
      entries,
      RideCarStaticInstanceIssue.UnavailableModelGeometry);
    UnavailableStaticPoseCount = Count(
      entries,
      RideCarStaticInstanceIssue.UnavailableStaticPose);
  }

  public IReadOnlyList<RideCarStaticInstanceEntry> Entries { get; }
  public int CarCount => Entries.Count;
  public int ResolvedCount { get; }
  public int UnresolvedCount => CarCount - ResolvedCount;
  public int UnresolvedCarResourceCount { get; }
  public int UnresolvedSavedCursorCount { get; }
  public int MissingBodyTemplateCount { get; }
  public int UnavailableModelGeometryCount { get; }
  public int UnavailableStaticPoseCount { get; }

  public static RideCarStaticInstanceRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualTemplateRegistry visualTemplates
  ) => Build(
    carRuntime,
    savedCursors,
    visualTemplates,
    RideCarStaticInstanceRegistryLimits.Default);

  internal static RideCarStaticInstanceRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualTemplateRegistry visualTemplates,
    RideCarStaticInstanceRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(carRuntime);
    ArgumentNullException.ThrowIfNull(savedCursors);
    ArgumentNullException.ThrowIfNull(visualTemplates);
    ValidateLimits(limits);
    if (visualTemplates.IsDisposed)
      throw Invalid("visual-template registry is already disposed");

    ValidateCount(carRuntime.Entries.Count, limits.MaximumCarCount, "car");
    ValidateCount(savedCursors.Entries.Count, limits.MaximumCarCount, "saved cursor");
    ValidateCount(visualTemplates.Templates.Count, limits.MaximumTemplateCount, "template");
    ValidateRegistryCounts(carRuntime, savedCursors, visualTemplates);

    var templatesByCar = IndexTemplates(visualTemplates, limits);
    var entries = new RideCarStaticInstanceEntry[carRuntime.Entries.Count];
    var seenCarIds = new HashSet<ulong>();
    foreach (var index in Enumerable.Range(0, entries.Length)) {
      var car = carRuntime.Entries[index];
      var savedCursor = savedCursors.Entries[index];
      ValidateCarAndCursor(
        car,
        savedCursor,
        index,
        carRuntime.SavedCarCount,
        seenCarIds);
      entries[index] = Compose(car, savedCursor, templatesByCar, index);
    }
    return new(entries);
  }

  private static RideCarStaticInstanceEntry Compose(
    RideCarInstanceRuntimeEntry car,
    RideCarSavedWheelCursorEntry savedCursor,
    IReadOnlyDictionary<RideCarLink, RideCarVisualMeshTemplate> templatesByCar,
    int index
  ) {
    var issues = RideCarStaticInstanceIssue.None;
    RideCarVisualMeshTemplate? template = null;
    RideCarLongitudinalGeometry? geometry = null;
    string? geometryUnavailableDetail = null;
    string? staticPoseUnavailableDetail = null;

    if (!car.HasResolvedResource) {
      ValidateUnresolvedResource(car);
      issues |= RideCarStaticInstanceIssue.UnresolvedCarResource;
    } else {
      ValidateResolvedResource(car);
      if (!templatesByCar.TryGetValue(car.CarResource!, out template)) {
        issues |= RideCarStaticInstanceIssue.MissingBodyTemplate;
      } else {
        ValidateTemplateIdentity(car, template);
        if (template.ShapeKind != RideCarVisualTemplateShapeKind.BoneShape) {
          issues |= RideCarStaticInstanceIssue.UnavailableModelGeometry;
          geometryUnavailableDetail =
            $"body template '{template.Shape}' is not a BSH resource";
        } else {
          try {
            geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(
              template.BoneShape!);
          } catch (InvalidDataException exception) {
            issues |= RideCarStaticInstanceIssue.UnavailableModelGeometry;
            geometryUnavailableDetail = exception.Message;
          }
        }
      }
    }

    if (!savedCursor.IsResolved)
      issues |= RideCarStaticInstanceIssue.UnresolvedSavedCursor;

    RideCarStaticPose? pose = null;
    if (issues == RideCarStaticInstanceIssue.None) {
      try {
        pose = RideCarStaticPoseBuilder.Build(savedCursor, geometry!);
      } catch (InvalidDataException exception) {
        issues |= RideCarStaticInstanceIssue.UnavailableStaticPose;
        staticPoseUnavailableDetail = exception.Message;
      }
    }
    ValidateOutcome(
      issues,
      template,
      geometry,
      pose,
      geometryUnavailableDetail,
      staticPoseUnavailableDetail);
    return new(
      index,
      car,
      savedCursor,
      issues,
      template,
      geometry,
      pose,
      geometryUnavailableDetail,
      staticPoseUnavailableDetail);
  }

  private static Dictionary<RideCarLink, RideCarVisualMeshTemplate> IndexTemplates(
    RideCarVisualTemplateRegistry registry,
    RideCarStaticInstanceRegistryLimits limits
  ) {
    var result = new Dictionary<RideCarLink, RideCarVisualMeshTemplate>(
      ReferenceEqualityComparer.Instance);
    var batchReferences = 0ul;
    foreach (var template in registry.Templates) {
      ValidateTemplate(template);
      if (!result.TryAdd(template.Link.Car, template))
        throw Invalid(
          $"exact RIC link '{template.Link.Car.Reference}' has duplicate body templates");
      Reserve(
        ref batchReferences,
        Convert.ToUInt64(template.Batches.Count),
        limits.MaximumMaterialBatchReferences,
        "material-batch references");
    }
    return result;
  }

  private static void ValidateTemplate(RideCarVisualMeshTemplate template) {
    if (template == null || template.Link == null || template.Lod == null ||
        template.Link.Car == null || template.Link.Train == null ||
        template.Link.Ride == null || template.Link.Visual == null ||
        template.Batches == null)
      throw Invalid("visual-template registry contains an incomplete body template");
    if (template.Link.Visual.Role != RideVisualRole.Body)
      throw Invalid($"RIC '{template.Link.Car.Reference}' template is not a body visual");
    if (!ContainsReference(template.Link.Lods, template.Lod))
      throw Invalid(
        $"RIC '{template.Link.Car.Reference}' template changed exact LOD-link identity");
    if (template.Batches.Count == 0 || template.Batches.Any(batch => batch?.Mesh == null))
      throw Invalid($"RIC '{template.Link.Car.Reference}' template has no valid mesh batches");

    switch (template.ShapeKind) {
      case RideCarVisualTemplateShapeKind.StaticShape:
        if (template.StaticShape == null || template.BoneShape != null ||
            !ReferenceEquals(
              template.Lod.StaticShapeSource?.Resource,
              template.StaticShape) ||
            template.Lod.BoneShapeSource != null)
          throw Invalid(
            $"RIC '{template.Link.Car.Reference}' static template changed shape identity");
        break;
      case RideCarVisualTemplateShapeKind.BoneShape:
        if (template.BoneShape == null || template.StaticShape != null ||
            !ReferenceEquals(
              template.Lod.BoneShapeSource?.Resource,
              template.BoneShape) ||
            template.Lod.StaticShapeSource != null)
          throw Invalid(
            $"RIC '{template.Link.Car.Reference}' bone template changed shape identity");
        break;
      default:
        throw Invalid(
          $"RIC '{template.Link.Car.Reference}' template has unknown shape kind " +
          $"{template.ShapeKind}");
    }
  }

  private static void ValidateCarAndCursor(
    RideCarInstanceRuntimeEntry car,
    RideCarSavedWheelCursorEntry savedCursor,
    int index,
    int savedCarCount,
    ISet<ulong> seenCarIds
  ) {
    if (car == null || savedCursor == null || car.CarInstance == null ||
        car.TrainRuntime == null)
      throw Invalid($"car or saved-cursor entry {index} is incomplete");
    if (car.RegistryIndex != index || savedCursor.RegistryIndex != index)
      throw Invalid($"entry {index} changed exact registry order");
    if (!ReferenceEquals(savedCursor.CarRuntime, car))
      throw Invalid(
        $"saved cursor {index} changed exact ride-car runtime object identity");
    if (car.CarInstanceEntryId == 0 || !seenCarIds.Add(car.CarInstanceEntryId))
      throw Invalid($"saved car ID {car.CarInstanceEntryId} is missing or duplicated");
    if (car.SavedCarIndex < 0 || car.SavedCarIndex >= savedCarCount)
      throw Invalid($"saved car {car.CarInstanceEntryId} has an invalid source index");
    var savedTrainCars = car.TrainRuntime.TrainResource.TrainInstance.Cars;
    if (car.WhichCar < 0 || car.WhichCar >= savedTrainCars.Count ||
        savedTrainCars[car.WhichCar] != car.CarInstanceEntryId)
      throw Invalid($"saved car {car.CarInstanceEntryId} changed exact train order");
    if (savedCursor.CarInstanceEntryId != car.CarInstanceEntryId)
      throw Invalid($"saved cursor {index} changed exact DAT car identity");
  }

  private static void ValidateResolvedResource(RideCarInstanceRuntimeEntry car) {
    if (car.ResourceStatus != RideCarResourceRuntimeStatus.Resolved ||
        car.CarResource == null || car.ConsistCar == null ||
        car.TrainConsist?.IsResolved != true || car.TrainConsist.RideGraph == null ||
        car.TrainConsist.TrainGraph == null)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} advertises an incomplete resolved RIC identity");
    if (!ReferenceEquals(car.TrainConsist.TrainRuntime, car.TrainRuntime) ||
        !ReferenceEquals(car.ConsistCar.CarResource, car.CarResource))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact train, consist, or RIC identity");
    if (car.TrainConsist.Roles == null ||
        car.TrainConsist.Roles.Entries.Count != car.TrainConsist.Cars.Count ||
        car.WhichCar < 0 || car.WhichCar >= car.TrainConsist.Roles.Entries.Count ||
        car.TrainConsist.Roles.Entries[car.WhichCar] != car.ConsistCar.Role)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact consist-role identity");
    if (car.WhichCar < 0 || car.WhichCar >= car.TrainConsist.Cars.Count ||
        !ReferenceEquals(car.TrainConsist.Cars[car.WhichCar], car.ConsistCar))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact consist order");
    if (car.SavedRole != car.ConsistCar.Role.Role ||
        car.SavedRole != car.CarResource.Role)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact consist role");
  }

  private static void ValidateUnresolvedResource(RideCarInstanceRuntimeEntry car) {
    if (car.ResourceStatus == RideCarResourceRuntimeStatus.Resolved ||
        car.CarResource != null || car.ConsistCar != null)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} exposes a RIC resource while unresolved");
  }

  private static void ValidateTemplateIdentity(
    RideCarInstanceRuntimeEntry car,
    RideCarVisualMeshTemplate template
  ) {
    if (!ReferenceEquals(template.Link.Car, car.CarResource) ||
        !ReferenceEquals(template.Link.Train, car.TrainConsist!.TrainGraph) ||
        !ReferenceEquals(template.Link.Ride, car.TrainConsist.RideGraph) ||
        !ReferenceEquals(
          template.Link,
          car.ConsistCar!.PeepSlotEvidence.BodyVisual) ||
        !ReferenceEquals(car.ConsistCar.PeepSlotEvidence.Car, car.CarResource))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} matched a foreign RIC, RIT, or TRR template");
    if (!ContainsReference(car.CarResource!.Visuals, template.Link.Visual) ||
        !string.Equals(
          car.CarResource.Reference,
          car.ConsistCar!.Role.ResourceName,
          StringComparison.Ordinal))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact RIC visual or role identity");
  }

  private static void ValidateOutcome(
    RideCarStaticInstanceIssue issues,
    RideCarVisualMeshTemplate? template,
    RideCarLongitudinalGeometry? geometry,
    RideCarStaticPose? pose,
    string? geometryUnavailableDetail,
    string? staticPoseUnavailableDetail
  ) {
    if ((issues & ~KnownIssues) != 0)
      throw Invalid($"static instance has unknown issue flags {issues}");
    if ((issues & RideCarStaticInstanceIssue.UnresolvedCarResource) != 0 &&
        (template != null || geometry != null || pose != null))
      throw Invalid("unresolved RIC outcome retains resolved visual state");
    if ((issues & RideCarStaticInstanceIssue.MissingBodyTemplate) != 0 && template != null)
      throw Invalid("missing body-template outcome retains a body template");
    if ((issues & RideCarStaticInstanceIssue.UnavailableModelGeometry) != 0 &&
        (template == null || geometry != null ||
         string.IsNullOrWhiteSpace(geometryUnavailableDetail)))
      throw Invalid("unavailable model-geometry outcome is inconsistent");
    if ((issues & RideCarStaticInstanceIssue.UnavailableModelGeometry) == 0 &&
        geometryUnavailableDetail != null)
      throw Invalid("resolved model geometry retains an unresolved detail");
    if ((issues & RideCarStaticInstanceIssue.UnavailableStaticPose) != 0 &&
        (template == null || geometry == null || pose != null ||
         string.IsNullOrWhiteSpace(staticPoseUnavailableDetail)))
      throw Invalid("unavailable static-pose outcome is inconsistent");
    if ((issues & RideCarStaticInstanceIssue.UnavailableStaticPose) == 0 &&
        staticPoseUnavailableDetail != null)
      throw Invalid("resolved static pose retains an unresolved detail");
    if (issues == RideCarStaticInstanceIssue.None &&
        (template == null || geometry == null || pose == null))
      throw Invalid("resolved static instance is incomplete");
    if (issues != RideCarStaticInstanceIssue.None && pose != null)
      throw Invalid("unresolved static instance exposes a pose");
  }

  private static void ValidateRegistryCounts(
    RideCarInstanceRuntimeRegistry cars,
    RideCarSavedWheelCursorRegistry cursors,
    RideCarVisualTemplateRegistry templates
  ) {
    if (cars.LinkedCarCount != cars.Entries.Count ||
        cars.SavedCarCount != cars.LinkedCarCount + cars.UnreferencedCarCount)
      throw Invalid("ride-car runtime counts have drifted from their exact entry lists");
    if (cursors.CarCount != cursors.Entries.Count ||
        cursors.CarCount != cars.Entries.Count ||
        cursors.ResolvedCarCount != cursors.Entries.Count(entry => entry.IsResolved) ||
        cursors.ResolvedContactCount != cursors.Entries.Sum(entry =>
          Convert.ToInt32(entry.Front.IsResolved) + Convert.ToInt32(entry.Rear.IsResolved)))
      throw Invalid("saved wheel-cursor counts or car conservation have drifted");
    if (templates.BodyVisualOccurrenceCount < templates.Templates.Count ||
        templates.UnresolvedBodyVisualCount !=
          templates.BodyVisualOccurrenceCount - templates.Templates.Count)
      throw Invalid("visual-template body occurrence counts have drifted");
  }

  private static bool ContainsReference<T>(IReadOnlyList<T>? values, T target)
    where T : class {
    if (values == null) return false;
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static int Count(
    IEnumerable<RideCarStaticInstanceEntry> entries,
    RideCarStaticInstanceIssue issue
  ) => entries.Count(entry => (entry.Issues & issue) != 0);

  private static void ValidateLimits(RideCarStaticInstanceRegistryLimits limits) {
    if (limits.MaximumCarCount < 0 || limits.MaximumTemplateCount < 0 ||
        limits.MaximumMaterialBatchReferences == 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-car static-instance {description} count exceeds the limit {maximum}.");
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

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car static-instance registry input is invalid: {message}.");
}

/// <summary>Allocation and traversal ceilings for static ride-car composition.</summary>
internal readonly record struct RideCarStaticInstanceRegistryLimits(
  int MaximumCarCount,
  int MaximumTemplateCount,
  ulong MaximumMaterialBatchReferences
) {
  public static RideCarStaticInstanceRegistryLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumTemplateCount: 256 * 1024,
    MaximumMaterialBatchReferences: 4_000_000);
}
