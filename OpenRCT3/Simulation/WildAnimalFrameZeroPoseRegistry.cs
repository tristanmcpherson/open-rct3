// Wild Animal Frame-Zero Pose Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One exact serialized WAD slot and its frame-zero pose, when resolved.</summary>
internal sealed record WildAnimalFrameZeroPoseSlotLink(
  WildAnimalModelAnimationSlotLink AnimationSlotLink,
  ModelAnimationFrameZeroPose? Pose
) {
  public int SerializedIndex => AnimationSlotLink.SerializedIndex;
  public bool IsResolved => AnimationSlotLink.IsResolved;
  public bool IsPlaceholder => AnimationSlotLink.IsPlaceholder;
}

/// <summary>One serialized WAS variant linked through all 31 WAD slots.</summary>
internal sealed record WildAnimalFrameZeroPoseVariantLink(
  WildAnimalSpeciesModelVariantLink VariantLink,
  WildAnimalModelAnimationResourceBridgeResult AnimationResources,
  IReadOnlyList<WildAnimalFrameZeroPoseSlotLink> Slots
);

/// <summary>Retains exact frame-zero poses for every serialized variant and WAD slot.</summary>
/// <remarks>
/// Bridge records and decoded resources are borrowed. Poses are reused only when both the exact
/// model-source wrapper and exact animation-source wrapper are shared. This registry does not
/// select a saved variant, interpret animation time, interpolate, or build render meshes.
/// </remarks>
internal sealed class WildAnimalFrameZeroPoseRegistry {
  private const int SerializedVariantCount = 4;
  private const int SerializedSlotCount = 31;
  private const string PlaceholderReference = ":modelanim";

  private WildAnimalFrameZeroPoseRegistry(
    IReadOnlyList<WildAnimalFrameZeroPoseVariantLink> variants,
    IReadOnlyList<ModelAnimationFrameZeroPose> poses
  ) {
    Variants = variants;
    Poses = poses;
  }

  public IReadOnlyList<WildAnimalFrameZeroPoseVariantLink> Variants { get; }
  public IReadOnlyList<ModelAnimationFrameZeroPose> Poses { get; }
  public int VariantCount => Variants.Count;
  public int PoseCount => Poses.Count;
  public int ResolvedSlotCount => Variants.Sum(variant =>
    variant.Slots.Count(slot => slot.IsResolved));
  public int PlaceholderSlotCount => Variants.Sum(variant =>
    variant.Slots.Count(slot => slot.IsPlaceholder));

  public static WildAnimalFrameZeroPoseRegistry Build(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>
      animationResources
  ) => Build(
    resources,
    animationResources,
    ModelAnimationFrameZeroPoseEvaluator.Evaluate);

  internal static WildAnimalFrameZeroPoseRegistry Build(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>
      animationResources,
    Func<ModelDefinition, ModelAnimationDefinition, ModelAnimationFrameZeroPose> evaluate
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(animationResources);
    ArgumentNullException.ThrowIfNull(evaluate);

    var variantLinks = ValidateSpeciesBridge(resources);
    var animationByReference = ValidateAnimationBridges(
      resources,
      variantLinks,
      animationResources);
    var posesByModel = new Dictionary<
      WildAnimalSpeciesModelResourceSource,
      Dictionary<WildAnimalModelAnimationResourceSource, ModelAnimationFrameZeroPose>>(
        ReferenceEqualityComparer.Instance);
    var poses = new List<ModelAnimationFrameZeroPose>();
    var variants = new WildAnimalFrameZeroPoseVariantLink[SerializedVariantCount];

    foreach (var variantIndex in Enumerable.Range(0, SerializedVariantCount)) {
      var variant = variantLinks[variantIndex];
      var animationReference = variant.Variant.AnimationDataReference;
      var animation = animationByReference[animationReference];
      var slots = new WildAnimalFrameZeroPoseSlotLink[SerializedSlotCount];
      foreach (var slotIndex in Enumerable.Range(0, SerializedSlotCount)) {
        var animationSlot = animation.Slots[slotIndex];
        ModelAnimationFrameZeroPose? pose = null;
        if (animationSlot.IsResolved) {
          var animationSource = animationSlot.Source!;
          if (!posesByModel.TryGetValue(variant.ModelSource, out var byAnimation)) {
            byAnimation = new(ReferenceEqualityComparer.Instance);
            posesByModel.Add(variant.ModelSource, byAnimation);
          }
          if (!byAnimation.TryGetValue(animationSource, out pose)) {
            pose = evaluate(variant.ModelSource.Resource, animationSource.Resource) ??
              throw Invalid(
                $"pose evaluator returned null for variant {variantIndex}, slot {slotIndex}");
            ValidatePose(variant, animationSlot, pose, variantIndex, slotIndex);
            byAnimation.Add(animationSource, pose);
            poses.Add(pose);
          }
        }
        slots[slotIndex] = new(animationSlot, pose);
      }
      variants[variantIndex] = new(
        variant,
        animation,
        Array.AsReadOnly(slots));
    }

    return new(
      Array.AsReadOnly(variants),
      Array.AsReadOnly(poses.ToArray()));
  }

  private static IReadOnlyList<WildAnimalSpeciesModelVariantLink> ValidateSpeciesBridge(
    WildAnimalSpeciesModelResourceBridgeResult resources
  ) {
    ValidateTaggedIdentity(resources.SpeciesReference, "was", "species reference");
    if (resources.SpeciesFile == null || resources.Species == null)
      throw Invalid("species bridge has a null file or decoded resource");
    var speciesName = TaggedName(resources.SpeciesReference);
    if (resources.SpeciesFile.Type != FileType.WildAnimalSpecies ||
        !Same(resources.SpeciesFile.Name, speciesName) ||
        !Same(resources.Species.Name, speciesName))
      throw Invalid("species reference, file, and decoded identity disagree");
    var speciesCommonPath = ValidateCommonPath(
      resources.SpeciesCommonPath,
      "species common path");
    var modelCommonPath = ValidateCommonPath(
      resources.ModelPackageCommonPath,
      "model package common path");
    RequirePairOwner(resources.SpeciesFile.Path, speciesCommonPath, "species file");
    if (resources.Species.Variants == null ||
        resources.Species.Variants.Count != SerializedVariantCount)
      throw Invalid("decoded species does not contain exactly four variants");
    if (resources.Variants == null || resources.Variants.Count != SerializedVariantCount)
      throw Invalid("species/model bridge does not contain exactly four variant links");

    var sourcesByIdentity = new Dictionary<string, WildAnimalSpeciesModelResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    var result = new WildAnimalSpeciesModelVariantLink[SerializedVariantCount];
    foreach (var index in Enumerable.Range(0, SerializedVariantCount)) {
      var link = resources.Variants[index];
      if (link == null || link.SerializedIndex != index)
        throw Invalid($"variant slot {index} is null, duplicated, or out of order");
      if (!ReferenceEquals(link.Variant, resources.Species.Variants[index]))
        throw Invalid($"variant slot {index} changed its exact decoded variant identity");
      ValidateTaggedIdentity(link.Variant.ModelReference, "mdl", $"variant {index} model");
      ValidateTaggedIdentity(
        link.Variant.AnimationDataReference,
        "wad",
        $"variant {index} animation data");
      ValidateModelSource(link, modelCommonPath, index);

      var identity = link.ModelSource.Identity;
      if (sourcesByIdentity.TryGetValue(identity, out var existing)) {
        if (!ReferenceEquals(existing, link.ModelSource))
          throw Invalid(
            $"variant slot {index} duplicates model identity '{identity}' with a changed source");
      } else {
        sourcesByIdentity.Add(identity, link.ModelSource);
      }
      result[index] = link;
    }
    return Array.AsReadOnly(result);
  }

  private static void ValidateModelSource(
    WildAnimalSpeciesModelVariantLink link,
    string modelCommonPath,
    int index
  ) {
    if (link.ModelSource == null || link.ModelSource.File == null ||
        link.ModelSource.Resource == null)
      throw Invalid($"variant slot {index} has a null model source");
    var source = link.ModelSource;
    var modelName = TaggedName(link.Variant.ModelReference);
    if (source.File.Type != FileType.Model ||
        !Same(source.File.Name, modelName) ||
        !Same(source.Resource.Name, modelName) ||
        !Same(source.Identity, link.Variant.ModelReference))
      throw Invalid($"variant slot {index} model reference, file, and resource disagree");
    RequirePairOwner(source.File.Path, modelCommonPath, $"variant {index} model file");
    if (!PathsEqual(source.Resource.SourcePath, source.File.Path))
      throw Invalid($"variant slot {index} decoded model changed its exact file owner");
  }

  private static IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>
    ValidateAnimationBridges(
      WildAnimalSpeciesModelResourceBridgeResult resources,
      IReadOnlyList<WildAnimalSpeciesModelVariantLink> variants,
      IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult> supplied
    ) {
    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var variant in variants)
      expected.Add(variant.Variant.AnimationDataReference);

    var indexed = new Dictionary<string, WildAnimalModelAnimationResourceBridgeResult>(
      StringComparer.OrdinalIgnoreCase);
    foreach (var pair in supplied) {
      ValidateTaggedIdentity(pair.Key, "wad", "animation bridge key");
      if (pair.Value == null)
        throw Invalid($"animation bridge '{pair.Key}' is null");
      if (!indexed.TryAdd(pair.Key, pair.Value))
        throw Invalid($"animation bridge key '{pair.Key}' is duplicated");
    }
    if (indexed.Count != expected.Count)
      throw Invalid(
        $"received {indexed.Count} animation bridges for {expected.Count} distinct variant WADs");

    foreach (var reference in expected) {
      if (!indexed.TryGetValue(reference, out var animation))
        throw Invalid($"variant WAD '{reference}' has no exact animation bridge");
      ValidateAnimationBridge(resources, reference, animation);
    }
    return indexed;
  }

  private static void ValidateAnimationBridge(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    string expectedReference,
    WildAnimalModelAnimationResourceBridgeResult animation
  ) {
    ValidateTaggedIdentity(
      animation.AnimationDataReference,
      "wad",
      $"animation bridge '{expectedReference}' reference");
    if (!Same(animation.AnimationDataReference, expectedReference))
      throw Invalid(
        $"animation bridge '{expectedReference}' changed its animation-data reference");
    if (!PathsEqual(animation.ModelPackageCommonPath, resources.ModelPackageCommonPath))
      throw Invalid(
        $"animation bridge '{expectedReference}' changed its model-package owner");
    var modelCommonPath = ValidateCommonPath(
      resources.ModelPackageCommonPath,
      "model package common path");
    var animationCommonPath = ValidateCommonPath(
      animation.AnimationPackageCommonPath,
      $"animation bridge '{expectedReference}' package path");
    if (!PathsEqual(
          Path.GetDirectoryName(animationCommonPath),
          Path.GetDirectoryName(modelCommonPath)))
      throw Invalid(
        $"animation bridge '{expectedReference}' package is not an exact model-package sibling");
    if (animation.AnimationDataFile == null || animation.AnimationData == null)
      throw Invalid($"animation bridge '{expectedReference}' has a null WAD file or resource");

    var wadName = TaggedName(expectedReference);
    if (animation.AnimationDataFile.Type != FileType.WildAnimalAnimData ||
        !Same(animation.AnimationDataFile.Name, wadName) ||
        !Same(animation.AnimationData.Name, wadName))
      throw Invalid($"animation bridge '{expectedReference}' WAD identities disagree");
    RequireExactUniqueOwner(
      animation.AnimationDataFile.Path,
      animationCommonPath,
      $"animation bridge '{expectedReference}' WAD file");
    if (!PathsEqual(animation.AnimationData.SourcePath, animation.AnimationDataFile.Path))
      throw Invalid($"animation bridge '{expectedReference}' decoded WAD changed its owner");
    if (animation.AnimationData.SerializedCountAt08 != SerializedSlotCount ||
        animation.AnimationData.ModelAnimationReferencesAt38 == null ||
        animation.AnimationData.ModelAnimationReferencesAt38.Count != SerializedSlotCount)
      throw Invalid($"animation bridge '{expectedReference}' WAD does not retain 31 slots");
    if (animation.Slots == null || animation.Slots.Count != SerializedSlotCount)
      throw Invalid($"animation bridge '{expectedReference}' does not contain 31 slot links");

    var sourcesByIdentity = new Dictionary<string, WildAnimalModelAnimationResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    foreach (var index in Enumerable.Range(0, SerializedSlotCount)) {
      var slot = animation.Slots[index];
      if (slot == null || slot.SerializedIndex != index)
        throw Invalid(
          $"animation bridge '{expectedReference}' slot {index} is null, duplicated, or out of order");
      var wadReference = animation.AnimationData.ModelAnimationReferencesAt38[index];
      if (!string.Equals(slot.Reference, wadReference, StringComparison.Ordinal))
        throw Invalid(
          $"animation bridge '{expectedReference}' slot {index} changed its serialized reference");
      ValidateAnimationSlot(
        expectedReference,
        animationCommonPath,
        slot,
        index,
        sourcesByIdentity);
    }
  }

  private static void ValidateAnimationSlot(
    string wadReference,
    string animationCommonPath,
    WildAnimalModelAnimationSlotLink slot,
    int index,
    IDictionary<string, WildAnimalModelAnimationResourceSource> sourcesByIdentity
  ) {
    if (slot.Status == WildAnimalModelAnimationSlotStatus.Placeholder) {
      if (!Same(slot.Reference, PlaceholderReference) || slot.Source != null)
        throw Invalid(
          $"animation bridge '{wadReference}' placeholder slot {index} changed its source");
      return;
    }
    if (slot.Status != WildAnimalModelAnimationSlotStatus.Resolved || slot.Source == null ||
        slot.Source.File == null || slot.Source.Resource == null)
      throw Invalid($"animation bridge '{wadReference}' resolved slot {index} has no source");

    ValidateTaggedIdentity(
      slot.Reference,
      "modelanim",
      $"animation bridge '{wadReference}' slot {index}");
    var source = slot.Source;
    var animationName = TaggedName(slot.Reference);
    if (source.File.Type != FileType.ModelAnim ||
        !Same(source.File.Name, animationName) ||
        !Same(source.Resource.Name, animationName) ||
        !Same(source.Identity, slot.Reference))
      throw Invalid(
        $"animation bridge '{wadReference}' slot {index} source identities disagree");
    RequireExactCommonOwner(
      source.File.Path,
      animationCommonPath,
      $"animation bridge '{wadReference}' slot {index} ModelAnim file");
    if (!PathsEqual(source.Resource.SourcePath, source.File.Path))
      throw Invalid(
        $"animation bridge '{wadReference}' slot {index} ModelAnim changed its owner");

    if (sourcesByIdentity.TryGetValue(source.Identity, out var existing)) {
      if (!ReferenceEquals(existing, source))
        throw Invalid(
          $"animation bridge '{wadReference}' slot {index} duplicates animation identity " +
          $"'{source.Identity}' with a changed source");
    } else {
      sourcesByIdentity.Add(source.Identity, source);
    }
  }

  private static void ValidatePose(
    WildAnimalSpeciesModelVariantLink variant,
    WildAnimalModelAnimationSlotLink animationSlot,
    ModelAnimationFrameZeroPose pose,
    int variantIndex,
    int slotIndex
  ) {
    var model = variant.ModelSource.Resource;
    var animation = animationSlot.Source!.Resource;
    if (!ReferenceEquals(pose.Model, model) || !ReferenceEquals(pose.Animation, animation))
      throw Invalid(
        $"pose evaluator changed source identity for variant {variantIndex}, slot {slotIndex}");
    if (model.Bones == null || pose.Bones == null || pose.Bones.Count != model.Bones.Count)
      throw Invalid(
        $"pose evaluator changed skeleton size for variant {variantIndex}, slot {slotIndex}");
    if (pose.TranslatedBoneCount != Convert.ToInt32(animation.AnimatedBoneCountAt18) ||
        pose.RotatedBoneCount != Convert.ToInt32(animation.FullBoneCountAt1C))
      throw Invalid(
        $"pose evaluator changed channel counts for variant {variantIndex}, slot {slotIndex}");
    foreach (var index in Enumerable.Range(0, pose.Bones.Count)) {
      var bone = pose.Bones[index];
      if (bone == null || bone.ModelBoneIndex != index ||
          !ReferenceEquals(bone.Bone, model.Bones[index]) ||
          !IsFinite(bone.LocalTransform) || !IsFinite(bone.WorldTransform) ||
          !IsFinite(bone.SkinTransform))
        throw Invalid(
          $"pose evaluator changed bone {index} for variant {variantIndex}, slot {slotIndex}");
    }
  }

  private static void ValidateTaggedIdentity(string? value, string tag, string description) {
    if (string.IsNullOrWhiteSpace(value) ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid($"{description} identity is empty or padded");
    var separator = value.IndexOf(':');
    if (separator <= 0 || separator != value.LastIndexOf(':') ||
        separator == value.Length - 1 ||
        !value[(separator + 1)..].Equals(tag, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{value}' is not one exact name:{tag} identity");
    var name = value[..separator];
    if (name is "." or ".." || name.IndexOfAny(['\\', '/', '*', '?']) >= 0)
      throw Invalid($"{description} '{value}' does not contain one bare resource name");
  }

  private static string TaggedName(string value) => value[..value.LastIndexOf(':')];

  private static string ValidateCommonPath(string? path, string description) {
    if (string.IsNullOrWhiteSpace(path) ||
        !path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} is not an exact common OVL path");
    try {
      if (!Path.IsPathFullyQualified(path))
        throw Invalid($"{description} is not an exact absolute common OVL path");
      return Path.GetFullPath(path);
    } catch (Exception error) when (
      error is ArgumentException or NotSupportedException or PathTooLongException) {
      throw Invalid($"{description} is not a valid common OVL path");
    }
  }

  private static void RequirePairOwner(string? path, string commonPath, string description) {
    if (!PathsEqual(path, commonPath) && !PathsEqual(path, ToUniquePath(commonPath)))
      throw Invalid($"{description} is outside its exact declared OVL pair");
  }

  private static void RequireExactCommonOwner(
    string? path,
    string commonPath,
    string description
  ) {
    if (!PathsEqual(path, commonPath))
      throw Invalid($"{description} is not owned by its exact common archive");
  }

  private static void RequireExactUniqueOwner(
    string? path,
    string commonPath,
    string description
  ) {
    if (!PathsEqual(path, ToUniquePath(commonPath)))
      throw Invalid($"{description} is not owned by its exact unique archive");
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";

  private static bool PathsEqual(string? left, string? right) {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
    try {
      return string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        StringComparison.OrdinalIgnoreCase);
    } catch (Exception error) when (
      error is ArgumentException or NotSupportedException or PathTooLongException) {
      return false;
    }
  }

  private static bool Same(string? left, string? right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild animal frame-zero pose registry: {message}.");
}
