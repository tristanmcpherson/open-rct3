// Model Animation Pose Evaluator
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One model-order bone pose sampled from a ModelAnim timeline.</summary>
internal sealed record ModelAnimationBonePose(
  int ModelBoneIndex,
  ModelBone Bone,
  int? TranslationBoneIndex,
  int? RotationBoneIndex,
  Matrix4x4 LocalTransform,
  Matrix4x4 WorldTransform,
  Matrix4x4 SkinTransform
);

/// <summary>One native-compatible ModelAnim pose and its selected frame interval.</summary>
internal sealed record ModelAnimationPose(
  ModelDefinition Model,
  ModelAnimationDefinition Animation,
  float SavedTime,
  float WadPeriod,
  bool Looping,
  bool Forward,
  float NormalizedTime,
  int FrameIndex,
  int NextFrameIndex,
  float FrameFraction,
  IReadOnlyList<ModelAnimationBonePose> Bones,
  int TranslatedBoneCount,
  int RotatedBoneCount
);

/// <summary>Samples one decoded ModelAnim using Complete Edition playback semantics.</summary>
/// <remarks>
/// Complete Edition adds saved Wild-animal time directly to the engine delta at
/// <c>0x00DED183..0x00DED18D</c>, wraps it by the matching WAD +0x10 slot value at
/// <c>0x00DED193..0x00DED1F1</c>, and divides by that same value before setting controller time at
/// <c>0x00DECA8E</c>. The controller selects frames at <c>0x00F10E49</c> with MXCSR
/// round-toward-zero, equivalent to floor for its non-negative normalized range, and snaps within
/// 0.05 of either endpoint. Translation is linear. Rotation uses the native positive-dot
/// normalized polynomial path and its negative-dot trigonometric/antipodal path.
/// </remarks>
internal static class ModelAnimationPoseEvaluator {
  private const float LowerFrameDeadZone = 0.05f;
  private const float UpperFrameDeadZone = 0.95f;
  private const float PositiveDotScale = 0.9f;
  private const float PositiveDotCorrection = 0.45f;
  private const float MinimumNormalFloat = 1.17549435E-38f;
  private const double QuaternionLengthSquaredTolerance = 0.0001;

  public static ModelAnimationPose Evaluate(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    float savedTime,
    float wadPeriod,
    bool loop,
    bool forward
  ) {
    ArgumentNullException.ThrowIfNull(model);
    ArgumentNullException.ThrowIfNull(animation);
    if (!forward)
      throw Invalid(model, animation, "reverse playback direction is not executable-proven");
    if (!float.IsFinite(savedTime))
      throw Invalid(model, animation, "saved time is non-finite");
    if (!float.IsFinite(wadPeriod) || wadPeriod <= 0f)
      throw Invalid(model, animation, "WAD playback period is non-finite or not positive");

    var evaluatedSavedTime = savedTime;
    var normalizedTime = evaluatedSavedTime / wadPeriod;
    if (!float.IsFinite(normalizedTime))
      throw Invalid(model, animation, "saved time divided by WAD period is non-finite");
    if (!loop && (normalizedTime < 0f || normalizedTime > 1f))
      throw Invalid(
        model,
        animation,
        "non-looping controller is outside [0, 1] and would be destroyed natively");
    if (loop && (normalizedTime < 0f || normalizedTime >= 1f)) {
      var wholePeriods = MathF.Floor(normalizedTime);
      evaluatedSavedTime -= wholePeriods * wadPeriod;
      normalizedTime = evaluatedSavedTime / wadPeriod;
      if (!float.IsFinite(normalizedTime) || normalizedTime < 0f || normalizedTime >= 1f)
        throw Invalid(model, animation, "native WAD period wrapping left the playback range");
    }
    return EvaluateNormalized(
      model,
      animation,
      evaluatedSavedTime,
      wadPeriod,
      loop,
      forward,
      normalizedTime);
  }

  internal static ModelAnimationPose EvaluateNormalized(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    float normalizedTime
  ) => EvaluateNormalized(
    model,
    animation,
    normalizedTime,
    1f,
    true,
    true,
    normalizedTime);

  private static ModelAnimationPose EvaluateNormalized(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    float savedTime,
    float wadPeriod,
    bool loop,
    bool forward,
    float normalizedTime
  ) {
    ArgumentNullException.ThrowIfNull(model);
    ArgumentNullException.ThrowIfNull(animation);
    if (!float.IsFinite(normalizedTime) || normalizedTime < 0f || normalizedTime > 1f)
      throw Invalid(model, animation, "normalized time is outside [0, 1] or non-finite");

    var frameZero = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);
    ValidateAllSamples(model, animation);
    var selection = SelectFrames(model, animation, normalizedTime);
    var translations = new Vector3[frameZero.Bones.Count];
    var rotations = new Quaternion[frameZero.Bones.Count];
    foreach (var pose in frameZero.Bones) {
      var modelBoneIndex = pose.ModelBoneIndex;
      translations[modelBoneIndex] = pose.TranslationBoneIndex.HasValue
        ? SampleTranslation(
          animation,
          pose.TranslationBoneIndex.Value,
          selection)
        : BindTranslation(pose.Bone);
      rotations[modelBoneIndex] = pose.RotationBoneIndex.HasValue
        ? SampleRotation(
          animation,
          pose.RotationBoneIndex.Value,
          selection)
        : BindRotation(pose.Bone);
    }

    var localTransforms = new Matrix4x4[frameZero.Bones.Count];
    foreach (var index in Enumerable.Range(0, frameZero.Bones.Count))
      localTransforms[index] = Local(
        translations[index],
        rotations[index],
        model,
        animation,
        $"model bone {index} '{frameZero.Bones[index].Bone.Name}'");
    var bones = frameZero.Bones.Select(item => item.Bone).ToArray();
    var worldTransforms = ComposeWorldTransforms(model, animation, bones, localTransforms);
    var poses = new ModelAnimationBonePose[frameZero.Bones.Count];
    foreach (var index in Enumerable.Range(0, poses.Length)) {
      var skin = bones[index].Matrix * worldTransforms[index];
      if (!IsFinite(skin))
        throw Invalid(model, animation, $"bone {index} produced a non-finite skin matrix");
      poses[index] = new(
        index,
        bones[index],
        frameZero.Bones[index].TranslationBoneIndex,
        frameZero.Bones[index].RotationBoneIndex,
        localTransforms[index],
        worldTransforms[index],
        skin);
    }
    return new(
      model,
      animation,
      savedTime,
      wadPeriod,
      loop,
      forward,
      normalizedTime,
      selection.FrameIndex,
      selection.NextFrameIndex,
      selection.Fraction,
      Array.AsReadOnly(poses),
      frameZero.TranslatedBoneCount,
      frameZero.RotatedBoneCount);
  }

  private static FrameSelection SelectFrames(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    float normalizedTime
  ) {
    var lastFrame = Convert.ToInt32(animation.FrameCountAt04 - 1);
    var framePosition = Convert.ToSingle(lastFrame) * normalizedTime;
    var frameIndex = Convert.ToInt32(MathF.Truncate(framePosition));
    var fraction = framePosition - Convert.ToSingle(frameIndex);
    if (fraction < LowerFrameDeadZone) fraction = 0f;
    else if (fraction > UpperFrameDeadZone) {
      frameIndex++;
      fraction = 0f;
    }
    if (frameIndex < 0 || frameIndex > lastFrame)
      throw Invalid(model, animation, "native frame selection left the decoded frame range");
    var nextFrameIndex = fraction == 0f ? frameIndex : frameIndex + 1;
    if (nextFrameIndex > lastFrame)
      throw Invalid(model, animation, "native interpolation requires a missing next frame");
    return new(frameIndex, nextFrameIndex, fraction);
  }

  private static Vector3 SampleTranslation(
    ModelAnimationDefinition animation,
    int channelIndex,
    FrameSelection selection
  ) {
    var channelCount = Convert.ToInt32(animation.AnimatedBoneCountAt18);
    var first = animation.TriplesAt28[checked(
      selection.FrameIndex * channelCount + channelIndex)];
    var current = new Vector3(first.Field00, first.Field04, first.Field08);
    if (selection.Fraction == 0f) return current;
    var second = animation.TriplesAt28[checked(
      selection.NextFrameIndex * channelCount + channelIndex)];
    return new(
      Interpolate(first.Field00, second.Field00, selection.Fraction),
      Interpolate(first.Field04, second.Field04, selection.Fraction),
      Interpolate(first.Field08, second.Field08, selection.Fraction));
  }

  private static Quaternion SampleRotation(
    ModelAnimationDefinition animation,
    int channelIndex,
    FrameSelection selection
  ) {
    var channelCount = Convert.ToInt32(animation.FullBoneCountAt1C);
    var first = animation.NormalizedFourTuplesAt2C[checked(
      selection.FrameIndex * channelCount + channelIndex)];
    var current = Quaternion(first);
    if (selection.Fraction == 0f) return current;
    var second = animation.NormalizedFourTuplesAt2C[checked(
      selection.NextFrameIndex * channelCount + channelIndex)];
    return InterpolateRotation(current, Quaternion(second), selection.Fraction);
  }

  private static float Interpolate(float current, float next, float fraction) {
    var value = next - current;
    value *= fraction;
    value += current;
    return value;
  }

  private static Quaternion InterpolateRotation(
    Quaternion current,
    Quaternion next,
    float fraction
  ) {
    var dot = current.X * next.X;
    dot += current.Y * next.Y;
    dot += current.Z * next.Z;
    dot += current.W * next.W;
    return dot < 0f
      ? InterpolateNegativeDot(current, next, fraction, dot)
      : InterpolatePositiveDot(current, next, fraction, dot);
  }

  private static Quaternion InterpolatePositiveDot(
    Quaternion current,
    Quaternion next,
    float fraction,
    float dot
  ) {
    var correction = dot * PositiveDotScale;
    correction = 1f - correction;
    correction *= correction;
    correction *= PositiveDotCorrection;
    var twiceCorrection = correction + correction;
    var factor = -correction - twiceCorrection;
    factor += twiceCorrection * fraction;
    factor *= fraction;
    factor += 1f + correction;
    factor *= fraction;

    var x = Interpolate(current.X, next.X, factor);
    var y = Interpolate(current.Y, next.Y, factor);
    var z = Interpolate(current.Z, next.Z, factor);
    var w = Interpolate(current.W, next.W, factor);
    var lengthSquared = x * x;
    lengthSquared += y * y;
    lengthSquared += z * z;
    lengthSquared += w * w;
    var length = NativeSqrt(lengthSquared);
    if (length <= MinimumNormalFloat) return System.Numerics.Quaternion.Identity;
    var inverseLength = 1f / length;
    return new(
      x * inverseLength,
      y * inverseLength,
      z * inverseLength,
      w * inverseLength);
  }

  private static Quaternion InterpolateNegativeDot(
    Quaternion current,
    Quaternion next,
    float fraction,
    float dot
  ) {
    var antipodalDistance = dot + 1f;
    if (!(MinimumNormalFloat < antipodalDistance)) {
      var perpendicular = Perpendicular(new(current.X, current.Y, current.Z));
      var orthogonal = new Quaternion(perpendicular, 0f);
      if (fraction < 0.5f)
        return NativeSlerp(current, orthogonal, fraction + fraction);
      var secondHalf = fraction - 0.5f;
      return NativeSlerp(orthogonal, next, secondHalf + secondHalf);
    }
    return NativeSlerp(current, next, fraction);
  }

  private static Quaternion NativeSlerp(
    Quaternion current,
    Quaternion next,
    float fraction
  ) {
    var dot = current.X * next.X;
    dot += current.Y * next.Y;
    dot += current.Z * next.Z;
    dot += current.W * next.W;
    var distance = 1f - dot;
    float currentWeight;
    float nextWeight;
    if (!(MinimumNormalFloat < distance)) {
      currentWeight = 1f - fraction;
      nextWeight = fraction;
    } else {
      var angle = NativeAcos(dot < 0f ? -dot : dot);
      var inverseSine = 1f / NativeSin(angle);
      currentWeight = 1f - fraction;
      currentWeight *= angle;
      currentWeight = NativeSin(currentWeight);
      currentWeight *= inverseSine;
      nextWeight = angle * fraction;
      nextWeight = NativeSin(nextWeight);
      nextWeight *= inverseSine;
      if (dot < 0f) nextWeight = -nextWeight;
    }
    return new(
      next.X * nextWeight + current.X * currentWeight,
      next.Y * nextWeight + current.Y * currentWeight,
      next.Z * nextWeight + current.Z * currentWeight,
      next.W * nextWeight + current.W * currentWeight);
  }

  private static Vector3 Perpendicular(Vector3 value) {
    var absoluteX = MathF.Abs(value.X);
    var absoluteY = MathF.Abs(value.Y);
    var absoluteZ = MathF.Abs(value.Z);
    Vector3 perpendicular;
    if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
      perpendicular = new(-value.Z, 0f, value.X);
    else if (absoluteY >= absoluteZ)
      perpendicular = new(value.Y, -value.X, 0f);
    else perpendicular = new(0f, value.Z, -value.Y);

    var lengthSquared = perpendicular.X * perpendicular.X;
    lengthSquared += perpendicular.Y * perpendicular.Y;
    lengthSquared += perpendicular.Z * perpendicular.Z;
    var length = NativeSqrt(lengthSquared);
    if (length == 0f) return Vector3.UnitZ;
    var inverseLength = 1f / length;
    return perpendicular * inverseLength;
  }

  private static float NativeAcos(float value) =>
    Convert.ToSingle(Math.Acos(Convert.ToDouble(value)));

  private static float NativeSin(float value) =>
    Convert.ToSingle(Math.Sin(Convert.ToDouble(value)));

  private static float NativeSqrt(float value) =>
    Convert.ToSingle(Math.Sqrt(Convert.ToDouble(value)));

  private static void ValidateAllSamples(
    ModelDefinition model,
    ModelAnimationDefinition animation
  ) {
    foreach (var indexed in animation.TriplesAt28.Select((value, index) => (value, index))) {
      if (!float.IsFinite(indexed.value.Field00) ||
          !float.IsFinite(indexed.value.Field04) ||
          !float.IsFinite(indexed.value.Field08))
        throw Invalid(
          model,
          animation,
          $"translation sample {indexed.index} is non-finite");
    }
    foreach (var indexed in animation.NormalizedFourTuplesAt2C.Select(
               (value, index) => (value, index)))
      RequireNormalized(
        Quaternion(indexed.value),
        model,
        animation,
        $"rotation sample {indexed.index}");
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

  private static Quaternion Quaternion(ModelAnimationFourTuple value) => new(
    value.Field00,
    value.Field04,
    value.Field08,
    value.Field0C);

  private static Matrix4x4 Local(
    Vector3 translation,
    Quaternion rotation,
    ModelDefinition model,
    ModelAnimationDefinition animation,
    string description
  ) {
    if (!IsFinite(rotation))
      throw Invalid(model, animation, $"{description} rotation is non-finite");
    var local = Matrix4x4.CreateFromQuaternion(rotation) *
      Matrix4x4.CreateTranslation(translation);
    if (!IsFinite(local))
      throw Invalid(model, animation, $"{description} is non-finite");
    return local;
  }

  private static Matrix4x4[] ComposeWorldTransforms(
    ModelDefinition model,
    ModelAnimationDefinition animation,
    IReadOnlyList<ModelBone> bones,
    IReadOnlyList<Matrix4x4> localTransforms
  ) {
    var children = Enumerable.Range(0, bones.Count).Select(_ => new List<int>()).ToArray();
    var ready = new Queue<int>();
    foreach (var index in Enumerable.Range(0, bones.Count)) {
      if (bones[index].Parent == ushort.MaxValue) ready.Enqueue(index);
      else children[bones[index].Parent].Add(index);
    }

    var world = new Matrix4x4[bones.Count];
    var visited = 0;
    while (ready.TryDequeue(out var index)) {
      world[index] = bones[index].Parent == ushort.MaxValue
        ? localTransforms[index]
        : localTransforms[index] * world[bones[index].Parent];
      if (!IsFinite(world[index]))
        throw Invalid(model, animation, $"bone {index} produced a non-finite world matrix");
      visited++;
      foreach (var child in children[index]) ready.Enqueue(child);
    }
    if (visited != bones.Count)
      throw Invalid(model, animation, "model skeleton has a cycle");
    return world;
  }

  private static void RequireNormalized(
    Quaternion value,
    ModelDefinition model,
    ModelAnimationDefinition animation,
    string description
  ) {
    if (!IsFinite(value))
      throw Invalid(model, animation, $"{description} is non-finite");
    var lengthSquared = Convert.ToDouble(value.X) * value.X;
    lengthSquared += Convert.ToDouble(value.Y) * value.Y;
    lengthSquared += Convert.ToDouble(value.Z) * value.Z;
    lengthSquared += Convert.ToDouble(value.W) * value.W;
    if (Math.Abs(lengthSquared - 1.0) > QuaternionLengthSquaredTolerance)
      throw Invalid(model, animation, $"{description} is not normalized");
  }

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
    ModelAnimationDefinition animation,
    string message
  ) => new(
    $"Cannot evaluate model animation '{animation.Name}' for MDL '{model.Name}': {message}.");

  private sealed record FrameSelection(
    int FrameIndex,
    int NextFrameIndex,
    float Fraction
  );
}
