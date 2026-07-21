// Scenery Animation Controller Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryAnimationControllerTests {
  [Test]
  public void CreateAndAdvance_WrapSavedAndElapsedTimeByExactBanPeriod() {
    var controller = SceneryAnimationController.Create(Resolution(2.25f, 2f));

    var first = controller.Advance(0.5f);
    var second = controller.Advance(1.5f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(first.PreviousTime, Is.EqualTo(0.25f));
      Assert.That(first.CurrentTime, Is.EqualTo(0.75f));
      Assert.That(first.Wrapped, Is.False);
      Assert.That(second.PreviousTime, Is.EqualTo(0.75f));
      Assert.That(second.CurrentTime, Is.EqualTo(0.25f));
      Assert.That(second.Wrapped, Is.True);
      Assert.That(controller.CurrentTime, Is.EqualTo(0.25f));
    }
  }

  [Test]
  public void Create_WrapsNegativeSavedTimeWithoutDiscardingPlanEvidence() {
    var resolution = Resolution(-0.25f, 2f);

    var controller = SceneryAnimationController.Create(resolution);

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.Plan, Is.SameAs(resolution.ExactClip));
      Assert.That(controller.Plan.SavedState.CurrentAnimationTime, Is.EqualTo(-0.25f));
      Assert.That(controller.CurrentTime, Is.EqualTo(1.75f));
    }
  }

  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  [TestCase(-0.01f)]
  public void Advance_RejectsInvalidElapsedTime(float elapsed) {
    var controller = SceneryAnimationController.Create(Resolution(0f, 2f));

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      controller.Advance(elapsed)));
  }

  [Test]
  public void Create_RejectsTypedSkip() {
    var resolution = new SceneryAnimationResolution(
      SceneryAnimationResolutionStatus.TriggeredState,
      null,
      "triggered");

    Assert.Throws<ArgumentException>(new Action(() =>
      SceneryAnimationController.Create(resolution)));
  }

  private static SceneryAnimationResolution Resolution(float time, float period) {
    var saved = new SceneryAnimationState(true, 0, time, false);
    var animation = new BoneAnimation("Loop", period, []);
    var lod = new SceneryItemVisualLod(
      "bone",
      SvdLodType.BoneShape,
      null,
      "AnimatedScenery:bsh",
      null,
      null,
      new(0f, 0f, 0f, 0f, 0f, 0f),
      100f,
      ["Loop:ban"]);
    var boneLod = new ResolvedSceneryBoneLod(
      new("AnimatedScenery", 0, 0f, 1f, 0f, 1f, [lod], null),
      lod,
      new("AnimatedScenery", Vector3.Zero, Vector3.One, [], [])) {
      Animations = [animation]
    };
    var plan = new SceneryAnimationClipPlan(
      "AnimatedScenery",
      0,
      saved,
      boneLod,
      0,
      "Loop:ban",
      animation);
    return new(
      SceneryAnimationResolutionStatus.ExactSingleClipAutoLoop,
      plan,
      null);
  }
}
