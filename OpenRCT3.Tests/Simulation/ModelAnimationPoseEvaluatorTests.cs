// Model Animation Pose Evaluator Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class ModelAnimationPoseEvaluatorTests {
  private const float Epsilon = 0.0001f;

  [Test]
  public void Evaluate_AtZeroMatchesReferenceProvenFrameZeroPose() {
    var model = Model(
      Bone(
        "Root",
        Vector3.UnitX,
        Quaternion.Identity,
        Matrix4x4.CreateTranslation(-1f, 0f, 0f),
        ushort.MaxValue,
        0),
      Bone(
        "Child",
        Vector3.UnitY,
        Quaternion.Identity,
        Matrix4x4.CreateTranslation(-1f, -1f, 0f),
        0,
        1));
    var animation = Animation(
      ["Root"],
      ["Child", "Root"],
      2,
      [new(3f, 4f, 5f), new(30f, 40f, 50f)],
      [
        Tuple(Quaternion.Identity), Tuple(Quaternion.Identity),
        Tuple(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f)),
        Tuple(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1f)),
      ]);

    var expected = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);
    var actual = ModelAnimationPoseEvaluator.Evaluate(
      model,
      animation,
      0f,
      10f,
      true,
      true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.NormalizedTime, Is.Zero);
      Assert.That(actual.FrameIndex, Is.Zero);
      Assert.That(actual.NextFrameIndex, Is.Zero);
      Assert.That(actual.FrameFraction, Is.Zero);
      Assert.That(actual.TranslatedBoneCount, Is.EqualTo(expected.TranslatedBoneCount));
      Assert.That(actual.RotatedBoneCount, Is.EqualTo(expected.RotatedBoneCount));
      Assert.That(actual.Bones, Has.Count.EqualTo(expected.Bones.Count));
    }
    foreach (var index in Enumerable.Range(0, actual.Bones.Count)) {
      using (Assert.EnterMultipleScope()) {
        Assert.That(
          actual.Bones[index].LocalTransform,
          Is.EqualTo(expected.Bones[index].LocalTransform));
        Assert.That(
          actual.Bones[index].WorldTransform,
          Is.EqualTo(expected.Bones[index].WorldTransform));
        Assert.That(
          actual.Bones[index].SkinTransform,
          Is.EqualTo(expected.Bones[index].SkinTransform));
      }
    }
  }

  [TestCase(0.02f, 0, 0, 0f, 0f)]
  [TestCase(0.25f, 0, 1, 0.5f, 5f)]
  [TestCase(0.49f, 1, 1, 0f, 10f)]
  [TestCase(0.625f, 1, 2, 0.25f, 12.5f)]
  [TestCase(1f, 2, 2, 0f, 20f)]
  public void EvaluateNormalized_UsesTowardZeroFramesAndNativeDeadZones(
    float normalizedTime,
    int expectedFrame,
    int expectedNextFrame,
    float expectedFraction,
    float expectedTranslation
  ) {
    var model = RootModel();
    var animation = RootAnimation(
      [Vector3.Zero, new(10f, 0f, 0f), new(20f, 0f, 0f)],
      [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity]);

    var pose = ModelAnimationPoseEvaluator.EvaluateNormalized(
      model,
      animation,
      normalizedTime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.FrameIndex, Is.EqualTo(expectedFrame));
      Assert.That(pose.NextFrameIndex, Is.EqualTo(expectedNextFrame));
      Assert.That(pose.FrameFraction, Is.EqualTo(expectedFraction).Within(Epsilon));
      Assert.That(
        pose.Bones.Single().LocalTransform.Translation.X,
        Is.EqualTo(expectedTranslation).Within(Epsilon));
    }
  }

  [Test]
  public void EvaluateNormalized_UsesNativePositiveDotPolynomial() {
    var model = RootModel();
    var next = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 3f);
    var animation = RootAnimation(
      [Vector3.Zero, Vector3.Zero],
      [Quaternion.Identity, next]);

    var pose = ModelAnimationPoseEvaluator.EvaluateNormalized(model, animation, 0.25f);
    var transformed = Vector3.Transform(
      Vector3.UnitX,
      pose.Bones.Single().LocalTransform);

    using (Assert.EnterMultipleScope()) {
      Assert.That(transformed.X, Is.EqualTo(0.8715517f).Within(Epsilon));
      Assert.That(transformed.Y, Is.EqualTo(0.4903037f).Within(Epsilon));
      Assert.That(transformed.Z, Is.Zero.Within(Epsilon));
    }
  }

  [Test]
  public void EvaluateNormalized_UsesNativeNegativeDotShortestPath() {
    var model = RootModel();
    var represented = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2f * MathF.PI / 3f);
    var negative = new Quaternion(-represented.X, -represented.Y, -represented.Z, -represented.W);
    var animation = RootAnimation(
      [Vector3.Zero, Vector3.Zero],
      [Quaternion.Identity, negative]);

    var pose = ModelAnimationPoseEvaluator.EvaluateNormalized(model, animation, 0.25f);
    var transformed = Vector3.Transform(
      Vector3.UnitX,
      pose.Bones.Single().LocalTransform);

    using (Assert.EnterMultipleScope()) {
      Assert.That(transformed.X, Is.EqualTo(MathF.Cos(MathF.PI / 6f)).Within(Epsilon));
      Assert.That(transformed.Y, Is.EqualTo(0.5f).Within(Epsilon));
      Assert.That(transformed.Z, Is.Zero.Within(Epsilon));
    }
  }

  [Test]
  public void EvaluateNormalized_UsesNativeAntipodalPerpendicularPath() {
    var model = RootModel();
    var animation = RootAnimation(
      [Vector3.Zero, Vector3.Zero],
      [Quaternion.Identity, new(0f, 0f, 0f, -1f)]);

    var pose = ModelAnimationPoseEvaluator.EvaluateNormalized(model, animation, 0.25f);
    var transformed = Vector3.Transform(
      Vector3.UnitX,
      pose.Bones.Single().LocalTransform);

    using (Assert.EnterMultipleScope()) {
      Assert.That(transformed.X, Is.Zero.Within(Epsilon));
      Assert.That(transformed.Y, Is.EqualTo(1f).Within(Epsilon));
      Assert.That(transformed.Z, Is.Zero.Within(Epsilon));
    }
  }

  [Test]
  public void EvaluateNormalized_UsesNativeYDominantAntipodalPerpendicularPath() {
    var model = RootModel();
    var current = new Quaternion(0.2f, 0.8f, 0.4f, 0.4f);
    var next = new Quaternion(-current.X, -current.Y, -current.Z, -current.W);
    var animation = RootAnimation(
      [Vector3.Zero, Vector3.Zero],
      [current, next]);
    var perpendicular = Vector3.Normalize(new(current.Y, -current.X, 0f));
    var expectedRotation = Quaternion.Slerp(
      current,
      new Quaternion(perpendicular, 0f),
      0.5f);
    var expected = Vector3.Transform(Vector3.UnitX, expectedRotation);

    var pose = ModelAnimationPoseEvaluator.EvaluateNormalized(model, animation, 0.25f);
    var actual = Vector3.Transform(
      Vector3.UnitX,
      pose.Bones.Single().LocalTransform);

    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(Epsilon));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Epsilon));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(Epsilon));
    }
  }

  [TestCase(12f, 2f, 0.2f, 2f)]
  [TestCase(10f, 0f, 0f, 0f)]
  [TestCase(-0.5f, 9.5f, 0.95f, 9.5f)]
  public void Evaluate_WrapsSavedTimeByWadPeriod(
    float inputTime,
    float expectedSavedTime,
    float expectedNormalizedTime,
    float expectedTranslation
  ) {
    var model = RootModel();
    var animation = RootAnimation(
      [Vector3.Zero, new(10f, 0f, 0f)],
      [Quaternion.Identity, Quaternion.Identity]);

    var pose = ModelAnimationPoseEvaluator.Evaluate(
      model,
      animation,
      inputTime,
      10f,
      true,
      true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Looping, Is.True);
      Assert.That(pose.Forward, Is.True);
      Assert.That(pose.SavedTime, Is.EqualTo(expectedSavedTime).Within(Epsilon));
      Assert.That(
        pose.NormalizedTime,
        Is.EqualTo(expectedNormalizedTime).Within(Epsilon));
      Assert.That(
        pose.Bones.Single().LocalTransform.Translation.X,
        Is.EqualTo(expectedTranslation).Within(Epsilon));
    }
  }

  [TestCase(MalformedPoseInput.NonFiniteSavedTime)]
  [TestCase(MalformedPoseInput.NonPositivePeriod)]
  [TestCase(MalformedPoseInput.ReversePlaybackDirection)]
  [TestCase(MalformedPoseInput.NonLoopingOutsideRange)]
  [TestCase(MalformedPoseInput.NonFiniteLaterTranslation)]
  [TestCase(MalformedPoseInput.NonNormalizedLaterRotation)]
  [TestCase(MalformedPoseInput.SkeletonCycle)]
  public void Evaluate_RejectsInvalidPlaybackOrPoseInput(MalformedPoseInput malformed) {
    var model = RootModel();
    var animation = RootAnimation(
      [Vector3.Zero, Vector3.One],
      [Quaternion.Identity, Quaternion.Identity]);
    var savedTime = 0f;
    var period = 1f;
    var loop = true;
    var forward = true;
    switch (malformed) {
      case MalformedPoseInput.NonFiniteSavedTime:
        savedTime = float.NaN;
        break;
      case MalformedPoseInput.NonPositivePeriod:
        period = 0f;
        break;
      case MalformedPoseInput.ReversePlaybackDirection:
        forward = false;
        break;
      case MalformedPoseInput.NonLoopingOutsideRange:
        savedTime = 2f;
        loop = false;
        break;
      case MalformedPoseInput.NonFiniteLaterTranslation:
        animation = animation with {
          TriplesAt28 = [new(0f, 0f, 0f), new(float.PositiveInfinity, 0f, 0f)],
        };
        break;
      case MalformedPoseInput.NonNormalizedLaterRotation:
        animation = animation with {
          NormalizedFourTuplesAt2C = [
            Tuple(Quaternion.Identity), new(0f, 0f, 0f, 2f),
          ],
        };
        break;
      case MalformedPoseInput.SkeletonCycle:
        var root = Bone(
          "Root",
          Vector3.Zero,
          Quaternion.Identity,
          Matrix4x4.Identity,
          1,
          0);
        var child = Bone(
          "Child",
          Vector3.Zero,
          Quaternion.Identity,
          Matrix4x4.Identity,
          0,
          1);
        model = Model(root, child);
        animation = Animation(
          ["Root"],
          ["Root", "Child"],
          1,
          [new(0f, 0f, 0f)],
          [Tuple(Quaternion.Identity), Tuple(Quaternion.Identity)]);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
    }

    Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationPoseEvaluator.Evaluate(
        model,
        animation,
        savedTime,
        period,
        loop,
        forward)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Evaluate_InstalledAdultElephantUsesItsExactWadSlotPeriod() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var modelPath = Path.Combine(
      root!, "WildAnimals", "elephant", "Elephant_data.common.ovl");
    var animationPath = Path.Combine(
      root!, "WildAnimals", "elephant", "elephant_anims.common.ovl");
    Assert.That(modelPath, Does.Exist, $"Installed Elephant model OVL not found: {modelPath}");
    Assert.That(
      animationPath,
      Does.Exist,
      $"Installed Elephant animation OVL not found: {animationPath}");

    using var modelOvl = Ovl.Load(modelPath);
    using var animationOvl = Ovl.Load(animationPath);
    var model = Models.Extract(modelOvl, "AdultElephant:mdl");
    var animation = ModelAnimations.Extract(animationOvl, "AdultElephantWalk:modelanim");
    var wad = WildAnimalAnimationData.Extract(animationOvl, "Elephant:wad");
    var slot = wad.ModelAnimationReferencesAt38
      .Select((reference, index) => (reference, index))
      .Single(item => item.reference.Equals(
        "AdultElephantWalk:modelanim",
        StringComparison.OrdinalIgnoreCase));
    var period = wad.ValuesAt10[slot.index];

    var pose = ModelAnimationPoseEvaluator.Evaluate(
      model,
      animation,
      period * 0.5f,
      period,
      true,
      true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(period, Is.GreaterThan(0f));
      Assert.That(float.IsFinite(period), Is.True);
      Assert.That(pose.NormalizedTime, Is.EqualTo(0.5f).Within(Epsilon));
      Assert.That(pose.Bones, Has.Count.EqualTo(35));
      Assert.That(pose.Bones.All(IsFinite), Is.True);
    }
  }

  private static ModelDefinition RootModel() => Model(Bone(
    "Root",
    Vector3.Zero,
    Quaternion.Identity,
    Matrix4x4.Identity,
    ushort.MaxValue,
    0));

  private static ModelAnimationDefinition RootAnimation(
    Vector3[] translations,
    Quaternion[] rotations
  ) => Animation(
    ["Root"],
    ["Root"],
    Convert.ToUInt32(translations.Length),
    translations.Select(value => new ModelAnimationTriple(value.X, value.Y, value.Z)).ToArray(),
    rotations.Select(Tuple).ToArray());

  private static ModelDefinition Model(params ModelBone[] bones) => new(
    "Animal",
    @"WildAnimals\Models.common.ovl",
    100,
    200,
    Convert.ToUInt32(bones.Length),
    0,
    0,
    0) {
    Bones = Array.AsReadOnly(bones),
  };

  private static ModelBone Bone(
    string name,
    Vector3 position,
    Quaternion rotation,
    Matrix4x4 inverseBindWorld,
    ushort parent,
    ushort number
  ) => new(
    name,
    new Vector4(position, 1f),
    new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
    inverseBindWorld,
    parent,
    number);

  private static ModelAnimationDefinition Animation(
    string[] animatedNames,
    string[] fullNames,
    uint frameCount,
    ModelAnimationTriple[] translations,
    ModelAnimationFourTuple[] rotations
  ) => new(
    "Walk",
    @"WildAnimals\Animations.common.ovl",
    300,
    Convert.ToSingle(frameCount - 1) / 30f,
    frameCount,
    400,
    500,
    Convert.ToUInt32(animatedNames.Length),
    Convert.ToUInt32(fullNames.Length),
    600,
    700,
    Array.AsReadOnly(new uint[animatedNames.Length]),
    Array.AsReadOnly(new uint[fullNames.Length]),
    Array.AsReadOnly(translations),
    Array.AsReadOnly(rotations),
    Array.AsReadOnly(animatedNames),
    Array.AsReadOnly(fullNames));

  private static ModelAnimationFourTuple Tuple(Quaternion value) => new(
    value.X,
    value.Y,
    value.Z,
    value.W);

  private static bool IsFinite(ModelAnimationBonePose pose) =>
    IsFinite(pose.LocalTransform) &&
    IsFinite(pose.WorldTransform) &&
    IsFinite(pose.SkinTransform);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  public enum MalformedPoseInput {
    NonFiniteSavedTime,
    NonPositivePeriod,
    ReversePlaybackDirection,
    NonLoopingOutsideRange,
    NonFiniteLaterTranslation,
    NonNormalizedLaterRotation,
    SkeletonCycle,
  }
}
