// Ride Car Visual Hierarchy Static Instance Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>A fail-closed reason that prevented one selected car or hierarchy part instance.</summary>
internal enum RideCarVisualHierarchyStaticInstanceIssueStatus {
  UpstreamCarUnavailable,
  HierarchyUnavailable,
  BodyHierarchyUnresolved,
  PartHierarchyUnresolved,
  TemplateUnavailable,
}

/// <summary>Exact evidence for one selected car or hierarchy part that was not instantiated.</summary>
internal sealed record RideCarVisualHierarchyStaticInstanceIssue(
  RideCarVariantStaticInstanceEntry SavedCar,
  RideCarVisualHierarchyResolution? Hierarchy,
  RideVisualRole? Role,
  RideCarVisualHierarchyPart? Part,
  RideCarVisualHierarchyStaticInstanceIssueStatus Status,
  string Detail
);

/// <summary>One exact axle or wheel visual placed under its selected saved car body.</summary>
/// <remarks>
/// Every referenced object is borrowed. In particular, <see cref="MaterialBatches"/> and their
/// meshes remain owned by <see cref="RideCarVisualTemplateRegistry"/>.
/// </remarks>
internal sealed record RideCarVisualHierarchyStaticPartInstance(
  int RegistryIndex,
  RideCarVariantStaticInstanceEntry SavedCar,
  RideCarVisualHierarchyResolution Hierarchy,
  RideVisualRole Role,
  uint Type,
  RideCarVisualHierarchyPart Part,
  RideCarVisualShapeLink Visual,
  RideCarVisualMeshTemplate Template,
  IReadOnlyList<StaticShapeMeshBatch> MaterialBatches,
  Matrix4x4 WorldTransform
);

/// <summary>
/// Immutable static axle and wheel instances composed from selected cars and exact visual evidence.
/// </summary>
/// <remarks>
/// Instance order is saved-car order followed by native hierarchy role order. Missing keyed
/// hierarchy evidence blocks that car; an unresolved declared hierarchy blocks every part under
/// that car because no partial parent transform is authoritative. A missing exact visual template
/// blocks only that part. This registry creates and owns no renderer resource.
/// </remarks>
internal sealed class RideCarVisualHierarchyStaticInstanceRegistry {
  private static RideVisualRole[] PartRoles { get; } = [
    RideVisualRole.FrontAxle,
    RideVisualRole.RearAxle,
    RideVisualRole.FrontRightWheel,
    RideVisualRole.FrontLeftWheel,
    RideVisualRole.BackRightWheel,
    RideVisualRole.BackLeftWheel,
  ];

  private RideCarVisualHierarchyStaticInstanceRegistry(
    RideCarVisualHierarchyStaticPartInstance[] instances,
    RideCarVisualHierarchyStaticInstanceIssue[] issues,
    int sourceCarCount,
    int eligibleCarCount,
    int plannedCarCount
  ) {
    Instances = Array.AsReadOnly(instances);
    Issues = Array.AsReadOnly(issues);
    SourceCarCount = sourceCarCount;
    EligibleCarCount = eligibleCarCount;
    PlannedCarCount = plannedCarCount;
  }

  public IReadOnlyList<RideCarVisualHierarchyStaticPartInstance> Instances { get; }
  public IReadOnlyList<RideCarVisualHierarchyStaticInstanceIssue> Issues { get; }
  public int SourceCarCount { get; }
  public int EligibleCarCount { get; }
  public int PlannedCarCount { get; }
  public int PartCount => Instances.Count;
  public int UnavailableCarCount => SourceCarCount - PlannedCarCount;
  public int TemplateUnavailableCount => Issues.Count(issue =>
    issue.Status == RideCarVisualHierarchyStaticInstanceIssueStatus.TemplateUnavailable);

  public static RideCarVisualHierarchyStaticInstanceRegistry Build(
    RideCarVariantStaticInstanceRegistry selectedCars,
    RideCarVisualHierarchyRegistry hierarchies,
    RideCarVisualTemplateRegistry visualTemplates
  ) => Build(
    selectedCars,
    hierarchies,
    visualTemplates,
    RideCarVisualHierarchyStaticInstanceRegistryLimits.Default);

  internal static RideCarVisualHierarchyStaticInstanceRegistry Build(
    RideCarVariantStaticInstanceRegistry selectedCars,
    RideCarVisualHierarchyRegistry hierarchies,
    RideCarVisualTemplateRegistry visualTemplates,
    RideCarVisualHierarchyStaticInstanceRegistryLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(selectedCars);
    ArgumentNullException.ThrowIfNull(hierarchies);
    ArgumentNullException.ThrowIfNull(visualTemplates);
    ValidateLimits(limits);
    if (visualTemplates.IsDisposed)
      throw Invalid("visual-template registry is already disposed");
    ValidateCount(selectedCars.Entries.Count, limits.MaximumCarCount, "car");
    ValidateCount(visualTemplates.Templates.Count, limits.MaximumTemplateCount, "template");
    ValidateTemplateCounts(visualTemplates);

    var templates = IndexTemplates(visualTemplates);
    var instances = new List<RideCarVisualHierarchyStaticPartInstance>();
    var issues = new List<RideCarVisualHierarchyStaticInstanceIssue>();
    var batchReferences = 0ul;
    var eligibleCarCount = 0;
    var plannedCarCount = 0;

    foreach (var index in Enumerable.Range(0, selectedCars.Entries.Count)) {
      var car = selectedCars.Entries[index];
      ValidateSavedCarOrder(car, index);
      if (!car.IsResolved) {
        issues.Add(new(
          car,
          Hierarchy: null,
          Role: car.RequiredBodyRole,
          Part: null,
          RideCarVisualHierarchyStaticInstanceIssueStatus.UpstreamCarUnavailable,
          $"selected static car has blockers {car.Issues}"));
        continue;
      }

      eligibleCarCount++;
      ValidateSelectedCar(car);
      var bodyRole = car.RequiredBodyRole!.Value;
      if (!hierarchies.TryGet(car.CarRuntime.CarResource!, bodyRole, out var hierarchy)) {
        issues.Add(new(
          car,
          Hierarchy: null,
          bodyRole,
          Part: null,
          RideCarVisualHierarchyStaticInstanceIssueStatus.HierarchyUnavailable,
          "no exact car/body-role hierarchy key exists"));
        continue;
      }
      ValidateHierarchyKey(car, hierarchy, bodyRole);
      if (hierarchy.BodyStatus != RideCarVisualHierarchyPartStatus.Resolved) {
        issues.Add(new(
          car,
          hierarchy,
          bodyRole,
          Part: null,
          RideCarVisualHierarchyStaticInstanceIssueStatus.BodyHierarchyUnresolved,
          $"selected body hierarchy has status {hierarchy.BodyStatus}"));
        continue;
      }
      if (AddUnresolvedPartIssues(car, hierarchy, issues)) continue;

      var plan = RideCarVisualHierarchyPosePlanner.Resolve(car.Pose!.Transform, hierarchy);
      ValidatePlan(car, hierarchy, plan);
      plannedCarCount++;
      foreach (var pose in plan.Parts) {
        var visual = pose.Part.ShapeVisual!;
        if (!templates.TryGetValue(visual, out var template)) {
          issues.Add(new(
            car,
            hierarchy,
            pose.Role,
            pose.Part,
            RideCarVisualHierarchyStaticInstanceIssueStatus.TemplateUnavailable,
            "exact resolved BSH/SHS visual template is unavailable"));
          continue;
        }
        ValidatePartTemplate(hierarchy, pose, template);
        Reserve(
          ref batchReferences,
          Convert.ToUInt64(template.Batches.Count),
          limits.MaximumMaterialBatchReferences,
          "material-batch references");
        if (instances.Count >= limits.MaximumPartInstanceCount)
          throw Limit("part instance", limits.MaximumPartInstanceCount);
        instances.Add(new(
          instances.Count,
          car,
          hierarchy,
          pose.Role,
          pose.Type,
          pose.Part,
          visual,
          template,
          template.Batches,
          pose.WorldTransform));
      }
    }

    return new(
      instances.ToArray(),
      issues.ToArray(),
      selectedCars.Entries.Count,
      eligibleCarCount,
      plannedCarCount);
  }

  private static Dictionary<RideCarVisualShapeLink, RideCarVisualMeshTemplate> IndexTemplates(
    RideCarVisualTemplateRegistry registry
  ) {
    var indexed = new Dictionary<RideCarVisualShapeLink, RideCarVisualMeshTemplate>(
      ReferenceEqualityComparer.Instance);
    foreach (var template in registry.Templates) {
      ValidateTemplate(template);
      if (!indexed.TryAdd(template.Link, template))
        throw Invalid("visual-template registry repeats an exact visual occurrence");
    }
    return indexed;
  }

  private static bool AddUnresolvedPartIssues(
    RideCarVariantStaticInstanceEntry car,
    RideCarVisualHierarchyResolution hierarchy,
    ICollection<RideCarVisualHierarchyStaticInstanceIssue> issues
  ) {
    var unresolved = false;
    var parts = hierarchy.Parts;
    if (parts.Count != PartRoles.Length)
      throw Invalid("hierarchy changed its bounded axle/wheel part count");
    foreach (var index in Enumerable.Range(0, parts.Count)) {
      var part = parts[index];
      var role = PartRoles[index];
      ValidatePartIdentity(hierarchy, part, role);
      if (part.Status is RideCarVisualHierarchyPartStatus.Resolved or
          RideCarVisualHierarchyPartStatus.VisualNotDeclared)
        continue;
      unresolved = true;
      issues.Add(new(
        car,
        hierarchy,
        role,
        part,
        RideCarVisualHierarchyStaticInstanceIssueStatus.PartHierarchyUnresolved,
        $"declared hierarchy part has status {part.Status}"));
    }
    return unresolved;
  }

  private static void ValidateSavedCarOrder(
    RideCarVariantStaticInstanceEntry? car,
    int index
  ) {
    if (car == null || car.CarRuntime == null || car.VisualSelection == null ||
        car.VisualTemplate == null)
      throw Invalid($"selected car entry {index} is incomplete");
    if (car.RegistryIndex != index || car.CarRuntime.RegistryIndex != index ||
        car.VisualSelection.RegistryIndex != index || car.VisualTemplate.RegistryIndex != index)
      throw Invalid($"selected car entry {index} changed deterministic registry order");
    if (!ReferenceEquals(car.VisualSelection.CarRuntime, car.CarRuntime) ||
        !ReferenceEquals(car.VisualTemplate.Selection, car.VisualSelection))
      throw Invalid($"selected car entry {index} changed exact upstream identity");
  }

  private static void ValidateSelectedCar(RideCarVariantStaticInstanceEntry car) {
    var role = car.RequiredBodyRole;
    var body = car.VisualSelection.Body;
    var template = car.BodyTemplate;
    if (role is not RideVisualRole.Body and not RideVisualRole.WildFlippedBody ||
        car.CarRuntime.CarResource == null || body == null || template == null ||
        car.Pose == null || car.VisualTemplate.Template == null ||
        !ReferenceEquals(template, car.VisualTemplate.Template) ||
        !ReferenceEquals(template.Link, body) ||
        !ReferenceEquals(body.Car, car.CarRuntime.CarResource) ||
        body.Visual.Role != role || template.ShapeKind != RideCarVisualTemplateShapeKind.BoneShape)
      throw Invalid(
        $"selected car {car.CarInstanceEntryId} changed exact body or BSH pose identity");
  }

  private static void ValidateHierarchyKey(
    RideCarVariantStaticInstanceEntry car,
    RideCarVisualHierarchyResolution hierarchy,
    RideVisualRole bodyRole
  ) {
    var body = car.VisualSelection.Body!;
    if (!ReferenceEquals(hierarchy.Car, car.CarRuntime.CarResource) ||
        !ReferenceEquals(hierarchy.Ride, body.Ride) ||
        !ReferenceEquals(hierarchy.Train, body.Train) ||
        hierarchy.BodyRole != bodyRole ||
        !ReferenceEquals(hierarchy.BodyVisual, body.Visual) ||
        !ReferenceEquals(hierarchy.BodyShapeVisual, body))
      throw Invalid(
        $"selected car {car.CarInstanceEntryId} has a foreign keyed hierarchy identity");
  }

  private static void ValidatePartIdentity(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPart? part,
    RideVisualRole role
  ) {
    if (part == null || part.Role != role || !Enum.IsDefined(part.Status))
      throw Invalid($"{role} changed deterministic hierarchy identity");
    if (part.Status == RideCarVisualHierarchyPartStatus.Resolved && !part.IsResolved)
      throw Invalid($"{role} advertises an incomplete resolved hierarchy");
    if (part.Visual != null &&
        (part.Visual.Role != role || part.ShapeVisual != null &&
          !ReferenceEquals(part.ShapeVisual.Visual, part.Visual)))
      throw Invalid($"{role} retains a foreign visual identity");
    if (part.ShapeVisual != null &&
        (!ReferenceEquals(part.ShapeVisual.Ride, hierarchy.Ride) ||
         !ReferenceEquals(part.ShapeVisual.Train, hierarchy.Train) ||
         !ReferenceEquals(part.ShapeVisual.Car, hierarchy.Car)))
      throw Invalid($"{role} retains a foreign shape occurrence identity");
  }

  private static void ValidatePlan(
    RideCarVariantStaticInstanceEntry car,
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPosePlan plan
  ) {
    if (!ReferenceEquals(plan.Hierarchy, hierarchy) ||
        plan.BodyWorldTransform != car.Pose!.Transform ||
        plan.Parts.Count > PartRoles.Length)
      throw Invalid($"selected car {car.CarInstanceEntryId} produced a foreign pose plan");
    var lastRoleIndex = -1;
    foreach (var pose in plan.Parts) {
      var roleIndex = Array.IndexOf(PartRoles, pose.Role);
      if (roleIndex <= lastRoleIndex || !ReferenceEquals(pose.Part, hierarchy.Parts[roleIndex]) ||
          pose.Type != pose.Part.Type || pose.Part.ShapeVisual == null)
        throw Invalid(
          $"selected car {car.CarInstanceEntryId} changed deterministic part pose identity");
      lastRoleIndex = roleIndex;
    }
  }

  private static void ValidatePartTemplate(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPartPose pose,
    RideCarVisualMeshTemplate template
  ) {
    if (!ReferenceEquals(template.Link, pose.Part.ShapeVisual) ||
        !ReferenceEquals(template.Link.Ride, hierarchy.Ride) ||
        !ReferenceEquals(template.Link.Train, hierarchy.Train) ||
        !ReferenceEquals(template.Link.Car, hierarchy.Car) ||
        !ReferenceEquals(template.Link.Visual, pose.Part.Visual) ||
        template.Link.Visual.Role != pose.Role)
      throw Invalid($"{pose.Role} template has a foreign visual identity");
  }

  private static void ValidateTemplate(RideCarVisualMeshTemplate? template) {
    if (template == null || template.Link == null || template.Lod == null ||
        template.Link.Visual == null || template.Link.Lods == null || template.Batches == null ||
        !ContainsReference(template.Link.Lods, template.Lod) ||
        template.Batches.Count == 0 || template.Batches.Any(batch => batch?.Mesh == null))
      throw Invalid("visual-template registry contains an incomplete template");
    switch (template.ShapeKind) {
      case RideCarVisualTemplateShapeKind.StaticShape:
        if (template.StaticShape == null || template.BoneShape != null ||
            !ReferenceEquals(template.Lod.StaticShapeSource?.Resource, template.StaticShape) ||
            template.Lod.BoneShapeSource != null)
          throw Invalid("SHS visual template changed exact shape identity");
        break;
      case RideCarVisualTemplateShapeKind.BoneShape:
        if (template.BoneShape == null || template.StaticShape != null ||
            !ReferenceEquals(template.Lod.BoneShapeSource?.Resource, template.BoneShape) ||
            template.Lod.StaticShapeSource != null)
          throw Invalid("BSH visual template changed exact shape identity");
        break;
      default:
        throw Invalid($"visual template has unsupported shape kind {template.ShapeKind}");
    }
  }

  private static void ValidateTemplateCounts(RideCarVisualTemplateRegistry templates) {
    if (templates.VisualOccurrenceCount < templates.Templates.Count ||
        templates.UnresolvedVisualCount !=
          templates.VisualOccurrenceCount - templates.Templates.Count ||
        templates.BodyVisualOccurrenceCount < templates.BodyTemplates.Count ||
        templates.UnresolvedBodyVisualCount !=
          templates.BodyVisualOccurrenceCount - templates.BodyTemplates.Count)
      throw Invalid("visual-template occurrence counts have drifted");
  }

  private static bool ContainsReference<T>(IReadOnlyList<T>? values, T target)
    where T : class {
    if (values == null) return false;
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static void ValidateLimits(
    RideCarVisualHierarchyStaticInstanceRegistryLimits limits
  ) {
    if (limits.MaximumCarCount < 0 || limits.MaximumTemplateCount < 0 ||
        limits.MaximumPartInstanceCount < 0 || limits.MaximumMaterialBatchReferences == 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum) throw Limit(description, maximum);
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

  private static InvalidOperationException Limit(string description, int maximum) =>
    new($"Ride-car visual hierarchy static-instance {description} count exceeds " +
      $"the limit {maximum}.");

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car visual hierarchy static-instance input is invalid: {message}.");
}

/// <summary>Allocation and traversal ceilings for static axle and wheel composition.</summary>
internal readonly record struct RideCarVisualHierarchyStaticInstanceRegistryLimits(
  int MaximumCarCount,
  int MaximumTemplateCount,
  int MaximumPartInstanceCount,
  ulong MaximumMaterialBatchReferences
) {
  public static RideCarVisualHierarchyStaticInstanceRegistryLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumTemplateCount: 8_000_000,
    MaximumPartInstanceCount: 6_000_000,
    MaximumMaterialBatchReferences: 24_000_000);
}
