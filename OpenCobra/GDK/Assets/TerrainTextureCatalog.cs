// Terrain Texture Catalog
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using OpenCobra.OVL.Files;

using Texture = OpenCobra.GDK.Materials.Texture;

[assembly: InternalsVisibleTo("Tests")]

namespace OpenCobra.GDK.Assets;

internal enum TerrainTextureCatalogLayer {
  Base,
  CompleteEditionExpansion
}

internal sealed record TerrainTextureCatalogEntry(
  string TerrainName,
  uint Number,
  TerrainTypeKind Kind,
  TerrainParameters Parameters,
  string TextureReference,
  Texture Texture,
  TerrainTextureCatalogLayer Layer,
  string OwningCommonOvlPath
);

/// <summary>
/// Stable, numerically ordered terrain and cliff textures composed from the shipped terrain OVL
/// pairs.
/// </summary>
public sealed class TerrainTextureCatalog : IDisposable {
  public const int BaseSurfaceCount = 26;
  public const int SurfaceCount = 32;
  public const int CliffCount = 6;
  internal const string SurfacePrefix = "Terrain_";
  internal const string CliffPrefix = "TerrainCliff";

  private readonly ReadOnlyCollection<Texture> surfaceTextures;
  private readonly ReadOnlyCollection<Texture> cliffTextures;
  private readonly ReadOnlyCollection<string> surfaceNames;
  private readonly ReadOnlyCollection<string> cliffNames;
  private readonly ReadOnlyCollection<TerrainTypeKind> surfaceKinds;
  private readonly ReadOnlyCollection<TerrainTypeKind> cliffKinds;
  private readonly ReadOnlyCollection<TerrainParameters> surfaceParameters;
  private readonly ReadOnlyCollection<TerrainParameters> cliffParameters;
  private readonly bool includesCompleteEditionExpansion;
  private Action<TerrainTextureCatalog>? disposalCallback;
  private int disposed;

  internal TerrainTextureCatalog(
    IEnumerable<TerrainTextureCatalogEntry> entries,
    bool includesCompleteEditionExpansion
  ) {
    var ownedEntries = new List<TerrainTextureCatalogEntry>();
    try {
      ArgumentNullException.ThrowIfNull(entries);
      foreach (var entry in entries) ownedEntries.Add(entry);
      ValidateEntries(ownedEntries, includesCompleteEditionExpansion);
      var surfaces = OrderAndValidate(
        ownedEntries,
        entry => entry.Kind is TerrainTypeKind.GroundUnblended or TerrainTypeKind.GroundBlended,
        "surface",
        includesCompleteEditionExpansion ? SurfaceCount : BaseSurfaceCount);
      var cliffs = OrderAndValidate(
        ownedEntries, entry => entry.Kind == TerrainTypeKind.Cliff, "cliff", CliffCount);
      surfaceTextures = Array.AsReadOnly(surfaces.Select(entry => entry.Texture).ToArray());
      cliffTextures = Array.AsReadOnly(cliffs.Select(entry => entry.Texture).ToArray());
      surfaceNames = Array.AsReadOnly(surfaces.Select(entry => entry.Texture.Name).ToArray());
      cliffNames = Array.AsReadOnly(cliffs.Select(entry => entry.Texture.Name).ToArray());
      surfaceKinds = Array.AsReadOnly(surfaces.Select(entry => entry.Kind).ToArray());
      cliffKinds = Array.AsReadOnly(cliffs.Select(entry => entry.Kind).ToArray());
      surfaceParameters = Array.AsReadOnly(surfaces.Select(entry => entry.Parameters).ToArray());
      cliffParameters = Array.AsReadOnly(cliffs.Select(entry => entry.Parameters).ToArray());
      this.includesCompleteEditionExpansion = includesCompleteEditionExpansion;
    } catch {
      foreach (var texture in ownedEntries
        .OfType<TerrainTextureCatalogEntry>()
        .Select(entry => entry.Texture)
        .OfType<Texture>()
        .Distinct())
        texture.Dispose();
      throw;
    }
  }

  /// <summary>Surface textures ordered by their numeric <c>Terrain_XX</c> suffix.</summary>
  public IReadOnlyList<Texture> SurfaceTextures {
    get {
      ThrowIfDisposed();
      return surfaceTextures;
    }
  }

  /// <summary>Cliff textures ordered by their numeric <c>TerrainCliffN</c> suffix.</summary>
  public IReadOnlyList<Texture> CliffTextures {
    get {
      ThrowIfDisposed();
      return cliffTextures;
    }
  }

  /// <summary>Surface resource names in index order.</summary>
  public IReadOnlyList<string> SurfaceNames {
    get {
      ThrowIfDisposed();
      return surfaceNames;
    }
  }

  /// <summary>Cliff resource names in index order.</summary>
  public IReadOnlyList<string> CliffNames {
    get {
      ThrowIfDisposed();
      return cliffNames;
    }
  }

  /// <summary>Gets the texture addressed by a terrain cell's surface index.</summary>
  public Texture GetSurface(byte index) {
    ThrowIfDisposed();
    ValidateSurfaceIndex(index);
    return surfaceTextures[index];
  }

  /// <summary>Gets the decoded rendering kind addressed by a terrain cell's surface index.</summary>
  public TerrainTypeKind GetSurfaceKind(byte index) {
    ThrowIfDisposed();
    ValidateSurfaceIndex(index);
    return surfaceKinds[index];
  }

  /// <summary>Gets the decoded rendering parameters addressed by a terrain cell's surface index.</summary>
  public TerrainParameters GetSurfaceParameters(byte index) {
    ThrowIfDisposed();
    ValidateSurfaceIndex(index);
    return surfaceParameters[index];
  }

  /// <summary>Gets the texture addressed by a terrain cell's cliff index.</summary>
  public Texture GetCliff(byte index) {
    ThrowIfDisposed();
    ValidateCliffIndex(index);
    return cliffTextures[index];
  }

  /// <summary>Gets the decoded rendering kind addressed by a terrain cell's cliff index.</summary>
  public TerrainTypeKind GetCliffKind(byte index) {
    ThrowIfDisposed();
    ValidateCliffIndex(index);
    return cliffKinds[index];
  }

  /// <summary>Gets the decoded rendering parameters addressed by a terrain cell's cliff index.</summary>
  public TerrainParameters GetCliffParameters(byte index) {
    ThrowIfDisposed();
    ValidateCliffIndex(index);
    return cliffParameters[index];
  }

  internal static bool IsCatalogAssetName(string name) =>
    TryParseIndex(name, SurfacePrefix, out _) || TryParseIndex(name, CliffPrefix, out _);

  internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

  internal void SetDisposalCallback(Action<TerrainTextureCatalog> callback) {
    disposalCallback = callback;
    if (Volatile.Read(ref disposed) == 0) return;
    Interlocked.Exchange(ref disposalCallback, null)?.Invoke(this);
  }

  private static TerrainTextureCatalogEntry[] OrderAndValidate(
    IEnumerable<TerrainTextureCatalogEntry> entries,
    Func<TerrainTextureCatalogEntry, bool> predicate,
    string kind,
    int expectedCount
  ) {
    var indexed = entries
      .Where(predicate)
      .OrderBy(entry => entry.Number)
      .ToArray();

    if (indexed.Length == 0)
      throw new InvalidDataException($"The terrain catalog contains no {kind} textures.");

    var duplicate = indexed.GroupBy(entry => entry.Number).FirstOrDefault(group => group.Count() > 1);
    if (duplicate != null)
      throw new InvalidDataException(
        $"The terrain catalog contains duplicate {kind} index {duplicate.Key}.");

    foreach (var (entry, expectedIndex) in indexed.Select((entry, index) => (entry, index))) {
      if (entry.Number != Convert.ToUInt32(expectedIndex))
        throw new InvalidDataException(
          $"The terrain catalog is missing {kind} index {expectedIndex}.");
    }

    if (indexed.Length < expectedCount)
      throw new InvalidDataException(
        $"The terrain catalog is missing {kind} index {indexed.Length}.");
    if (indexed.Length != expectedCount)
      throw new InvalidDataException(
        $"The terrain catalog must contain exactly {expectedCount} {kind} textures; " +
        $"found {indexed.Length}.");

    return indexed;
  }

  private static void ValidateEntries(
    IEnumerable<TerrainTextureCatalogEntry> entries,
    bool includesCompleteEditionExpansion
  ) {
    foreach (var entry in entries) {
      ArgumentNullException.ThrowIfNull(entry);
      ArgumentNullException.ThrowIfNull(entry.Texture);
      ArgumentNullException.ThrowIfNull(entry.Parameters);
      if (string.IsNullOrWhiteSpace(entry.TerrainName))
        throw new InvalidDataException("Terrain resource names cannot be empty.");
      if (string.IsNullOrWhiteSpace(entry.TextureReference))
        throw new InvalidDataException(
          $"Terrain resource '{entry.TerrainName}' has an empty texture reference.");
      if (!string.Equals(entry.TextureReference, entry.Texture.Name, StringComparison.Ordinal))
        throw new InvalidDataException(
          $"Terrain resource '{entry.TerrainName}' resolved texture '{entry.Texture.Name}' instead " +
          $"of its declared '{entry.TextureReference}' reference.");
      if (string.IsNullOrWhiteSpace(entry.OwningCommonOvlPath))
        throw new InvalidDataException(
          $"Terrain resource '{entry.TerrainName}' has no owning OVL pair.");
      if (entry.Number > byte.MaxValue)
        throw new InvalidDataException(
          $"Terrain resource '{entry.TerrainName}' has unsupported number {entry.Number}.");
      if (!float.IsFinite(entry.Parameters.InvWidth) || entry.Parameters.InvWidth <= 0 ||
          !float.IsFinite(entry.Parameters.InvHeight) || entry.Parameters.InvHeight <= 0)
        throw new InvalidDataException(
          $"Terrain resource '{entry.TerrainName}' has invalid inverse texture dimensions.");

      switch (entry.Kind) {
        case TerrainTypeKind.GroundUnblended:
        case TerrainTypeKind.GroundBlended:
          ValidateSurfaceLayer(entry, includesCompleteEditionExpansion);
          break;
        case TerrainTypeKind.Cliff:
          if (entry.Layer != TerrainTextureCatalogLayer.Base)
            throw new InvalidDataException(
              $"Complete Edition terrain resource '{entry.TerrainName}' cannot supply cliff index " +
              $"{entry.Number}; Terrain_CT is a surface-only overlay.");
          if (entry.Number >= CliffCount)
            throw new InvalidDataException(
              $"Base terrain resource '{entry.TerrainName}' has out-of-range cliff index " +
              $"{entry.Number}; expected 0-{CliffCount - 1}.");
          break;
        default:
          throw new InvalidDataException(
            $"Terrain resource '{entry.TerrainName}' has unsupported kind " +
            $"{Convert.ToUInt32(entry.Kind)}.");
      }
    }
  }

  private static void ValidateSurfaceLayer(
    TerrainTextureCatalogEntry entry,
    bool includesCompleteEditionExpansion
  ) {
    if (entry.Number < BaseSurfaceCount) {
      if (entry.Layer != TerrainTextureCatalogLayer.Base)
        throw new InvalidDataException(
          $"Complete Edition terrain resource '{entry.TerrainName}' duplicates base surface index " +
          $"{entry.Number}.");
      return;
    }

    if (entry.Number >= SurfaceCount)
      throw new InvalidDataException(
        $"Terrain resource '{entry.TerrainName}' has out-of-range surface index {entry.Number}; " +
        $"expected 0-{SurfaceCount - 1}.");
    if (entry.Layer != TerrainTextureCatalogLayer.CompleteEditionExpansion)
      throw new InvalidDataException(
        $"Base terrain resource '{entry.TerrainName}' cannot supply Complete Edition surface index " +
        $"{entry.Number}.");
    if (!includesCompleteEditionExpansion)
      throw new InvalidDataException(
        $"Terrain resource '{entry.TerrainName}' was marked as an expansion surface without a " +
        "complete Terrain_CT OVL pair.");
  }

  private static bool TryParseIndex(string name, string prefix, out int index) {
    index = -1;
    if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
    var suffix = name[prefix.Length..];
    var expectedLength = prefix == SurfacePrefix ? 2 : 1;
    return suffix.Length == expectedLength
      && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out index)
      && index <= byte.MaxValue;
  }

  private static string FormatName(string prefix, int index) => prefix == SurfacePrefix
    ? $"{prefix}{index:D2}"
    : $"{prefix}{index}";

  private void ValidateSurfaceIndex(byte index) {
    if (!includesCompleteEditionExpansion && index >= BaseSurfaceCount && index < SurfaceCount)
      throw new InvalidDataException(
        $"Surface index {index} requires the Complete Edition Terrain_CT common/unique OVL pair; " +
        "the base Terrain_RCT3 catalog only provides indices 0-25.");
    if (index >= surfaceTextures.Count)
      throw new ArgumentOutOfRangeException(
        nameof(index), index,
        $"Surface index {index} is out of range for {surfaceTextures.Count} catalog entries.");
  }

  private void ValidateCliffIndex(byte index) {
    if (index >= cliffTextures.Count)
      throw new ArgumentOutOfRangeException(
        nameof(index), index,
        $"Cliff index {index} is out of range for {cliffTextures.Count} catalog entries.");
  }

  private void ThrowIfDisposed() =>
    ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

  public void Dispose() {
    if (Interlocked.Exchange(ref disposed, 1) != 0) return;
    foreach (var texture in surfaceTextures.Concat(cliffTextures).Distinct()) texture.Dispose();
    Interlocked.Exchange(ref disposalCallback, null)?.Invoke(this);
    GC.SuppressFinalize(this);
  }
}
