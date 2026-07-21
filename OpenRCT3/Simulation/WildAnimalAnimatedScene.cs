// Wild Animal Animated Scene
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One exact saved single-clip animal and its scene-owned model batches.</summary>
internal sealed class WildAnimalAnimatedSceneEntry {
  internal WildAnimalAnimatedSceneEntry(
    WildAnimalParkPlacementResource placement,
    WildAnimalSavedVariantSelection selection,
    WildAnimalSavedAnimationResolution animationResolution,
    WildAnimalFrameZeroPoseVariantLink poseVariant,
    WildAnimalFrameZeroPoseSlotLink poseSlot,
    ModelDefinition model,
    ModelAnimationDefinition animation,
    IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> bindings,
    float savedTime,
    float period
  ) {
    Placement = placement;
    Selection = selection;
    AnimationResolution = animationResolution;
    PoseVariant = poseVariant;
    PoseSlot = poseSlot;
    Model = model;
    Animation = animation;
    Bindings = bindings;
    CurrentSavedTime = savedTime;
    Period = period;
  }

  public WildAnimalParkPlacementResource Placement { get; }
  public WildAnimalSavedVariantSelection Selection { get; }
  public WildAnimalSavedAnimationResolution AnimationResolution { get; }
  public WildAnimalFrameZeroPoseVariantLink PoseVariant { get; }
  public WildAnimalFrameZeroPoseSlotLink PoseSlot { get; }
  public ModelDefinition Model { get; }
  public ModelAnimationDefinition Animation { get; }
  public IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> Bindings { get; }
  public float Period { get; }

  // Complete Edition writes descriptor word 0x0101 for the Wild controller. Its first byte sets
  // looping and its second byte selects forward (+1) playback. WAD +0x14 is not this descriptor.
  public bool Looping => true;
  public bool Forward => true;

  public float CurrentSavedTime { get; internal set; }
  public ModelAnimationPose? CurrentPose { get; internal set; }
}

/// <summary>Owns provable animated models and retains unsupported saved states as typed skips.</summary>
/// <remarks>
/// Adoption transfers ownership of every model in the frame-zero load. Exact single clips become
/// animation entries. No-active-clip and weighted saved states remain the original typed skips;
/// this layer does not synthesize a clip or blend.
/// </remarks>
internal sealed class WildAnimalAnimatedScene : IDisposable {
  private readonly object ownershipGate = new();
  private bool disposed;
  private bool ownsModels = true;

  private WildAnimalAnimatedScene(
    WildAnimalFrameZeroSceneLoadResult source,
    IReadOnlyList<Model> models,
    IReadOnlyList<WildAnimalAnimatedSceneEntry> entries,
    IReadOnlyList<WildAnimalFrameZeroScenePlacementSkip> skippedPlacements
  ) {
    Source = source;
    Models = models;
    Entries = entries;
    SkippedPlacements = skippedPlacements;
  }

  public WildAnimalFrameZeroSceneLoadResult Source { get; }
  public IReadOnlyList<Model> Models { get; }
  public IReadOnlyList<WildAnimalAnimatedSceneEntry> Entries { get; }
  public IReadOnlyList<WildAnimalFrameZeroScenePlacementSkip> SkippedPlacements { get; }
  public int AnimatedPlacementCount => Entries.Count;
  public int AnimatedModelCount => Entries.Sum(entry => entry.Bindings.Count);
  public int SkippedPlacementCount => SkippedPlacements.Count;
  public bool IsDisposed {
    get {
      lock (ownershipGate) return disposed;
    }
  }
  public bool OwnsModels {
    get {
      lock (ownershipGate) return ownsModels;
    }
  }

  public static WildAnimalAnimatedScene Adopt(WildAnimalFrameZeroSceneLoadResult source) {
    ArgumentNullException.ThrowIfNull(source);
    var models = source.Scene?.Models;
    if (models == null)
      throw Invalid("frame-zero load has no scene model list");

    try {
      var entries = BuildEntries(source);
      var builtPlacements = entries
        .Select(entry => entry.Placement.PlacementIndex)
        .ToHashSet();
      var skips = ValidateSkips(source, builtPlacements);
      return new(
        source,
        Array.AsReadOnly(models.ToArray()),
        entries,
        skips);
    } catch (Exception primaryError) {
      var cleanupErrors = DisposeModels(models);
      if (cleanupErrors.Count != 0)
        throw new AggregateException(
          "Animated Wild-animal scene adoption failed and model cleanup also failed.",
          [primaryError, .. cleanupErrors]);
      throw;
    }
  }

  /// <summary>
  /// Relinquishes model ownership after successful publication while retaining borrowed references.
  /// </summary>
  /// <remarks>
  /// Call only after <see cref="Models"/> were published to their next owner. That owner must keep
  /// every model alive while this scene's animation controller is in use and becomes solely
  /// responsible for disposal. This scene remains usable after transfer.
  /// </remarks>
  public void RelinquishModelOwnership() {
    lock (ownershipGate) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (!ownsModels)
        throw new InvalidOperationException(
          "Animated Wild-animal scene model ownership was already transferred.");
      ownsModels = false;
    }
  }

  public void Dispose() {
    IReadOnlyList<Model>? models = null;
    lock (ownershipGate) {
      if (disposed) return;
      disposed = true;
      if (ownsModels) models = Models;
      ownsModels = false;
    }
    if (models == null) return;
    var errors = DisposeModels(models);
    if (errors.Count != 0)
      throw new AggregateException(
        "Animated Wild-animal scene model cleanup reported errors.",
        errors);
  }

  private static IReadOnlyList<WildAnimalAnimatedSceneEntry> BuildEntries(
    WildAnimalFrameZeroSceneLoadResult source
  ) {
    var scene = source.Scene ?? throw Invalid("frame-zero load has no scene result");
    if (source.Resources == null || scene.ModelBindings == null ||
        scene.SkippedPlacements == null || scene.Models == null ||
        scene.Models.Count != scene.ModelBindings.Count ||
        scene.ModelCount != scene.Models.Count)
      throw Invalid("frame-zero scene ownership or binding counts are inconsistent");
    if (scene.BuiltPlacementCount < 0 ||
        scene.BuiltPlacementCount + scene.SkippedPlacementCount !=
          scene.VisiblePlacementCount)
      throw Invalid("frame-zero placement counts are inconsistent");

    var modelSet = new HashSet<Model>(ReferenceEqualityComparer.Instance);
    var meshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var materialSet = new HashSet<Material>(ReferenceEqualityComparer.Instance);
    var bindingsByPlacement = new Dictionary<
      int,
      List<WildAnimalFrameZeroSceneModelBinding>>();
    foreach (var index in Enumerable.Range(0, scene.ModelBindings.Count)) {
      var model = scene.Models[index] ?? throw Invalid($"scene model {index} is null");
      var binding = scene.ModelBindings[index] ??
        throw Invalid($"scene model binding {index} is null");
      ValidateModelBinding(model, binding, index, modelSet, meshSet, materialSet);
      var placementIndex = binding.Placement.PlacementIndex;
      if (!bindingsByPlacement.TryGetValue(placementIndex, out var bindings))
        bindingsByPlacement.Add(placementIndex, bindings = []);
      bindings.Add(binding);
    }

    if (bindingsByPlacement.Count != scene.BuiltPlacementCount)
      throw Invalid("animated placement count differs from the frame-zero build result");
    var entries = new List<WildAnimalAnimatedSceneEntry>(bindingsByPlacement.Count);
    foreach (var pair in bindingsByPlacement.OrderBy(pair => pair.Key))
      entries.Add(BuildEntry(source, pair.Key, pair.Value));
    return Array.AsReadOnly(entries.ToArray());
  }

  private static WildAnimalAnimatedSceneEntry BuildEntry(
    WildAnimalFrameZeroSceneLoadResult source,
    int placementIndex,
    IReadOnlyList<WildAnimalFrameZeroSceneModelBinding> bindings
  ) {
    if (placementIndex < 0 || placementIndex >= source.Resources.Placements.Count ||
        bindings.Count == 0)
      throw Invalid($"animated placement {placementIndex} is missing or out of range");
    var first = bindings[0];
    if (!ReferenceEquals(first.Placement, source.Resources.Placements[placementIndex]))
      throw Invalid($"animated placement {placementIndex} changed park-resource identity");
    var exact = first.Animation.ExactSingleClipEntry;
    if (first.Animation.Status != WildAnimalSavedAnimationResolutionStatus.ExactSingleClip ||
        exact == null || exact.SavedEntry.Weight != 1f)
      throw Invalid($"animated placement {placementIndex} is not one exact full-weight clip");
    var slotIndex = exact.SavedEntry.Type;
    var animationData = first.PoseVariant.AnimationResources.AnimationData;
    if (slotIndex < 0 || slotIndex >= 31 || animationData?.ValuesAt10 == null ||
        animationData.ValuesAt10.Count != 31)
      throw Invalid($"animated placement {placementIndex} has no exact WAD period slot");
    var period = animationData.ValuesAt10[slotIndex];
    if (!float.IsFinite(period) || period <= 0f)
      throw Invalid(
        $"animated placement {placementIndex} WAD period is non-finite or not positive");
    if (!float.IsFinite(exact.SavedEntry.Time))
      throw Invalid($"animated placement {placementIndex} saved time is non-finite");
    var model = first.Selection.Variant.Template.ModelSource.Resource;
    var animation = exact.Slot.Source?.Resource;
    if (model == null || animation == null ||
        !ReferenceEquals(first.PoseSlot.AnimationSlotLink, exact.Slot) ||
        !ReferenceEquals(first.PoseSlot.Pose?.Model, model) ||
        !ReferenceEquals(first.PoseSlot.Pose?.Animation, animation))
      throw Invalid($"animated placement {placementIndex} changed MDL or ModelAnim identity");

    foreach (var index in Enumerable.Range(0, bindings.Count)) {
      var binding = bindings[index];
      if (!ReferenceEquals(binding.Placement, first.Placement) ||
          !ReferenceEquals(binding.Selection, first.Selection) ||
          !ReferenceEquals(binding.Animation, first.Animation) ||
          !ReferenceEquals(binding.PoseVariant, first.PoseVariant) ||
          !ReferenceEquals(binding.PoseSlot, first.PoseSlot) ||
          binding.MaterialBatchIndex != index)
        throw Invalid(
          $"animated placement {placementIndex} batch {index} changed exact source identity");
    }
    return new(
      first.Placement,
      first.Selection,
      first.Animation,
      first.PoseVariant,
      first.PoseSlot,
      model,
      animation,
      Array.AsReadOnly(bindings.ToArray()),
      exact.SavedEntry.Time,
      period);
  }

  private static void ValidateModelBinding(
    Model model,
    WildAnimalFrameZeroSceneModelBinding binding,
    int index,
    ISet<Model> models,
    ISet<Mesh> meshes,
    ISet<Material> materials
  ) {
    if (!ReferenceEquals(binding.Model, model) || binding.SkinnedBatch?.Mesh == null ||
        !ReferenceEquals(model.Mesh, binding.SkinnedBatch.Mesh) || model.Material == null ||
        model.Mesh.State == State.Disposed || model.Material.State == State.Disposed ||
        !models.Add(model) || !meshes.Add(model.Mesh) || !materials.Add(model.Material))
      throw Invalid($"scene model binding {index} changed exact ownership identity");
  }

  private static IReadOnlyList<WildAnimalFrameZeroScenePlacementSkip> ValidateSkips(
    WildAnimalFrameZeroSceneLoadResult source,
    IReadOnlySet<int> builtPlacements
  ) {
    var scene = source.Scene;
    if (source.Resources?.Placements == null ||
        source.Resources.SpeciesResources == null ||
        source.SpeciesProvenance == null ||
        source.SpeciesProvenance.Count != source.Resources.SpeciesResources.Count ||
        scene.SkippedPlacements.Count != scene.SkippedPlacementCount)
      throw Invalid("typed skip count differs from the frame-zero build result");
    var placements = new HashSet<int>();
    foreach (var skip in scene.SkippedPlacements) {
      if (skip?.Placement == null || skip.Selection == null || skip.Animation == null ||
          !placements.Add(skip.Placement.PlacementIndex))
        throw Invalid("typed skip list contains null or duplicate placement evidence");
      var placementIndex = skip.Placement.PlacementIndex;
      if (placementIndex < 0 || placementIndex >= source.Resources.Placements.Count ||
          !ReferenceEquals(
            skip.Placement,
            source.Resources.Placements[placementIndex]))
        throw Invalid("typed skip list changed exact park-placement identity");
      if (builtPlacements.Contains(placementIndex))
        throw Invalid($"typed skip placement {placementIndex} is also a built placement");
      ValidateSkipEvidence(source, skip, placementIndex);
      var expectedStatus = skip.Reason switch {
        WildAnimalFrameZeroSceneSkipReason.NoActiveClip =>
          WildAnimalSavedAnimationResolutionStatus.NoActiveClip,
        WildAnimalFrameZeroSceneSkipReason.WeightedState =>
          WildAnimalSavedAnimationResolutionStatus.WeightedState,
        _ => throw Invalid("typed skip list contains an unknown reason"),
      };
      var activeEntries = skip.Animation.Entries.Count(entry => entry?.IsActive == true);
      var statusEvidenceMatches = expectedStatus switch {
        WildAnimalSavedAnimationResolutionStatus.NoActiveClip =>
          activeEntries == 0 && skip.Animation.ExactSingleClipEntry == null,
        WildAnimalSavedAnimationResolutionStatus.WeightedState =>
          activeEntries > 0 && skip.Animation.ExactSingleClipEntry == null &&
          !(activeEntries == 1 &&
            skip.Animation.Entries.Any(entry =>
              entry?.IsActive == true && entry.SavedEntry.Weight == 1f)),
        _ => false,
      };
      if (skip.Animation.Status != expectedStatus || !statusEvidenceMatches)
        throw Invalid(
          $"typed skip placement {skip.Placement.PlacementIndex} changed saved status");
    }
    return Array.AsReadOnly(scene.SkippedPlacements.ToArray());
  }

  private static void ValidateSkipEvidence(
    WildAnimalFrameZeroSceneLoadResult source,
    WildAnimalFrameZeroScenePlacementSkip skip,
    int placementIndex
  ) {
    var placement = skip.Placement;
    var saved = placement.Placement;
    if (saved?.Animal == null || saved.Visual == null || !saved.Visual.Visible ||
        placement.SpeciesResource == null ||
        !ReferenceEquals(skip.Selection.Animal, saved.Animal) ||
        skip.Selection.SerializedVariantIndex != saved.Animal.Type ||
        skip.Selection.Variant?.VariantLink?.Variant == null ||
        skip.Selection.Variant.Template?.ModelSource == null ||
        !ReferenceEquals(
          skip.Selection.Variant.Template.ModelSource,
          skip.Selection.Variant.VariantLink.ModelSource) ||
        skip.Animation.Resources == null)
      throw Invalid(
        $"typed skip placement {placementIndex} changed selection, visual, or visibility evidence");
    var speciesIndex = placement.SpeciesResource.RegistryIndex;
    if (speciesIndex < 0 || speciesIndex >= source.Resources.SpeciesResources.Count ||
        !ReferenceEquals(
          placement.SpeciesResource,
          source.Resources.SpeciesResources[speciesIndex]) ||
        placement.SpeciesResource.Bridge?.Variants == null ||
        skip.Selection.SerializedVariantIndex < 0 ||
        skip.Selection.SerializedVariantIndex >=
          placement.SpeciesResource.Bridge.Variants.Count ||
        !ReferenceEquals(
          skip.Selection.Variant.VariantLink,
          placement.SpeciesResource.Bridge.Variants[
            skip.Selection.SerializedVariantIndex]))
      throw Invalid(
        $"typed skip placement {placementIndex} changed exact species or variant evidence");
    var provenance = source.SpeciesProvenance[speciesIndex];
    if (provenance == null ||
        !ReferenceEquals(provenance.SpeciesResource, placement.SpeciesResource) ||
        provenance.AnimationResources == null ||
        !ContainsExact(provenance.AnimationResources, skip.Animation.Resources) ||
        !ReferenceEquals(skip.Animation.Visual, saved.Visual) ||
        !string.Equals(
          skip.Animation.Resources.AnimationDataReference,
          skip.Selection.Variant.VariantLink.Variant.AnimationDataReference,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"typed skip placement {placementIndex} changed exact visual or WAD evidence");
    ValidateSavedAnimationEntries(skip, placementIndex);
  }

  private static void ValidateSavedAnimationEntries(
    WildAnimalFrameZeroScenePlacementSkip skip,
    int placementIndex
  ) {
    var savedEntries = skip.Placement.Placement.Visual.AnimationData;
    var resolutionEntries = skip.Animation.Entries;
    var slots = skip.Animation.Resources.Slots;
    if (savedEntries == null || resolutionEntries == null || slots == null ||
        resolutionEntries.Count != savedEntries.Count)
      throw Invalid(
        $"typed skip placement {placementIndex} changed saved animation entry count");
    foreach (var index in Enumerable.Range(0, savedEntries.Count)) {
      var entry = resolutionEntries[index];
      var saved = savedEntries[index];
      if (entry == null || entry.SavedIndex != index || entry.SavedEntry != saved ||
          saved.Type < 0 || saved.Type >= slots.Count ||
          !ReferenceEquals(entry.Slot, slots[saved.Type]))
        throw Invalid(
          $"typed skip placement {placementIndex} animation entry {index} " +
          "changed exact saved or WAD-slot evidence");
    }
  }

  private static bool ContainsExact<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static List<Exception> DisposeModels(IReadOnlyList<Model> models) {
    var errors = new List<Exception>();
    foreach (var model in models.Reverse()) {
      try {
        model.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid animated Wild-animal scene: {message}.");
}
