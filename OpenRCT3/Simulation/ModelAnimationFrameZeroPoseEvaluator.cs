// Model Animation Frame-Zero Pose Evaluator
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One model-order bone pose evaluated from the proven first animation frame.</summary>
internal sealed record ModelAnimationFrameZeroBonePose(
  int ModelBoneIndex,
  ModelBone Bone,
  int? TranslationBoneIndex,
  int? RotationBoneIndex,
  Matrix4x4 LocalTransform,
  Matrix4x4 WorldTransform,
  Matrix4x4 SkinTransform
);

/// <summary>One exact frame-zero model pose without unproven timeline interpolation.</summary>
internal sealed record ModelAnimationFrameZeroPose(
  ModelDefinition Model,
  ModelAnimationDefinition Animation,
  IReadOnlyList<ModelAnimationFrameZeroBonePose> Bones,
  int TranslatedBoneCount,
  int RotatedBoneCount
);

/// <summary>Evaluates only the reference-proven first frame of one MDL/modelanim pair.</summary>
/// <remarks>
/// Complete Edition evidence proves that the first <c>AnimatedBoneCount</c> +0x28 triples are
/// absolute local translations in animated-bone-name order. The first <c>FullBoneCount</c> +0x2C
/// four-tuples are absolute local XYZW quaternions in full-bone-name order. MDL locals use
/// <c>R(q) * T(p)</c> with row vectors, children compose as
/// <c>local * parentWorld</c>, and each stored MDL matrix is inverse bind-world. Both arrays are
/// frame-major, but native timeline interpolation remains unproven, so this evaluator deliberately
/// cannot select another frame or time.
/// </remarks>
internal static class ModelAnimationFrameZeroPoseEvaluator {
  private const int MaximumBoneCount = 64 * 1024;
  private const ulong MaximumSampleCount = 10_000_000;
  private const double QuaternionLengthSquaredTolerance = 0.0001;

  public static ModelAnimationFrameZeroPose Evaluate(
    ModelDefinition model,
    ModelAnimationDefinition animation
  ) {
    ArgumentNullException.ThrowIfNull(model);
    ArgumentNullException.ThrowIfNull(animation);
    var bones = ValidateModel(model);
    var channels = ValidateAnimation(model, animation, bones);

    var translations = new Vector3[bones.Count];
    var rotations = new Quaternion[bones.Count];
    var translatedByModelIndex = new int?[bones.Count];
    var rotatedByModelIndex = new int?[bones.Count];
    foreach (var modelBoneIndex in Enumerable.Range(0, bones.Count)) {
      translations[modelBoneIndex] = BindTranslation(bones[modelBoneIndex]);
      rotations[modelBoneIndex] = BindRotation(bones[modelBoneIndex]);
    }
    foreach (var animatedBoneIndex in Enumerable.Range(
               0,
               channels.TranslationModelIndices.Length)) {
      var modelBoneIndex = channels.TranslationModelIndices[animatedBoneIndex];
      var translation = animation.TriplesAt28[animatedBoneIndex];
      translations[modelBoneIndex] = new(
        translation.Field00,
        translation.Field04,
        translation.Field08);
      translatedByModelIndex[modelBoneIndex] = animatedBoneIndex;
    }
    foreach (var fullBoneIndex in Enumerable.Range(0, channels.RotationModelIndices.Length)) {
      var modelBoneIndex = channels.RotationModelIndices[fullBoneIndex];
      var rotation = animation.NormalizedFourTuplesAt2C[fullBoneIndex];
      rotations[modelBoneIndex] = new(
        rotation.Field00,
        rotation.Field04,
        rotation.Field08,
        rotation.Field0C);
      rotatedByModelIndex[modelBoneIndex] = fullBoneIndex;
    }

    var localTransforms = new Matrix4x4[bones.Count];
    foreach (var modelBoneIndex in Enumerable.Range(0, bones.Count))
      localTransforms[modelBoneIndex] = Local(
        translations[modelBoneIndex],
        rotations[modelBoneIndex],
        $"model bone {modelBoneIndex} '{bones[modelBoneIndex].Name}'");
    var worldTransforms = ComposeWorldTransforms(bones, localTransforms);
    var poses = new ModelAnimationFrameZeroBonePose[bones.Count];
    foreach (var index in Enumerable.Range(0, bones.Count)) {
      var skin = bones[index].Matrix * worldTransforms[index];
      if (!IsFinite(skin))
        throw Invalid(model, animation, $"bone {index} produced a non-finite skin matrix");
      poses[index] = new(
        index,
        bones[index],
        translatedByModelIndex[index],
        rotatedByModelIndex[index],
        localTransforms[index],
        worldTransforms[index],
        skin);
    }
    return new(
      model,
      animation,
      Array.AsReadOnly(poses),
      channels.TranslationModelIndices.Length,
      channels.RotationModelIndices.Length);
  }

  private static IReadOnlyList<ModelBone> ValidateModel(ModelDefinition model) {
    if (model.Bones == null || model.BoneCount == 0 || model.BoneCount > MaximumBoneCount ||
        model.Bones.Count != Convert.ToInt32(model.BoneCount))
      throw Invalid(model, null, "model bone count is missing, inconsistent, or outside limits");
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var rootCount = 0;
    foreach (var index in Enumerable.Range(0, model.Bones.Count)) {
      var bone = model.Bones[index]
        ?? throw Invalid(model, null, $"model bone {index} is null");
      if (string.IsNullOrWhiteSpace(bone.Name) || !names.Add(bone.Name))
        throw Invalid(model, null, $"model bone {index} has a missing or duplicate name");
      if (!IsFinite(bone.PositionQuaternion) || !IsFinite(bone.RotationQuaternion) ||
          !IsFinite(bone.Matrix))
        throw Invalid(model, null, $"model bone {index} contains a non-finite bind value");
      RequireNormalized(
        new Quaternion(
          bone.RotationQuaternion.X,
          bone.RotationQuaternion.Y,
          bone.RotationQuaternion.Z,
          bone.RotationQuaternion.W),
        model,
        null,
        $"model bone {index} bind quaternion");
      if (bone.Parent == ushort.MaxValue) rootCount++;
      else if (bone.Parent >= model.Bones.Count || bone.Parent == index)
        throw Invalid(model, null, $"model bone {index} has invalid parent {bone.Parent}");
    }
    if (rootCount == 0)
      throw Invalid(model, null, "model skeleton has no root bone");
    return model.Bones;
  }

  private static FrameZeroChannels ValidateAnimation(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    IReadOnlyList<ModelBone> bones
  ) {
    if (animation.FrameCountAt04 == 0 ||
        animation.AnimatedBoneCountAt18 == 0 ||
        animation.AnimatedBoneCountAt18 > MaximumBoneCount ||
        animation.FullBoneCountAt1C == 0 ||
        animation.FullBoneCountAt1C > model.BoneCount ||
        animation.AnimatedBoneCountAt18 > animation.FullBoneCountAt1C)
      throw Invalid(model, animation,
        "animation frame or skeleton counts are inconsistent or outside limits");
    if (!float.IsFinite(animation.DurationAt00) || animation.DurationAt00 < 0f)
      throw Invalid(model, animation, "animation duration is negative or non-finite");
    var animatedCount = Convert.ToInt32(animation.AnimatedBoneCountAt18);
    var fullCount = Convert.ToInt32(animation.FullBoneCountAt1C);
    if (animation.AnimatedBoneNames == null ||
        animation.FullBoneNames == null ||
        animation.TriplesAt28 == null ||
        animation.NormalizedFourTuplesAt2C == null ||
        animation.AnimatedBoneNames.Count != animatedCount ||
        animation.FullBoneNames.Count != fullCount)
      throw Invalid(model, animation, "animation names are missing or inconsistent with counts");
    var translationSampleCount = Convert.ToUInt64(animation.FrameCountAt04) *
      Convert.ToUInt64(animation.AnimatedBoneCountAt18);
    var rotationSampleCount = Convert.ToUInt64(animation.FrameCountAt04) *
      Convert.ToUInt64(animation.FullBoneCountAt1C);
    if (translationSampleCount > MaximumSampleCount ||
        rotationSampleCount > MaximumSampleCount ||
        animation.TriplesAt28.Count != Convert.ToInt32(translationSampleCount) ||
        animation.NormalizedFourTuplesAt2C.Count != Convert.ToInt32(rotationSampleCount))
      throw Invalid(model, animation, "animation sample arrays are inconsistent or outside limits");

    var modelByName = bones
      .Select((bone, index) => (bone.Name, index))
      .ToDictionary(item => item.Name, item => item.index, StringComparer.OrdinalIgnoreCase);
    var fullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in animation.FullBoneNames) {
      if (string.IsNullOrWhiteSpace(name) || !fullNames.Add(name) || !modelByName.ContainsKey(name))
        throw Invalid(model, animation,
          "full-bone names are missing, duplicated, or differ from the MDL skeleton");
    }
    var animatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var translationModelIndices = new int[animatedCount];
    foreach (var animatedIndex in Enumerable.Range(0, animatedCount)) {
      var name = animation.AnimatedBoneNames[animatedIndex];
      if (string.IsNullOrWhiteSpace(name) ||
          !animatedNames.Add(name) ||
          !fullNames.Contains(name) ||
          !modelByName.TryGetValue(name, out var modelIndex))
        throw Invalid(model, animation,
          $"animated bone {animatedIndex} is missing, duplicated, or absent from the full skeleton");
      translationModelIndices[animatedIndex] = modelIndex;
      var translation = animation.TriplesAt28[animatedIndex];
      if (!float.IsFinite(translation.Field00) ||
          !float.IsFinite(translation.Field04) ||
          !float.IsFinite(translation.Field08))
        throw Invalid(model, animation,
          $"animated bone {animatedIndex} has a non-finite frame-zero translation");
    }
    var rotationModelIndices = new int[fullCount];
    foreach (var fullIndex in Enumerable.Range(0, fullCount)) {
      var name = animation.FullBoneNames[fullIndex];
      rotationModelIndices[fullIndex] = modelByName[name];
      var rotation = animation.NormalizedFourTuplesAt2C[fullIndex];
      RequireNormalized(
        new Quaternion(
          rotation.Field00,
          rotation.Field04,
          rotation.Field08,
          rotation.Field0C),
        model,
        animation,
        $"full bone {fullIndex} frame-zero quaternion");
    }
    return new(translationModelIndices, rotationModelIndices);
  }

  private static Vector3 BindTranslation(ModelBone bone) => new(
    bone.PositionQuaternion.X,
    bone.PositionQuaternion.Y,
    bone.PositionQuaternion.Z);

  private static Quaternion BindRotation(ModelBone bone) => new(
    bone.RotationQuaternion.X,
    bone.RotationQuaternion.Y,
    bone.RotationQuaternion.Z,
    bone.RotationQuaternion.W);

  private static Matrix4x4 Local(Vector3 translation, Quaternion rotation, string description) {
    var local = Matrix4x4.CreateFromQuaternion(rotation) *
      Matrix4x4.CreateTranslation(translation);
    if (!IsFinite(local))
      throw new InvalidDataException($"Cannot evaluate model animation: {description} is non-finite.");
    return local;
  }

  private static Matrix4x4[] ComposeWorldTransforms(
    IReadOnlyList<ModelBone> bones,
    IReadOnlyList<Matrix4x4> localTransforms
  ) {
    var children = Enumerable.Range(0, bones.Count).Select(_ => new List<int>()).ToArray();
    var pendingParents = new int[bones.Count];
    var ready = new Queue<int>();
    foreach (var index in Enumerable.Range(0, bones.Count)) {
      if (bones[index].Parent == ushort.MaxValue) ready.Enqueue(index);
      else {
        pendingParents[index] = 1;
        children[bones[index].Parent].Add(index);
      }
    }

    var world = new Matrix4x4[bones.Count];
    var visited = 0;
    while (ready.TryDequeue(out var index)) {
      world[index] = bones[index].Parent == ushort.MaxValue
        ? localTransforms[index]
        : localTransforms[index] * world[bones[index].Parent];
      if (!IsFinite(world[index]))
        throw new InvalidDataException(
          $"Cannot evaluate model animation: bone {index} produced a non-finite world matrix.");
      visited++;
      foreach (var child in children[index]) {
        pendingParents[child]--;
        if (pendingParents[child] == 0) ready.Enqueue(child);
      }
    }
    if (visited != bones.Count)
      throw new InvalidDataException("Cannot evaluate model animation: model skeleton has a cycle.");
    return world;
  }

  private static void RequireNormalized(
    Quaternion value,
    ModelDefinition model,
    ModelAnimationDefinition? animation,
    string description
  ) {
    if (!IsFinite(value))
      throw Invalid(model, animation, $"{description} is non-finite");
    var lengthSquared = Convert.ToDouble(value.X) * value.X +
      Convert.ToDouble(value.Y) * value.Y +
      Convert.ToDouble(value.Z) * value.Z +
      Convert.ToDouble(value.W) * value.W;
    if (Math.Abs(lengthSquared - 1.0) > QuaternionLengthSquaredTolerance)
      throw Invalid(model, animation, $"{description} is not normalized");
  }

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static bool IsFinite(Quaternion value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(
    ModelDefinition model,
    ModelAnimationDefinition? animation,
    string message
  ) => new(
    $"Cannot evaluate model animation '{animation?.Name ?? "<unknown>"}' for MDL " +
    $"'{model.Name}': {message}.");

  private sealed record FrameZeroChannels(
    int[] TranslationModelIndices,
    int[] RotationModelIndices
  );
}
