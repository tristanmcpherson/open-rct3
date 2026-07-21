// Scenery Animation Resource Resolver
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The exact level of general-scenery animation playback proven by saved state.</summary>
internal enum SceneryAnimationResolutionStatus {
  NoActiveState,
  ExactSingleClipAutoLoop,
  WeightedState,
  TriggeredState,
  MalformedState,
}

/// <summary>One exact saved autoloop linked to its serialized SVD BAN slot.</summary>
internal sealed record SceneryAnimationClipPlan(
  string ObjectKey,
  int SavedStateIndex,
  SceneryAnimationState SavedState,
  ResolvedSceneryBoneLod BoneLod,
  int SerializedAnimationIndex,
  string AnimationReference,
  BoneAnimation Animation
) {
  public float Period => Animation.TotalTime;
}

/// <summary>A playback plan or a typed reason why the saved state cannot be played exactly.</summary>
internal sealed record SceneryAnimationResolution(
  SceneryAnimationResolutionStatus Status,
  SceneryAnimationClipPlan? ExactClip,
  string? SkipDetail
);

/// <summary>Links persisted general-scenery state to one exact SVD BAN slot when provable.</summary>
/// <remarks>
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/sceneryvisual.h">
/// Pinned rct3-importer SVD layout
/// </see>
/// and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSVD.cpp">
/// its serializer
/// </see>
/// establish that the LOD animation array is ordered and directly contains BAN references. The
/// persisted DAT fields retain an animation index, time, autoloop flag, and deletion flag. This
/// resolver only accepts one non-deleted autoloop whose index names that exact array slot. Multiple
/// retained states and non-autoloops remain typed skips because their blend and trigger behavior is
/// not established by those sources.
/// </remarks>
internal static class SceneryAnimationResourceResolver {
  public static SceneryAnimationResolution Resolve(
    SceneryPlacement placement,
    ResolvedSceneryBoneLod boneLod
  ) {
    ArgumentNullException.ThrowIfNull(boneLod);
    if (string.IsNullOrWhiteSpace(placement.ObjectKey))
      return Malformed("placement object key is empty");
    if (placement.AnimationStates == null)
      return Malformed("placement animation-state list is null");
    var malformedResources = ValidateResources(boneLod);
    if (malformedResources != null) return Malformed(malformedResources);

    var active = new List<(int Index, SceneryAnimationState State)>();
    foreach (var indexed in placement.AnimationStates.Select(
               (state, index) => (State: state, Index: index))) {
      if (!float.IsFinite(indexed.State.CurrentAnimationTime))
        return Malformed($"saved state {indexed.Index} has a non-finite time");
      if (!indexed.State.MarkedForDeletion) active.Add((indexed.Index, indexed.State));
    }

    if (active.Count == 0)
      return new(SceneryAnimationResolutionStatus.NoActiveState, null, null);
    if (active.Count > 1)
      return new(
        SceneryAnimationResolutionStatus.WeightedState,
        null,
        $"{active.Count} non-deleted saved states require unproven blend behavior");

    var selected = active[0];
    if (!selected.State.AutoLoop)
      return new(
        SceneryAnimationResolutionStatus.TriggeredState,
        null,
        "the exact saved state is not marked for autoloop playback");
    if (selected.State.CurrentAnimation < 0 ||
        selected.State.CurrentAnimation >= boneLod.Animations.Count)
      return Malformed(
        $"saved state {selected.Index} animation index " +
        $"{selected.State.CurrentAnimation} is outside the SVD BAN slot range");

    var animationIndex = selected.State.CurrentAnimation;
    var animation = boneLod.Animations[animationIndex];
    if (!float.IsFinite(animation.TotalTime) || animation.TotalTime <= 0f)
      return Malformed(
        $"BAN slot {animationIndex} has a non-finite or non-positive total time");
    var reference = boneLod.Lod.AnimationRefs[animationIndex];
    return new(
      SceneryAnimationResolutionStatus.ExactSingleClipAutoLoop,
      new(
        placement.ObjectKey,
        selected.Index,
        selected.State,
        boneLod,
        animationIndex,
        reference,
        animation),
      null);
  }

  private static string? ValidateResources(ResolvedSceneryBoneLod boneLod) {
    if (boneLod.Visual == null || boneLod.Lod == null || boneLod.Shape == null)
      return "resolved bone LOD is incomplete";
    if (boneLod.Lod.Type != SvdLodType.BoneShape)
      return "resolved LOD is not a bone-shape LOD";
    if (boneLod.Lod.AnimationRefs == null || boneLod.Animations == null)
      return "resolved bone LOD has a null animation list";
    if (boneLod.Lod.AnimationRefs.Count != boneLod.Animations.Count)
      return "resolved BAN references and decoded animations have different counts";

    foreach (var index in Enumerable.Range(0, boneLod.Animations.Count)) {
      var animation = boneLod.Animations[index];
      if (animation == null)
        return $"decoded BAN slot {index} is null";
      if (!TryParseBanReference(boneLod.Lod.AnimationRefs[index], out var name) ||
          !string.Equals(name, animation.Name, StringComparison.OrdinalIgnoreCase))
        return $"decoded BAN slot {index} changed its exact serialized reference";
    }
    return null;
  }

  private static bool TryParseBanReference(string reference, out string name) {
    name = string.Empty;
    if (string.IsNullOrWhiteSpace(reference)) return false;
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1) return false;
    name = reference[..separator];
    var tag = reference[(separator + 1)..];
    return string.Equals(name, name.Trim(), StringComparison.Ordinal) &&
      string.Equals(tag, "ban", StringComparison.OrdinalIgnoreCase);
  }

  private static SceneryAnimationResolution Malformed(string detail) =>
    new(SceneryAnimationResolutionStatus.MalformedState, null, detail);
}
