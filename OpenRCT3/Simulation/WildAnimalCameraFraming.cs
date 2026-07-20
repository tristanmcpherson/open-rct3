// Wild Animal Camera Framing
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One finite diagnostic frame around exact built Wild-animal placements.</summary>
internal readonly record struct WildAnimalCameraFramingResult(
  Vector3 Target,
  float Radius,
  float Distance,
  float MinimumDistance,
  int PlacementCount
);

/// <summary>Calculates an opt-in native-QA camera frame from rendered animal placements.</summary>
/// <remarks>
/// A placement may own several material-batch models. Its exact rendered transform contributes one
/// position so animals with more material batches cannot bias the diagnostic target.
/// </remarks>
internal static class WildAnimalCameraFraming {
  internal const float RadiusDistanceScale = 2f;
  internal const float DistancePadding = 8f;
  internal const float MinimumDiagnosticDistance = 18f;
  internal const float MaximumDiagnosticDistance = 80f;
  internal const float MinimumCameraDistance = Camera.NearPlaneDistance * 2f;
  private const int MaximumPlacementCount = 100_000;
  private const int MaximumModelCount = 4_000_000;

  /// <summary>Returns no frame for a valid scene with no built placements.</summary>
  public static WildAnimalCameraFramingResult? Calculate(
    WildAnimalStaticSceneBuildResult scene
  ) {
    ArgumentNullException.ThrowIfNull(scene);
    ValidateCounts(scene);
    if (scene.Models == null || scene.ModelBindings == null ||
        scene.Models.Count != scene.ModelCount ||
        scene.ModelBindings.Count != scene.ModelCount)
      throw Invalid("model and binding lists changed exact scene counts");
    if (scene.BuiltPlacementCount == 0) return null;

    var positions = new List<Vector3>(scene.BuiltPlacementCount);
    var placements = new Dictionary<int, BuiltPlacement>();
    var models = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var previousPlacementIndex = -1;
    var previousBatchIndex = -1;
    foreach (var index in Enumerable.Range(0, scene.ModelCount)) {
      var model = scene.Models[index]
        ?? throw Invalid($"model {index} is null");
      var binding = scene.ModelBindings[index]
        ?? throw Invalid($"binding {index} is null");
      ValidateBinding(scene, binding, model, index, models);

      var placementIndex = binding.Placement.PlacementIndex;
      if (placementIndex < previousPlacementIndex ||
          (placementIndex == previousPlacementIndex &&
           binding.MaterialBatchIndex <= previousBatchIndex))
        throw Invalid("bindings changed exact placement or material-batch order");
      previousBatchIndex = binding.MaterialBatchIndex;
      previousPlacementIndex = placementIndex;

      var matrix = model.Transform.Matrix;
      if (placements.TryGetValue(placementIndex, out var existing)) {
        if (!ReferenceEquals(existing.Placement, binding.Placement) ||
            existing.Transform != matrix)
          throw Invalid(
            $"placement {placementIndex} has inconsistent binding identity or transform");
        continue;
      }

      var position = matrix.Translation;
      if (!IsFinite(position))
        throw Invalid($"placement {placementIndex} has a non-finite translation");
      placements.Add(placementIndex, new(binding.Placement, matrix));
      positions.Add(position);
    }
    if (placements.Count != scene.BuiltPlacementCount)
      throw Invalid(
        $"built placement count {scene.BuiltPlacementCount} differs from exact binding count " +
        $"{placements.Count}");

    var target = Mean(positions);
    var radius = Radius(positions, target);
    var distance = Convert.ToSingle(Math.Clamp(
      (Convert.ToDouble(radius) * RadiusDistanceScale) + DistancePadding,
      MinimumDiagnosticDistance,
      MaximumDiagnosticDistance));
    if (!IsFinite(target) || !float.IsFinite(radius) || !float.IsFinite(distance))
      throw Invalid("calculated framing is non-finite");
    return new(
      target,
      radius,
      distance,
      MinimumCameraDistance,
      placements.Count);
  }

  private static void ValidateCounts(WildAnimalStaticSceneBuildResult scene) {
    if (scene.SourcePlacementCount < 0 ||
        scene.SourcePlacementCount > MaximumPlacementCount ||
        scene.VisiblePlacementCount < 0 ||
        scene.HiddenPlacementCount < 0 ||
        scene.BuiltPlacementCount < 0 ||
        scene.SkippedPlacementCount < 0 ||
        scene.ModelCount < 0 ||
        scene.ModelCount > MaximumModelCount ||
        scene.MissingMaterialBatchCount < 0 ||
        scene.VisiblePlacementCount + scene.HiddenPlacementCount !=
          scene.SourcePlacementCount ||
        scene.BuiltPlacementCount > scene.VisiblePlacementCount ||
        scene.SkippedPlacementCount !=
          scene.SourcePlacementCount - scene.BuiltPlacementCount ||
        (scene.BuiltPlacementCount == 0) != (scene.ModelCount == 0))
      throw Invalid("scene summary counts are inconsistent or outside resource limits");
  }

  private static void ValidateBinding(
    WildAnimalStaticSceneBuildResult scene,
    WildAnimalStaticSceneModelBinding binding,
    Model model,
    int index,
    ISet<Model> models
  ) {
    if (!ReferenceEquals(binding.Model, model) || !models.Add(model) ||
        model.Mesh == null || model.Mesh.State == State.Disposed ||
        model.Material == null || model.Material.State == State.Disposed ||
        model.Transform == null || !IsFinite(model.Transform.Matrix))
      throw Invalid($"binding {index} changed exact live model identity or transform");
    var placement = binding.Placement;
    if (placement == null || placement.Placement == null ||
        placement.Placement.Animal == null || placement.Placement.Visual == null ||
        placement.PlacementIndex < 0 ||
        placement.PlacementIndex >= scene.SourcePlacementCount ||
        !placement.Placement.Visual.Visible ||
        binding.Selection == null ||
        !ReferenceEquals(binding.Selection.Animal, placement.Placement.Animal) ||
        binding.Selection.SerializedVariantIndex != placement.Placement.Animal.Type ||
        binding.Selection.Variant == null || binding.Selection.Variant.Template == null ||
        binding.Selection.Variant.Template.Batches == null ||
        binding.MaterialBatchIndex < 0 ||
        binding.MaterialBatchIndex >= binding.Selection.Variant.Template.Batches.Count ||
        binding.Batch == null ||
        !ReferenceEquals(
          binding.Batch,
          binding.Selection.Variant.Template.Batches[binding.MaterialBatchIndex]))
      throw Invalid($"binding {index} changed exact saved placement or batch identity");
    var expectedTransform = WildAnimalWorldTransform.ToPark(
      placement.Placement.Visual.WorldMatrix);
    if (model.Transform.Matrix != expectedTransform)
      throw Invalid($"binding {index} changed the exact saved placement transform");
  }

  private static Vector3 Mean(IReadOnlyList<Vector3> positions) {
    var x = 0d;
    var y = 0d;
    var z = 0d;
    foreach (var position in positions) {
      x += position.X;
      y += position.Y;
      z += position.Z;
    }
    var count = Convert.ToDouble(positions.Count);
    var target = new Vector3(
      Convert.ToSingle(x / count),
      Convert.ToSingle(y / count),
      Convert.ToSingle(z / count));
    if (!IsFinite(target)) throw Invalid("placement mean is non-finite");
    return target;
  }

  private static float Radius(IReadOnlyList<Vector3> positions, Vector3 target) {
    var radius = 0d;
    foreach (var position in positions) {
      var x = Convert.ToDouble(position.X) - target.X;
      var y = Convert.ToDouble(position.Y) - target.Y;
      var z = Convert.ToDouble(position.Z) - target.Z;
      var distance = Math.Sqrt((x * x) + (y * y) + (z * z));
      if (!double.IsFinite(distance) || distance > float.MaxValue)
        throw Invalid("placement radius is non-finite or outside renderer range");
      radius = Math.Max(radius, distance);
    }
    return Convert.ToSingle(radius);
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

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot frame static Wild-animal scene: {message}.");

  private sealed record BuiltPlacement(
    WildAnimalParkPlacementResource Placement,
    Matrix4x4 Transform
  );
}
