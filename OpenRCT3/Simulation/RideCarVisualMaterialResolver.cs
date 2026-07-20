// Ride Car Visual Material Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>The exact outcome of resolving one ride-car visual material.</summary>
internal enum RideCarVisualMaterialResolutionStatus {
  Resolved,
  MissingFlexibleTexture,
}

/// <summary>A fresh render material, or typed evidence for one missing nonblank FTX edge.</summary>
/// <remarks>The caller owns and must dispose a non-null <see cref="Material"/>.</remarks>
internal sealed record RideCarVisualMaterialResolution(
  RideCarVisualMaterialResolutionStatus Status,
  Material? Material,
  string? MissingFlexibleTextureReference
) {
  public bool IsResolved => Status == RideCarVisualMaterialResolutionStatus.Resolved;
}

/// <summary>
/// Lazily decodes exact ride-car FTX resources and creates one fresh material per render model.
/// </summary>
/// <remarks>
/// The retained ride-track load context is borrowed. This resolver owns decoded textures; materials
/// acquire leases so they can safely outlive resolver disposal.
/// </remarks>
internal sealed class RideCarVisualMaterialResolver : IDisposable {
  private const int MaximumTextureFamilies = 64 * 1024;
  private const int MaximumTextureVariants = 256 * 1024;
  private readonly object syncRoot = new();
  private readonly RideTrackResourceCatalogLoadContext resources;
  private readonly Func<Ovl, OvlFile, FlexiTextureList> decoder;
  private readonly Dictionary<ResourceKey, TextureFamily> textureFamilies =
    new(ResourceKeyComparer.Instance);
  private int textureVariantCount;
  private bool disposed;

  public bool IsDisposed => disposed;

  internal RideCarVisualMaterialResolver(RideTrackResourceCatalogLoadContext resources)
    : this(resources, FlexiTextureList.Load) { }

  internal RideCarVisualMaterialResolver(
    RideTrackResourceCatalogLoadContext resources,
    Func<Ovl, OvlFile, FlexiTextureList> decoder
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(decoder);
    this.resources = resources;
    this.decoder = decoder;
  }

  /// <summary>
  /// Resolves one material from the car's exact archive closure and owning track colours.
  /// </summary>
  internal RideCarVisualMaterialResolution Resolve(
    RideCarStaticInstanceEntry car,
    StaticShapeMeshBatch batch
  ) {
    ArgumentNullException.ThrowIfNull(car);
    ArgumentNullException.ThrowIfNull(batch);
    if (!car.IsResolved || car.BodyTemplate?.Link?.Car?.Source == null)
      throw Invalid("static car entry is not fully resource-resolved");
    if (!ReferenceEquals(car.CarRuntime.CarResource, car.BodyTemplate.Link.Car))
      throw Invalid("static car entry changed exact RIC link identity");
    if (!ContainsReference(car.BodyTemplate.Batches, batch))
      throw Invalid("material batch is not owned by the static car's exact body template");

    var track = car.CarRuntime.TrainRuntime.TrackRuntime.Track
      ?? throw Invalid("static car entry has no owning ride track");
    var colours = SceneryFlexiColours.FromSerialized(
      track.FlexiColour0,
      track.FlexiColour1,
      track.FlexiColour2);
    return Resolve(batch, car.BodyTemplate.Link.Car.Source.AllowedArchivePaths, colours);
  }

  /// <summary>
  /// Scene-builder adapter returning <see langword="null"/> only for a typed missing FTX edge.
  /// </summary>
  internal Material? ResolveMaterial(
    RideCarStaticInstanceEntry car,
    StaticShapeMeshBatch batch
  ) => Resolve(car, batch).Material;

  internal RideCarVisualMaterialResolution Resolve(
    StaticShapeMeshBatch batch,
    IReadOnlyList<string> allowedArchivePaths,
    SceneryFlexiColours flexiColours
  ) {
    ArgumentNullException.ThrowIfNull(batch);
    ArgumentNullException.ThrowIfNull(allowedArchivePaths);

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      var cullBackFaces = batch.Sides switch {
        1 => false,
        3 => true,
        _ => throw Invalid(
          $"batch '{batch.FtxRef}' has invalid sides {batch.Sides}"),
      };

      var engineGlobal = CreateEngineGlobalMaterial(batch);
      if (engineGlobal != null)
        return Resolved(SetCullBackFaces(engineGlobal, cullBackFaces));

      if (string.IsNullOrWhiteSpace(batch.FtxRef))
        return Resolved(SetCullBackFaces(new Flat(), cullBackFaces));

      var resource = resources.FindExactResource(
        allowedArchivePaths,
        batch.FtxRef,
        FileType.FlexibleTexture);
      if (resource == null)
        return new RideCarVisualMaterialResolution(
          RideCarVisualMaterialResolutionStatus.MissingFlexibleTexture,
          null,
          batch.FtxRef);

      var texture = ResolveTexture(resource, flexiColours);
      var blendMode = batch.Transparency switch {
        0 => MaterialBlendMode.Opaque,
        1 => MaterialBlendMode.AlphaMask,
        2 => MaterialBlendMode.Alpha,
        _ => throw Invalid(
          $"batch '{batch.FtxRef}' has invalid transparency {batch.Transparency}"),
      };
      var alphaReference = ResolveAlphaReference(batch.TxsRef);
      var material = alphaReference.HasValue
        ? new Textured(blendMode, alphaReference.Value)
        : new Textured(blendMode);
      try {
        material.AlbedoTexture = texture;
        material.CullBackFaces = cullBackFaces;
        return Resolved(material);
      } catch {
        material.Dispose();
        throw;
      }
    }
  }

  public void Dispose() {
    lock (syncRoot) {
      if (disposed) return;
      disposed = true;
      var errors = new List<Exception>();
      foreach (var family in textureFamilies.Values) {
        try {
          family.Dispose();
        } catch (Exception error) {
          errors.Add(error);
        }
      }
      textureFamilies.Clear();
      if (errors.Count > 0)
        throw new AggregateException(
          "Ride-car visual texture cleanup reported errors.",
          errors);
    }
    GC.SuppressFinalize(this);
  }

  private Texture ResolveTexture(
    RideTrackResourceCatalogEntry resource,
    SceneryFlexiColours colours
  ) {
    var key = new ResourceKey(resource.Archive, resource.File);
    if (!textureFamilies.TryGetValue(key, out var family)) {
      if (textureFamilies.Count >= MaximumTextureFamilies)
        throw Invalid($"texture-family count exceeds {MaximumTextureFamilies}");
      family = Decode(resource);
      textureFamilies.Add(key, family);
    }

    var texture = family.Resolve(
      colours,
      textureVariantCount < MaximumTextureVariants,
      out var createdVariant);
    if (createdVariant) textureVariantCount = checked(textureVariantCount + 1);
    return texture;
  }

  private TextureFamily Decode(RideTrackResourceCatalogEntry resource) {
    var decoded = decoder(resource.Archive, resource.File);
    if (decoded.Frames == null || decoded.Frames.Length == 0)
      throw Invalid($"FTX '{resource.File.Name}' contains no decoded frames");

    var firstImage = decoded.Frames[0].Texture;
    if (firstImage == null) {
      DisposeFrames(decoded.Frames, null);
      throw Invalid($"FTX '{resource.File.Name}' has no first-frame image");
    }

    Texture? texture = null;
    try {
      texture = new Texture(
        resource.File.Name,
        firstImage.Width,
        firstImage.Height,
        firstImage,
        decoded.Frames[0].Recolorable);
      DisposeFrames(decoded.Frames[1..], firstImage);
      return new TextureFamily(decoded.Frames[0], texture);
    } catch {
      texture?.Dispose();
      DisposeFrames(decoded.Frames, texture == null ? null : firstImage);
      throw;
    }
  }

  private static Material? CreateEngineGlobalMaterial(StaticShapeMeshBatch batch) {
    if (string.Equals(batch.TxsRef, "SIWater:txs", StringComparison.OrdinalIgnoreCase)) {
      if (batch.Transparency != 2)
        throw Invalid(
          $"SIWater batch '{batch.FtxRef}' must use alpha transparency 2, got " +
          $"{batch.Transparency}");
      return new Water();
    }

    if (string.Equals(
          batch.TxsRef,
          "SIOpaqueChrome:txs",
          StringComparison.OrdinalIgnoreCase)) {
      if (batch.Transparency != 0)
        throw Invalid(
          $"SIOpaqueChrome batch '{batch.FtxRef}' must be opaque, got transparency " +
          $"{batch.Transparency}");
      return new Chrome();
    }
    return null;
  }

  private static byte? ResolveAlphaReference(string? taggedReference) {
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMaskLow")) return 100;
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMask"))
      return Textured.DefaultAlphaMaskReference;
    if (IsTextureStyleFamily(taggedReference, "SIAlpha")) return 8;
    return null;
  }

  private static bool IsTextureStyleFamily(
    string? taggedReference,
    string familyName
  ) {
    const string tag = ":txs";
    if (string.IsNullOrEmpty(taggedReference) ||
        !taggedReference.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
      return false;
    var resourceName = taggedReference[..^tag.Length];
    return resourceName.StartsWith(familyName, StringComparison.OrdinalIgnoreCase);
  }

  private static RideCarVisualMaterialResolution Resolved(Material material) =>
    new(RideCarVisualMaterialResolutionStatus.Resolved, material, null);

  private static Material SetCullBackFaces(Material material, bool cullBackFaces) {
    try {
      material.CullBackFaces = cullBackFaces;
      return material;
    } catch {
      material.Dispose();
      throw;
    }
  }

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T expected)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, expected)) return true;
    return false;
  }

  private static void DisposeFrames(
    IEnumerable<FlexiTexture> frames,
    SixLabors.ImageSharp.Image? retainedImage
  ) {
    foreach (var frame in frames) {
      if (frame.Texture != null && !ReferenceEquals(frame.Texture, retainedImage))
        frame.Texture.Dispose();
    }
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-car visual material: {message}.");

  private readonly record struct ResourceKey(Ovl Archive, OvlFile File);

  private readonly record struct VariantKey(int First, int Second, int Third) {
    public static VariantKey Create(
      Recolorable recolorable,
      SceneryFlexiColours colours
    ) => new(
      (recolorable & Recolorable.First) != 0 ? colours.First : 0,
      (recolorable & Recolorable.Second) != 0 ? colours.Second : 0,
      (recolorable & Recolorable.Third) != 0 ? colours.Third : 0);
  }

  private sealed class TextureFamily(FlexiTexture frame, Texture original) : IDisposable {
    private readonly Dictionary<VariantKey, Texture> variants = [];

    public Texture Resolve(
      SceneryFlexiColours colours,
      bool canCreateVariant,
      out bool createdVariant
    ) {
      createdVariant = false;
      if (frame.Recolorable == Recolorable.None) return original;

      var key = VariantKey.Create(frame.Recolorable, colours);
      if (variants.TryGetValue(key, out var cached)) return cached;
      if (!canCreateVariant)
        throw Invalid($"texture-variant count exceeds {MaximumTextureVariants}");

      var image = FlexiTextureRecolorer.Recolor(
        frame,
        FlexiColourPalette.Get(key.First),
        FlexiColourPalette.Get(key.Second),
        FlexiColourPalette.Get(key.Third));
      Texture? texture = null;
      try {
        texture = new Texture(
          original.Name,
          image.Width,
          image.Height,
          image,
          frame.Recolorable);
        variants.Add(key, texture);
        createdVariant = true;
        return texture;
      } catch {
        if (texture == null) image.Dispose();
        else texture.Dispose();
        throw;
      }
    }

    public void Dispose() {
      foreach (var texture in variants.Values)
        texture.Dispose();
      variants.Clear();
      original.Dispose();
    }
  }

  private sealed class ResourceKeyComparer : IEqualityComparer<ResourceKey> {
    public static ResourceKeyComparer Instance { get; } = new();

    public bool Equals(ResourceKey x, ResourceKey y) =>
      ReferenceEquals(x.Archive, y.Archive) && ReferenceEquals(x.File, y.File);

    public int GetHashCode(ResourceKey key) => HashCode.Combine(
      RuntimeHelpers.GetHashCode(key.Archive),
      RuntimeHelpers.GetHashCode(key.File));
  }
}
