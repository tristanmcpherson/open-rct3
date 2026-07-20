// Wild Animal Model Material Resource Bridge
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

using DecodedTexture = OpenCobra.OVL.Files.Texture;

namespace OpenRCT3.Simulation;

/// <summary>One exact TEX, its relocation-proven TXS edge, and caller-owned decoded pixels.</summary>
internal sealed record WildAnimalModelTextureMaterialResource(
  OvlFile TextureFile,
  uint TextureDataAddress,
  string TextureReference,
  string TextureStyleReference,
  DecodedTexture Albedo
);

/// <summary>One group-local MDL mesh bound through its serialized surface record to a TEX.</summary>
internal sealed record WildAnimalModelMeshMaterialBinding(
  WildAnimalSpeciesModelResourceSource ModelSource,
  int SourceGroupIndex,
  int SourceMeshIndex,
  int SourceSurfaceIndex,
  ushort TextureStringIndex,
  WildAnimalModelTextureMaterialResource Material
) {
  public string SourceMeshName =>
    $"{ModelSource.Resource.Name}/group-{SourceGroupIndex}/mesh-{SourceMeshIndex}";
  public string SerializedTextureName =>
    ModelSource.Resource.Strings[TextureStringIndex];
}

/// <summary>Owns decoded TEX clones retained by one exact MDL material resolution.</summary>
internal sealed class WildAnimalModelMaterialResourceBridgeResult : IDisposable {
  private readonly DecodedTexture[] ownedAlbedos;
  private bool disposed;

  internal WildAnimalModelMaterialResourceBridgeResult(
    string modelPackageCommonPath,
    IReadOnlyList<WildAnimalModelTextureMaterialResource> materials,
    IReadOnlyList<WildAnimalModelMeshMaterialBinding> bindings,
    IReadOnlyList<DecodedTexture> ownedAlbedos
  ) {
    ModelPackageCommonPath = modelPackageCommonPath;
    Materials = Array.AsReadOnly(materials.ToArray());
    Bindings = Array.AsReadOnly(bindings.ToArray());
    this.ownedAlbedos = ownedAlbedos.ToArray();
  }

  public string ModelPackageCommonPath { get; }
  public IReadOnlyList<WildAnimalModelTextureMaterialResource> Materials { get; }
  public IReadOnlyList<WildAnimalModelMeshMaterialBinding> Bindings { get; }
  public bool IsDisposed => disposed;

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    var errors = DisposeTextures(ownedAlbedos);
    if (errors.Count != 0)
      throw new AggregateException(
        "Wild-animal MDL material cleanup reported errors.",
        errors);
  }

  internal static List<Exception> DisposeTextures(
    IReadOnlyList<DecodedTexture> textures
  ) {
    var errors = new List<Exception>();
    for (var index = textures.Count - 1; index >= 0; index--) {
      try {
        textures[index].Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }
}

/// <summary>
/// Resolves the installed Wild MDL surface table to exact TEX resources and their exact TXS refs.
/// </summary>
/// <remarks>
/// Installed Wild animal MDLs encode each active surface value as <c>0x00010000 | meshIndex</c>.
/// The surface's <c>Field0</c> indexes the MDL string table, whose value names one exact TEX in the
/// paired package. TEX field <c>+0x2C</c> is then resolved through the archive's owner-proven
/// SymbolRef index. This bridge preserves that identity and decoded albedo pixels; it deliberately
/// does not infer blend, culling, specular, or other stock TXS shader behavior.
/// </remarks>
internal static class WildAnimalModelMaterialResourceBridge {
  private const int SerializedVariantCount = 4;
  private const uint TextureStyleFieldOffset = 44;
  private const uint SupportedSurfaceValueKind = 1;
  private const int MaximumIdentifierCharacters = 4_096;
  private const int MaximumMaterials = 64 * 1024;
  private const int MaximumBindings = 256 * 1024;
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";

  public static WildAnimalModelMaterialResourceBridgeResult ResolveInstalled(
    WildAnimalSpeciesModelResourceBridgeResult resources
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    using var archive = new OvlWildAnimalModelMaterialArchive(
      resources.ModelPackageCommonPath);
    return Resolve(resources, archive);
  }

  internal static WildAnimalModelMaterialResourceBridgeResult Resolve(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    IWildAnimalModelMaterialArchive archive
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(archive);
    ValidateCommonPath(resources.ModelPackageCommonPath);
    if (!PathsEqual(archive.CommonPath, resources.ModelPackageCommonPath))
      throw Invalid(
        $"material archive '{archive.CommonPath}' does not match exact model pair " +
        $"'{resources.ModelPackageCommonPath}'");
    if (archive.Files == null)
      throw Invalid("material archive file list is null");

    var modelSources = ValidateModelSources(resources, archive);
    var materials = new List<WildAnimalModelTextureMaterialResource>();
    var materialsByName = new Dictionary<string, WildAnimalModelTextureMaterialResource>(
      StringComparer.OrdinalIgnoreCase);
    var bindings = new List<WildAnimalModelMeshMaterialBinding>();
    var ownedAlbedos = new List<DecodedTexture>();
    var ownedAlbedoSet = new HashSet<DecodedTexture>(ReferenceEqualityComparer.Instance);
    try {
      foreach (var modelSource in modelSources)
        ResolveModel(
          resources.ModelPackageCommonPath,
          modelSource,
          archive,
          materials,
          materialsByName,
          bindings,
          ownedAlbedos,
          ownedAlbedoSet);

      return new WildAnimalModelMaterialResourceBridgeResult(
        resources.ModelPackageCommonPath,
        materials,
        bindings,
        ownedAlbedos);
    } catch (Exception resolveError) {
      var cleanupErrors =
        WildAnimalModelMaterialResourceBridgeResult.DisposeTextures(ownedAlbedos);
      if (cleanupErrors.Count != 0)
        throw new AggregateException([resolveError, .. cleanupErrors]);
      throw;
    }
  }

  private static IReadOnlyList<WildAnimalSpeciesModelResourceSource> ValidateModelSources(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    IWildAnimalModelMaterialArchive archive
  ) {
    if (resources.Variants == null || resources.Variants.Count != SerializedVariantCount)
      throw Invalid("source bridge does not contain exactly four variant links");

    var byIdentity = new Dictionary<string, WildAnimalSpeciesModelResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    var ordered = new List<WildAnimalSpeciesModelResourceSource>();
    foreach (var index in Enumerable.Range(0, resources.Variants.Count)) {
      var link = resources.Variants[index];
      if (link == null || link.SerializedIndex != index || link.Variant == null ||
          link.ModelSource == null || link.ModelSource.File == null ||
          link.ModelSource.Resource == null)
        throw Invalid($"variant slot {index} is null, incomplete, or out of order");
      RequireTaggedIdentity(link.Variant.ModelReference, "mdl", $"variant {index} model");
      if (!string.Equals(
            link.Variant.ModelReference,
            link.ModelSource.Identity,
            StringComparison.OrdinalIgnoreCase))
        throw Invalid($"variant slot {index} changed its exact MDL identity");
      ValidateModelSource(resources.ModelPackageCommonPath, link.ModelSource, archive);

      if (byIdentity.TryGetValue(link.ModelSource.Identity, out var existing)) {
        if (!ReferenceEquals(existing, link.ModelSource))
          throw Invalid(
            $"variant slot {index} repeats MDL '{link.ModelSource.Identity}' with a " +
            "changed source");
        continue;
      }
      byIdentity.Add(link.ModelSource.Identity, link.ModelSource);
      ordered.Add(link.ModelSource);
    }
    return Array.AsReadOnly(ordered.ToArray());
  }

  private static void ValidateModelSource(
    string commonPath,
    WildAnimalSpeciesModelResourceSource source,
    IWildAnimalModelMaterialArchive archive
  ) {
    if (source.File.Type != FileType.Model ||
        !string.Equals(source.File.Name, source.Resource.Name, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"MDL source '{source.Identity}' has changed file or decoded identity");
    RequirePairOwner(source.File.Path, commonPath, $"MDL '{source.Identity}' file");
    if (!PathsEqual(source.Resource.SourcePath, source.File.Path))
      throw Invalid($"MDL '{source.Identity}' changed its decoded file owner");

    var matches = archive.Files.Where(file =>
      file != null &&
      file.Type == FileType.Model &&
      string.Equals(file.Name, source.File.Name, StringComparison.OrdinalIgnoreCase) &&
      PathsEqual(file.Path, source.File.Path)).ToArray();
    if (matches.Length != 1)
      throw Invalid(
        $"MDL '{source.Identity}' resolves to {matches.Length} exact symbols in the " +
        "reloaded material pair instead of one");
    if (!archive.TryGetDataPointer(matches[0], out var address) ||
        address != source.Resource.DataAddress)
      throw Invalid($"MDL '{source.Identity}' changed its exact data address");
  }

  private static void ResolveModel(
    string commonPath,
    WildAnimalSpeciesModelResourceSource modelSource,
    IWildAnimalModelMaterialArchive archive,
    List<WildAnimalModelTextureMaterialResource> materials,
    Dictionary<string, WildAnimalModelTextureMaterialResource> materialsByName,
    List<WildAnimalModelMeshMaterialBinding> bindings,
    List<DecodedTexture> ownedAlbedos,
    HashSet<DecodedTexture> ownedAlbedoSet
  ) {
    var model = modelSource.Resource;
    if (model.Strings == null || model.Groups == null)
      throw Invalid($"MDL '{modelSource.Identity}' has a null string or group list");
    if (model.Strings.Count == 0)
      throw Invalid($"MDL '{modelSource.Identity}' has no TEX-name strings");

    foreach (var groupIndex in Enumerable.Range(0, model.Groups.Count)) {
      var group = model.Groups[groupIndex];
      if (group == null || group.Meshes == null || group.SurfaceRecords == null)
        throw Invalid($"MDL '{modelSource.Identity}' group {groupIndex} is incomplete");
      if (group.Meshes.Count != group.MeshCount ||
          group.SurfaceRecords.Count != group.SurfaceRecordCount)
        throw Invalid($"MDL '{modelSource.Identity}' group {groupIndex} count drifted");

      var mapped = new WildAnimalModelMeshMaterialBinding?[group.Meshes.Count];
      foreach (var surfaceIndex in Enumerable.Range(0, group.SurfaceRecords.Count)) {
        var surface = group.SurfaceRecords[surfaceIndex];
        if (surface == null)
          throw Invalid(
            $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} is null");
        ValidateSurface(modelSource, groupIndex, surfaceIndex, surface, model.Strings.Count);
        var textureName = model.Strings[surface.Field0];
        ValidateBareName(
          textureName,
          $"MDL '{modelSource.Identity}' string {surface.Field0}");
        var material = ResolveMaterial(
          commonPath,
          textureName,
          archive,
          materials,
          materialsByName,
          ownedAlbedos,
          ownedAlbedoSet);

        foreach (var value in surface.ValuesA) {
          var kind = value >> 16;
          if (kind != SupportedSurfaceValueKind)
            throw Invalid(
              $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} " +
              $"value 0x{value:X8} has unsupported high word {kind}");
          var meshIndex = Convert.ToInt32(value & ushort.MaxValue);
          if (meshIndex >= group.Meshes.Count)
            throw Invalid(
              $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} " +
              $"maps outside {group.Meshes.Count} meshes to {meshIndex}");
          if (group.Meshes[meshIndex] == null)
            throw Invalid(
              $"MDL '{modelSource.Identity}' group {groupIndex} mesh {meshIndex} is null");
          if (mapped[meshIndex] != null)
            throw Invalid(
              $"MDL '{modelSource.Identity}' group {groupIndex} mesh {meshIndex} is mapped " +
              "more than once");
          mapped[meshIndex] = new WildAnimalModelMeshMaterialBinding(
            modelSource,
            groupIndex,
            meshIndex,
            surfaceIndex,
            surface.Field0,
            material);
        }
      }

      foreach (var meshIndex in Enumerable.Range(0, mapped.Length)) {
        var binding = mapped[meshIndex] ??
          throw Invalid(
            $"MDL '{modelSource.Identity}' group {groupIndex} mesh {meshIndex} has no " +
            "surface-to-TEX mapping");
        if (bindings.Count >= MaximumBindings)
          throw Invalid($"mesh material binding count exceeds {MaximumBindings}");
        bindings.Add(binding);
      }
    }
  }

  private static void ValidateSurface(
    WildAnimalSpeciesModelResourceSource modelSource,
    int groupIndex,
    int surfaceIndex,
    ModelSurfaceRecord surface,
    int stringCount
  ) {
    if (surface.Field0 >= stringCount)
      throw Invalid(
        $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} string " +
        $"index {surface.Field0} is outside {stringCount} strings");
    if (surface.ValuesA == null || surface.ValuesA.Count != surface.CountA)
      throw Invalid(
        $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} CountA " +
        "does not match its values");
    if (surface.CountB != 0 || surface.ValuesB == null || surface.ValuesB.Count != 0)
      throw Invalid(
        $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} uses " +
        "unsupported values B");
    if (surface.Field6 != 0)
      throw Invalid(
        $"MDL '{modelSource.Identity}' group {groupIndex} surface {surfaceIndex} uses " +
        $"unsupported field +0x06 value {surface.Field6}");
  }

  private static WildAnimalModelTextureMaterialResource ResolveMaterial(
    string commonPath,
    string textureName,
    IWildAnimalModelMaterialArchive archive,
    List<WildAnimalModelTextureMaterialResource> materials,
    Dictionary<string, WildAnimalModelTextureMaterialResource> materialsByName,
    List<DecodedTexture> ownedAlbedos,
    HashSet<DecodedTexture> ownedAlbedoSet
  ) {
    if (materialsByName.TryGetValue(textureName, out var cached)) return cached;
    if (materials.Count >= MaximumMaterials)
      throw Invalid($"TEX material count exceeds {MaximumMaterials}");

    var matches = archive.Files.Where(file =>
      file != null &&
      file.Type == FileType.Texture &&
      string.Equals(file.Name, textureName, StringComparison.OrdinalIgnoreCase) &&
      PathBelongsToPair(file.Path, commonPath)).ToArray();
    if (matches.Length != 1)
      throw Invalid(
        $"MDL TEX '{textureName}:tex' resolves to {matches.Length} exact symbols in its " +
        "declared pair instead of one");
    var textureFile = matches[0];
    if (!archive.TryGetDataPointer(textureFile, out var dataAddress) || dataAddress == 0)
      throw Invalid($"TEX '{textureFile.Name}:tex' has no exact data address");
    if (dataAddress > uint.MaxValue - TextureStyleFieldOffset)
      throw Invalid($"TEX '{textureFile.Name}:tex' style field address overflows");
    var styleFieldAddress = dataAddress + TextureStyleFieldOffset;
    if (!archive.TryGetSymbolReference(styleFieldAddress, out var styleReference) ||
        styleReference == null)
      throw Invalid(
        $"TEX '{textureFile.Name}:tex' has no relocation-proven TXS SymbolRef at +0x2C");
    ValidateStyleReference(textureFile, dataAddress, styleReference);

    var albedo = archive.DecodeTexture(textureFile) ??
      throw Invalid($"TEX '{textureFile.Name}:tex' has no decoded albedo pixels");
    if (!ownedAlbedoSet.Add(albedo))
      throw Invalid("TEX decoder reused one albedo object across material identities");
    ownedAlbedos.Add(albedo);
    if (!string.Equals(albedo.Name, textureFile.Name, StringComparison.OrdinalIgnoreCase) ||
        albedo.Width == 0 || albedo.Height == 0 || albedo.MipLevels.Length == 0 ||
        albedo.MipLevels[0] == null)
      throw Invalid($"TEX '{textureFile.Name}:tex' decoded albedo identity or pixels drifted");
    albedo.Style = styleReference.Symbol;

    var material = new WildAnimalModelTextureMaterialResource(
      textureFile,
      dataAddress,
      $"{textureFile.Name}:tex",
      styleReference.Symbol,
      albedo);
    materialsByName.Add(textureName, material);
    materials.Add(material);
    return material;
  }

  private static void ValidateStyleReference(
    OvlFile textureFile,
    uint dataAddress,
    ResourceSymbolReference styleReference
  ) {
    RequireTaggedIdentity(
      styleReference.Symbol,
      "txs",
      $"TEX '{textureFile.Name}:tex' style");
    if (!string.Equals(styleReference.OwnerTag, "tex", StringComparison.OrdinalIgnoreCase) ||
        styleReference.OwnerDataAddress != dataAddress ||
        !PathsEqual(styleReference.OwnerSourcePath, textureFile.Path))
      throw Invalid(
        $"TEX '{textureFile.Name}:tex' TXS SymbolRef changed its exact loader owner");
  }

  private static void ValidateCommonPath(string? commonPath) {
    if (string.IsNullOrWhiteSpace(commonPath) ||
        !commonPath.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      throw Invalid("declared model package path is not one common OVL path");
    try {
      if (!Path.IsPathFullyQualified(commonPath) ||
          !string.Equals(commonPath, Path.GetFullPath(commonPath), StringComparison.Ordinal))
        throw Invalid("declared model package path is not one exact absolute path");
    } catch (Exception error) when (
      error is ArgumentException or NotSupportedException or PathTooLongException) {
      throw Invalid("declared model package path is invalid");
    }
  }

  private static void ValidateBareName(string? value, string description) {
    var validated = RequireIdentifier(value, description);
    if (validated is "." or ".." ||
        validated.IndexOfAny([':', '\\', '/', '*', '?']) >= 0)
      throw Invalid($"{description} '{validated}' is not one bare TEX name");
  }

  private static void RequireTaggedIdentity(
    string? value,
    string tag,
    string description
  ) {
    var validated = RequireIdentifier(value, description);
    var separator = validated.IndexOf(':');
    if (separator <= 0 || separator != validated.LastIndexOf(':') ||
        separator == validated.Length - 1 ||
        !validated[(separator + 1)..].Equals(tag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} '{validated}' is not one exact name:{tag} identity");
  }

  private static string RequireIdentifier(string? value, string description) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > MaximumIdentifierCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds {MaximumIdentifierCharacters} characters");
    return value;
  }

  private static void RequirePairOwner(
    string? path,
    string commonPath,
    string description
  ) {
    if (!PathBelongsToPair(path, commonPath))
      throw Invalid($"{description} is outside its exact declared OVL pair");
  }

  private static bool PathBelongsToPair(string? path, string commonPath) =>
    PathsEqual(path, commonPath) || PathsEqual(path, ToUniquePath(commonPath));

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^CommonSuffix.Length] + UniqueSuffix;

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

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild animal model material bridge: {message}.");
}

internal interface IWildAnimalModelMaterialArchive {
  string CommonPath { get; }
  IReadOnlyList<OvlFile> Files { get; }
  bool TryGetDataPointer(OvlFile file, out uint address);
  bool TryGetSymbolReference(uint fieldAddress, out ResourceSymbolReference? reference);
  DecodedTexture? DecodeTexture(OvlFile file);
}

internal sealed class OvlWildAnimalModelMaterialArchive
  : IWildAnimalModelMaterialArchive, IDisposable {
  private readonly Ovl archive;
  private readonly ResourceSymbolReferenceIndex symbolReferences;
  private TextureCollection? decodedTextures;
  private bool disposed;

  public OvlWildAnimalModelMaterialArchive(string commonPath) {
    CommonPath = commonPath;
    archive = Ovl.Load(commonPath);
    try {
      symbolReferences = ResourceSymbolReferenceIndex.Create(archive);
      Files = Array.AsReadOnly(archive.Keys.ToArray());
    } catch {
      archive.Dispose();
      throw;
    }
  }

  public string CommonPath { get; }
  public IReadOnlyList<OvlFile> Files { get; }

  public bool TryGetDataPointer(OvlFile file, out uint address) =>
    archive.TryGetDataPointer(file, out address);

  public bool TryGetSymbolReference(
    uint fieldAddress,
    out ResourceSymbolReference? reference
  ) => symbolReferences.TryGetValue(fieldAddress, out reference);

  public DecodedTexture? DecodeTexture(OvlFile file) {
    ObjectDisposedException.ThrowIf(disposed, this);
    decodedTextures ??= Textures.Extract(archive);
    var decodedIdentity = file.ToString();
    var matches = decodedTextures.Where(texture =>
      string.Equals(
        texture.Name,
        decodedIdentity,
        StringComparison.OrdinalIgnoreCase)).ToArray();
    return matches.Length == 1 ? matches[0].WithName(file.Name) : null;
  }

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    Exception? textureError = null;
    try {
      decodedTextures?.Dispose();
    } catch (Exception error) {
      textureError = error;
    }
    try {
      archive.Dispose();
    } catch (Exception archiveError) {
      if (textureError != null)
        throw new AggregateException(textureError, archiveError);
      throw;
    }
    if (textureError != null) throw textureError;
  }
}
