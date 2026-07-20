// Wild Animal Saved Animation Resolver
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The exact level of clip selection proven by one saved animation state.</summary>
internal enum WildAnimalSavedAnimationResolutionStatus {
  NoActiveClip,
  ExactSingleClip,
  WeightedState,
}

/// <summary>One retained saved blend entry linked to its direct serialized WAD slot.</summary>
internal sealed record WildAnimalSavedAnimationEntryResolution(
  int SavedIndex,
  DatWildAnimalAnimationData SavedEntry,
  WildAnimalModelAnimationSlotLink Slot
) {
  public bool IsActive => SavedEntry.Weight > 0f;
}

/// <summary>All retained saved blend entries and any exact full-weight clip selection.</summary>
internal sealed record WildAnimalSavedAnimationResolution(
  DatWildAnimalVisualData Visual,
  WildAnimalModelAnimationResourceBridgeResult Resources,
  IReadOnlyList<WildAnimalSavedAnimationEntryResolution> Entries,
  WildAnimalSavedAnimationResolutionStatus Status,
  WildAnimalSavedAnimationEntryResolution? ExactSingleClipEntry
) {
  public int ActiveEntryCount => Entries.Count(entry => entry.IsActive);
}

/// <summary>Links saved Wild-animal animation state to exact WAD slot identities.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers
/// each saved entry as <c>Type</c>, <c>Time</c>, and <c>Weight</c> at offsets 0, 4, and 8.
/// The native path at <c>0x00DEC998</c> uses <c>Type</c> directly to index both the WAD
/// ModelAnim reference array at <c>0x00DEC9A3</c> and its value array at <c>0x00DECA8E</c>.
/// <c>0x00DEC989</c> makes only weights greater than zero active and <c>0x00DECA71</c>
/// forwards that weight to the native blender. The lookup/create path at <c>0x00DED2B0</c>
/// establishes one retained entry per Type. This resolver therefore preserves raw time and weight
/// values without normalizing time, sampling poses, or inventing blend behavior.
/// </remarks>
/// <seealso cref="DatWildAnimalAnimationData"/>
/// <seealso cref="WildAnimalModelAnimationResourceBridge"/>
internal static class WildAnimalSavedAnimationResolver {
  private const int SerializedSlotCount = 31;
  private const string PlaceholderReference = ":modelanim";

  public static WildAnimalSavedAnimationResolution Resolve(
    DatWildAnimalVisualData visual,
    WildAnimalModelAnimationResourceBridgeResult resources
  ) {
    ArgumentNullException.ThrowIfNull(visual);
    ArgumentNullException.ThrowIfNull(resources);
    ValidateResources(visual, resources);
    if (visual.AnimationData == null)
      throw Invalid(visual, "saved animation entry list is null");
    if (visual.AnimationData.Count > SerializedSlotCount)
      throw Invalid(visual, "saved animation entry count exceeds the 31 unique native Types");

    var seenTypes = new bool[SerializedSlotCount];
    var entries = new List<WildAnimalSavedAnimationEntryResolution>(
      visual.AnimationData.Count);
    foreach (var indexed in visual.AnimationData.Select((entry, index) => (entry, index))) {
      var saved = indexed.entry;
      if (saved.Type < 0 || saved.Type >= SerializedSlotCount)
        throw Invalid(
          visual,
          $"entry {indexed.index} Type {saved.Type} is outside the native range 0 through 30");
      if (seenTypes[saved.Type])
        throw Invalid(visual, $"entry {indexed.index} duplicates Type {saved.Type}");
      if (!float.IsFinite(saved.Time))
        throw Invalid(visual, $"entry {indexed.index} Time is not finite");
      if (!float.IsFinite(saved.Weight))
        throw Invalid(visual, $"entry {indexed.index} Weight is not finite");

      seenTypes[saved.Type] = true;
      var slot = resources.Slots[saved.Type];
      if (saved.Weight > 0f && !slot.IsResolved)
        throw Invalid(
          visual,
          $"active entry {indexed.index} Type {saved.Type} maps to a placeholder WAD slot");
      entries.Add(new WildAnimalSavedAnimationEntryResolution(indexed.index, saved, slot));
    }

    var active = entries.Where(entry => entry.IsActive).ToArray();
    var status = active.Length switch {
      0 => WildAnimalSavedAnimationResolutionStatus.NoActiveClip,
      1 when active[0].SavedEntry.Weight == 1f =>
        WildAnimalSavedAnimationResolutionStatus.ExactSingleClip,
      _ => WildAnimalSavedAnimationResolutionStatus.WeightedState,
    };
    var exact = status == WildAnimalSavedAnimationResolutionStatus.ExactSingleClip
      ? active[0]
      : null;
    return new(visual, resources, entries.AsReadOnly(), status, exact);
  }

  private static void ValidateResources(
    DatWildAnimalVisualData visual,
    WildAnimalModelAnimationResourceBridgeResult resources
  ) {
    if (resources.AnimationData == null)
      throw Invalid(visual, "WAD resource is null");
    if (resources.AnimationData.SerializedCountAt08 != SerializedSlotCount)
      throw Invalid(visual, "WAD serialized count is not 31");
    if (resources.AnimationData.ModelAnimationReferencesAt38 == null ||
        resources.AnimationData.ModelAnimationReferencesAt38.Count != SerializedSlotCount)
      throw Invalid(visual, "WAD ModelAnim reference count is not 31");
    if (resources.AnimationData.ValuesAt10 == null ||
        resources.AnimationData.ValuesAt10.Count != SerializedSlotCount)
      throw Invalid(visual, "WAD native time-value count is not 31");
    if (resources.Slots == null || resources.Slots.Count != SerializedSlotCount)
      throw Invalid(visual, "resource bridge slot count is not 31");

    foreach (var index in Enumerable.Range(0, SerializedSlotCount)) {
      var slot = resources.Slots[index];
      if (slot == null)
        throw Invalid(visual, $"resource bridge slot {index} is null");
      if (slot.SerializedIndex != index)
        throw Invalid(
          visual,
          $"resource bridge slot {index} claims serialized index {slot.SerializedIndex}");
      if (!string.Equals(
            slot.Reference,
            resources.AnimationData.ModelAnimationReferencesAt38[index],
            StringComparison.Ordinal))
        throw Invalid(visual, $"resource bridge slot {index} changed its WAD reference");

      switch (slot.Status) {
        case WildAnimalModelAnimationSlotStatus.Placeholder:
          if (slot.Source != null || !string.Equals(
                slot.Reference,
                PlaceholderReference,
                StringComparison.OrdinalIgnoreCase))
            throw Invalid(visual, $"resource bridge slot {index} is not an exact placeholder");
          break;
        case WildAnimalModelAnimationSlotStatus.Resolved:
          ValidateResolvedSlot(visual, slot, index);
          break;
        default:
          throw Invalid(visual, $"resource bridge slot {index} has an unknown status");
      }
    }
  }

  private static void ValidateResolvedSlot(
    DatWildAnimalVisualData visual,
    WildAnimalModelAnimationSlotLink slot,
    int index
  ) {
    var source = slot.Source;
    if (source?.File == null || source.Resource == null)
      throw Invalid(visual, $"resolved resource bridge slot {index} has no ModelAnim source");
    if (source.File.Type != FileType.ModelAnim ||
        !string.Equals(source.Identity, slot.Reference, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          source.File.Name,
          source.Resource.Name,
          StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          source.File.Path,
          source.Resource.SourcePath,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(visual, $"resolved resource bridge slot {index} changed ModelAnim identity");
  }

  private static InvalidDataException Invalid(
    DatWildAnimalVisualData visual,
    string message
  ) => new(
    $"Cannot resolve saved Wild animal visual {visual.EntryId} animation: {message}.");
}
