// Wild Animal Model Animation Resource Bridge
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The structural state of one serialized WAD animation slot.</summary>
internal enum WildAnimalModelAnimationSlotStatus {
  Resolved,
  Placeholder,
}

/// <summary>One decoded ModelAnim and its exact symbol in the animation package pair.</summary>
internal sealed record WildAnimalModelAnimationResourceSource(
  OvlFile File,
  ModelAnimationDefinition Resource
) {
  public string Identity => $"{File.Name}:modelanim";
}

/// <summary>One serialized WAD slot and its exact ModelAnim target, when named.</summary>
internal sealed record WildAnimalModelAnimationSlotLink(
  int SerializedIndex,
  string Reference,
  WildAnimalModelAnimationSlotStatus Status,
  WildAnimalModelAnimationResourceSource? Source
) {
  public bool IsResolved => Status == WildAnimalModelAnimationSlotStatus.Resolved;
  public bool IsPlaceholder => Status == WildAnimalModelAnimationSlotStatus.Placeholder;
}

/// <summary>The exact WAD owner and all 31 serialized WAD-to-ModelAnim links.</summary>
internal sealed record WildAnimalModelAnimationResourceBridgeResult(
  string AnimationDataReference,
  string ModelPackageCommonPath,
  string AnimationPackageCommonPath,
  OvlFile AnimationDataFile,
  WildAnimalAnimationDataDefinition AnimationData,
  IReadOnlyList<WildAnimalModelAnimationSlotLink> Slots
) {
  public int ResolvedSlotCount => Slots.Count(slot => slot.IsResolved);
  public int PlaceholderSlotCount => Slots.Count(slot => slot.IsPlaceholder);
}

/// <summary>
/// Resolves one owner-proven WAD through all 31 serialized slots to exact decoded ModelAnims.
/// </summary>
/// <remarks>
/// The already-proven model package names its animation package through the OVL external-reference
/// table. Only that exact sibling pair is searched. Named <c>Name:modelanim</c> slots must resolve
/// once in that pair; the exact tag-only <c>:modelanim</c> value is preserved as an explicit
/// placeholder. Slot order and duplicate targets are retained. Pose, coordinate, quaternion, and
/// playback semantics remain outside this structural graph.
/// </remarks>
/// <seealso cref="WildAnimalAnimationData"/>
/// <seealso cref="ModelAnimations"/>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLDump/OVLDump.cpp#L586-L664">
/// Pinned version-five external-reference, loader, and SymbolRef reader
/// </seealso>
internal static class WildAnimalModelAnimationResourceBridge {
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";
  private const string PlaceholderReference = ":modelanim";
  private const int SerializedSlotCount = 31;
  private const int SerializedVariantCount = 4;
  private const int MaximumIdentifierCharacters = 4_096;
  private const int MaximumExternalReferences = 1_024;
  private const int MaximumArchiveFiles = 1_000_000;

  public static WildAnimalModelAnimationResourceBridgeResult ResolveInstalled(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    string animationDataReference
  ) => ResolveInstalled(
    resources,
    animationDataReference,
    FileSystemWildAnimalModelAnimationBridgeSource.Instance);

  internal static WildAnimalModelAnimationResourceBridgeResult ResolveInstalled(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    string animationDataReference,
    IWildAnimalModelAnimationBridgeSource source
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentException.ThrowIfNullOrWhiteSpace(animationDataReference);
    ArgumentNullException.ThrowIfNull(source);

    var wadName = ParseTaggedName(
      animationDataReference,
      "wad",
      "animation-data reference");
    ValidateRequestedReference(resources, animationDataReference);
    var modelCommonPath = ValidateCommonPath(
      resources.ModelPackageCommonPath,
      "model package common path");
    RequirePair(source, modelCommonPath, "model package");

    using var modelArchive = LoadPair(source, modelCommonPath, "model package");
    ValidateLoadedPair(modelArchive, modelCommonPath, "model package");
    var dependencyPaths = ResolveDependencyPaths(modelArchive, modelCommonPath, source);

    WildAnimalModelAnimationResourceBridgeResult? result = null;
    foreach (var animationCommonPath in dependencyPaths) {
      using var animationArchive = LoadPair(
        source,
        animationCommonPath,
        "declared animation package");
      ValidateLoadedPair(animationArchive, animationCommonPath, "declared animation package");
      var wadFiles = FindExactFiles(
        animationArchive,
        wadName,
        FileType.WildAnimalAnimData,
        "WAD");
      if (wadFiles.Length == 0) continue;
      if (wadFiles.Length != 1)
        throw Invalid(
          $"'{wadName}:wad' resolves to {wadFiles.Length} exact symbols in declared pair " +
          $"'{animationCommonPath}' instead of one");
      if (result != null)
        throw Invalid(
          $"'{wadName}:wad' resolves in more than one declared animation package");

      var wadFile = wadFiles[0];
      RequireExactUniqueOwner(wadFile, animationCommonPath, "WAD");
      var wad = animationArchive.DecodeAnimationData(animationDataReference) ??
        throw Invalid($"WAD decoder returned null for '{animationDataReference}'");
      ValidateAnimationData(wad, wadFile, wadName, animationCommonPath);
      var sourcesByName = DecodeModelAnimationSources(animationArchive, animationCommonPath);
      var slots = ResolveSlots(wad, sourcesByName);
      result = new WildAnimalModelAnimationResourceBridgeResult(
        animationDataReference,
        modelCommonPath,
        animationCommonPath,
        wadFile,
        wad,
        slots);
    }

    return result ?? throw Invalid(
      $"'{wadName}:wad' is absent from every exact external dependency of model package " +
      $"'{modelCommonPath}'");
  }

  private static void ValidateRequestedReference(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    string animationDataReference
  ) {
    if (resources.Variants == null || resources.Variants.Count != SerializedVariantCount)
      throw Invalid(
        $"species/model bridge does not contain exactly {SerializedVariantCount} variants");
    foreach (var indexed in resources.Variants.Select((variant, index) => (variant, index))) {
      if (indexed.variant?.Variant == null)
        throw Invalid($"species/model bridge variant {indexed.index} is null");
      _ = ParseTaggedName(
        indexed.variant.Variant.AnimationDataReference,
        "wad",
        $"species/model bridge variant {indexed.index} animation-data reference");
    }
    if (!resources.Variants.Any(variant => string.Equals(
          variant.Variant.AnimationDataReference,
          animationDataReference,
          StringComparison.OrdinalIgnoreCase)))
      throw Invalid(
        $"animation-data reference '{animationDataReference}' is not serialized by the " +
        "species/model bridge");
  }

  private static IReadOnlyList<string> ResolveDependencyPaths(
    IWildAnimalModelAnimationArchive archive,
    string modelCommonPath,
    IWildAnimalModelAnimationBridgeSource source
  ) {
    if (archive.ExternalReferences == null)
      throw Invalid("model package external-reference list is null");
    if (archive.ExternalReferences.Count == 0)
      throw Invalid("model package has no declared external OVL dependencies");
    if (archive.ExternalReferences.Count > MaximumExternalReferences)
      throw Invalid(
        $"model package external-reference count {archive.ExternalReferences.Count} exceeds " +
        $"{MaximumExternalReferences}");

    var directory = Path.GetDirectoryName(modelCommonPath);
    if (string.IsNullOrWhiteSpace(directory))
      throw Invalid($"model package '{modelCommonPath}' has no parent directory");
    var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var paths = new List<string>(archive.ExternalReferences.Count);
    foreach (var reference in archive.ExternalReferences) {
      var name = ParseExternalReference(reference);
      var commonPath = Path.GetFullPath(Path.Combine(directory, name + CommonSuffix));
      if (!IsContainedBy(directory, commonPath))
        throw Invalid($"external dependency '{reference}' leaves the model package directory");
      if (!unique.Add(commonPath))
        throw Invalid($"external dependency '{reference}' duplicates a declared package");
      RequirePair(source, commonPath, $"external dependency '{reference}'");
      paths.Add(commonPath);
    }
    return paths.AsReadOnly();
  }

  private static string ParseExternalReference(string reference) {
    ValidateIdentifier(reference, "external dependency");
    if (reference is "." or ".." ||
        reference.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase) ||
        reference.IndexOfAny(['\\', '/', ':', '*', '?']) >= 0)
      throw Invalid(
        $"external dependency '{reference}' is not one bare extensionless OVL pair name");
    return reference;
  }

  private static Dictionary<string, WildAnimalModelAnimationResourceSource>
    DecodeModelAnimationSources(
      IWildAnimalModelAnimationArchive archive,
      string commonPath
    ) {
    if (archive.Files == null) throw Invalid("animation package file list is null");
    if (archive.Files.Count > MaximumArchiveFiles)
      throw Invalid(
        $"animation package file count {archive.Files.Count} exceeds {MaximumArchiveFiles}");
    var files = archive.Files.Where(file =>
      file != null && file.Type == FileType.ModelAnim).ToArray();
    var filesByName = new Dictionary<string, OvlFile>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files) {
      ValidateIdentifier(file.Name, "ModelAnim symbol name");
      RequireExactCommonOwner(file, commonPath, "ModelAnim");
      if (!filesByName.TryAdd(file.Name, file))
        throw Invalid(
          $"'{file.Name}:modelanim' is ambiguous in declared pair '{commonPath}'");
    }

    var definitions = archive.DecodeModelAnimations() ??
      throw Invalid("ModelAnim decoder returned null");
    if (definitions.Count != filesByName.Count)
      throw Invalid(
        $"ModelAnim decoder returned {definitions.Count} resources for " +
        $"{filesByName.Count} exact symbols");
    var result = new Dictionary<string, WildAnimalModelAnimationResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    foreach (var definition in definitions) {
      if (definition == null) throw Invalid("ModelAnim decoder returned a null resource");
      ValidateIdentifier(definition.Name, "decoded ModelAnim name");
      if (!filesByName.TryGetValue(definition.Name, out var file))
        throw Invalid(
          $"decoded ModelAnim '{definition.Name}' has no exact symbol in pair '{commonPath}'");
      if (!PathsEqual(definition.SourcePath, file.Path))
        throw Invalid(
          $"decoded ModelAnim '{definition.Name}' source '{definition.SourcePath}' does not " +
          $"match exact symbol owner '{file.Path}'");
      if (!result.TryAdd(definition.Name,
            new WildAnimalModelAnimationResourceSource(file, definition)))
        throw Invalid($"ModelAnim decoder returned duplicate '{definition.Name}' resources");
    }
    return result;
  }

  private static IReadOnlyList<WildAnimalModelAnimationSlotLink> ResolveSlots(
    WildAnimalAnimationDataDefinition wad,
    IReadOnlyDictionary<string, WildAnimalModelAnimationResourceSource> sourcesByName
  ) {
    if (wad.SerializedCountAt08 != SerializedSlotCount)
      throw Invalid(
        $"WAD '{wad.Name}' serialized count {wad.SerializedCountAt08} is not " +
        $"{SerializedSlotCount}");
    if (wad.ModelAnimationReferencesAt38 == null ||
        wad.ModelAnimationReferencesAt38.Count != SerializedSlotCount)
      throw Invalid(
        $"WAD '{wad.Name}' does not expose exactly {SerializedSlotCount} serialized slots");

    var links = new WildAnimalModelAnimationSlotLink[SerializedSlotCount];
    foreach (var index in Enumerable.Range(0, SerializedSlotCount)) {
      var reference = wad.ModelAnimationReferencesAt38[index];
      ValidateIdentifier(reference, $"WAD '{wad.Name}' slot {index} reference");
      if (reference.Equals(PlaceholderReference, StringComparison.OrdinalIgnoreCase)) {
        links[index] = new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Placeholder,
          null);
        continue;
      }

      var modelAnimationName = ParseTaggedName(
        reference,
        "modelanim",
        $"WAD '{wad.Name}' slot {index} reference");
      if (!sourcesByName.TryGetValue(modelAnimationName, out var source))
        throw Invalid(
          $"WAD '{wad.Name}' slot {index} target '{reference}' is absent from its exact " +
          "animation package");
      links[index] = new WildAnimalModelAnimationSlotLink(
        index,
        reference,
        WildAnimalModelAnimationSlotStatus.Resolved,
        source);
    }
    return Array.AsReadOnly(links);
  }

  private static void ValidateAnimationData(
    WildAnimalAnimationDataDefinition wad,
    OvlFile file,
    string expectedName,
    string commonPath
  ) {
    if (!string.Equals(wad.Name, expectedName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"WAD decoder returned '{wad.Name}' for requested WAD '{expectedName}'");
    ValidateIdentifier(wad.Name, "decoded WAD name");
    if (!PathsEqual(wad.SourcePath, file.Path))
      throw Invalid(
        $"decoded WAD '{wad.Name}' source '{wad.SourcePath}' does not match exact symbol " +
        $"owner '{file.Path}'");
    if (!PathsEqual(wad.SourcePath, ToUniquePath(commonPath)))
      throw Invalid(
        $"decoded WAD '{wad.Name}' is not owned by exact unique archive " +
        $"'{ToUniquePath(commonPath)}'");
  }

  private static OvlFile[] FindExactFiles(
    IWildAnimalModelAnimationArchive archive,
    string name,
    FileType type,
    string tag
  ) {
    if (archive.Files == null) throw Invalid($"{tag} archive file list is null");
    if (archive.Files.Count > MaximumArchiveFiles)
      throw Invalid($"{tag} archive file count exceeds {MaximumArchiveFiles}");
    return archive.Files.Where(file =>
      file != null &&
      file.Type == type &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
  }

  private static string ParseTaggedName(
    string reference,
    string expectedTag,
    string description
  ) {
    ValidateIdentifier(reference, description);
    var separator = reference.IndexOf(':');
    if (separator <= 0 ||
        separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} '{reference}' is not one exact name:{expectedTag} identity");
    var name = reference[..separator];
    ValidateIdentifier(name, description);
    if (name is "." or ".." || name.IndexOfAny(['\\', '/', '*', '?']) >= 0)
      throw Invalid($"{description} '{reference}' does not contain one bare resource name");
    return name;
  }

  private static void RequireExactCommonOwner(
    OvlFile file,
    string commonPath,
    string tag
  ) {
    if (!PathsEqual(file.Path, commonPath))
      throw Invalid(
        $"{tag} '{file.Name}' symbol source '{file.Path}' is not exact common archive " +
        $"'{commonPath}'");
  }

  private static void RequireExactUniqueOwner(
    OvlFile file,
    string commonPath,
    string tag
  ) {
    var uniquePath = ToUniquePath(commonPath);
    if (!PathsEqual(file.Path, uniquePath))
      throw Invalid(
        $"{tag} '{file.Name}' symbol source '{file.Path}' is not exact unique archive " +
        $"'{uniquePath}'");
  }

  private static IWildAnimalModelAnimationArchive LoadPair(
    IWildAnimalModelAnimationBridgeSource source,
    string commonPath,
    string description
  ) => source.LoadPair(commonPath) ??
    throw Invalid($"{description} loader returned null for '{commonPath}'");

  private static void ValidateLoadedPair(
    IWildAnimalModelAnimationArchive archive,
    string expectedCommonPath,
    string description
  ) {
    if (!PathsEqual(archive.CommonPath, expectedCommonPath))
      throw Invalid(
        $"{description} loader returned pair '{archive.CommonPath}' instead of exact pair " +
        $"'{expectedCommonPath}'");
  }

  private static void RequirePair(
    IWildAnimalModelAnimationBridgeSource source,
    string commonPath,
    string description
  ) {
    var uniquePath = ToUniquePath(commonPath);
    if (!source.FileExists(commonPath) || !source.FileExists(uniquePath))
      throw Invalid(
        $"{description} OVL pair is incomplete; required '{commonPath}' and '{uniquePath}'");
  }

  private static string ValidateCommonPath(string path, string description) {
    ValidateIdentifier(path, description);
    if (!Path.IsPathFullyQualified(path) ||
        !path.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{path}' is not an exact absolute common OVL path");
    try {
      return Path.GetFullPath(path);
    } catch (Exception exception) when (
      exception is ArgumentException or NotSupportedException or PathTooLongException) {
      throw Invalid($"{description} '{path}' is invalid");
    }
  }

  private static void ValidateIdentifier(string? value, string description) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > MaximumIdentifierCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds {MaximumIdentifierCharacters} characters");
  }

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) &&
      !string.Equals(relative, "..", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static bool PathsEqual(string? left, string? right) =>
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
    new($"Invalid Wild animal model-animation bridge: {message}.");
}

internal interface IWildAnimalModelAnimationBridgeSource {
  bool FileExists(string path);
  IWildAnimalModelAnimationArchive LoadPair(string commonPath);
}

internal interface IWildAnimalModelAnimationArchive : IDisposable {
  string CommonPath { get; }
  IReadOnlyList<string> ExternalReferences { get; }
  IReadOnlyList<OvlFile> Files { get; }
  WildAnimalAnimationDataDefinition DecodeAnimationData(string reference);
  IReadOnlyList<ModelAnimationDefinition> DecodeModelAnimations();
}

internal sealed class FileSystemWildAnimalModelAnimationBridgeSource
  : IWildAnimalModelAnimationBridgeSource {
  public static FileSystemWildAnimalModelAnimationBridgeSource Instance { get; } = new();

  private FileSystemWildAnimalModelAnimationBridgeSource() { }

  public bool FileExists(string path) => File.Exists(path);

  public IWildAnimalModelAnimationArchive LoadPair(string commonPath) =>
    new OvlWildAnimalModelAnimationArchive(commonPath);
}

internal sealed class OvlWildAnimalModelAnimationArchive : IWildAnimalModelAnimationArchive {
  private readonly Ovl archive;

  public OvlWildAnimalModelAnimationArchive(string commonPath) {
    CommonPath = commonPath;
    archive = Ovl.Load(commonPath);
    ExternalReferences = Array.AsReadOnly(archive.ExternalReferences.ToArray());
    Files = Array.AsReadOnly(archive.Keys.ToArray());
  }

  public string CommonPath { get; }
  public IReadOnlyList<string> ExternalReferences { get; }
  public IReadOnlyList<OvlFile> Files { get; }

  public WildAnimalAnimationDataDefinition DecodeAnimationData(string reference) =>
    WildAnimalAnimationData.Extract(archive, reference);

  public IReadOnlyList<ModelAnimationDefinition> DecodeModelAnimations() =>
    ModelAnimations.Extract(archive);

  public void Dispose() => archive.Dispose();
}
