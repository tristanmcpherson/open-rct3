// Wild Animal Saved Variant Selector
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;

namespace OpenRCT3.Simulation;

/// <summary>One saved animal bound to the exact serialized WAS variant it names.</summary>
internal sealed record WildAnimalSavedVariantSelection(
  DatWildAnimalData Animal,
  int SerializedVariantIndex,
  WildAnimalModelVariantTemplateLink Variant
);

/// <summary>Selects one exact model-template variant from valid saved animal state.</summary>
/// <remarks>
/// Complete Edition's native paths at <c>0x00D943E8</c>, <c>0x00D9468C</c>, and
/// <c>0x00D946E8</c> establish the order adult-male, adult-female, baby-male,
/// baby-female. The selector at <c>0x00DA4747</c> indexes the four WAS pointers directly from
/// saved <c>Type</c>. Native malformed values fall through to slot three; this implementation
/// rejects them and any contradictory adult or sex state.
/// </remarks>
internal static class WildAnimalSavedVariantSelector {
  private const int SerializedVariantCount = 4;

  public static WildAnimalSavedVariantSelection Select(
    DatWildAnimalData animal,
    WildAnimalModelTemplateRegistry registry
  ) {
    ArgumentNullException.ThrowIfNull(animal);
    ArgumentNullException.ThrowIfNull(registry);
    if (registry.IsDisposed)
      throw Invalid(animal, "model-template registry is disposed");
    if (registry.Variants == null || registry.Variants.Count != SerializedVariantCount)
      throw Invalid(animal, "model-template registry does not contain exactly four variants");
    if (animal.Type < 0 || animal.Type >= SerializedVariantCount)
      throw Invalid(animal, $"Type {animal.Type} is outside the native range 0 through 3");

    var expectedAdult = animal.Type is 0 or 1;
    var expectedMale = animal.Type is 0 or 2;
    if (animal.IsAdult != expectedAdult || animal.IsMale != expectedMale)
      throw Invalid(
        animal,
        $"Type {animal.Type} requires IsAdult={expectedAdult} and IsMale={expectedMale}");

    var variant = registry.Variants[animal.Type];
    if (variant == null || variant.VariantLink == null ||
        variant.VariantLink.SerializedIndex != animal.Type)
      throw Invalid(animal, $"registry variant {animal.Type} changed serialized identity");
    return new(animal, animal.Type, variant);
  }

  private static InvalidDataException Invalid(DatWildAnimalData animal, string message) =>
    new($"Cannot select saved Wild animal {animal.EntryId} variant: {message}.");
}
