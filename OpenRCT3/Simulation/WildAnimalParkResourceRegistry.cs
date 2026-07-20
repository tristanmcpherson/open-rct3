// Wild Animal Park Resource Registry
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One used DAT species identity resolved once to its exact installed WAS bridge.</summary>
internal sealed record WildAnimalParkSpeciesResource(
  int RegistryIndex,
  DatWildAnimalSpeciesDatabaseEntryData Species,
  WildAnimalSpeciesModelResourceBridgeResult Bridge,
  IReadOnlyList<int> PlacementIndices
);

/// <summary>One saved animal placement bound to the exact bridge for its referenced species.</summary>
internal sealed record WildAnimalParkPlacementResource(
  int PlacementIndex,
  DatWildAnimalPlacementData Placement,
  WildAnimalParkSpeciesResource SpeciesResource
) {
  public DatWildAnimalVariantSelectionStatus VariantSelectionStatus =>
    Placement.VariantSelectionStatus;
}

/// <summary>
/// Resolves each distinct used DAT Wild-animal species once and preserves saved placement order.
/// </summary>
/// <remarks>
/// The registry proves the DAT owner path and resolver source agree. It does not interpret the
/// adult, sex, or type fields, select one of the four WAS variants, or convert native matrices.
/// </remarks>
internal sealed class WildAnimalParkResourceRegistry {
  private const int MaximumPlacementCount = 100_000;
  private const int MaximumUsedSpeciesCount = 100_000;
  private const int MaximumIdentifierCharacters = 4_096;
  private const string WasTag = ":was";
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";

  public IReadOnlyList<WildAnimalParkSpeciesResource> SpeciesResources { get; }
  public IReadOnlyList<WildAnimalParkPlacementResource> Placements { get; }

  private WildAnimalParkResourceRegistry(
    IReadOnlyList<WildAnimalParkSpeciesResource> speciesResources,
    IReadOnlyList<WildAnimalParkPlacementResource> placements
  ) {
    SpeciesResources = Array.AsReadOnly(speciesResources.ToArray());
    Placements = Array.AsReadOnly(placements.ToArray());
  }

  public static WildAnimalParkResourceRegistry Build(Park park, string installRoot) =>
    Build(park, installRoot, InstalledWildAnimalParkSpeciesResourceResolver.Instance);

  internal static WildAnimalParkResourceRegistry Build(
    Park park,
    string installRoot,
    IWildAnimalParkSpeciesResourceResolver resolver
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(resolver);
    if (!string.Equals(installRoot, installRoot.Trim(), StringComparison.Ordinal))
      throw new ArgumentException(
        "The installation root cannot have outer whitespace.",
        nameof(installRoot));
    if (park.WildAnimalPlacements.Count > MaximumPlacementCount)
      throw Invalid(
        $"placement count {park.WildAnimalPlacements.Count} exceeds " +
        $"{MaximumPlacementCount}");

    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
    var groups = GroupPlacements(park.WildAnimalPlacements);
    if (groups.Count > MaximumUsedSpeciesCount)
      throw Invalid(
        $"used species count {groups.Count} exceeds {MaximumUsedSpeciesCount}");

    var speciesResources = ResolveSpecies(root, groups, resolver);
    var placements = new WildAnimalParkPlacementResource[park.WildAnimalPlacements.Count];
    foreach (var group in groups) {
      var speciesResource = speciesResources[group.RegistryIndex];
      foreach (var placementIndex in group.PlacementIndices) {
        placements[placementIndex] = new WildAnimalParkPlacementResource(
          placementIndex,
          park.WildAnimalPlacements[placementIndex],
          speciesResource);
      }
    }
    return new WildAnimalParkResourceRegistry(speciesResources, placements);
  }

  private static List<SpeciesGroup> GroupPlacements(
    IReadOnlyList<DatWildAnimalPlacementData> placements
  ) {
    var groups = new List<SpeciesGroup>();
    var groupsByEntryId = new Dictionary<ulong, SpeciesGroup>();
    var entryIdsByIdentity =
      new Dictionary<(string Overlay, string Symbol), ulong>(
        SpeciesIdentityComparer.Instance);
    var animalIds = new HashSet<ulong>();
    foreach (var index in Enumerable.Range(0, placements.Count)) {
      var placement = placements[index]
        ?? throw new ArgumentException(
          "Wild-animal placements cannot contain null.",
          nameof(placements));
      ValidatePlacement(placement, animalIds);
      var species = placement.Species;
      if (groupsByEntryId.TryGetValue(species.EntryId, out var group)) {
        RequireStableSpeciesIdentity(group.Species, species);
      } else {
        var identity = (
          NormalizeSeparators(species.OverlayFilename),
          species.SymbolName);
        if (entryIdsByIdentity.TryGetValue(identity, out var existingEntryId))
          throw Invalid(
            $"duplicate DAT WAS identity '{species.OverlayFilename}|" +
            $"{species.SymbolName}:was' has entry IDs {existingEntryId} and " +
            $"{species.EntryId}");
        group = new SpeciesGroup(groups.Count, species);
        groups.Add(group);
        groupsByEntryId.Add(species.EntryId, group);
        entryIdsByIdentity.Add(identity, species.EntryId);
      }
      group.PlacementIndices.Add(index);
    }
    return groups;
  }

  private static WildAnimalParkSpeciesResource[] ResolveSpecies(
    string installRoot,
    IReadOnlyList<SpeciesGroup> groups,
    IWildAnimalParkSpeciesResourceResolver resolver
  ) {
    var resources = new WildAnimalParkSpeciesResource[groups.Count];
    var resolverResults = new HashSet<WildAnimalSpeciesModelResourceBridgeResult>(
      ReferenceEqualityComparer.Instance);
    foreach (var group in groups) {
      var speciesReference = group.Species.SymbolName + WasTag;
      var result = resolver.Resolve(installRoot, speciesReference)
        ?? throw Invalid($"resolver returned null for '{speciesReference}'");
      if (!resolverResults.Add(result))
        throw Invalid(
          $"resolver reused one bridge result for distinct DAT WAS entry " +
          $"{group.Species.EntryId}");
      ValidateResolverResult(installRoot, group.Species, speciesReference, result);
      resources[group.RegistryIndex] = new WildAnimalParkSpeciesResource(
        group.RegistryIndex,
        group.Species,
        result,
        Array.AsReadOnly(group.PlacementIndices.ToArray()));
    }
    return resources;
  }

  private static void ValidatePlacement(
    DatWildAnimalPlacementData placement,
    HashSet<ulong> animalIds
  ) {
    if (placement.Animal is null || placement.Species is null || placement.Visual is null)
      throw Invalid("placement contains a null DAT record");
    if (placement.Animal.EntryId == 0 || !animalIds.Add(placement.Animal.EntryId))
      throw Invalid(
        $"animal entry ID {placement.Animal.EntryId} is missing or duplicated");
    if (placement.Species.EntryId == 0)
      throw Invalid("WAS database entry ID 0 is missing");
    if (placement.Visual.EntryId == 0)
      throw Invalid($"animal {placement.Animal.EntryId} has visual entry ID 0");
    if (placement.Animal.SpeciesDatabaseEntryId != placement.Species.EntryId)
      throw Invalid(
        $"animal {placement.Animal.EntryId} references WAS entry " +
        $"{placement.Animal.SpeciesDatabaseEntryId}, but placement carries " +
        $"{placement.Species.EntryId}");
    if (placement.Animal.VisualEntryId != placement.Visual.EntryId)
      throw Invalid(
        $"animal {placement.Animal.EntryId} references visual " +
        $"{placement.Animal.VisualEntryId}, but placement carries " +
        $"{placement.Visual.EntryId}");
    if (placement.VariantSelectionStatus != DatWildAnimalVariantSelectionStatus.Unsupported)
      throw Invalid(
        $"animal {placement.Animal.EntryId} has an inferred WAS variant selection");

    ValidateOverlay(placement.Species.OverlayFilename, "DAT WAS overlay filename");
    ValidateBareName(placement.Species.SymbolName, "DAT WAS symbol name");
  }

  private static void RequireStableSpeciesIdentity(
    DatWildAnimalSpeciesDatabaseEntryData expected,
    DatWildAnimalSpeciesDatabaseEntryData actual
  ) {
    if (expected.IsUnlocked != actual.IsUnlocked ||
        !string.Equals(
          expected.OverlayFilename,
          actual.OverlayFilename,
          StringComparison.Ordinal) ||
        !string.Equals(expected.SymbolName, actual.SymbolName, StringComparison.Ordinal))
      throw Invalid(
        $"WAS entry {expected.EntryId} changes overlay, symbol, or unlock identity " +
        "between placements");
  }

  private static void ValidateResolverResult(
    string installRoot,
    DatWildAnimalSpeciesDatabaseEntryData species,
    string speciesReference,
    WildAnimalSpeciesModelResourceBridgeResult result
  ) {
    if (result.SpeciesFile is null || result.Species is null ||
        result.Species.Variants is null || result.Variants is null)
      throw Invalid($"resolver returned incomplete evidence for '{speciesReference}'");
    if (!string.Equals(result.SpeciesReference, speciesReference, StringComparison.Ordinal))
      throw Invalid(
        $"resolver returned species reference '{result.SpeciesReference}' instead of " +
        $"'{speciesReference}'");

    var expectedCommonPath = ResolveExpectedCommonPath(
      installRoot,
      species.OverlayFilename,
      "DAT WAS overlay filename");
    if (!PathsEqual(result.SpeciesCommonPath, expectedCommonPath))
      throw Invalid(
        $"resolver owner '{result.SpeciesCommonPath}' does not match DAT overlay " +
        $"'{species.OverlayFilename}' at '{expectedCommonPath}'");
    if (!PathBelongsToPair(result.SpeciesFile.Path, expectedCommonPath))
      throw Invalid(
        $"resolver WAS symbol source '{result.SpeciesFile.Path}' is outside DAT owner pair " +
        $"'{expectedCommonPath}'");
    if (result.SpeciesFile.Type != FileType.WildAnimalSpecies)
      throw Invalid(
        $"resolver symbol '{result.SpeciesFile.Name}' has type " +
        $"'{result.SpeciesFile.Type.ToTagString()}' instead of 'was'");
    if (!string.Equals(
          result.SpeciesFile.Name,
          species.SymbolName,
          StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
          result.Species.Name,
          species.SymbolName,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"resolver WAS identity drifted from DAT symbol '{species.SymbolName}'");
    if (result.Species.Variants.Count != 4 || result.Variants.Count != 4)
      throw Invalid(
        $"resolver returned {result.Species.Variants.Count} decoded and " +
        $"{result.Variants.Count} linked WAS variants instead of 4 for " +
        $"'{speciesReference}'");
    foreach (var index in Enumerable.Range(0, result.Variants.Count)) {
      var variant = result.Variants[index];
      if (variant is null || variant.Variant is null || variant.ModelSource is null ||
          variant.ModelSource.File is null || variant.ModelSource.Resource is null)
        throw Invalid(
          $"resolver returned incomplete WAS variant {index} for '{speciesReference}'");
      if (variant.SerializedIndex != index ||
          !ReferenceEquals(variant.Variant, result.Species.Variants[index]))
        throw Invalid(
          $"resolver WAS variant {index} drifted from serialized order for " +
          $"'{speciesReference}'");
    }
  }

  private static string ResolveExpectedCommonPath(
    string root,
    string overlay,
    string description
  ) {
    ValidateOverlay(overlay, description);
    var normalized = NormalizeSeparators(overlay);
    var segments = normalized.Split(Path.DirectorySeparatorChar);
    if (segments.Length == 0 || segments.Any(segment =>
          string.IsNullOrWhiteSpace(segment) ||
          segment is "." or ".." ||
          segment.Contains(':', StringComparison.Ordinal)))
      throw Invalid($"{description} '{overlay}' contains an invalid path segment");

    string commonPath;
    try {
      commonPath = Path.GetFullPath(Path.Combine(root, normalized + CommonSuffix));
    } catch (Exception exception) when (
      exception is ArgumentException or NotSupportedException or PathTooLongException) {
      throw Invalid($"{description} '{overlay}' is not a valid installed path");
    }
    if (!IsContainedBy(root, commonPath))
      throw Invalid($"{description} '{overlay}' leaves the installation root");
    return commonPath;
  }

  private static void ValidateOverlay(string value, string description) {
    ValidateIdentifier(value, MaximumIdentifierCharacters, description);
    var normalized = NormalizeSeparators(value);
    if (value.IndexOfAny(['*', '?']) >= 0 ||
        value.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase) ||
        Path.IsPathFullyQualified(normalized))
      throw Invalid(
        $"{description} '{value}' is rooted, wildcarded, or has an OVL suffix");
  }

  private static void ValidateBareName(string value, string description) {
    ValidateIdentifier(
      value,
      MaximumIdentifierCharacters - WasTag.Length,
      description);
    if (value is "." or ".." || value.IndexOfAny([':', '\\', '/', '*', '?']) >= 0)
      throw Invalid($"{description} '{value}' is not one bare resource name");
  }

  private static void ValidateIdentifier(
    string value,
    int maximumLength,
    string description
  ) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > maximumLength ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds {maximumLength} characters");
  }

  private static string NormalizeSeparators(string path) => path
    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
    .Replace('\\', Path.DirectorySeparatorChar)
    .Replace('/', Path.DirectorySeparatorChar);

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) &&
      !string.Equals(relative, "..", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static bool PathBelongsToPair(string path, string commonPath) =>
    PathsEqual(path, commonPath) || PathsEqual(path, ToUniquePath(commonPath));

  private static bool PathsEqual(string left, string right) =>
    TryGetAbsolutePath(left, out var absoluteLeft) &&
    TryGetAbsolutePath(right, out var absoluteRight) &&
    string.Equals(absoluteLeft, absoluteRight, StringComparison.OrdinalIgnoreCase);

  private static bool TryGetAbsolutePath(string? path, out string absolutePath) {
    absolutePath = string.Empty;
    if (string.IsNullOrWhiteSpace(path)) return false;
    try {
      if (!Path.IsPathFullyQualified(path)) return false;
      absolutePath = Path.GetFullPath(path);
      return true;
    } catch (Exception exception) when (
      exception is ArgumentException or NotSupportedException or PathTooLongException) {
      absolutePath = string.Empty;
      return false;
    }
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^CommonSuffix.Length] + UniqueSuffix;

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild-animal park resource registry: {message}.");

  private sealed class SpeciesGroup(
    int registryIndex,
    DatWildAnimalSpeciesDatabaseEntryData species
  ) {
    public int RegistryIndex { get; } = registryIndex;
    public DatWildAnimalSpeciesDatabaseEntryData Species { get; } = species;
    public List<int> PlacementIndices { get; } = [];
  }

  private sealed class SpeciesIdentityComparer
    : IEqualityComparer<(string Overlay, string Symbol)> {
    public static SpeciesIdentityComparer Instance { get; } = new();

    private SpeciesIdentityComparer() { }

    public bool Equals(
      (string Overlay, string Symbol) left,
      (string Overlay, string Symbol) right
    ) => string.Equals(left.Overlay, right.Overlay, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(left.Symbol, right.Symbol, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Overlay, string Symbol) value) =>
      HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(value.Overlay),
        StringComparer.OrdinalIgnoreCase.GetHashCode(value.Symbol));
  }
}

internal interface IWildAnimalParkSpeciesResourceResolver {
  WildAnimalSpeciesModelResourceBridgeResult Resolve(
    string installRoot,
    string speciesReference);
}

internal sealed class InstalledWildAnimalParkSpeciesResourceResolver
  : IWildAnimalParkSpeciesResourceResolver {
  public static InstalledWildAnimalParkSpeciesResourceResolver Instance { get; } = new();

  private InstalledWildAnimalParkSpeciesResourceResolver() { }

  public WildAnimalSpeciesModelResourceBridgeResult Resolve(
    string installRoot,
    string speciesReference
  ) => WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
    installRoot,
    speciesReference);
}
