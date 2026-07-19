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
  /// Loads and caches the numerically ordered terrain/cliff texture catalog from a paired OVL.
  /// The same live catalog is returned for equivalent paths until it is disposed.
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

    try {
      using var ovl = Ovl.Load(commonPath);
      var files = ovl.Keys
        .Where(file => file.Type == FileType.Texture
          && TerrainTextureCatalog.IsCatalogAssetName(file.Name))
        .OrderBy(file => file.Name, StringComparer.Ordinal)
        .ToArray();
      if (files.Length == 0)
        throw new InvalidDataException(
          "The paired OVL contains no Terrain_XX or TerrainCliffN textures.");

      using var decodedTextures = Textures.Extract(ovl);
      var decodedNames = decodedTextures.Names.ToHashSet(StringComparer.Ordinal);
      var textures = new List<Texture>(files.Length);
      try {
        foreach (var file in files) {
          var decodedName = file.ToString();
          if (!decodedNames.Contains(decodedName))
            throw new InvalidDataException($"Texture '{decodedName}' failed to decode.");
          textures.Add(CreateTexture(file, decodedTextures[decodedName]));
        }
        return new TerrainTextureCatalog(textures);
      } catch {
        foreach (var texture in textures) texture.Dispose();
        throw;
      }
    } catch (AssetException) {
      throw;
    } catch (Exception ex) {
      throw new AssetException(Path.GetFileName(commonPath), ex);
    }
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
