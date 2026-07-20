// Ride Car Visual Hierarchy Scene Transform Updater
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Counts from one complete axle and wheel transform update.</summary>
internal readonly record struct RideCarVisualHierarchySceneTransformUpdateResult(
  int TargetCarCount,
  int UpdatedCarCount,
  int UpdatedPartCount,
  int UpdatedModelCount,
  int UnusedTargetCount
);

/// <summary>Allocation ceilings for one hierarchy scene transform update.</summary>
internal readonly record struct RideCarVisualHierarchySceneTransformUpdaterLimits(
  int MaximumCarCount,
  int MaximumPartCount,
  int MaximumModelCount
) {
  public static RideCarVisualHierarchySceneTransformUpdaterLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumPartCount: 6_000_000,
    MaximumModelCount: 24_000_000);
}

/// <summary>Atomically recomputes and applies exact axle and wheel model transforms.</summary>
/// <remarks>
/// The complete target set, saved-car identities, hierarchy parts, models, and newly planned
/// matrices are validated before any mutable transform changes. Targets without hierarchy models
/// are retained as typed unused counts so callers can pass the same ordered body-target list used
/// by <see cref="RideCarSceneTransformUpdater"/>. This updater owns or disposes no resource.
/// </remarks>
internal static class RideCarVisualHierarchySceneTransformUpdater {
  public static RideCarVisualHierarchySceneTransformUpdateResult Update(
    RideCarVisualHierarchySceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> bodyTargets
  ) => Update(
    scene,
    bodyTargets,
    RideCarVisualHierarchySceneTransformUpdaterLimits.Default);

  internal static RideCarVisualHierarchySceneTransformUpdateResult Update(
    RideCarVisualHierarchySceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> bodyTargets,
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(scene);
    ArgumentNullException.ThrowIfNull(bodyTargets);
    ValidateLimits(limits);
    var plan = Preflight(scene, bodyTargets, limits);
    foreach (var assignment in plan.Assignments)
      assignment.Transform.Matrix = assignment.Matrix;
    return new(
      bodyTargets.Count,
      plan.UpdatedCarCount,
      plan.UpdatedPartCount,
      plan.Assignments.Count,
      bodyTargets.Count - plan.UpdatedCarCount);
  }

  private static UpdatePlan Preflight(
    RideCarVisualHierarchySceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> bodyTargets,
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    ValidateSceneCounts(scene, limits);
    var bindings = SnapshotBindings(scene);
    var evidence = IndexBindings(scene, bindings, limits);
    var targets = IndexTargets(scene, bodyTargets, evidence.Cars, limits);
    var matrices = PlanPartMatrices(evidence.Cars, targets, limits);
    var assignments = new List<PlannedAssignment>(bindings.Count);
    foreach (var binding in bindings) {
      if (!matrices.TryGetValue(binding.Instance, out var matrix))
        throw Invalid(
          $"part {binding.PartRegistryIndex} has no recomputed hierarchy pose");
      assignments.Add(new(binding.Model.Transform, matrix));
    }
    return new(
      Array.AsReadOnly(assignments.ToArray()),
      evidence.Cars.Count,
      evidence.Parts.Count);
  }

  private static void ValidateSceneCounts(
    RideCarVisualHierarchySceneBuildResult scene,
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    if (scene.Models == null || scene.ModelBindings == null)
      throw Invalid("scene model or binding list is null");
    if (scene.Models.Count > limits.MaximumModelCount ||
        scene.ModelBindings.Count > limits.MaximumModelCount)
      throw Limit("model", limits.MaximumModelCount);
    if (scene.Models.Count != scene.ModelBindings.Count ||
        scene.ModelCount != scene.Models.Count || scene.SourceCarCount < 0 ||
        scene.PlannedCarCount < 0 || scene.PlannedCarCount > scene.SourceCarCount ||
        scene.SourcePartCount < 0 || scene.BuiltPartCount < 0 ||
        scene.BuiltPartCount > scene.SourcePartCount || scene.SkippedPartCount < 0 ||
        scene.SkippedPartCount != scene.SourcePartCount - scene.BuiltPartCount ||
        scene.MissingMaterialBatchCount < 0 || scene.UpstreamIssueCount < 0 ||
        scene.TemplateUnavailableCount < 0 ||
        scene.TemplateUnavailableCount > scene.UpstreamIssueCount)
      throw Invalid("scene hierarchy counts are inconsistent");
    if (scene.SourceCarCount > limits.MaximumCarCount)
      throw Limit("car", limits.MaximumCarCount);
    if (scene.SourcePartCount > limits.MaximumPartCount)
      throw Limit("part", limits.MaximumPartCount);
  }

  private static IReadOnlyList<RideCarVisualHierarchySceneModelBinding> SnapshotBindings(
    RideCarVisualHierarchySceneBuildResult scene
  ) {
    var bindings = new RideCarVisualHierarchySceneModelBinding[scene.ModelBindings.Count];
    foreach (var index in Enumerable.Range(0, bindings.Length)) {
      var model = scene.Models[index]
        ?? throw Invalid($"scene model {index} is null");
      var binding = scene.ModelBindings[index]
        ?? throw Invalid($"model binding {index} is null");
      if (!ReferenceEquals(binding.Model, model))
        throw Invalid($"model binding {index} changed exact model identity");
      bindings[index] = binding;
    }
    return Array.AsReadOnly(bindings);
  }

  private static BindingEvidence IndexBindings(
    RideCarVisualHierarchySceneBuildResult scene,
    IReadOnlyList<RideCarVisualHierarchySceneModelBinding> bindings,
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    var cars = new Dictionary<int, ExpectedCar>();
    var parts = new Dictionary<
      RideCarVisualHierarchyStaticPartInstance,
      ExpectedPart>(ReferenceEqualityComparer.Instance);
    var models = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var transforms = new HashSet<Transform>(ReferenceEqualityComparer.Instance);
    var batches = new HashSet<(int PartRegistryIndex, int MaterialBatchIndex)>();
    var previousPartIndex = -1;
    var previousBatchIndex = -1;

    foreach (var bindingIndex in Enumerable.Range(0, bindings.Count)) {
      var binding = bindings[bindingIndex];
      var instance = binding.Instance
        ?? throw Invalid($"model binding {bindingIndex} has no hierarchy instance");
      ValidateInstance(scene, binding, instance, bindingIndex);
      if (!batches.Add((binding.PartRegistryIndex, binding.MaterialBatchIndex)))
        throw Invalid($"model binding {bindingIndex} duplicates a part batch");
      if (binding.PartRegistryIndex < previousPartIndex ||
          binding.PartRegistryIndex == previousPartIndex &&
            binding.MaterialBatchIndex <= previousBatchIndex)
        throw Invalid($"model binding {bindingIndex} changed deterministic part order");

      var model = binding.Model;
      if (model.Mesh == null || model.Material == null || model.Transform == null ||
          model.Mesh.State == State.Disposed || model.Material.State == State.Disposed)
        throw Invalid($"model binding {bindingIndex} references an incomplete model");
      if (!models.Add(model) || !transforms.Add(model.Transform))
        throw Invalid($"model binding {bindingIndex} reuses a model or mutable transform");

      var addedPart = false;
      if (parts.TryGetValue(instance, out var priorPart)) {
        if (priorPart.RegistryIndex != instance.RegistryIndex)
          throw Invalid($"part {instance.RegistryIndex} changed exact instance identity");
      } else {
        if (parts.Count >= limits.MaximumPartCount)
          throw Limit("bound part", limits.MaximumPartCount);
        parts.Add(instance, new(instance.RegistryIndex));
        addedPart = true;
      }

      var carIndex = instance.SavedCar.RegistryIndex;
      if (cars.TryGetValue(carIndex, out var priorCar)) {
        if (!ReferenceEquals(priorCar.SavedCar, instance.SavedCar) ||
            !ReferenceEquals(priorCar.Hierarchy, instance.Hierarchy) ||
            priorCar.CarInstanceEntryId != instance.SavedCar.CarInstanceEntryId)
          throw Invalid($"car {carIndex} changed exact hierarchy identity");
        if (addedPart) priorCar.Parts.Add(instance);
      } else {
        if (cars.Count >= limits.MaximumCarCount)
          throw Limit("bound car", limits.MaximumCarCount);
        cars.Add(carIndex, new(
          instance.SavedCar,
          instance.Hierarchy,
          instance.SavedCar.CarInstanceEntryId,
          [instance]));
      }
      previousPartIndex = binding.PartRegistryIndex;
      previousBatchIndex = binding.MaterialBatchIndex;
    }

    if (parts.Count != scene.BuiltPartCount)
      throw Invalid("scene built-part count changed from exact model bindings");
    if (cars.Count > scene.PlannedCarCount)
      throw Invalid("bound car count exceeds the planned hierarchy cars");
    return new(cars, parts);
  }

  private static void ValidateInstance(
    RideCarVisualHierarchySceneBuildResult scene,
    RideCarVisualHierarchySceneModelBinding binding,
    RideCarVisualHierarchyStaticPartInstance instance,
    int bindingIndex
  ) {
    var car = instance.SavedCar;
    if (binding.PartRegistryIndex != instance.RegistryIndex ||
        instance.RegistryIndex < 0 || instance.RegistryIndex >= scene.SourcePartCount ||
        binding.MaterialBatchIndex < 0 ||
        binding.MaterialBatchIndex >= instance.MaterialBatches.Count ||
        car == null || car.CarRuntime == null || car.CarRuntime.CarInstance == null ||
        !car.IsResolved || car.RegistryIndex < 0 || car.RegistryIndex >= scene.SourceCarCount ||
        car.CarRuntime.RegistryIndex != car.RegistryIndex || car.CarInstanceEntryId == 0 ||
        instance.Hierarchy == null || instance.Part == null || instance.Visual == null ||
        instance.Template == null || instance.MaterialBatches == null ||
        instance.Role != instance.Part.Role || instance.Type != instance.Part.Type ||
        !ReferenceEquals(instance.Part.ShapeVisual, instance.Visual) ||
        !ReferenceEquals(instance.Template.Link, instance.Visual) ||
        !ReferenceEquals(instance.MaterialBatches, instance.Template.Batches) ||
        !ContainsReference(instance.Hierarchy.Parts, instance.Part))
      throw Invalid($"model binding {bindingIndex} changed exact saved-car or part identity");
  }

  private static IReadOnlyDictionary<int, RideCarSceneTransformTarget> IndexTargets(
    RideCarVisualHierarchySceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> bodyTargets,
    IReadOnlyDictionary<int, ExpectedCar> expectedCars,
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    if (bodyTargets.Count > limits.MaximumCarCount)
      throw Limit("car target", limits.MaximumCarCount);
    var targets = new Dictionary<int, RideCarSceneTransformTarget>(bodyTargets.Count);
    var entryIds = new HashSet<ulong>();
    var previousRegistryIndex = -1;
    foreach (var targetIndex in Enumerable.Range(0, bodyTargets.Count)) {
      var target = bodyTargets[targetIndex];
      if (target.RegistryIndex < 0 || target.RegistryIndex >= scene.SourceCarCount ||
          target.RegistryIndex <= previousRegistryIndex)
        throw Invalid($"target {targetIndex} changed deterministic saved-car order");
      if (target.CarInstanceEntryId == 0 || !entryIds.Add(target.CarInstanceEntryId) ||
          !TrackMath.IsFinite(target.Transform) ||
          !targets.TryAdd(target.RegistryIndex, target))
        throw Invalid($"target {targetIndex} has invalid identity or transform");
      previousRegistryIndex = target.RegistryIndex;
    }
    foreach (var expected in expectedCars) {
      if (!targets.TryGetValue(expected.Key, out var target))
        throw Invalid($"bound car {expected.Key} has no body target");
      if (target.CarInstanceEntryId != expected.Value.CarInstanceEntryId)
        throw Invalid($"car {expected.Key} changed exact saved-car entry identity");
    }
    return targets;
  }

  private static IReadOnlyDictionary<
    RideCarVisualHierarchyStaticPartInstance,
    Matrix4x4> PlanPartMatrices(
      IReadOnlyDictionary<int, ExpectedCar> cars,
      IReadOnlyDictionary<int, RideCarSceneTransformTarget> targets,
      RideCarVisualHierarchySceneTransformUpdaterLimits limits
    ) {
    var matrices = new Dictionary<
      RideCarVisualHierarchyStaticPartInstance,
      Matrix4x4>(ReferenceEqualityComparer.Instance);
    foreach (var car in cars.OrderBy(pair => pair.Key)) {
      var target = targets[car.Key];
      var plan = RideCarVisualHierarchyPosePlanner.Resolve(
        target.Transform,
        car.Value.Hierarchy);
      foreach (var instance in car.Value.Parts) {
        var poses = plan.Parts.Where(pose => ReferenceEquals(pose.Part, instance.Part)).ToArray();
        if (poses.Length != 1 || poses[0].Role != instance.Role ||
            poses[0].Type != instance.Type || !TrackMath.IsFinite(poses[0].WorldTransform) ||
            !matrices.TryAdd(instance, poses[0].WorldTransform))
          throw Invalid(
            $"part {instance.RegistryIndex} changed exact recomputed pose identity");
        if (matrices.Count > limits.MaximumPartCount)
          throw Limit("planned part", limits.MaximumPartCount);
      }
    }
    return matrices;
  }

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static void ValidateLimits(
    RideCarVisualHierarchySceneTransformUpdaterLimits limits
  ) {
    if (limits.MaximumCarCount <= 0 || limits.MaximumPartCount <= 0 ||
        limits.MaximumModelCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car hierarchy scene transform update is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, int maximum) =>
    new($"Ride-car hierarchy scene transform update {resource} count exceeds " +
      $"the limit {maximum}.");

  private sealed record ExpectedPart(int RegistryIndex);

  private sealed record ExpectedCar(
    RideCarVariantStaticInstanceEntry SavedCar,
    RideCarVisualHierarchyResolution Hierarchy,
    ulong CarInstanceEntryId,
    List<RideCarVisualHierarchyStaticPartInstance> Parts
  );

  private sealed record BindingEvidence(
    IReadOnlyDictionary<int, ExpectedCar> Cars,
    IReadOnlyDictionary<RideCarVisualHierarchyStaticPartInstance, ExpectedPart> Parts
  );

  private sealed record PlannedAssignment(Transform Transform, Matrix4x4 Matrix);

  private sealed record UpdatePlan(
    IReadOnlyList<PlannedAssignment> Assignments,
    int UpdatedCarCount,
    int UpdatedPartCount
  );
}
