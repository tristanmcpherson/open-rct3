// TextureLoader
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenCobra.GDK.Assets;

public static class TextureLoader {
  private static readonly TerrainTextureCatalogCache terrainCatalogs =
    new(LoadTerrainCatalogUncached);

  public static Texture LoadTexture(string ovlPath, string name) {
    if (!File.Exists(ovlPath)) throw new FileNotFoundException(ovlPath);
    using var ovl = Ovl.Load(ovlPath);
    return LoadTexture(ovl, ovl.Find(name, FileType.Texture) ?? throw new AssetException($"Texture '{name}' not found in OVL."));
  }

  public static Texture LoadTexture(Ovl ovl, OvlFile file) {
    try {
      using var textures = Textures.Extract(ovl);
      return CreateTexture(file, textures[file.ToString()]);
    }
    catch (Exception ex) {
      throw new AssetException(file.Name, ex);
    }
  }

  /// <summary>
  /// Loads and caches the numerically ordered terrain/cliff texture catalog from the base terrain
  /// OVL pair and, when installed, the shipped Complete Edition <c>Terrain_CT</c> overlay pair. The
  /// same live catalog is returned for equivalent base paths until it is disposed.
  /// </summary>
  public static TerrainTextureCatalog LoadTerrainCatalog(string ovlPath) =>
    terrainCatalogs.Get(GetTerrainCommonPath(ovlPath));

  public static Texture LoadFlexiTexture(string ovlPath, string name) {
    if (!File.Exists(ovlPath)) throw new FileNotFoundException(ovlPath);
    using var ovl = Ovl.Load(ovlPath);

    var textures = FlexiTextureList.Load(ovl, ovl.Find(name, FileType.FlexibleTexture) ??
      throw new AssetException($"Flexi-texture '{name}' not found in OVL."));
    return new Texture(name, textures.Width, textures.Height, textures[0].Texture, textures.Recolorable);
  }

  public static AnimatedTexture LoadAnimatedTexture(string ovlPath, string name) {
    if (!File.Exists(ovlPath)) throw new FileNotFoundException(ovlPath);
    using var ovl = Ovl.Load(ovlPath);

    var textures = FlexiTextureList.Load(ovl, ovl.Find(name, FileType.FlexibleTexture) ??
      throw new AssetException($"Flexi-texture '{name}' not found in OVL."));
    return new AnimatedTexture(name, textures);
  }

  private static TerrainTextureCatalog LoadTerrainCatalogUncached(string commonPath) {
    var uniquePath = GetPairedPath(commonPath, ".unique.ovl");
    if (!File.Exists(commonPath))
      throw new FileNotFoundException($"Terrain common OVL not found: {commonPath}", commonPath);
    if (!File.Exists(uniquePath))
      throw new FileNotFoundException($"Terrain unique OVL not found: {uniquePath}", uniquePath);
    var expansionCommonPath = FindCompleteEditionTerrainCommonPath(commonPath);

    var entries = new List<TerrainTextureCatalogEntry>();
    try {
      entries.AddRange(LoadTerrainPair(commonPath, TerrainTextureCatalogLayer.Base));
      if (expansionCommonPath != null)
        entries.AddRange(LoadTerrainPair(
          expansionCommonPath, TerrainTextureCatalogLayer.CompleteEditionExpansion));
    } catch (AssetException) {
      DisposeEntries(entries);
      throw;
    } catch (Exception ex) {
      DisposeEntries(entries);
      throw new AssetException(Path.GetFileName(commonPath), ex);
    }

    try {
      return new TerrainTextureCatalog(entries, expansionCommonPath != null);
    } catch (AssetException) {
      throw;
    } catch (Exception ex) {
      // TerrainTextureCatalog assumes ownership before validating and disposes on failure.
      throw new AssetException(Path.GetFileName(commonPath), ex);
    }
  }

  private static IReadOnlyList<TerrainTextureCatalogEntry> LoadTerrainPair(
    string commonPath,
    TerrainTextureCatalogLayer layer
  ) {
    using var ovl = Ovl.Load(commonPath);
    var terrains = TerrainTypes.Extract(ovl);
    if (terrains.Count == 0)
      throw new InvalidDataException(
        $"Terrain OVL pair '{Path.GetFileName(commonPath)}' contains no TER resources.");

    using var decodedTextures = Textures.Extract(ovl);
    var decodedNames = decodedTextures.Names.ToHashSet(StringComparer.Ordinal);
    var entries = new List<TerrainTextureCatalogEntry>(terrains.Count);
    try {
      foreach (var terrain in terrains) {
        var files = ovl.Keys
          .Where(file => file.Type == FileType.Texture
            && string.Equals(file.Name, terrain.TextureRef, StringComparison.Ordinal))
          .ToArray();
        if (files.Length != 1)
          throw new InvalidDataException(
            $"Terrain resource '{terrain.Name}' in '{Path.GetFileName(commonPath)}' declares " +
            $"texture '{terrain.TextureRef}', but its owning OVL pair contains {files.Length} " +
            "matching resources.");

        var file = files[0];
        var decodedName = file.ToString();
        if (!decodedNames.Contains(decodedName))
          throw new InvalidDataException(
            $"Terrain texture '{decodedName}' from '{Path.GetFileName(commonPath)}' failed to decode.");
        var texture = CreateTexture(file, decodedTextures[decodedName]);
        entries.Add(new TerrainTextureCatalogEntry(
          terrain.Name,
          terrain.Number,
          terrain.Type,
          terrain.TextureRef,
          texture,
          layer,
          commonPath));
      }
      return entries;
    } catch {
      DisposeEntries(entries);
      throw;
    }
  }

  private static string? FindCompleteEditionTerrainCommonPath(string baseCommonPath) {
    if (!string.Equals(
      Path.GetFileName(baseCommonPath),
      "Terrain_RCT3.common.ovl",
      StringComparison.OrdinalIgnoreCase)) return null;

    var baseDirectory = Path.GetDirectoryName(baseCommonPath);
    if (baseDirectory == null || !string.Equals(
      Path.GetFileName(baseDirectory), "RCT3", StringComparison.OrdinalIgnoreCase)) return null;
    var terrainDirectory = Path.GetDirectoryName(baseDirectory);
    if (terrainDirectory == null) return null;

    var expansionCommonPath = Path.Combine(
      terrainDirectory, "CT", "Terrain_CT.common.ovl");
    var expansionUniquePath = GetPairedPath(expansionCommonPath, ".unique.ovl");
    var commonExists = File.Exists(expansionCommonPath);
    var uniqueExists = File.Exists(expansionUniquePath);
    if (!commonExists && !uniqueExists) return null;
    if (!commonExists)
      throw new FileNotFoundException(
        $"Complete Edition terrain common OVL not found: {expansionCommonPath}",
        expansionCommonPath);
    if (!uniqueExists)
      throw new FileNotFoundException(
        $"Complete Edition terrain unique OVL not found: {expansionUniquePath}",
        expansionUniquePath);
    return expansionCommonPath;
  }

  private static void DisposeEntries(IEnumerable<TerrainTextureCatalogEntry> entries) {
    foreach (var texture in entries.Select(entry => entry.Texture).Distinct()) texture.Dispose();
  }

  private static string GetTerrainCommonPath(string ovlPath) {
    ArgumentException.ThrowIfNullOrWhiteSpace(ovlPath);
    var fullPath = Path.GetFullPath(ovlPath);
    var fileName = Path.GetFileName(fullPath);
    if (!fileName.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase)
        && !fileName.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        "Terrain OVL path must end with '.common.ovl' or '.unique.ovl'.", nameof(ovlPath));

    var commonPath = GetPairedPath(fullPath, ".common.ovl");
    var uniquePath = GetPairedPath(fullPath, ".unique.ovl");
    if (!File.Exists(commonPath))
      throw new FileNotFoundException($"Terrain common OVL not found: {commonPath}", commonPath);
    if (!File.Exists(uniquePath))
      throw new FileNotFoundException($"Terrain unique OVL not found: {uniquePath}", uniquePath);
    return commonPath;
  }

  private static string GetPairedPath(string ovlPath, string suffix) {
    var directory = Path.GetDirectoryName(ovlPath) ?? string.Empty;
    var fileName = Path.GetFileName(ovlPath);
    var pairMarker = fileName.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase)
      ? ".common.ovl"
      : ".unique.ovl";
    return Path.Combine(directory, fileName[..^pairMarker.Length] + suffix);
  }

  private static Texture CreateTexture(OvlFile file, OpenCobra.OVL.Files.Texture decoded) {
    if (decoded.MipLevels.Length == 0 || decoded.MipLevels[0] == null)
      throw new InvalidDataException($"Texture '{file}' has no decoded base mip.");

    var pixels = decoded.MipLevels[0].Clone();
    return new Texture(file.Name, pixels.Width, pixels.Height, pixels);
  }
}
