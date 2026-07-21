// Scenery Animation Resource Resolver Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryAnimationResourceResolverTests {
  [Test]
  public void Resolve_LinksSingleAutoloopToExactSerializedBanSlot() {
    var boneLod = BoneLod(
      ("Idle", 1f),
      ("Loop", 2f));
    var saved = new SceneryAnimationState(true, 1, 2.25f, false);

    var result = SceneryAnimationResourceResolver.Resolve(
      Placement(saved),
      boneLod);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status,
        Is.EqualTo(SceneryAnimationResolutionStatus.ExactSingleClipAutoLoop));
      Assert.That(result.SkipDetail, Is.Null);
      Assert.That(result.ExactClip, Is.Not.Null);
      Assert.That(result.ExactClip!.ObjectKey, Is.EqualTo("AnimatedScenery"));
      Assert.That(result.ExactClip.SavedStateIndex, Is.Zero);
      Assert.That(result.ExactClip.SavedState, Is.EqualTo(saved));
      Assert.That(result.ExactClip.BoneLod, Is.SameAs(boneLod));
      Assert.That(result.ExactClip.SerializedAnimationIndex, Is.EqualTo(1));
      Assert.That(result.ExactClip.AnimationReference, Is.EqualTo("Loop:ban"));
      Assert.That(result.ExactClip.Animation, Is.SameAs(boneLod.Animations[1]));
      Assert.That(result.ExactClip.Period, Is.EqualTo(2f));
    }
  }

  [Test]
  public void Resolve_RetainsUnsupportedSavedModesAsTypedSkips() {
    var boneLod = BoneLod(("Loop", 2f));
    var noActive = SceneryAnimationResourceResolver.Resolve(
      Placement(new SceneryAnimationState(true, 0, 0f, true)),
      boneLod);
    var weighted = SceneryAnimationResourceResolver.Resolve(
      Placement(
        new SceneryAnimationState(true, 0, 0f, false),
        new SceneryAnimationState(true, 0, 1f, false)),
      boneLod);
    var triggered = SceneryAnimationResourceResolver.Resolve(
      Placement(new SceneryAnimationState(false, 0, 0.5f, false)),
      boneLod);

    using (Assert.EnterMultipleScope()) {
      Assert.That(noActive.Status,
        Is.EqualTo(SceneryAnimationResolutionStatus.NoActiveState));
      Assert.That(weighted.Status,
        Is.EqualTo(SceneryAnimationResolutionStatus.WeightedState));
      Assert.That(weighted.SkipDetail, Does.Contain("2 non-deleted"));
      Assert.That(triggered.Status,
        Is.EqualTo(SceneryAnimationResolutionStatus.TriggeredState));
      Assert.That(noActive.ExactClip, Is.Null);
      Assert.That(weighted.ExactClip, Is.Null);
      Assert.That(triggered.ExactClip, Is.Null);
    }
  }

  [TestCase(MalformedSceneryAnimation.NullStates)]
  [TestCase(MalformedSceneryAnimation.NonFiniteTime)]
  [TestCase(MalformedSceneryAnimation.AnimationBelowRange)]
  [TestCase(MalformedSceneryAnimation.AnimationAboveRange)]
  [TestCase(MalformedSceneryAnimation.ReferenceCountMismatch)]
  [TestCase(MalformedSceneryAnimation.ReferenceIdentityMismatch)]
  [TestCase(MalformedSceneryAnimation.NonPositivePeriod)]
  public void Resolve_ReturnsTypedMalformedSkip(MalformedSceneryAnimation malformed) {
    var placement = Placement(new SceneryAnimationState(true, 0, 0.25f, false));
    var boneLod = BoneLod(("Loop", 2f));
    switch (malformed) {
      case MalformedSceneryAnimation.NullStates:
        placement.AnimationStates = null!;
        break;
      case MalformedSceneryAnimation.NonFiniteTime:
        placement.AnimationStates = [
          new SceneryAnimationState(true, 0, float.NaN, false)
        ];
        break;
      case MalformedSceneryAnimation.AnimationBelowRange:
        placement.AnimationStates = [
          new SceneryAnimationState(true, -1, 0f, false)
        ];
        break;
      case MalformedSceneryAnimation.AnimationAboveRange:
        placement.AnimationStates = [
          new SceneryAnimationState(true, 1, 0f, false)
        ];
        break;
      case MalformedSceneryAnimation.ReferenceCountMismatch:
        boneLod = boneLod with { Animations = [] };
        break;
      case MalformedSceneryAnimation.ReferenceIdentityMismatch:
        boneLod = boneLod with { Animations = [Animation("Other", 2f)] };
        break;
      case MalformedSceneryAnimation.NonPositivePeriod:
        boneLod = boneLod with { Animations = [Animation("Loop", 0f)] };
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed));
    }

    var result = SceneryAnimationResourceResolver.Resolve(placement, boneLod);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status,
        Is.EqualTo(SceneryAnimationResolutionStatus.MalformedState));
      Assert.That(result.ExactClip, Is.Null);
      Assert.That(result.SkipDetail, Is.Not.Null.And.Not.Empty);
    }
  }

  private static SceneryPlacement Placement(params SceneryAnimationState[] states) =>
    new("AnimatedScenery", 2, 3) { AnimationStates = states };

  private static ResolvedSceneryBoneLod BoneLod(
    params (string Name, float Period)[] clips
  ) {
    var animations = clips.Select(clip => Animation(clip.Name, clip.Period)).ToArray();
    var lod = new SceneryItemVisualLod(
      "bone",
      SvdLodType.BoneShape,
      null,
      "AnimatedScenery:bsh",
      null,
      null,
      new(0f, 0f, 0f, 0f, 0f, 0f),
      100f,
      clips.Select(clip => $"{clip.Name}:ban").ToArray());
    var visual = new SceneryItemVisual(
      "AnimatedScenery",
      0,
      0f,
      1f,
      0f,
      1f,
      [lod],
      null);
    var shape = new BoneShape(
      "AnimatedScenery",
      Vector3.Zero,
      Vector3.One,
      [],
      []);
    return new(visual, lod, shape) { Animations = animations };
  }

  private static BoneAnimation Animation(string name, float period) =>
    new(name, period, []);

  public enum MalformedSceneryAnimation {
    NullStates,
    NonFiniteTime,
    AnimationBelowRange,
    AnimationAboveRange,
    ReferenceCountMismatch,
    ReferenceIdentityMismatch,
    NonPositivePeriod,
  }
}
