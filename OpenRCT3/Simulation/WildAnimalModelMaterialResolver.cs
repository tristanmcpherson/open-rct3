// Wild Animal Model Material Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

using RenderTexture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>Creates fresh renderer materials from exact Wild-animal MDL mesh bindings.</summary>
/// <remarks>
/// TEX pixels and TEX-to-TXS identities are exact. The current renderer approximates installed
/// <c>SIOpaque*</c> and <c>SIAlpha*</c> style families with its ordinary textured shaders; stock
/// specular behavior is intentionally not inferred. The resolver owns one shared render texture per
/// exact TEX. Each returned material acquires its own texture lease and remains valid after this
/// resolver releases the owning texture handles.
/// </remarks>
internal sealed class WildAnimalModelMaterialResolver : IDisposable {
  private const string TextureStyleTag = ":txs";
  private readonly IReadOnlyDictionary<
    WildAnimalSpeciesModelResourceSource,
    IReadOnlyDictionary<(int Group, int Mesh), WildAnimalModelMeshMaterialBinding>> bindings;
  private readonly Dictionary<
    WildAnimalModelTextureMaterialResource,
    RenderTexture> textures;
  private readonly RenderTexture[] ownedTextures;
  private readonly Action<RenderTexture> disposeTexture;
  private bool disposed;

  public WildAnimalModelMaterialResolver(
    WildAnimalModelMaterialResourceBridgeResult resources
  ) : this(resources, WildAnimalModelMaterialResolverOperations.Default) { }

  internal WildAnimalModelMaterialResolver(
    WildAnimalModelMaterialResourceBridgeResult resources,
    WildAnimalModelMaterialResolverOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(operations);
    if (operations.CreateTexture == null || operations.DisposeTexture == null)
      throw new ArgumentException(
        "Wild-animal material resolver operations cannot contain null delegates.",
        nameof(operations));
    if (resources.IsDisposed)
      throw Invalid("material bridge result is disposed");
    if (resources.Materials == null || resources.Bindings == null)
      throw Invalid("material bridge result has a null material or binding list");

    disposeTexture = operations.DisposeTexture;
    textures = new Dictionary<
      WildAnimalModelTextureMaterialResource,
      RenderTexture>(ReferenceEqualityComparer.Instance);
    var owned = new List<RenderTexture>();
    var ownedSet = new HashSet<RenderTexture>(ReferenceEqualityComparer.Instance);
    var textureReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    RenderTexture? pendingTexture = null;
    try {
      foreach (var material in resources.Materials) {
        ValidateMaterial(material);
        if (textures.ContainsKey(material) ||
            !textureReferences.Add(material.TextureReference))
          throw Invalid("material bridge repeats one exact TEX identity");
        pendingTexture = operations.CreateTexture(material)
          ?? throw Invalid(
            $"TEX '{material.TextureReference}' texture factory returned null");
        ValidateCreatedTexture(material, pendingTexture);
        if (!ownedSet.Add(pendingTexture)) {
          pendingTexture = null;
          throw Invalid("texture factory reused one render texture for distinct TEX identities");
        }
        owned.Add(pendingTexture);
        var texture = pendingTexture;
        pendingTexture = null;
        textures.Add(material, texture);
      }
      bindings = IndexBindings(resources.Bindings, textures);
      ownedTextures = owned.ToArray();
    } catch (Exception primaryError) {
      var cleanupErrors = new List<Exception>();
      if (pendingTexture != null)
        TryDisposeTexture(pendingTexture, disposeTexture, cleanupErrors);
      cleanupErrors.AddRange(DisposeTextures(owned, disposeTexture));
      if (cleanupErrors.Count != 0)
        throw new AggregateException([primaryError, .. cleanupErrors]);
      throw;
    }
  }

  public bool IsDisposed => disposed;

  public Material ResolveMaterial(
    WildAnimalSavedVariantSelection selection,
    ModelDefinitionMeshBatch batch
  ) {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(selection);
    ArgumentNullException.ThrowIfNull(batch);
    if (selection.Variant?.Template?.ModelSource == null)
      throw Invalid("saved variant selection has no exact MDL source");
    var modelSource = selection.Variant.Template.ModelSource;
    if (selection.Variant.VariantLink == null ||
        !ReferenceEquals(selection.Variant.VariantLink.ModelSource, modelSource) ||
        selection.SerializedVariantIndex != selection.Variant.VariantLink.SerializedIndex ||
        selection.Animal == null || selection.Animal.Type != selection.SerializedVariantIndex)
      throw Invalid("saved variant selection changed its exact serialized MDL identity");
    if (batch.Mesh == null || batch.Mesh.State == OpenCobra.GDK.State.Disposed)
      throw Invalid("selected MDL batch has a null or disposed template mesh");
    if (!ContainsExactBatch(selection.Variant.Template.Batches, batch))
      throw Invalid("selected MDL batch is not owned by the selected exact template");
    if (!bindings.TryGetValue(modelSource, out var modelBindings))
      throw Invalid($"selected MDL '{modelSource.Identity}' has no material binding set");
    if (!modelBindings.TryGetValue(
          (batch.SourceGroupIndex, batch.SourceMeshIndex),
          out var binding))
      throw Invalid(
        $"selected MDL '{modelSource.Identity}' batch {batch.SourceGroupIndex}/" +
        $"{batch.SourceMeshIndex} has no exact material binding");
    if (!string.Equals(
          binding.SourceMeshName,
          batch.SourceMeshName,
          StringComparison.Ordinal) ||
        !textures.TryGetValue(binding.Material, out var texture))
      throw Invalid(
        $"selected MDL '{modelSource.Identity}' batch changed exact material identity");

    var style = ResolveStyle(binding.Material.TextureStyleReference);
    var material = style.AlphaReference.HasValue
      ? new Textured(style.BlendMode, style.AlphaReference.Value)
      : new Textured(style.BlendMode);

    try {
      material.AlbedoTexture = texture;
      material.CullBackFaces = style.CullBackFaces;
      return material;
    } catch {
      material.Dispose();
      throw;
    }
  }

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    var errors = DisposeTextures(ownedTextures, disposeTexture);
    if (errors.Count != 0)
      throw new AggregateException(
        "Wild-animal render texture cleanup reported errors.",
        errors);
  }

  private static IReadOnlyDictionary<
    WildAnimalSpeciesModelResourceSource,
    IReadOnlyDictionary<(int Group, int Mesh), WildAnimalModelMeshMaterialBinding>>
    IndexBindings(
      IReadOnlyList<WildAnimalModelMeshMaterialBinding> source,
      IReadOnlyDictionary<WildAnimalModelTextureMaterialResource, RenderTexture> textures
    ) {
    var mutable = new Dictionary<
      WildAnimalSpeciesModelResourceSource,
      Dictionary<(int Group, int Mesh), WildAnimalModelMeshMaterialBinding>>(
        ReferenceEqualityComparer.Instance);
    foreach (var binding in source) {
      if (binding?.ModelSource?.Resource == null || binding.Material == null)
        throw Invalid("material binding list contains an incomplete binding");
      if (!textures.ContainsKey(binding.Material))
        throw Invalid(
          $"MDL '{binding.ModelSource.Identity}' binding references an undeclared TEX");
      ValidateBindingIdentity(binding);
      if (!mutable.TryGetValue(binding.ModelSource, out var modelBindings))
        mutable.Add(binding.ModelSource, modelBindings = []);
      if (!modelBindings.TryAdd(
            (binding.SourceGroupIndex, binding.SourceMeshIndex),
            binding))
        throw Invalid(
          $"MDL '{binding.ModelSource.Identity}' repeats material binding " +
          $"{binding.SourceGroupIndex}/{binding.SourceMeshIndex}");
    }

    var result = new Dictionary<
      WildAnimalSpeciesModelResourceSource,
      IReadOnlyDictionary<(int Group, int Mesh), WildAnimalModelMeshMaterialBinding>>(
        ReferenceEqualityComparer.Instance);
    foreach (var pair in mutable)
      result.Add(pair.Key, pair.Value);
    return result;
  }

  private static void ValidateMaterial(WildAnimalModelTextureMaterialResource? material) {
    if (material?.TextureFile == null || material.Albedo == null)
      throw Invalid("material list contains an incomplete TEX resource");
    if (material.TextureFile.Type != FileType.Texture ||
        !string.Equals(
          material.Albedo.Name,
          material.TextureFile.Name,
          StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(material.TextureReference) ||
        !string.Equals(
          material.TextureReference,
          $"{material.TextureFile.Name}:tex",
          StringComparison.OrdinalIgnoreCase) ||
        material.TextureDataAddress == 0)
      throw Invalid("material list contains a changed TEX identity");
    _ = ResolveStyle(material.TextureStyleReference);
    if (material.Albedo.MipLevels == null || material.Albedo.MipLevels.Length == 0 ||
        material.Albedo.MipLevels[0] == null ||
        material.Albedo.MipLevels[0].Width <= 0 ||
        material.Albedo.MipLevels[0].Height <= 0)
      throw Invalid($"TEX '{material.TextureReference}' has no decoded base pixels");
  }

  private static void ValidateCreatedTexture(
    WildAnimalModelTextureMaterialResource material,
    RenderTexture texture
  ) {
    var pixels = material.Albedo.MipLevels[0];
    if (texture.State == OpenCobra.GDK.State.Disposed ||
        !string.Equals(texture.Name, material.TextureReference, StringComparison.Ordinal) ||
        texture.Width != pixels.Width || texture.Height != pixels.Height ||
        texture.Pixels == null || texture.Pixels.Width != pixels.Width ||
        texture.Pixels.Height != pixels.Height)
      throw Invalid(
        $"TEX '{material.TextureReference}' texture factory changed exact identity or pixels");
  }

  private static void ValidateBindingIdentity(
    WildAnimalModelMeshMaterialBinding binding
  ) {
    var source = binding.ModelSource;
    var model = source.Resource;
    if (source.File == null ||
        source.File.Type != FileType.Model ||
        !string.Equals(source.File.Name, model.Name, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"MDL '{source.Identity}' binding changed its exact model source");
    if (model.Groups == null || model.Strings == null ||
        binding.SourceGroupIndex < 0 || binding.SourceGroupIndex >= model.Groups.Count)
      throw Invalid(
        $"MDL '{source.Identity}' binding is outside its exact group range");
    var group = model.Groups[binding.SourceGroupIndex];
    if (group == null || group.Meshes == null || group.SurfaceRecords == null ||
        binding.SourceMeshIndex < 0 || binding.SourceMeshIndex >= group.Meshes.Count ||
        group.Meshes[binding.SourceMeshIndex] == null)
      throw Invalid(
        $"MDL '{source.Identity}' binding is outside its exact mesh range");
    if (binding.SourceSurfaceIndex < 0 ||
        binding.SourceSurfaceIndex >= group.SurfaceRecords.Count ||
        group.SurfaceRecords[binding.SourceSurfaceIndex] == null ||
        binding.TextureStringIndex >= model.Strings.Count)
      throw Invalid(
        $"MDL '{source.Identity}' binding changed its surface or TEX string range");

    var surface = group.SurfaceRecords[binding.SourceSurfaceIndex];
    var expectedValue = (1u << 16) | Convert.ToUInt32(binding.SourceMeshIndex);
    if (surface.Field0 != binding.TextureStringIndex || surface.ValuesA == null ||
        !surface.ValuesA.Contains(expectedValue) ||
        !string.Equals(
          model.Strings[binding.TextureStringIndex],
          binding.Material.TextureFile.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"MDL '{source.Identity}' binding changed exact surface-to-TEX identity");
  }

  private static string ParseStyleName(string? reference) {
    if (string.IsNullOrWhiteSpace(reference) ||
        !reference.EndsWith(TextureStyleTag, StringComparison.OrdinalIgnoreCase) ||
        reference.Length == TextureStyleTag.Length ||
        reference.IndexOf(':') != reference.LastIndexOf(':') ||
        !string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
      throw Invalid($"TXS reference '{reference}' is malformed");
    return reference[..^TextureStyleTag.Length];
  }

  private static MaterialStyle ResolveStyle(string? reference) {
    var style = ParseStyleName(reference);
    var doubleSided = style.EndsWith("DS", StringComparison.OrdinalIgnoreCase);
    if (style.StartsWith("SIOpaque", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.Opaque, null, !doubleSided);
    if (style.StartsWith("SIAlphaMaskLow", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.AlphaMask, 100, !doubleSided);
    if (style.StartsWith("SIAlphaMask", StringComparison.OrdinalIgnoreCase))
      return new(
        MaterialBlendMode.AlphaMask,
        Textured.DefaultAlphaMaskReference,
        !doubleSided);
    // Installed animal fur and feather alpha surfaces use this family as cutouts. The threshold
    // matches the existing scenery and ride-car material approximation for SIAlpha styles.
    if (style.StartsWith("SIAlpha", StringComparison.OrdinalIgnoreCase))
      return new(MaterialBlendMode.AlphaMask, 8, !doubleSided);
    throw Invalid($"TXS reference '{reference}' uses an unsupported style family");
  }

  private static bool ContainsExactBatch(
    IReadOnlyList<ModelDefinitionMeshBatch>? batches,
    ModelDefinitionMeshBatch target
  ) {
    if (batches == null) return false;
    foreach (var batch in batches)
      if (ReferenceEquals(batch, target)) return true;
    return false;
  }

  private static List<Exception> DisposeTextures(
    IReadOnlyList<RenderTexture> values,
    Action<RenderTexture> disposeTexture
  ) {
    var errors = new List<Exception>();
    for (var index = values.Count - 1; index >= 0; index--)
      TryDisposeTexture(values[index], disposeTexture, errors);
    return errors;
  }

  private static void TryDisposeTexture(
    RenderTexture texture,
    Action<RenderTexture> disposeTexture,
    ICollection<Exception> errors
  ) {
    try {
      disposeTexture(texture);
    } catch (Exception error) {
      errors.Add(error);
    }
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild-animal model material resolution: {message}.");

  private readonly record struct MaterialStyle(
    MaterialBlendMode BlendMode,
    byte? AlphaReference,
    bool CullBackFaces
  );
}

internal sealed record WildAnimalModelMaterialResolverOperations(
  Func<WildAnimalModelTextureMaterialResource, RenderTexture> CreateTexture,
  Action<RenderTexture> DisposeTexture
) {
  public static WildAnimalModelMaterialResolverOperations Default { get; } = new(
    Create,
    texture => texture.Dispose());

  private static RenderTexture Create(WildAnimalModelTextureMaterialResource material) =>
    RenderTexture.FromDecoded(material.TextureReference, material.Albedo);
}
