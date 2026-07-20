// Wild Animal Species Model Resource Bridge
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One decoded MDL and its exact symbol in the package pair.</summary>
internal sealed record WildAnimalSpeciesModelResourceSource(
  OvlFile File,
  ModelDefinition Resource
) {
  public string Identity => $"{File.Name}:mdl";
}

/// <summary>One serialized WAS variant linked to its exact decoded MDL.</summary>
internal sealed record WildAnimalSpeciesModelVariantLink(
  int SerializedIndex,
  WildAnimalSpeciesVariant Variant,
  WildAnimalSpeciesModelResourceSource ModelSource
);

/// <summary>The exact WAS owner and four serialized variant-to-MDL links.</summary>
internal sealed record WildAnimalSpeciesModelResourceBridgeResult(
  string SpeciesReference,
  string SpeciesCommonPath,
  OvlFile SpeciesFile,
  WildAnimalSpeciesDefinition Species,
  string ModelPackageCommonPath,
  IReadOnlyList<WildAnimalSpeciesModelVariantLink> Variants
);

/// <summary>
/// Resolves one installed Wild animal species through its serialized package path to exact MDLs.
/// </summary>
/// <remarks>
/// The installed WAS owner is the single exact <c>WildAnimals\WildAnimals</c> pair. Its serialized
/// package path then names one exact pair under the installation root. No directory enumeration,
/// sibling probing, semantic variant selection, or renderer ownership occurs here.
/// </remarks>
internal static class WildAnimalSpeciesModelResourceBridge {
  private const string SpeciesOverlayPath = @"WildAnimals\WildAnimals";
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";
  private const int SerializedVariantCount = 4;
  private const int MaximumIdentifierCharacters = 4_096;

  public static WildAnimalSpeciesModelResourceBridgeResult ResolveInstalled(
    string installRoot,
    string speciesReference
  ) => ResolveInstalled(
    installRoot,
    speciesReference,
    FileSystemWildAnimalSpeciesModelBridgeSource.Instance);

  internal static WildAnimalSpeciesModelResourceBridgeResult ResolveInstalled(
    string installRoot,
    string speciesReference,
    IWildAnimalSpeciesModelBridgeSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(speciesReference);
    ArgumentNullException.ThrowIfNull(source);
    if (!string.Equals(installRoot, installRoot.Trim(), StringComparison.Ordinal))
      throw new ArgumentException(
        "The installation root cannot have outer whitespace.",
        nameof(installRoot));

    var root = NormalizeRoot(installRoot);
    var speciesName = ParseTaggedName(speciesReference, "was", "species reference");
    var speciesCommonPath = ResolveExactCommonPath(root, SpeciesOverlayPath, "WAS owner path");
    RequirePair(source, speciesCommonPath, "WAS owner");

    using var speciesArchive = source.LoadPair(speciesCommonPath);
    ValidateLoadedPair(speciesArchive, speciesCommonPath, "WAS owner");
    var speciesFile = FindExactFile(
      speciesArchive,
      speciesName,
      FileType.WildAnimalSpecies,
      "WAS");
    RequireFileOwnedByPair(speciesFile, speciesCommonPath, "WAS");
    var species = speciesArchive.DecodeSpecies(speciesReference) ??
      throw Invalid("WAS decoder returned null");
    ValidateSpecies(species, speciesName);

    var modelCommonPath = ResolveExactCommonPath(
      root,
      species.PackagePath,
      $"WAS '{species.Name}' package path");
    RequirePair(source, modelCommonPath, $"WAS '{species.Name}' model package");
    using var modelArchive = source.LoadPair(modelCommonPath);
    ValidateLoadedPair(modelArchive, modelCommonPath, "model package");

    var sourcesByName = new Dictionary<string, WildAnimalSpeciesModelResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    var links = new List<WildAnimalSpeciesModelVariantLink>(SerializedVariantCount);
    foreach (var indexed in species.Variants.Select((variant, index) => (variant, index))) {
      if (indexed.variant == null)
        throw Invalid($"WAS '{species.Name}' variant {indexed.index} is null");
      var modelName = ParseTaggedName(
        indexed.variant.ModelReference,
        "mdl",
        $"WAS '{species.Name}' variant {indexed.index} model reference");
      _ = ParseTaggedName(
        indexed.variant.AnimationDataReference,
        "wad",
        $"WAS '{species.Name}' variant {indexed.index} animation-data reference");

      if (!sourcesByName.TryGetValue(modelName, out var modelSource)) {
        var modelFile = FindExactFile(modelArchive, modelName, FileType.Model, "MDL");
        RequireFileOwnedByPair(modelFile, modelCommonPath, "MDL");
        var model = modelArchive.DecodeModel(indexed.variant.ModelReference) ??
          throw Invalid($"MDL decoder returned null for '{indexed.variant.ModelReference}'");
        ValidateModel(model, modelFile, modelName, modelCommonPath);
        modelSource = new WildAnimalSpeciesModelResourceSource(modelFile, model);
        sourcesByName.Add(modelName, modelSource);
      }

      links.Add(new WildAnimalSpeciesModelVariantLink(
        indexed.index,
        indexed.variant,
        modelSource));
    }

    return new WildAnimalSpeciesModelResourceBridgeResult(
      speciesReference,
      speciesCommonPath,
      speciesFile,
      species,
      modelCommonPath,
      Array.AsReadOnly(links.ToArray()));
  }

  private static void ValidateSpecies(
    WildAnimalSpeciesDefinition species,
    string expectedName
  ) {
    if (!string.Equals(species.Name, expectedName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"WAS decoder returned '{species.Name}' for requested species '{expectedName}'");
    ValidateIdentifier(species.Name, "decoded WAS name");
    if (species.Variants == null)
      throw Invalid($"WAS '{species.Name}' has a null variant list");
    if (species.Variants.Count != SerializedVariantCount)
      throw Invalid(
        $"WAS '{species.Name}' exposes {species.Variants.Count} variants instead of " +
        $"{SerializedVariantCount}");
  }

  private static void ValidateModel(
    ModelDefinition model,
    OvlFile file,
    string expectedName,
    string modelCommonPath
  ) {
    if (!string.Equals(model.Name, expectedName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"MDL decoder returned '{model.Name}' for requested model '{expectedName}'");
    ValidateIdentifier(model.Name, "decoded MDL name");
    if (!PathsEqual(model.SourcePath, file.Path))
      throw Invalid(
        $"decoded MDL '{model.Name}' source '{model.SourcePath}' does not match its exact " +
        $"symbol owner '{file.Path}'");
    RequirePathOwnedByPair(model.SourcePath, modelCommonPath, $"decoded MDL '{model.Name}'");
  }

  private static OvlFile FindExactFile(
    IWildAnimalSpeciesModelArchive archive,
    string name,
    FileType type,
    string tag
  ) {
    if (archive.Files == null) throw Invalid($"{tag} archive file list is null");
    var matches = archive.Files.Where(file =>
      file != null &&
      file.Type == type &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matches.Length != 1)
      throw Invalid(
        $"'{name}:{type.ToTagString()}' resolves to {matches.Length} exact symbols in the " +
        $"declared {tag} pair instead of one");
    return matches[0];
  }

  private static void ValidateLoadedPair(
    IWildAnimalSpeciesModelArchive archive,
    string expectedCommonPath,
    string description
  ) {
    if (archive == null) throw Invalid($"{description} loader returned null");
    if (!PathsEqual(archive.CommonPath, expectedCommonPath))
      throw Invalid(
        $"{description} loader returned pair '{archive.CommonPath}' instead of exact pair " +
        $"'{expectedCommonPath}'");
  }

  private static void RequirePair(
    IWildAnimalSpeciesModelBridgeSource source,
    string commonPath,
    string description
  ) {
    var uniquePath = ToUniquePath(commonPath);
    var commonExists = source.FileExists(commonPath);
    var uniqueExists = source.FileExists(uniquePath);
    if (!commonExists || !uniqueExists)
      throw Invalid(
        $"{description} OVL pair is incomplete; required '{commonPath}' and '{uniquePath}'");
  }

  private static void RequireFileOwnedByPair(
    OvlFile file,
    string commonPath,
    string tag
  ) => RequirePathOwnedByPair(file.Path, commonPath, $"{tag} '{file.Name}' symbol");

  private static void RequirePathOwnedByPair(
    string path,
    string commonPath,
    string description
  ) {
    if (!TryGetAbsolutePath(path, out _))
      throw Invalid($"{description} has no exact absolute source path");
    if (!PathsEqual(path, commonPath) && !PathsEqual(path, ToUniquePath(commonPath)))
      throw Invalid(
        $"{description} source '{path}' is outside declared package pair '{commonPath}'");
  }

  private static string ResolveExactCommonPath(
    string root,
    string overlayPath,
    string description
  ) {
    ValidateIdentifier(overlayPath, description);
    if (!string.Equals(overlayPath, overlayPath.Trim(), StringComparison.Ordinal) ||
        overlayPath.IndexOfAny(['*', '?']) >= 0)
      throw Invalid($"{description} '{overlayPath}' is padded or contains a wildcard");
    if (overlayPath.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{overlayPath}' must name an extensionless OVL pair");

    var normalized = NormalizeSeparators(overlayPath);
    if (Path.IsPathFullyQualified(normalized))
      throw Invalid($"{description} '{overlayPath}' is rooted");
    var segments = normalized.Split(Path.DirectorySeparatorChar);
    if (segments.Length == 0 || segments.Any(segment =>
          string.IsNullOrWhiteSpace(segment) ||
          segment is "." or ".." ||
          segment.Contains(':', StringComparison.Ordinal)))
      throw Invalid($"{description} '{overlayPath}' contains an invalid path segment");

    string commonPath;
    try {
      commonPath = Path.GetFullPath(Path.Combine(root, normalized + CommonSuffix));
    } catch (Exception exception) when (
      exception is ArgumentException or NotSupportedException or PathTooLongException) {
      throw Invalid($"{description} '{overlayPath}' is not a valid installed path");
    }
    if (!IsContainedBy(root, commonPath))
      throw Invalid($"{description} '{overlayPath}' leaves the installation root");
    return commonPath;
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
        !reference[(separator + 1)..].Equals(
          expectedTag,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} '{reference}' is not one exact name:{expectedTag} identity");
    var name = reference[..separator];
    ValidateIdentifier(name, description);
    if (name is "." or ".." || name.IndexOfAny(['\\', '/', '*', '?']) >= 0)
      throw Invalid($"{description} '{reference}' does not contain one bare resource name");
    return name;
  }

  private static void ValidateIdentifier(string? value, string description) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > MaximumIdentifierCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{MaximumIdentifierCharacters} characters");
  }

  private static string NormalizeRoot(string root) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

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
    new($"Invalid Wild animal species model bridge: {message}.");
}

internal interface IWildAnimalSpeciesModelBridgeSource {
  bool FileExists(string path);
  IWildAnimalSpeciesModelArchive LoadPair(string commonPath);
}

internal interface IWildAnimalSpeciesModelArchive : IDisposable {
  string CommonPath { get; }
  IReadOnlyList<OvlFile> Files { get; }
  WildAnimalSpeciesDefinition DecodeSpecies(string reference);
  ModelDefinition DecodeModel(string reference);
}

internal sealed class FileSystemWildAnimalSpeciesModelBridgeSource
  : IWildAnimalSpeciesModelBridgeSource {
  public static FileSystemWildAnimalSpeciesModelBridgeSource Instance { get; } = new();

  private FileSystemWildAnimalSpeciesModelBridgeSource() { }

  public bool FileExists(string path) => File.Exists(path);

  public IWildAnimalSpeciesModelArchive LoadPair(string commonPath) =>
    new OvlWildAnimalSpeciesModelArchive(commonPath);
}

internal sealed class OvlWildAnimalSpeciesModelArchive : IWildAnimalSpeciesModelArchive {
  private readonly Ovl archive;

  public OvlWildAnimalSpeciesModelArchive(string commonPath) {
    CommonPath = commonPath;
    archive = Ovl.Load(commonPath);
    Files = Array.AsReadOnly(archive.Keys.ToArray());
  }

  public string CommonPath { get; }
  public IReadOnlyList<OvlFile> Files { get; }

  public WildAnimalSpeciesDefinition DecodeSpecies(string reference) =>
    WildAnimalSpecies.Extract(archive, reference);

  public ModelDefinition DecodeModel(string reference) => Models.Extract(archive, reference);

  public void Dispose() => archive.Dispose();
}
