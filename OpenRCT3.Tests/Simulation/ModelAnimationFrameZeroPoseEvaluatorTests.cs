// Model Animation Frame-Zero Pose Evaluator Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class ModelAnimationFrameZeroPoseEvaluatorTests {
  private const float Epsilon = 0.0001f;

  [Test]
  public void Evaluate_ComposesAbsoluteFrameZeroLocalsAndInverseBindSkinMatrices() {
    var model = Model(
      Bone("Root", new Vector3(1f, 0f, 0f), Quaternion.Identity,
        Matrix4x4.CreateTranslation(-1f, 0f, 0f), ushort.MaxValue, 0),
      Bone("Child", new Vector3(0f, 2f, 0f), Quaternion.Identity,
        Matrix4x4.CreateTranslation(-1f, -2f, 0f), 0, 1));
    var animation = Animation(
      ["Root"],
      ["Child", "Root"],
      frameCount: 2,
      [new(3f, 0f, 0f), new(999f, 999f, 999f)],
      [
        new(0f, 0f, 0f, 1f), new(0f, 0f, 0f, 1f),
        new(1f, 0f, 0f, 0f), new(0f, 1f, 0f, 0f),
      ]);

    var pose = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Model, Is.SameAs(model));
      Assert.That(pose.Animation, Is.SameAs(animation));
      Assert.That(pose.TranslatedBoneCount, Is.EqualTo(1));
      Assert.That(pose.RotatedBoneCount, Is.EqualTo(2));
      Assert.That(pose.Bones, Has.Count.EqualTo(2));
      Assert.That(pose.Bones[0].TranslationBoneIndex, Is.EqualTo(0));
      Assert.That(pose.Bones[1].TranslationBoneIndex, Is.Null);
      Assert.That(pose.Bones[0].RotationBoneIndex, Is.EqualTo(1));
      Assert.That(pose.Bones[1].RotationBoneIndex, Is.EqualTo(0));
      AssertMatrixTranslation(pose.Bones[0].LocalTransform, new Vector3(3f, 0f, 0f));
      AssertMatrixTranslation(pose.Bones[0].WorldTransform, new Vector3(3f, 0f, 0f));
      AssertMatrixTranslation(pose.Bones[1].LocalTransform, new Vector3(0f, 2f, 0f));
      AssertMatrixTranslation(pose.Bones[1].WorldTransform, new Vector3(3f, 2f, 0f));
      AssertMatrixTranslation(pose.Bones[0].SkinTransform, new Vector3(2f, 0f, 0f));
      AssertMatrixTranslation(pose.Bones[1].SkinTransform, new Vector3(2f, 0f, 0f));
    }
  }

  [Test]
  public void Evaluate_UntrackedModelBonesRetainBindLocals() {
    var model = Model(
      Bone("Tracked", Vector3.Zero, Quaternion.Identity,
        Matrix4x4.Identity, ushort.MaxValue, 0),
      Bone("ACamChase", new Vector3(7f, 8f, 9f), Quaternion.Identity,
        Matrix4x4.CreateTranslation(-7f, -8f, -9f), ushort.MaxValue, 1));
    var animation = Animation(
      ["Tracked"],
      ["Tracked"],
      frameCount: 1,
      [new(1f, 2f, 3f)],
      [new(0f, 0f, 0f, 1f)]);

    var pose = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Bones[1].TranslationBoneIndex, Is.Null);
      Assert.That(pose.Bones[1].RotationBoneIndex, Is.Null);
      AssertMatrixTranslation(pose.Bones[1].LocalTransform, new Vector3(7f, 8f, 9f));
      AssertMatrixTranslation(pose.Bones[1].SkinTransform, Vector3.Zero);
    }
  }

  [Test]
  public void Evaluate_UsesFrameZeroXyzwQuaternionAndIgnoresLaterSamples() {
    var model = Model(Bone(
      "Root",
      Vector3.Zero,
      Quaternion.Identity,
      Matrix4x4.Identity,
      ushort.MaxValue,
      0));
    var frameZero = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
    var later = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
    var animation = Animation(
      ["Root"],
      ["Root"],
      frameCount: 2,
      [new(4f, 5f, 6f), new(100f, 200f, 300f)],
      [Tuple(frameZero), Tuple(later)]);

    var pose = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);
    var transformed = Vector3.Transform(Vector3.UnitX, pose.Bones.Single().LocalTransform);

    using (Assert.EnterMultipleScope()) {
      Assert.That(transformed.X, Is.EqualTo(4f).Within(Epsilon));
      Assert.That(transformed.Y, Is.EqualTo(6f).Within(Epsilon));
      Assert.That(transformed.Z, Is.EqualTo(6f).Within(Epsilon));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Evaluate_InstalledAdultElephantMapsExactChannelsAndRetainsCameraBones() {
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

    var pose = ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Bones, Has.Count.EqualTo(35));
      Assert.That(pose.TranslatedBoneCount, Is.EqualTo(9));
      Assert.That(pose.RotatedBoneCount, Is.EqualTo(33));
      Assert.That(pose.Bones.All(IsFinite), Is.True);
      Assert.That(
        pose.Bones.Select(item => item.Bone.Name)
          .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        Has.Length.EqualTo(35));
    }
    foreach (var cameraName in new[] { "ACamChase", "ACamHead" }) {
      var camera = pose.Bones.Single(item =>
        item.Bone.Name.Equals(cameraName, StringComparison.OrdinalIgnoreCase));
      using (Assert.EnterMultipleScope()) {
        Assert.That(camera.TranslationBoneIndex, Is.Null);
        Assert.That(camera.RotationBoneIndex, Is.Null);
      }
    }
  }

  [TestCase(MalformedPoseInput.MissingAnimatedBone)]
  [TestCase(MalformedPoseInput.DuplicateFullBone)]
  [TestCase(MalformedPoseInput.SampleCount)]
  [TestCase(MalformedPoseInput.SkeletonCycle)]
  [TestCase(MalformedPoseInput.NonFiniteBind)]
  public void Evaluate_RejectsMalformedSkeletonOrAnimation(MalformedPoseInput malformed) {
    var root = Bone(
      "Root",
      Vector3.Zero,
      Quaternion.Identity,
      Matrix4x4.Identity,
      ushort.MaxValue,
      0);
    var child = Bone(
      "Child",
      Vector3.UnitY,
      Quaternion.Identity,
      Matrix4x4.CreateTranslation(0f, -1f, 0f),
      0,
      1);
    var model = Model(root, child);
    var animation = Animation(
      ["Root"],
      ["Root", "Child"],
      frameCount: 1,
      [new(0f, 0f, 0f)],
      [new(0f, 0f, 0f, 1f), new(0f, 0f, 0f, 1f)]);
    switch (malformed) {
      case MalformedPoseInput.MissingAnimatedBone:
        animation = animation with { AnimatedBoneNames = ["Missing"] };
        break;
      case MalformedPoseInput.DuplicateFullBone:
        animation = animation with { FullBoneNames = ["Root", "Root"] };
        break;
      case MalformedPoseInput.SampleCount:
        animation = animation with { TriplesAt28 = [] };
        break;
      case MalformedPoseInput.SkeletonCycle:
        model = Model(root with { Parent = 1 }, child);
        break;
      case MalformedPoseInput.NonFiniteBind:
        model = Model(root with { Matrix = Matrix4x4.CreateTranslation(float.NaN, 0f, 0f) }, child);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
    }

    Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation)));
  }

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
    "Stand",
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

  private static void AssertMatrixTranslation(Matrix4x4 matrix, Vector3 expected) {
    Assert.That(matrix.Translation.X, Is.EqualTo(expected.X).Within(Epsilon));
    Assert.That(matrix.Translation.Y, Is.EqualTo(expected.Y).Within(Epsilon));
    Assert.That(matrix.Translation.Z, Is.EqualTo(expected.Z).Within(Epsilon));
  }

  private static bool IsFinite(ModelAnimationFrameZeroBonePose pose) =>
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
    MissingAnimatedBone,
    DuplicateFullBone,
    SampleCount,
    SkeletonCycle,
    NonFiniteBind,
  }
}
