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
  public static Texture LoadTexture(string ovlPath, string name) {
    if (!File.Exists(ovlPath)) throw new FileNotFoundException(ovlPath);
    using var ovl = Ovl.Load(ovlPath);
    return LoadTexture(ovl, ovl.Find(name, FileType.Texture) ?? throw new AssetException($"Texture '{name}' not found in OVL."));
  }

  public static Texture LoadTexture(Ovl ovl, OvlFile file) {
    try {
      using var textures = Textures.Extract(ovl);
      var decoded = textures[file.ToString()];
      if (decoded.MipLevels.Length == 0 || decoded.MipLevels[0] == null)
        throw new InvalidDataException($"Texture '{file}' has no decoded base mip.");

      var pixels = decoded.MipLevels[0].Clone();
      return new Texture(file.Name, pixels.Width, pixels.Height, pixels);
    }
    catch (Exception ex) {
      throw new AssetException(file.Name, ex);
    }
  }

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
}
