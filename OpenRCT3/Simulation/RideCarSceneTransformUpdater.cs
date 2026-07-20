// Ride Car Scene Transform Updater
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One finite target transform for an exact saved ride-car identity.</summary>
internal readonly record struct RideCarSceneTransformTarget(
  int RegistryIndex,
  ulong CarInstanceEntryId,
  Matrix4x4 Transform
);

/// <summary>Counts from one complete transform update.</summary>
internal readonly record struct RideCarSceneTransformUpdateResult(
  int UpdatedCarCount,
  int UpdatedModelCount
);

/// <summary>Allocation ceilings for one saved ride-car scene transform update.</summary>
internal readonly record struct RideCarSceneTransformUpdaterLimits(
  int MaximumCarCount,
  int MaximumModelCount
) {
  public static RideCarSceneTransformUpdaterLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumModelCount: 4_000_000);
}

/// <summary>Atomically applies exact per-car transforms to existing scene model bindings.</summary>
/// <remarks>
/// The updater preflights the complete target set and every borrowed model binding before changing
/// a transform. It follows model-binding order, applies one car matrix to all of that car's
/// material batches, and owns or disposes no scene, model, transform, mesh, or material resource.
/// Track sampling, car spacing, and transform derivation remain caller policy.
/// </remarks>
internal static class RideCarSceneTransformUpdater {
  public static RideCarSceneTransformUpdateResult Update(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets
  ) => Update(scene, targets, RideCarSceneTransformUpdaterLimits.Default);

  internal static RideCarSceneTransformUpdateResult Update(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets,
    RideCarSceneTransformUpdaterLimits limits
  ) => Prepare(scene, targets, limits).Apply();

  /// <summary>
  /// Preflights one complete body-scene update without mutating any model transform.
  /// </summary>
  internal static PreparedUpdate Prepare(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets
  ) => Prepare(scene, targets, RideCarSceneTransformUpdaterLimits.Default);

  internal static PreparedUpdate Prepare(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets,
    RideCarSceneTransformUpdaterLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(scene);
    ArgumentNullException.ThrowIfNull(targets);
    ValidateLimits(limits);
    return Preflight(scene, targets, limits);
  }

  private static PreparedUpdate Preflight(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets,
    RideCarSceneTransformUpdaterLimits limits
  ) {
    if (scene.Models == null || scene.ModelBindings == null)
      throw Invalid("scene model or model-binding list is null");
    if (scene.Models.Count > limits.MaximumModelCount ||
        scene.ModelBindings.Count > limits.MaximumModelCount)
      throw Limit("model", limits.MaximumModelCount);
    if (targets.Count > limits.MaximumCarCount)
      throw Limit("car target", limits.MaximumCarCount);
    if (scene.Models.Count != scene.ModelBindings.Count ||
        scene.ModelCount != scene.Models.Count)
      throw Invalid("scene model and exact source-binding counts differ");
    if (scene.SourceCarCount < 0 || scene.BuiltCarCount < 0 || scene.SkippedCarCount < 0 ||
        scene.BuiltCarCount > scene.SourceCarCount ||
        scene.SkippedCarCount != scene.SourceCarCount - scene.BuiltCarCount)
      throw Invalid("scene car counts are inconsistent");

    var bindings = SnapshotBindings(scene);
    var expectedCars = IndexExpectedCars(scene, bindings, limits);
    var targetsByIndex = IndexTargets(scene, targets, expectedCars, limits);
    var assignments = new List<PlannedAssignment>(bindings.Count);
    foreach (var binding in bindings) {
      var target = targetsByIndex[binding.RegistryIndex];
      assignments.Add(new(binding.Model, binding.Model.Transform, target.Transform));
    }
    return new(assignments, expectedCars.Count);
  }

  private static IReadOnlyList<RideCarStaticSceneModelBinding> SnapshotBindings(
    RideCarStaticSceneBuildResult scene
  ) {
    var bindings = new RideCarStaticSceneModelBinding[scene.ModelBindings.Count];
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

  private static IReadOnlyDictionary<int, ExpectedCar> IndexExpectedCars(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarStaticSceneModelBinding> bindings,
    RideCarSceneTransformUpdaterLimits limits
  ) {
    var expected = new Dictionary<int, ExpectedCar>();
    var models = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var transforms = new HashSet<Transform>(ReferenceEqualityComparer.Instance);
    var materialBatches = new HashSet<(int RegistryIndex, int MaterialBatchIndex)>();
    var previousRegistryIndex = -1;
    var previousMaterialBatchIndex = -1;

    foreach (var bindingIndex in Enumerable.Range(0, bindings.Count)) {
      var binding = bindings[bindingIndex];
      var entry = binding.Entry
        ?? throw Invalid($"model binding {bindingIndex} has no saved-car entry");
      if (binding.RegistryIndex < 0 || binding.RegistryIndex >= scene.SourceCarCount ||
          entry.RegistryIndex != binding.RegistryIndex)
        throw Invalid($"model binding {bindingIndex} changed exact saved registry identity");
      if (entry.CarRuntime == null || entry.CarRuntime.CarInstance == null ||
          entry.CarRuntime.RegistryIndex != binding.RegistryIndex)
        throw Invalid($"model binding {bindingIndex} has an incomplete saved-car identity");
      if (entry.CarInstanceEntryId == 0)
        throw Invalid($"model binding {bindingIndex} has a missing saved-car entry ID");
      if (!entry.IsResolved)
        throw Invalid($"model binding {bindingIndex} references an unresolved saved car");
      if (entry.MaterialBatches == null || binding.MaterialBatchIndex < 0 ||
          binding.MaterialBatchIndex >= entry.MaterialBatches.Count ||
          !materialBatches.Add((binding.RegistryIndex, binding.MaterialBatchIndex)))
        throw Invalid($"model binding {bindingIndex} has a duplicate or invalid material batch");
      if (binding.RegistryIndex < previousRegistryIndex ||
          (binding.RegistryIndex == previousRegistryIndex &&
           binding.MaterialBatchIndex <= previousMaterialBatchIndex))
        throw Invalid($"model binding {bindingIndex} changed saved-car or batch order");

      var model = binding.Model;
      if (model.Mesh == null || model.Material == null || model.Transform == null)
        throw Invalid($"model binding {bindingIndex} references an incomplete model");
      if (!models.Add(model))
        throw Invalid($"model binding {bindingIndex} reuses a model");
      if (!transforms.Add(model.Transform))
        throw Invalid($"model binding {bindingIndex} reuses a mutable transform");

      if (expected.TryGetValue(binding.RegistryIndex, out var prior)) {
        if (!ReferenceEquals(prior.Entry, entry) ||
            prior.CarInstanceEntryId != entry.CarInstanceEntryId)
          throw Invalid($"car {binding.RegistryIndex} changed exact saved-car identity");
      } else {
        if (expected.Count >= limits.MaximumCarCount)
          throw Limit("bound car", limits.MaximumCarCount);
        expected.Add(
          binding.RegistryIndex,
          new(entry, entry.CarInstanceEntryId));
      }

      previousRegistryIndex = binding.RegistryIndex;
      previousMaterialBatchIndex = binding.MaterialBatchIndex;
    }

    if (scene.BuiltCarCount != expected.Count)
      throw Invalid("scene built-car count changed from its exact model bindings");
    return expected;
  }

  private static IReadOnlyDictionary<int, RideCarSceneTransformTarget> IndexTargets(
    RideCarStaticSceneBuildResult scene,
    IReadOnlyList<RideCarSceneTransformTarget> targets,
    IReadOnlyDictionary<int, ExpectedCar> expectedCars,
    RideCarSceneTransformUpdaterLimits limits
  ) {
    var byIndex = new Dictionary<int, RideCarSceneTransformTarget>(targets.Count);
    var entryIds = new HashSet<ulong>();
    foreach (var targetIndex in Enumerable.Range(0, targets.Count)) {
      var target = targets[targetIndex];
      if (target.RegistryIndex < 0 || target.RegistryIndex >= scene.SourceCarCount)
        throw Invalid($"target {targetIndex} has an out-of-range registry index");
      if (target.CarInstanceEntryId == 0)
        throw Invalid($"target {targetIndex} has a missing saved-car entry ID");
      if (!IsFinite(target.Transform))
        throw Invalid($"target {targetIndex} has a non-finite transform");
      if (!byIndex.TryAdd(target.RegistryIndex, target))
        throw Invalid($"target registry index {target.RegistryIndex} is duplicated");
      if (!entryIds.Add(target.CarInstanceEntryId))
        throw Invalid($"target saved-car entry ID {target.CarInstanceEntryId} is duplicated");
      if (byIndex.Count > limits.MaximumCarCount)
        throw Limit("car target", limits.MaximumCarCount);
    }

    foreach (var target in byIndex.Values)
      if (!expectedCars.ContainsKey(target.RegistryIndex))
        throw Invalid($"target car {target.RegistryIndex} has no model binding");
    foreach (var expected in expectedCars) {
      if (!byIndex.TryGetValue(expected.Key, out var target))
        throw Invalid($"bound car {expected.Key} has no target transform");
      if (target.CarInstanceEntryId != expected.Value.CarInstanceEntryId)
        throw Invalid($"car {expected.Key} changed exact saved-car entry identity");
    }
    return byIndex;
  }

  private static void ValidateLimits(RideCarSceneTransformUpdaterLimits limits) {
    if (limits.MaximumCarCount <= 0 || limits.MaximumModelCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
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
    new($"Ride-car scene transform update is invalid: {message}.");

  private static InvalidOperationException Limit(string resource, int maximum) =>
    new($"Ride-car scene transform update {resource} count exceeds the limit {maximum}.");

  private sealed record ExpectedCar(
    RideCarStaticInstanceEntry Entry,
    ulong CarInstanceEntryId
  );

  internal sealed record PlannedAssignment(
    Model Model,
    Transform Transform,
    Matrix4x4 Matrix
  );

  /// <summary>
  /// Immutable exact-transform assignments from one successful body-scene preflight.
  /// </summary>
  /// <remarks>
  /// Applying a prepared update performs only the already-validated matrix assignments. This lets
  /// a caller prepare other scene layers before any body transform is changed.
  /// </remarks>
  internal sealed class PreparedUpdate {
    private readonly IReadOnlyList<PlannedAssignment> assignments;

    internal IReadOnlyList<PlannedAssignment> Assignments => assignments;
    internal RideCarSceneTransformUpdateResult Result { get; }

    internal PreparedUpdate(
      IEnumerable<PlannedAssignment> assignments,
      int carCount
    ) {
      this.assignments = Array.AsReadOnly(assignments.ToArray());
      Result = new(carCount, this.assignments.Count);
    }

    internal RideCarSceneTransformUpdateResult Apply() {
      foreach (var assignment in assignments)
        assignment.Transform.Matrix = assignment.Matrix;
      return Result;
    }
  }
}
