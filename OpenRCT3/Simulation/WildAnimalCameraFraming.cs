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
    return Calculate(
      scene.Models,
      scene.ModelBindings,
      scene.SourcePlacementCount,
      scene.BuiltPlacementCount,
      scene.ModelCount,
      ValidateBinding,
      Invalid);
  }

  /// <summary>Returns no frame for a valid frame-zero scene with no built placements.</summary>
  public static WildAnimalCameraFramingResult? Calculate(
    WildAnimalFrameZeroSceneBuildResult scene
  ) {
    ArgumentNullException.ThrowIfNull(scene);
    ValidateCounts(scene);
    return Calculate(
      scene.Models,
      scene.ModelBindings,
      scene.SourcePlacementCount,
      scene.BuiltPlacementCount,
      scene.ModelCount,
      ValidateBinding,
      InvalidFrameZero);
  }

  private static WildAnimalCameraFramingResult? Calculate<TBinding>(
    IReadOnlyList<Model> models,
    IReadOnlyList<TBinding> sourceBindings,
    int sourcePlacementCount,
    int builtPlacementCount,
    int modelCount,
    Func<TBinding, FramingModelBinding> resolveBinding,
    Func<string, InvalidDataException> invalid
  ) where TBinding : class {
    if (models == null || sourceBindings == null || models.Count != modelCount ||
        sourceBindings.Count != modelCount)
      throw invalid("model and binding lists changed exact scene counts");
    if (builtPlacementCount == 0) return null;

    var positions = new List<Vector3>(builtPlacementCount);
    var placements = new Dictionary<int, BuiltPlacement>();
    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var previousPlacementIndex = -1;
    var previousBatchIndex = -1;
    foreach (var index in Enumerable.Range(0, modelCount)) {
      var model = models[index]
        ?? throw invalid($"model {index} is null");
      var sourceBinding = sourceBindings[index]
        ?? throw invalid($"binding {index} is null");
      var binding = resolveBinding(sourceBinding);
      ValidateBinding(
        binding,
        model,
        index,
        sourcePlacementCount,
        modelSet,
        invalid);

      var placementIndex = binding.Placement.PlacementIndex;
      if (placementIndex < previousPlacementIndex ||
          (placementIndex == previousPlacementIndex &&
           binding.MaterialBatchIndex <= previousBatchIndex))
        throw invalid("bindings changed exact placement or material-batch order");
      previousBatchIndex = binding.MaterialBatchIndex;
      previousPlacementIndex = placementIndex;

      var matrix = model.Transform.Matrix;
      if (placements.TryGetValue(placementIndex, out var existing)) {
        if (!ReferenceEquals(existing.Placement, binding.Placement) ||
            existing.Transform != matrix)
          throw invalid(
            $"placement {placementIndex} has inconsistent binding identity or transform");
        continue;
      }

      var position = matrix.Translation;
      if (!IsFinite(position))
        throw invalid($"placement {placementIndex} has a non-finite translation");
      placements.Add(placementIndex, new(binding.Placement, matrix));
      positions.Add(position);
    }
    if (placements.Count != builtPlacementCount)
      throw invalid(
        $"built placement count {builtPlacementCount} differs from exact binding count " +
        $"{placements.Count}");

    var target = Mean(positions, invalid);
    var radius = Radius(positions, target, invalid);
    var distance = Convert.ToSingle(Math.Clamp(
      (Convert.ToDouble(radius) * RadiusDistanceScale) + DistancePadding,
      MinimumDiagnosticDistance,
      MaximumDiagnosticDistance));
    if (!IsFinite(target) || !float.IsFinite(radius) || !float.IsFinite(distance))
      throw invalid("calculated framing is non-finite");
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

  private static void ValidateCounts(WildAnimalFrameZeroSceneBuildResult scene) {
    if (scene.SourcePlacementCount < 0 ||
        scene.SourcePlacementCount > MaximumPlacementCount ||
        scene.VisiblePlacementCount < 0 ||
        scene.HiddenPlacementCount < 0 ||
        scene.BuiltPlacementCount < 0 ||
        scene.SkippedPlacementCount < 0 ||
        scene.NoActiveClipPlacementCount < 0 ||
        scene.WeightedStatePlacementCount < 0 ||
        scene.ModelCount < 0 ||
        scene.ModelCount > MaximumModelCount ||
        scene.VisiblePlacementCount + scene.HiddenPlacementCount !=
          scene.SourcePlacementCount ||
        scene.BuiltPlacementCount + scene.SkippedPlacementCount !=
          scene.VisiblePlacementCount ||
        scene.NoActiveClipPlacementCount + scene.WeightedStatePlacementCount !=
          scene.SkippedPlacementCount ||
        (scene.BuiltPlacementCount == 0) != (scene.ModelCount == 0) ||
        scene.SkippedPlacements == null ||
        scene.SkippedPlacements.Count != scene.SkippedPlacementCount)
      throw InvalidFrameZero(
        "scene summary counts are inconsistent or outside resource limits");
  }

  private static FramingModelBinding ValidateBinding(
    WildAnimalStaticSceneModelBinding binding
  ) {
    var placement = binding.Placement;
    if (placement == null || placement.Placement == null ||
        placement.Placement.Animal == null || placement.Placement.Visual == null ||
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
      throw Invalid("binding changed exact saved placement or batch identity");
    return new(binding.Model, placement, binding.MaterialBatchIndex);
  }

  private static FramingModelBinding ValidateBinding(
    WildAnimalFrameZeroSceneModelBinding binding
  ) {
    var placement = binding.Placement;
    var selection = binding.Selection;
    var animation = binding.Animation;
    var poseVariant = binding.PoseVariant;
    var poseSlot = binding.PoseSlot;
    if (placement == null || placement.Placement == null ||
        placement.Placement.Animal == null || placement.Placement.Visual == null ||
        selection == null ||
        !ReferenceEquals(selection.Animal, placement.Placement.Animal) ||
        selection.SerializedVariantIndex != placement.Placement.Animal.Type ||
        selection.Variant == null || selection.Variant.Template == null ||
        selection.Variant.Template.Batches == null ||
        binding.MaterialBatchIndex < 0 ||
        binding.MaterialBatchIndex >= selection.Variant.Template.Batches.Count ||
        binding.MaterialSourceBatch == null ||
        !ReferenceEquals(
          binding.MaterialSourceBatch,
          selection.Variant.Template.Batches[binding.MaterialBatchIndex]) ||
        binding.SkinnedBatch == null ||
        !ReferenceEquals(binding.SkinnedBatch.Mesh, binding.Model.Mesh) ||
        binding.SkinnedBatch.SourceGroupIndex !=
          binding.MaterialSourceBatch.SourceGroupIndex ||
        binding.SkinnedBatch.SourceMeshIndex !=
          binding.MaterialSourceBatch.SourceMeshIndex ||
        !string.Equals(
          binding.SkinnedBatch.SourceMeshName,
          binding.MaterialSourceBatch.SourceMeshName,
          StringComparison.Ordinal))
      throw InvalidFrameZero("binding changed exact saved placement or batch identity");
    if (animation == null ||
        !ReferenceEquals(animation.Visual, placement.Placement.Visual) ||
        animation.Status != WildAnimalSavedAnimationResolutionStatus.ExactSingleClip ||
        animation.Entries == null || animation.ExactSingleClipEntry == null ||
        animation.ExactSingleClipEntry.SavedIndex < 0 ||
        animation.ExactSingleClipEntry.SavedIndex >= animation.Entries.Count ||
        !ReferenceEquals(
          animation.ExactSingleClipEntry,
          animation.Entries[animation.ExactSingleClipEntry.SavedIndex]) ||
        animation.ExactSingleClipEntry.SavedEntry.Weight != 1f ||
        poseVariant == null ||
        !ReferenceEquals(poseVariant.VariantLink, selection.Variant.VariantLink) ||
        !ReferenceEquals(poseVariant.AnimationResources, animation.Resources) ||
        poseVariant.Slots == null ||
        animation.ExactSingleClipEntry.SavedEntry.Type < 0 ||
        animation.ExactSingleClipEntry.SavedEntry.Type >= poseVariant.Slots.Count ||
        poseSlot == null ||
        !ReferenceEquals(
          poseSlot,
          poseVariant.Slots[animation.ExactSingleClipEntry.SavedEntry.Type]) ||
        !ReferenceEquals(
          poseSlot.AnimationSlotLink,
          animation.ExactSingleClipEntry.Slot) ||
        !poseSlot.IsResolved || poseSlot.Pose == null ||
        !ReferenceEquals(
          poseSlot.Pose.Model,
          selection.Variant.Template.ModelSource.Resource) ||
        animation.ExactSingleClipEntry.Slot.Source == null ||
        !ReferenceEquals(
          poseSlot.Pose.Animation,
          animation.ExactSingleClipEntry.Slot.Source.Resource))
      throw InvalidFrameZero("binding changed exact saved animation or pose identity");
    return new(binding.Model, placement, binding.MaterialBatchIndex);
  }

  private static void ValidateBinding(
    FramingModelBinding binding,
    Model model,
    int index,
    int sourcePlacementCount,
    ISet<Model> models,
    Func<string, InvalidDataException> invalid
  ) {
    if (!ReferenceEquals(binding.Model, model) || !models.Add(model) ||
        model.Mesh == null || model.Mesh.State == State.Disposed ||
        model.Material == null || model.Material.State == State.Disposed ||
        model.Transform == null || !IsFinite(model.Transform.Matrix))
      throw invalid($"binding {index} changed exact live model identity or transform");
    var placement = binding.Placement;
    if (placement == null || placement.Placement == null ||
        placement.Placement.Animal == null || placement.Placement.Visual == null ||
        placement.PlacementIndex < 0 || placement.PlacementIndex >= sourcePlacementCount ||
        !placement.Placement.Visual.Visible)
      throw invalid($"binding {index} changed exact saved placement identity");
    var expectedTransform = WildAnimalWorldTransform.ToPark(
      placement.Placement.Visual.WorldMatrix);
    if (model.Transform.Matrix != expectedTransform)
      throw invalid($"binding {index} changed the exact saved placement transform");
  }

  private static Vector3 Mean(
    IReadOnlyList<Vector3> positions,
    Func<string, InvalidDataException> invalid
  ) {
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
    if (!IsFinite(target)) throw invalid("placement mean is non-finite");
    return target;
  }

  private static float Radius(
    IReadOnlyList<Vector3> positions,
    Vector3 target,
    Func<string, InvalidDataException> invalid
  ) {
    var radius = 0d;
    foreach (var position in positions) {
      var x = Convert.ToDouble(position.X) - target.X;
      var y = Convert.ToDouble(position.Y) - target.Y;
      var z = Convert.ToDouble(position.Z) - target.Z;
      var distance = Math.Sqrt((x * x) + (y * y) + (z * z));
      if (!double.IsFinite(distance) || distance > float.MaxValue)
        throw invalid("placement radius is non-finite or outside renderer range");
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

  private static InvalidDataException InvalidFrameZero(string message) =>
    new($"Cannot frame frame-zero Wild-animal scene: {message}.");

  private sealed record FramingModelBinding(
    Model Model,
    WildAnimalParkPlacementResource Placement,
    int MaterialBatchIndex
  );

  private sealed record BuiltPlacement(
    WildAnimalParkPlacementResource Placement,
    Matrix4x4 Transform
  );
}
