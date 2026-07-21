// Scenery Animation Controller
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>Result of deterministic advancement in decoded BAN time units.</summary>
internal readonly record struct SceneryAnimationAdvanceResult(
  float PreviousTime,
  float CurrentTime,
  bool Wrapped
);

/// <summary>Advances one exact general-scenery autoloop without guessing pose semantics.</summary>
/// <remarks>
/// The controller consumes elapsed values in the same units as the saved DAT time and BAN
/// <c>TotalTime</c>. It deliberately does not interpret those units as wall-clock seconds or sample
/// translation/rotation tracks; neither behavior is established by the pinned importer sources.
/// </remarks>
internal sealed class SceneryAnimationController {
  private readonly SceneryAnimationClipPlan plan;
  private float currentTime;

  private SceneryAnimationController(SceneryAnimationClipPlan plan) {
    this.plan = plan;
    currentTime = Wrap(plan.SavedState.CurrentAnimationTime, plan.Period);
  }

  public SceneryAnimationClipPlan Plan => plan;
  public float CurrentTime => currentTime;

  public static SceneryAnimationController Create(SceneryAnimationResolution resolution) {
    ArgumentNullException.ThrowIfNull(resolution);
    if (resolution.Status != SceneryAnimationResolutionStatus.ExactSingleClipAutoLoop ||
        resolution.ExactClip == null || resolution.SkipDetail != null)
      throw new ArgumentException(
        "A scenery animation controller requires one exact autoloop plan.",
        nameof(resolution));
    ValidatePlan(resolution.ExactClip);
    return new(resolution.ExactClip);
  }

  /// <summary>Advances by caller-supplied decoded animation-time units.</summary>
  public SceneryAnimationAdvanceResult Advance(float elapsedAnimationTime) {
    if (!float.IsFinite(elapsedAnimationTime) || elapsedAnimationTime < 0f)
      throw new ArgumentOutOfRangeException(
        nameof(elapsedAnimationTime),
        "Scenery animation advancement must be finite and nonnegative.");
    ValidatePlan(plan);
    var previous = currentTime;
    var unwrapped = Convert.ToDouble(previous) + elapsedAnimationTime;
    currentTime = Wrap(unwrapped, plan.Period);
    return new(previous, currentTime, unwrapped >= plan.Period);
  }

  private static void ValidatePlan(SceneryAnimationClipPlan plan) {
    ArgumentNullException.ThrowIfNull(plan);
    if (string.IsNullOrWhiteSpace(plan.ObjectKey) || plan.BoneLod == null ||
        plan.Animation == null || !plan.SavedState.AutoLoop ||
        plan.SavedState.MarkedForDeletion ||
        !float.IsFinite(plan.SavedState.CurrentAnimationTime) ||
        plan.SerializedAnimationIndex != plan.SavedState.CurrentAnimation ||
        !float.IsFinite(plan.Period) || plan.Period <= 0f)
      throw new InvalidDataException("Scenery animation plan changed exact autoloop identity.");
  }

  private static float Wrap(double value, float period) {
    if (!double.IsFinite(value) || !float.IsFinite(period) || period <= 0f)
      throw new InvalidDataException("Scenery animation time or period is invalid.");
    var wrapped = value % period;
    if (wrapped < 0d) wrapped += period;
    var result = Convert.ToSingle(wrapped);
    if (!float.IsFinite(result) || result < 0f || result >= period)
      throw new InvalidDataException("Scenery animation wrapping left the BAN time range.");
    return result;
  }
}
