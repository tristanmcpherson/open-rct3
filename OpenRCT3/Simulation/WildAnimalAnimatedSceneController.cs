// Wild Animal Animated Scene Controller
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Counts from one atomic Wild-animal pose update.</summary>
internal readonly record struct WildAnimalAnimatedSceneUpdateResult(
  int AnimatedPlacementCount,
  int AnimatedModelCount,
  int UpdatedPlacementCount,
  int UpdatedModelCount,
  ulong UpdatedVertexCount
);

/// <summary>Bounded seams around exact sampling, CPU skinning, and renderer invalidation.</summary>
internal sealed record WildAnimalAnimatedSceneControllerOperations(
  Func<
    ModelDefinition,
    ModelAnimationDefinition,
    float,
    float,
    bool,
    bool,
    ModelAnimationPose> EvaluatePose,
  Func<ModelAnimationPose, IReadOnlyList<ModelDefinitionMeshBatch>> BuildBatches,
  Action<Mesh> ResetMeshUpload,
  Action<Mesh> DisposeTemporaryMesh
) {
  public static WildAnimalAnimatedSceneControllerOperations Default { get; } = new(
    ModelAnimationPoseEvaluator.Evaluate,
    BuildPoseBatches,
    mesh => mesh.ResetUpload(),
    mesh => mesh.Dispose());

  private static IReadOnlyList<ModelDefinitionMeshBatch> BuildPoseBatches(
    ModelAnimationPose pose
  ) {
    ArgumentNullException.ThrowIfNull(pose);
    if (pose.Bones == null)
      throw new InvalidDataException("Animated ModelAnim pose has a null bone list.");
    var bones = pose.Bones.Select(item => item == null
      ? throw new InvalidDataException("Animated ModelAnim pose has a null bone.")
      : new ModelAnimationFrameZeroBonePose(
        item.ModelBoneIndex,
        item.Bone,
        item.TranslationBoneIndex,
        item.RotationBoneIndex,
        item.LocalTransform,
        item.WorldTransform,
        item.SkinTransform)).ToArray();
    var meshPose = new ModelAnimationFrameZeroPose(
      pose.Model,
      pose.Animation,
      Array.AsReadOnly(bones),
      pose.TranslatedBoneCount,
      pose.RotatedBoneCount);
    return ModelAnimationFrameZeroMeshBuilder.BuildBatches(meshPose);
  }
}

/// <summary>Advances exact Wild single clips and atomically publishes CPU-skinned vertices.</summary>
/// <remarks>
/// Complete Edition advances each Wild controller entry by <c>Time += delta</c>, reduces it by the
/// exact WAD +0x10 period, then samples the controller with the executable-backed loop/forward
/// descriptor <c>0x0101</c>. Every pose and temporary mesh is planned before a renderer upload is
/// reset or a scene vertex changes. Weighted saved states remain typed scene skips.
/// </remarks>
internal sealed class WildAnimalAnimatedSceneController {
  private readonly WildAnimalAnimatedScene scene;
  private readonly WildAnimalAnimatedSceneControllerOperations operations;

  private WildAnimalAnimatedSceneController(
    WildAnimalAnimatedScene scene,
    WildAnimalAnimatedSceneControllerOperations operations
  ) {
    this.scene = scene;
    this.operations = operations;
  }

  public WildAnimalAnimatedScene Scene => scene;
  public int AnimatedPlacementCount => scene.AnimatedPlacementCount;
  public int AnimatedModelCount => scene.AnimatedModelCount;

  /// <summary>Builds a controller and replaces frame-zero geometry with exact saved-time poses.</summary>
  public static WildAnimalAnimatedSceneController Build(
    WildAnimalAnimatedScene scene
  ) => Build(scene, WildAnimalAnimatedSceneControllerOperations.Default);

  internal static WildAnimalAnimatedSceneController Build(
    WildAnimalAnimatedScene scene,
    WildAnimalAnimatedSceneControllerOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(scene);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    if (scene.IsDisposed)
      throw new ObjectDisposedException(nameof(scene));
    var controller = new WildAnimalAnimatedSceneController(scene, operations);
    controller.UpdateCore(0f, synchronize: true);
    return controller;
  }

  /// <summary>Runs one frame without allowing malformed installed data to escape a game loop.</summary>
  public bool TryUpdate(
    TimeSpan delta,
    out WildAnimalAnimatedSceneUpdateResult result,
    out Exception? error
  ) {
    try {
      result = Update(delta);
      error = null;
      return true;
    } catch (Exception caught) when (IsRecoverableAnimationFailure(caught)) {
      result = default;
      error = caught;
      return false;
    }
  }

  public WildAnimalAnimatedSceneUpdateResult Update(TimeSpan delta) {
    if (scene.IsDisposed)
      throw new ObjectDisposedException(nameof(scene));
    var elapsedSeconds = delta.TotalSeconds;
    if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
      throw new ArgumentOutOfRangeException(
        nameof(delta),
        "Wild-animal animation time step must be finite and nonnegative.");
    if (elapsedSeconds == 0d || scene.Entries.Count == 0)
      return new(
        scene.AnimatedPlacementCount,
        scene.AnimatedModelCount,
        0,
        0,
        0);
    var elapsed = Convert.ToSingle(elapsedSeconds);
    if (!float.IsFinite(elapsed))
      throw new ArgumentOutOfRangeException(
        nameof(delta),
        "Wild-animal animation time step exceeds finite float range.");
    return UpdateCore(elapsed, synchronize: false);
  }

  private WildAnimalAnimatedSceneUpdateResult UpdateCore(
    float elapsed,
    bool synchronize
  ) {
    var updates = new List<EntryUpdate>(scene.Entries.Count);
    var targets = new List<MeshUpdate>(scene.AnimatedModelCount);
    var sceneMeshes = SnapshotSceneMeshes(scene.Models);
    var targetMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var temporaryMeshes = new List<Mesh>();
    var temporaryMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var vertexCount = 0ul;

    try {
      foreach (var entry in scene.Entries) {
        ValidateEntry(entry);
        var requestedTime = synchronize
          ? entry.CurrentSavedTime
          : AdvanceTime(entry, elapsed);
        var pose = operations.EvaluatePose(
          entry.Model,
          entry.Animation,
          requestedTime,
          entry.Period,
          entry.Looping,
          entry.Forward)
          ?? throw Invalid(
            $"animal {entry.Placement.Placement.Animal.EntryId} pose evaluator returned null");
        ValidatePose(entry, pose);
        var batches = operations.BuildBatches(pose)
          ?? throw Invalid(
            $"animal {entry.Placement.Placement.Animal.EntryId} mesh builder returned null");
        AdoptTemporaryMeshes(
          entry,
          batches,
          sceneMeshes,
          temporaryMeshes,
          temporaryMeshSet);
        PlanMeshUpdates(entry, batches, targets, targetMeshes, ref vertexCount);
        updates.Add(new(entry, pose));
      }
    } catch (Exception primaryError) {
      var cleanupErrors = DisposeTemporaryMeshes(temporaryMeshes, operations);
      if (cleanupErrors.Count != 0)
        throw new AggregateException(
          "Wild-animal animation planning failed and temporary cleanup also failed.",
          [primaryError, .. cleanupErrors]);
      throw;
    }

    var successfulCleanupErrors = DisposeTemporaryMeshes(temporaryMeshes, operations);
    if (successfulCleanupErrors.Count != 0)
      throw new AggregateException(
        "Wild-animal animation temporary mesh cleanup failed before publication.",
        successfulCleanupErrors);

    // Reset every GPU upload first. A reset failure leaves all CPU geometry and controller times at
    // their previous pose; already-reset meshes remain safely eligible for an ordinary re-upload.
    foreach (var target in targets) operations.ResetMeshUpload(target.Mesh);
    foreach (var target in targets)
      foreach (var index in Enumerable.Range(0, target.Vertices.Length))
        target.Mesh.Vertices[index] = target.Vertices[index];
    foreach (var update in updates) {
      update.Entry.CurrentSavedTime = update.Pose.SavedTime;
      update.Entry.CurrentPose = update.Pose;
    }

    return new(
      scene.AnimatedPlacementCount,
      scene.AnimatedModelCount,
      updates.Count,
      targets.Count,
      vertexCount);
  }

  private static float AdvanceTime(WildAnimalAnimatedSceneEntry entry, float elapsed) {
    var signedElapsed = entry.Forward ? elapsed : -elapsed;
    var result = entry.CurrentSavedTime + signedElapsed;
    if (!float.IsFinite(result))
      throw Invalid(
        $"animal {entry.Placement.Placement.Animal.EntryId} time advance is non-finite");
    return result;
  }

  private static void ValidateEntry(WildAnimalAnimatedSceneEntry entry) {
    if (entry == null || entry.Placement?.Placement?.Animal == null ||
        entry.AnimationResolution == null || entry.PoseVariant == null ||
        entry.PoseSlot == null || entry.Model == null || entry.Animation == null ||
        entry.Bindings == null || entry.Bindings.Count == 0)
      throw Invalid("animated scene contains an incomplete entry");
    if (!float.IsFinite(entry.CurrentSavedTime) || !float.IsFinite(entry.Period) ||
        entry.Period <= 0f || !entry.Looping || !entry.Forward)
      throw Invalid(
        $"animal {entry.Placement.Placement.Animal.EntryId} changed playback state");
    foreach (var binding in entry.Bindings)
      if (binding?.Model?.Mesh == null || binding.Model.Material == null ||
          binding.Model.Mesh.State == State.Disposed ||
          binding.Model.Material.State == State.Disposed)
        throw Invalid(
          $"animal {entry.Placement.Placement.Animal.EntryId} has a disposed scene model");
  }

  private static void ValidatePose(
    WildAnimalAnimatedSceneEntry entry,
    ModelAnimationPose pose
  ) {
    if (!ReferenceEquals(pose.Model, entry.Model) ||
        !ReferenceEquals(pose.Animation, entry.Animation) ||
        pose.WadPeriod != entry.Period || pose.Looping != entry.Looping ||
        pose.Forward != entry.Forward || !float.IsFinite(pose.SavedTime) ||
        !float.IsFinite(pose.NormalizedTime) || pose.NormalizedTime < 0f ||
        pose.NormalizedTime > 1f || pose.Bones == null ||
        pose.Bones.Count != entry.Model.Bones.Count)
      throw Invalid(
        $"animal {entry.Placement.Placement.Animal.EntryId} pose changed exact playback identity");
  }

  private static void AdoptTemporaryMeshes(
    WildAnimalAnimatedSceneEntry entry,
    IReadOnlyList<ModelDefinitionMeshBatch> batches,
    IReadOnlySet<Mesh> sceneMeshes,
    ICollection<Mesh> temporaryMeshes,
    ISet<Mesh> temporaryMeshSet
  ) {
    var invalid = false;
    foreach (var batch in batches) {
      var mesh = batch?.Mesh;
      if (mesh == null || mesh.State == State.Disposed) {
        invalid = true;
        continue;
      }
      if (sceneMeshes.Contains(mesh)) {
        invalid = true;
        continue;
      }
      if (!temporaryMeshSet.Add(mesh)) {
        invalid = true;
        continue;
      }
      temporaryMeshes.Add(mesh);
    }
    if (invalid)
      throw Invalid(
        $"animal {entry.Placement.Placement.Animal.EntryId} mesh builder returned " +
        "a null, disposed, reused, or scene-owned temporary mesh");
  }

  private static IReadOnlySet<Mesh> SnapshotSceneMeshes(IReadOnlyList<Model> models) {
    var result = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    foreach (var index in Enumerable.Range(0, models.Count)) {
      var model = models[index];
      if (model?.Mesh == null || model.Mesh.State == State.Disposed ||
          !result.Add(model.Mesh))
        throw Invalid(
          $"scene model {index} has a null, disposed, or reused owned mesh");
    }
    return result;
  }

  private static void PlanMeshUpdates(
    WildAnimalAnimatedSceneEntry entry,
    IReadOnlyList<ModelDefinitionMeshBatch> batches,
    ICollection<MeshUpdate> targets,
    ISet<Mesh> targetMeshes,
    ref ulong vertexCount
  ) {
    if (batches.Count != entry.Bindings.Count)
      throw Invalid(
        $"animal {entry.Placement.Placement.Animal.EntryId} changed mesh batch count");
    foreach (var index in Enumerable.Range(0, batches.Count)) {
      var batch = batches[index];
      var binding = entry.Bindings[index];
      var target = binding.Model.Mesh;
      if (batch.SourceGroupIndex != binding.SkinnedBatch.SourceGroupIndex ||
          batch.SourceMeshIndex != binding.SkinnedBatch.SourceMeshIndex ||
          !string.Equals(
            batch.SourceMeshName,
            binding.SkinnedBatch.SourceMeshName,
            StringComparison.Ordinal) ||
          batch.Mesh.Vertices.Count != target.Vertices.Count ||
          !batch.Mesh.Indices.SequenceEqual(target.Indices) ||
          !targetMeshes.Add(target))
        throw Invalid(
          $"animal {entry.Placement.Placement.Animal.EntryId} batch {index} changed " +
          "exact topology or scene mesh identity");
      var vertices = batch.Mesh.Vertices.ToArray();
      if (vertices.Any(vertex => !IsFinite(vertex)))
        throw Invalid(
          $"animal {entry.Placement.Placement.Animal.EntryId} batch {index} " +
          "contains a non-finite skinned vertex");
      vertexCount = checked(vertexCount + Convert.ToUInt64(vertices.Length));
      targets.Add(new(target, vertices));
    }
  }

  private static List<Exception> DisposeTemporaryMeshes(
    List<Mesh> meshes,
    WildAnimalAnimatedSceneControllerOperations operations
  ) {
    var errors = new List<Exception>();
    while (meshes.Count > 0) {
      var index = meshes.Count - 1;
      var mesh = meshes[index];
      meshes.RemoveAt(index);
      try {
        operations.DisposeTemporaryMesh(mesh);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void ValidateOperations(
    WildAnimalAnimatedSceneControllerOperations operations
  ) {
    if (operations.EvaluatePose == null || operations.BuildBatches == null ||
        operations.ResetMeshUpload == null || operations.DisposeTemporaryMesh == null)
      throw new ArgumentException(
        "Wild-animal animated scene controller operations must all be configured.",
        nameof(operations));
  }

  private static bool IsRecoverableAnimationFailure(Exception error) =>
    error is InvalidDataException or ArgumentException or InvalidOperationException or
      OverflowException;

  private static bool IsFinite(Vertex value) =>
    IsFinite(value.Position) && IsFinite(value.Normal) && IsFinite(value.TexCoord) &&
    IsFinite(value.Color);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid animated Wild-animal controller state: {message}.");

  private sealed record EntryUpdate(
    WildAnimalAnimatedSceneEntry Entry,
    ModelAnimationPose Pose
  );

  private sealed record MeshUpdate(Mesh Mesh, Vertex[] Vertices);
}
