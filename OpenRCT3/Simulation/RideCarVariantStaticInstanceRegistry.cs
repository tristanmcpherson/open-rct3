// Ride Car Variant Static Instance Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed blockers preventing one selected saved car from becoming a static body.</summary>
[Flags]
internal enum RideCarVariantStaticInstanceIssue {
  None = 0,
  UnresolvedCarResource = 1,
  UnresolvedSavedCursor = 2,
  UnavailableVisualSelection = 4,
  UnavailableBodyTemplate = 8,
  UnavailableModelGeometry = 16,
  UnavailableStaticPose = 32,
}

/// <summary>
/// One saved car, exact visual variant, borrowed material batches, geometry, and static pose.
/// </summary>
/// <remarks>
/// Runtime, cursor, selector, template, decoded shape, and material-batch identities are borrowed.
/// Template meshes remain owned by <see cref="RideCarVariantVisualTemplateRegistry"/>. The geometry
/// and pose are immutable managed values and acquire no disposable renderer resource.
/// </remarks>
internal sealed record RideCarVariantStaticInstanceEntry(
  int RegistryIndex,
  RideCarInstanceRuntimeEntry CarRuntime,
  RideCarSavedWheelCursorEntry SavedCursor,
  RideCarVisualVariantSelectionEntry VisualSelection,
  RideCarVariantVisualTemplateEntry VisualTemplate,
  RideCarVariantStaticInstanceIssue Issues,
  RideCarLongitudinalGeometry? Geometry,
  RideCarStaticPose? Pose,
  string? GeometryUnavailableDetail,
  string? StaticPoseUnavailableDetail
) {
  public ulong CarInstanceEntryId => CarRuntime.CarInstanceEntryId;
  public RideTrainCarRole SavedRole => CarRuntime.SavedRole;
  public RideCarVisualVariant? SelectedVariant => VisualSelection.SelectedVariant;
  public RideVisualRole? RequiredBodyRole => VisualSelection.RequiredBodyRole;
  public RideCarVisualMeshTemplate? BodyTemplate => VisualTemplate.Template;
  public IReadOnlyList<StaticShapeMeshBatch>? MaterialBatches => BodyTemplate?.Batches;
  public bool IsResolved => Issues == RideCarVariantStaticInstanceIssue.None;
}

/// <summary>
/// Bounded immutable static composition of exact saved cars and selected normal or Wild bodies.
/// </summary>
/// <remarks>
/// Entries preserve authoritative saved-car order. This registry creates no mesh, material, model,
/// GPU object, or disposable owner. Body placement geometry is accepted only from the selected BSH
/// template because native wheel and car markers are BSH bones.
/// </remarks>
internal sealed class RideCarVariantStaticInstanceRegistry {
  private const RideCarVariantStaticInstanceIssue KnownIssues =
    RideCarVariantStaticInstanceIssue.UnresolvedCarResource |
    RideCarVariantStaticInstanceIssue.UnresolvedSavedCursor |
    RideCarVariantStaticInstanceIssue.UnavailableVisualSelection |
    RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate |
    RideCarVariantStaticInstanceIssue.UnavailableModelGeometry |
    RideCarVariantStaticInstanceIssue.UnavailableStaticPose;

  private RideCarVariantStaticInstanceRegistry(
    RideCarVariantStaticInstanceEntry[] entries
  ) {
    Entries = Array.AsReadOnly(entries);
    ResolvedCount = entries.Count(entry => entry.IsResolved);
    UnresolvedCarResourceCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnresolvedCarResource);
    UnresolvedSavedCursorCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnresolvedSavedCursor);
    UnavailableVisualSelectionCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnavailableVisualSelection);
    UnavailableBodyTemplateCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate);
    UnavailableModelGeometryCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnavailableModelGeometry);
    UnavailableStaticPoseCount = Count(
      entries,
      RideCarVariantStaticInstanceIssue.UnavailableStaticPose);
  }

  public IReadOnlyList<RideCarVariantStaticInstanceEntry> Entries { get; }
  public int CarCount => Entries.Count;
  public int ResolvedCount { get; }
  public int UnresolvedCount => CarCount - ResolvedCount;
  public int UnresolvedCarResourceCount { get; }
  public int UnresolvedSavedCursorCount { get; }
  public int UnavailableVisualSelectionCount { get; }
  public int UnavailableBodyTemplateCount { get; }
  public int UnavailableModelGeometryCount { get; }
  public int UnavailableStaticPoseCount { get; }

  public static RideCarVariantStaticInstanceRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualVariantSelectionRegistry visualSelections,
    RideCarVariantVisualTemplateRegistry visualTemplates
  ) => Build(
    carRuntime,
    savedCursors,
    visualSelections,
    visualTemplates,
    RideCarVariantStaticInstanceRegistryLimits.Default);

  internal static RideCarVariantStaticInstanceRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarSavedWheelCursorRegistry savedCursors,
    RideCarVisualVariantSelectionRegistry visualSelections,
    RideCarVariantVisualTemplateRegistry visualTemplates,
    RideCarVariantStaticInstanceRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(carRuntime);
    ArgumentNullException.ThrowIfNull(savedCursors);
    ArgumentNullException.ThrowIfNull(visualSelections);
    ArgumentNullException.ThrowIfNull(visualTemplates);
    ValidateLimits(limits);
    if (visualTemplates.IsDisposed)
      throw Invalid("variant visual-template registry is already disposed");

    ValidateCount(carRuntime.Entries.Count, limits.MaximumCarCount, "car");
    ValidateCount(savedCursors.Entries.Count, limits.MaximumCarCount, "saved cursor");
    ValidateCount(visualSelections.Entries.Count, limits.MaximumCarCount, "selection");
    ValidateCount(visualTemplates.Entries.Count, limits.MaximumTemplateCount, "template");
    ValidateRegistryCounts(carRuntime, savedCursors, visualSelections, visualTemplates);

    var entries = new RideCarVariantStaticInstanceEntry[carRuntime.Entries.Count];
    var seenCarIds = new HashSet<ulong>();
    var materialBatchReferences = 0ul;
    foreach (var index in Enumerable.Range(0, entries.Length)) {
      var car = carRuntime.Entries[index];
      var cursor = savedCursors.Entries[index];
      var selection = visualSelections.Entries[index];
      var template = visualTemplates.Entries[index];
      ValidateExactOrder(car, cursor, selection, template, index, carRuntime.SavedCarCount,
        seenCarIds);
      ValidateSelectionAndTemplate(
        car,
        selection,
        template,
        limits,
        ref materialBatchReferences);
      entries[index] = Compose(car, cursor, selection, template, index);
    }
    return new(entries);
  }

  private static RideCarVariantStaticInstanceEntry Compose(
    RideCarInstanceRuntimeEntry car,
    RideCarSavedWheelCursorEntry cursor,
    RideCarVisualVariantSelectionEntry selection,
    RideCarVariantVisualTemplateEntry templateEntry,
    int index
  ) {
    var issues = RideCarVariantStaticInstanceIssue.None;
    RideCarLongitudinalGeometry? geometry = null;
    string? geometryUnavailableDetail = null;
    string? staticPoseUnavailableDetail = null;

    if (!car.HasResolvedResource) {
      ValidateUnresolvedResource(car);
      issues |= RideCarVariantStaticInstanceIssue.UnresolvedCarResource;
    } else {
      ValidateResolvedResource(car);
    }
    if (!cursor.IsResolved)
      issues |= RideCarVariantStaticInstanceIssue.UnresolvedSavedCursor;
    if (!selection.IsSelected)
      issues |= RideCarVariantStaticInstanceIssue.UnavailableVisualSelection;
    else if (!templateEntry.IsResolved)
      issues |= RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate;

    var template = templateEntry.Template;
    if (car.HasResolvedResource && selection.IsSelected && templateEntry.IsResolved) {
      if (template!.ShapeKind != RideCarVisualTemplateShapeKind.BoneShape) {
        issues |= RideCarVariantStaticInstanceIssue.UnavailableModelGeometry;
        geometryUnavailableDetail =
          $"selected {selection.RequiredBodyRole} template '{template.Shape}' is not a BSH resource";
      } else {
        try {
          geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(template.BoneShape!);
        } catch (InvalidDataException exception) {
          issues |= RideCarVariantStaticInstanceIssue.UnavailableModelGeometry;
          geometryUnavailableDetail = exception.Message;
        }
      }
    }

    RideCarStaticPose? pose = null;
    if (issues == RideCarVariantStaticInstanceIssue.None) {
      try {
        pose = RideCarStaticPoseBuilder.Build(cursor, geometry!);
      } catch (InvalidDataException exception) {
        issues |= RideCarVariantStaticInstanceIssue.UnavailableStaticPose;
        staticPoseUnavailableDetail = exception.Message;
      }
    }
    ValidateOutcome(
      selection,
      templateEntry,
      issues,
      geometry,
      pose,
      geometryUnavailableDetail,
      staticPoseUnavailableDetail);
    return new(
      index,
      car,
      cursor,
      selection,
      templateEntry,
      issues,
      geometry,
      pose,
      geometryUnavailableDetail,
      staticPoseUnavailableDetail);
  }

  private static void ValidateSelectionAndTemplate(
    RideCarInstanceRuntimeEntry car,
    RideCarVisualVariantSelectionEntry selection,
    RideCarVariantVisualTemplateEntry templateEntry,
    RideCarVariantStaticInstanceRegistryLimits limits,
    ref ulong materialBatchReferences
  ) {
    if (!ReferenceEquals(templateEntry.Selection, selection))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact selector/template identity");
    if (!selection.IsSelected) {
      if (selection.Issue == null || selection.Body != null || selection.Moving != null ||
          selection.BodyControlFallback != null || templateEntry.IsResolved ||
          templateEntry.Template != null || templateEntry.Issue == null ||
          templateEntry.Status !=
            RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed ||
          templateEntry.Issue.SelectionStatus != selection.Status)
        throw Invalid(
          $"saved car {car.CarInstanceEntryId} has inconsistent failed visual evidence");
      return;
    }

    ValidateSelectedIdentity(car, selection);
    if (!templateEntry.IsResolved) {
      if (templateEntry.Template != null || templateEntry.Issue == null ||
          templateEntry.Status is
            RideCarVariantVisualTemplateStatus.Resolved or
            RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed ||
          templateEntry.Issue.SelectionStatus != selection.Status ||
          string.IsNullOrWhiteSpace(templateEntry.Issue.Detail))
        throw Invalid(
          $"saved car {car.CarInstanceEntryId} has inconsistent body-template blocker");
      return;
    }

    ValidateResolvedTemplate(car, selection, templateEntry);
    Reserve(
      ref materialBatchReferences,
      Convert.ToUInt64(templateEntry.Template!.Batches.Count),
      limits.MaximumMaterialBatchReferences,
      "material-batch references");
  }

  private static void ValidateSelectedIdentity(
    RideCarInstanceRuntimeEntry car,
    RideCarVisualVariantSelectionEntry selection
  ) {
    if (selection.Issue != null || selection.SelectedVariant == null ||
        selection.RequiredBodyRole == null || selection.RequiredMovingRole == null ||
        selection.Car == null || selection.Body == null ||
        car.ResourceStatus != RideCarResourceRuntimeStatus.Resolved ||
        !ReferenceEquals(selection.Car, car.CarResource) ||
        !ReferenceEquals(selection.Body.Car, car.CarResource) ||
        !ReferenceEquals(selection.Body.Train, car.TrainConsist?.TrainGraph) ||
        !ReferenceEquals(selection.Body.Ride, car.TrainConsist?.RideGraph))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact selected body graph identity");

    var expectedBodyRole = selection.SelectedVariant == RideCarVisualVariant.Normal
      ? RideVisualRole.Body
      : RideVisualRole.WildFlippedBody;
    var expectedMovingRole = selection.SelectedVariant == RideCarVisualVariant.Normal
      ? RideVisualRole.Moving
      : RideVisualRole.WildFlippedMoving;
    if (!Enum.IsDefined(selection.SelectedVariant.Value) ||
        selection.RequiredBodyRole != expectedBodyRole ||
        selection.RequiredMovingRole != expectedMovingRole ||
        selection.Body.Visual.Role != expectedBodyRole ||
        !ContainsReference(car.CarResource!.Visuals, selection.Body.Visual) ||
        car.TrainRuntime.SavedVisualVariant != selection.SavedVisualVariant ||
        !SavedVariantMatches(selection.SelectedVariant.Value, selection.SavedVisualVariant))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact selected body-role identity");

    var decodedCar = car.CarResource.Car;
    var serializedBody = selection.SelectedVariant == RideCarVisualVariant.Normal
      ? decodedCar?.Visual
      : decodedCar?.Wild?.FlippedVisual;
    if (decodedCar == null || serializedBody == null ||
        selection.SelectedVariant == RideCarVisualVariant.WildFlipped &&
        decodedCar.Version != RideCarVersion.Wild ||
        !string.Equals(
          selection.Body.Visual.Reference,
          serializedBody,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed serialized selected-body identity");

    if (selection.Moving == null) {
      if (selection.BodyControlFallback == null ||
          !ReferenceEquals(selection.BodyControlFallback.Car, selection.Car) ||
          !ReferenceEquals(selection.BodyControlFallback.Body, selection.Body) ||
          selection.BodyControlFallback.MissingMovingRole != expectedMovingRole)
        throw Invalid(
          $"saved car {car.CarInstanceEntryId} changed body-control fallback identity");
    } else if (selection.BodyControlFallback != null ||
               !ReferenceEquals(selection.Moving.Car, selection.Car) ||
               !ReferenceEquals(selection.Moving.Train, selection.Body.Train) ||
               !ReferenceEquals(selection.Moving.Ride, selection.Body.Ride) ||
               selection.Moving.Visual.Role != expectedMovingRole ||
               !ContainsReference(selection.Car.Visuals, selection.Moving.Visual)) {
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed selected moving-visual identity");
    }
  }

  private static void ValidateResolvedTemplate(
    RideCarInstanceRuntimeEntry car,
    RideCarVisualVariantSelectionEntry selection,
    RideCarVariantVisualTemplateEntry templateEntry
  ) {
    var template = templateEntry.Template;
    if (template == null || templateEntry.Issue != null ||
        templateEntry.Status != RideCarVariantVisualTemplateStatus.Resolved ||
        template.Link == null || template.Lod == null || template.Batches == null ||
        !ReferenceEquals(template.Link, selection.Body) ||
        !ReferenceEquals(template.Link.Car, car.CarResource) ||
        !ReferenceEquals(template.Link.Train, car.TrainConsist!.TrainGraph) ||
        !ReferenceEquals(template.Link.Ride, car.TrainConsist.RideGraph) ||
        template.Link.Visual.Role != selection.RequiredBodyRole ||
        !ContainsReference(template.Link.Lods, template.Lod) ||
        template.Batches.Count == 0 || template.Batches.Any(batch => batch?.Mesh == null))
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} changed exact resolved body-template identity");

    switch (template.ShapeKind) {
      case RideCarVisualTemplateShapeKind.StaticShape:
        if (template.StaticShape == null || template.BoneShape != null ||
            !ReferenceEquals(template.Lod.StaticShapeSource?.Resource, template.StaticShape) ||
            template.Lod.BoneShapeSource != null)
          throw Invalid(
            $"saved car {car.CarInstanceEntryId} changed exact SHS template identity");
        break;
      case RideCarVisualTemplateShapeKind.BoneShape:
        if (template.BoneShape == null || template.StaticShape != null ||
            !ReferenceEquals(template.Lod.BoneShapeSource?.Resource, template.BoneShape) ||
            template.Lod.StaticShapeSource != null)
          throw Invalid(
            $"saved car {car.CarInstanceEntryId} changed exact BSH template identity");
        break;
      default:
        throw Invalid(
          $"saved car {car.CarInstanceEntryId} has unknown template shape kind " +
          $"{template.ShapeKind}");
    }
  }

  private static void ValidateExactOrder(
    RideCarInstanceRuntimeEntry car,
    RideCarSavedWheelCursorEntry cursor,
    RideCarVisualVariantSelectionEntry selection,
    RideCarVariantVisualTemplateEntry template,
    int index,
    int savedCarCount,
    ISet<ulong> seenCarIds
  ) {
    if (car == null || cursor == null || selection == null || template == null ||
        car.CarInstance == null || car.TrainRuntime == null)
      throw Invalid($"car composition entry {index} is incomplete");
    if (car.RegistryIndex != index || cursor.RegistryIndex != index ||
        selection.RegistryIndex != index || template.RegistryIndex != index)
      throw Invalid($"entry {index} changed exact registry order");
    if (!ReferenceEquals(cursor.CarRuntime, car) ||
        !ReferenceEquals(selection.CarRuntime, car))
      throw Invalid($"entry {index} changed exact ride-car runtime object identity");
    if (car.CarInstanceEntryId == 0 || !seenCarIds.Add(car.CarInstanceEntryId))
      throw Invalid($"saved car ID {car.CarInstanceEntryId} is missing or duplicated");
    if (car.SavedCarIndex < 0 || car.SavedCarIndex >= savedCarCount)
      throw Invalid($"saved car {car.CarInstanceEntryId} has an invalid source index");
    var savedTrainCars = car.TrainRuntime.TrainResource.TrainInstance.Cars;
    if (car.WhichCar < 0 || car.WhichCar >= savedTrainCars.Count ||
        savedTrainCars[car.WhichCar] != car.CarInstanceEntryId)
      throw Invalid($"saved car {car.CarInstanceEntryId} changed exact train order");
    if (cursor.CarInstanceEntryId != car.CarInstanceEntryId ||
        selection.CarInstanceEntryId != car.CarInstanceEntryId ||
        template.CarInstanceEntryId != car.CarInstanceEntryId)
      throw Invalid($"entry {index} changed exact DAT car identity");
  }

  private static void ValidateResolvedResource(RideCarInstanceRuntimeEntry car) {
    if (car.ResourceStatus != RideCarResourceRuntimeStatus.Resolved ||
        car.CarResource == null || car.ConsistCar == null ||
        car.TrainConsist?.IsResolved != true || car.TrainConsist.RideGraph == null ||
        car.TrainConsist.TrainGraph == null)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} advertises an incomplete resolved RIC identity");
    if (!ReferenceEquals(car.TrainConsist.TrainRuntime, car.TrainRuntime) ||
        !ReferenceEquals(car.ConsistCar.CarResource, car.CarResource) ||
        !ReferenceEquals(car.ConsistCar.PeepSlotEvidence.Car, car.CarResource))
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
        car.SavedRole != car.CarResource.Role ||
        !string.Equals(
          car.CarResource.Reference,
          car.ConsistCar.Role.ResourceName,
          StringComparison.Ordinal))
      throw Invalid($"saved car {car.CarInstanceEntryId} changed exact consist role");
  }

  private static void ValidateUnresolvedResource(RideCarInstanceRuntimeEntry car) {
    if (car.ResourceStatus == RideCarResourceRuntimeStatus.Resolved ||
        car.CarResource != null || car.ConsistCar != null)
      throw Invalid(
        $"saved car {car.CarInstanceEntryId} exposes a RIC resource while unresolved");
  }

  private static void ValidateOutcome(
    RideCarVisualVariantSelectionEntry selection,
    RideCarVariantVisualTemplateEntry templateEntry,
    RideCarVariantStaticInstanceIssue issues,
    RideCarLongitudinalGeometry? geometry,
    RideCarStaticPose? pose,
    string? geometryUnavailableDetail,
    string? staticPoseUnavailableDetail
  ) {
    var template = templateEntry.Template;
    if ((issues & ~KnownIssues) != 0)
      throw Invalid($"variant static instance has unknown issue flags {issues}");
    if (selection.IsSelected ==
        ((issues & RideCarVariantStaticInstanceIssue.UnavailableVisualSelection) != 0))
      throw Invalid("visual-selection outcome is inconsistent");
    if (selection.IsSelected &&
        templateEntry.IsResolved ==
          ((issues & RideCarVariantStaticInstanceIssue.UnavailableBodyTemplate) != 0))
      throw Invalid("body-template outcome is inconsistent");
    if (!selection.IsSelected &&
        (template != null || geometry != null || pose != null))
      throw Invalid("failed visual selection retains resolved body state");
    if (!templateEntry.IsResolved &&
        (template != null || geometry != null || pose != null))
      throw Invalid("failed body template retains resolved body state");
    if ((issues & RideCarVariantStaticInstanceIssue.UnresolvedCarResource) != 0 &&
        (template != null || geometry != null || pose != null))
      throw Invalid("unresolved RIC outcome retains resolved body state");
    if ((issues & RideCarVariantStaticInstanceIssue.UnavailableModelGeometry) != 0 &&
        (template == null || geometry != null ||
         string.IsNullOrWhiteSpace(geometryUnavailableDetail)))
      throw Invalid("unavailable model-geometry outcome is inconsistent");
    if ((issues & RideCarVariantStaticInstanceIssue.UnavailableModelGeometry) == 0 &&
        geometryUnavailableDetail != null)
      throw Invalid("resolved model geometry retains an unresolved detail");
    if ((issues & RideCarVariantStaticInstanceIssue.UnavailableStaticPose) != 0 &&
        (template == null || geometry == null || pose != null ||
         string.IsNullOrWhiteSpace(staticPoseUnavailableDetail)))
      throw Invalid("unavailable static-pose outcome is inconsistent");
    if ((issues & RideCarVariantStaticInstanceIssue.UnavailableStaticPose) == 0 &&
        staticPoseUnavailableDetail != null)
      throw Invalid("resolved static pose retains an unresolved detail");
    if (issues == RideCarVariantStaticInstanceIssue.None &&
        (template == null || geometry == null || pose == null))
      throw Invalid("resolved variant static instance is incomplete");
    if (issues != RideCarVariantStaticInstanceIssue.None && pose != null)
      throw Invalid("unresolved variant static instance exposes a pose");
  }

  private static void ValidateRegistryCounts(
    RideCarInstanceRuntimeRegistry cars,
    RideCarSavedWheelCursorRegistry cursors,
    RideCarVisualVariantSelectionRegistry selections,
    RideCarVariantVisualTemplateRegistry templates
  ) {
    if (cars.LinkedCarCount != cars.Entries.Count ||
        cars.SavedCarCount != cars.LinkedCarCount + cars.UnreferencedCarCount)
      throw Invalid("ride-car runtime counts drifted from exact entry lists");
    if (cursors.CarCount != cursors.Entries.Count ||
        cursors.CarCount != cars.Entries.Count ||
        cursors.ResolvedCarCount != cursors.Entries.Count(entry => entry.IsResolved) ||
        cursors.ResolvedContactCount != cursors.Entries.Sum(entry =>
          Convert.ToInt32(entry.Front.IsResolved) + Convert.ToInt32(entry.Rear.IsResolved)))
      throw Invalid("saved wheel-cursor counts or car conservation drifted");
    if (selections.Entries.Count != cars.Entries.Count ||
        selections.SelectedCount != selections.Entries.Count(entry => entry.IsSelected) ||
        selections.BodyControlFallbackCount !=
          selections.Entries.Count(entry => entry.UsesBodyControlFallback))
      throw Invalid("visual-selection counts or car conservation drifted");
    if (templates.Entries.Count != selections.Entries.Count ||
        templates.ResolvedCount != templates.Entries.Count(entry => entry.IsResolved) ||
        templates.FailedCount != templates.Entries.Count - templates.ResolvedCount ||
        templates.UpstreamSelectionFailureCount != templates.Entries.Count(entry =>
          entry.Status == RideCarVariantVisualTemplateStatus.UpstreamSelectionFailed) ||
        templates.ResolvedCount > selections.SelectedCount)
      throw Invalid("variant template counts or selection conservation drifted");
  }

  private static bool SavedVariantMatches(
    RideCarVisualVariant variant,
    int? savedVariant
  ) => variant switch {
    RideCarVisualVariant.Normal => savedVariant is null or 0,
    RideCarVisualVariant.WildFlipped => savedVariant == 1,
    _ => false,
  };

  private static bool ContainsReference<T>(IReadOnlyList<T>? values, T target)
    where T : class {
    if (values == null) return false;
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static int Count(
    IEnumerable<RideCarVariantStaticInstanceEntry> entries,
    RideCarVariantStaticInstanceIssue issue
  ) => entries.Count(entry => (entry.Issues & issue) != 0);

  private static void ValidateLimits(RideCarVariantStaticInstanceRegistryLimits limits) {
    if (limits.MaximumCarCount < 0 || limits.MaximumTemplateCount < 0 ||
        limits.MaximumMaterialBatchReferences == 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-car variant static-instance {description} count exceeds the limit {maximum}.");
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
    new($"Ride-car variant static-instance registry input is invalid: {message}.");
}

/// <summary>Allocation and traversal ceilings for selected static ride-car composition.</summary>
internal readonly record struct RideCarVariantStaticInstanceRegistryLimits(
  int MaximumCarCount,
  int MaximumTemplateCount,
  ulong MaximumMaterialBatchReferences
) {
  public static RideCarVariantStaticInstanceRegistryLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumTemplateCount: 1_000_000,
    MaximumMaterialBatchReferences: 4_000_000);
}
