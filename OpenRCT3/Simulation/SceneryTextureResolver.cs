// Scenery Texture Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>
/// Lazily decodes and owns the first frame of exact scenery flexi-texture references.
/// </summary>
/// <remarks>
/// The resource catalog remains owned by the caller. Disposing this resolver releases its texture
/// ownership; materials that leased a texture keep it alive until they are also disposed.
/// </remarks>
public sealed class SceneryTextureResolver : IDisposable {
  private readonly object syncRoot = new();
  private readonly SceneryResourceCatalog resources;
  private readonly Func<Ovl, OvlFile, FlexiTextureList> decoder;
  private readonly TextureSamplingMode samplingMode;
  private readonly Dictionary<ResourceKey, TextureFamily> textureFamilies =
    new(ResourceKeyComparer.Instance);
  private bool disposed;

  /// <summary>Creates a resolver over an existing scenery resource catalog.</summary>
  public SceneryTextureResolver(SceneryResourceCatalog resources)
    : this(resources, FlexiTextureList.Load, TextureSamplingMode.GeneratedMipmaps) { }

  internal SceneryTextureResolver(
    SceneryResourceCatalog resources,
    TextureSamplingMode samplingMode
  ) : this(resources, FlexiTextureList.Load, samplingMode) { }

  internal SceneryTextureResolver(
    SceneryResourceCatalog resources,
    Func<Ovl, OvlFile, FlexiTextureList> decoder,
    TextureSamplingMode samplingMode = TextureSamplingMode.GeneratedMipmaps
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(decoder);
    this.resources = resources;
    this.decoder = decoder;
    this.samplingMode = samplingMode;
  }

  /// <summary>Resolves a required exact <c>name:ftx</c> reference.</summary>
  public Texture Resolve(string taggedReference) {
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    if (TryResolve(taggedReference, out var texture)) return texture;
    throw new KeyNotFoundException(
      $"Flexi-texture resource '{taggedReference}' was not found.");
  }

  /// <summary>
  /// Resolves a required exact <c>name:ftx</c> reference with the placed object's flexible-colour
  /// selections applied to every active recolour channel.
  /// </summary>
  public Texture Resolve(string taggedReference, SceneryFlexiColours flexiColours) {
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    if (TryResolve(taggedReference, flexiColours, out var texture)) return texture;
    throw new KeyNotFoundException(
      $"Flexi-texture resource '{taggedReference}' was not found.");
  }

  /// <summary>
  /// Tries to resolve an optional exact <c>name:ftx</c> reference. Missing references return
  /// <see langword="false"/>; malformed references and malformed referenced data still throw.
  /// </summary>
  public bool TryResolve(
    string? taggedReference,
    [NotNullWhen(true)] out Texture? texture
  ) => TryResolve(taggedReference, null, null, null, out texture);

  /// <summary>
  /// Tries to resolve an optional exact <c>name:ftx</c> reference with the placed object's
  /// flexible-colour selections applied. Selections for inactive channels do not affect cache
  /// identity.
  /// </summary>
  public bool TryResolve(
    string? taggedReference,
    SceneryFlexiColours flexiColours,
    [NotNullWhen(true)] out Texture? texture
  ) => TryResolve(
    taggedReference,
    (SceneryFlexiColours?)flexiColours,
    null,
    null,
    out texture);

  /// <summary>
  /// Resolves from the exact shape dependency closure, then the exact visual dependency closure,
  /// before consulting the catalog's global overlay fallbacks.
  /// </summary>
  internal bool TryResolveFrom(
    SceneryResourceEntry? shapeSource,
    SceneryResourceEntry? visualSource,
    string? taggedReference,
    SceneryFlexiColours flexiColours,
    [NotNullWhen(true)] out Texture? texture
  ) => TryResolve(
    taggedReference,
    flexiColours,
    shapeSource,
    visualSource,
    out texture);

  private bool TryResolve(
    string? taggedReference,
    SceneryFlexiColours? flexiColours,
    SceneryResourceEntry? shapeSource,
    SceneryResourceEntry? visualSource,
    [NotNullWhen(true)] out Texture? texture
  ) {
    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (string.IsNullOrWhiteSpace(taggedReference)) {
        texture = null;
        return false;
      }

      var reference = SceneryResourceCatalog.ParseTaggedReference(taggedReference);
      if (reference.Type != FileType.FlexibleTexture)
        throw new ArgumentException(
          $"Scenery textures require an 'ftx' reference, not '{reference.Type.ToTagString()}'.",
          nameof(taggedReference));

      var resource = shapeSource == null
        ? null
        : resources.FindWithinOwnerClosure(
          shapeSource,
          reference.Name,
          reference.Type);
      if (resource == null && visualSource != null &&
          !SameSource(shapeSource, visualSource))
        resource = resources.FindWithinOwnerClosure(
          visualSource,
          reference.Name,
          reference.Type);
      resource ??= resources.Find(reference.Name, reference.Type);
      if (resource == null) {
        texture = null;
        return false;
      }

      var key = new ResourceKey(resource.Archive, resource.File);
      if (!textureFamilies.TryGetValue(key, out var family)) {
        family = Decode(resource);
        textureFamilies.Add(key, family);
      }

      texture = flexiColours.HasValue
        ? family.Resolve(flexiColours.Value)
        : family.Original;
      return true;
    }
  }

  private static bool SameSource(
    SceneryResourceEntry? first,
    SceneryResourceEntry second
  ) => first != null &&
    ReferenceEquals(first.Archive, second.Archive) &&
    first.File == second.File;

  /// <inheritdoc />
  public void Dispose() {
    lock (syncRoot) {
      if (disposed) return;
      disposed = true;
      foreach (var family in textureFamilies.Values)
        family.Dispose();
      textureFamilies.Clear();
    }

    GC.SuppressFinalize(this);
  }

  private TextureFamily Decode(SceneryResourceEntry resource) {
    var decoded = decoder(resource.Archive, resource.File);
    if (decoded.Frames == null || decoded.Frames.Length == 0)
      throw new InvalidDataException(
        $"Flexi-texture '{resource.File.Name}' contains no decoded frames.");

    var firstImage = decoded.Frames[0].Texture;
    if (firstImage == null) {
      DisposeFrames(decoded.Frames, null);
      throw new InvalidDataException(
        $"Flexi-texture '{resource.File.Name}' has no first-frame image.");
    }

    Texture? texture = null;
    try {
      texture = new Texture(
        resource.File.Name,
        firstImage.Width,
        firstImage.Height,
        firstImage,
        decoded.Frames[0].Recolorable,
        samplingMode);
      DisposeFrames(decoded.Frames[1..], firstImage);
      return new TextureFamily(decoded.Frames[0], texture);
    } catch {
      texture?.Dispose();
      DisposeFrames(decoded.Frames, texture == null ? null : firstImage);
      throw;
    }
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

    public Texture Original { get; } = original;

    public Texture Resolve(SceneryFlexiColours colours) {
      if (frame.Recolorable == Recolorable.None) return Original;

      var key = VariantKey.Create(frame.Recolorable, colours);
      if (variants.TryGetValue(key, out var cached)) return cached;

      var image = FlexiTextureRecolorer.Recolor(
        frame,
        FlexiColourPalette.Get(key.First),
        FlexiColourPalette.Get(key.Second),
        FlexiColourPalette.Get(key.Third));
      Texture? texture = null;
      try {
        texture = new Texture(
          Original.Name,
          image.Width,
          image.Height,
          image,
          frame.Recolorable,
          Original.SamplingMode);
        variants.Add(key, texture);
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
      Original.Dispose();
    }
  }

  private sealed class ResourceKeyComparer : IEqualityComparer<ResourceKey> {
    public static ResourceKeyComparer Instance { get; } = new();

    public bool Equals(ResourceKey x, ResourceKey y) =>
      ReferenceEquals(x.Archive, y.Archive) &&
      x.File.Type == y.File.Type &&
      string.Equals(x.File.Name, y.File.Name, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(x.File.Path, y.File.Path, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(ResourceKey key) => HashCode.Combine(
      RuntimeHelpers.GetHashCode(key.Archive),
      key.File.Type,
      StringComparer.OrdinalIgnoreCase.GetHashCode(key.File.Name),
      StringComparer.OrdinalIgnoreCase.GetHashCode(key.File.Path));
  }
}
