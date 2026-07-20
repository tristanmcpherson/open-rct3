// FlexiTextureRecolorer
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.OVL.Files;

/// <summary>Applies selected scenery flexicolours to a decoded flexi-texture frame.</summary>
public static class FlexiTextureRecolorer {
  private const int PaletteLength = 256 * 4;
  private const int ShadesPerBand = 85;
  private const int SelectedShade = 41;
  private const uint ValidFlags = 7;

  /// <summary>Returns a recoloured copy of <paramref name="frame"/>.</summary>
  /// <remarks>
  /// The installed game uses a literal, hand-authored 32 by 85 shade table. This clean-room
  /// replacement preserves the runtime's band layout and selected-colour midpoint without
  /// embedding that table: shade 42 is the selected swatch, shade 1 is a soft highlight, and
  /// shade 85 is a 20 percent shadow. Intermediate shades use nearest-even sRGB interpolation.
  /// It therefore matches the original contract, but individual shade RGB values are approximate.
  /// </remarks>
  public static Image<Rgba32> Recolor(
    FlexiTexture frame,
    Rgba32 first,
    Rgba32 second,
    Rgba32 third
  ) {
    if (frame.Texture is null)
      throw new InvalidDataException("FlexiTexture frame has no decoded texture.");

    var rawFlags = Convert.ToUInt32(frame.Recolorable);
    if ((rawFlags & ~ValidFlags) != 0)
      throw new InvalidDataException(
        $"FlexiTexture frame has invalid recolorable flags {rawFlags}.");
    if (frame.PaletteBgra.Length != PaletteLength)
      throw new InvalidDataException(
        $"FlexiTexture palette must contain exactly {PaletteLength} bytes.");

    var pixelCount = Convert.ToInt64(frame.Texture.Width) * frame.Texture.Height;
    if (pixelCount > int.MaxValue)
      throw new InvalidDataException("FlexiTexture pixel count overflows.");
    if (frame.IndexedPixels.Length != Convert.ToInt32(pixelCount))
      throw new InvalidDataException(
        "FlexiTexture indexed pixel count does not match its decoded texture dimensions.");

    var pixels = new Rgba32[Convert.ToInt32(pixelCount)];
    frame.Texture.CopyPixelDataTo(pixels);
    var indices = frame.IndexedPixels.Span;
    var palette = frame.PaletteBgra.Span;
    foreach (var pixelIndex in Enumerable.Range(0, pixels.Length)) {
      var colour = ResolveColour(
        indices[pixelIndex], frame.Recolorable, palette, first, second, third);
      pixels[pixelIndex] = new Rgba32(
        colour.R, colour.G, colour.B, pixels[pixelIndex].A);
    }

    return Image.LoadPixelData<Rgba32>(pixels, frame.Texture.Width, frame.Texture.Height);
  }

  private static Rgba32 ResolveColour(
    byte paletteIndex,
    Recolorable recolorable,
    ReadOnlySpan<byte> palette,
    Rgba32 first,
    Rgba32 second,
    Rgba32 third
  ) {
    if (paletteIndex == 0) return ReadPaletteColour(palette, paletteIndex);

    var band = (paletteIndex - 1) / ShadesPerBand;
    var shade = (paletteIndex - 1) % ShadesPerBand;
    var flag = band switch {
      0 => Recolorable.First,
      1 => Recolorable.Second,
      _ => Recolorable.Third,
    };
    if ((recolorable & flag) == 0) return ReadPaletteColour(palette, paletteIndex);

    var selected = band switch {
      0 => first,
      1 => second,
      _ => third,
    };
    return Shade(selected, shade);
  }

  private static Rgba32 ReadPaletteColour(ReadOnlySpan<byte> palette, byte index) {
    var offset = Convert.ToInt32(index) * 4;
    return new Rgba32(
      palette[offset + 2], palette[offset + 1], palette[offset], byte.MaxValue);
  }

  private static Rgba32 Shade(Rgba32 selected, int shade) => new(
    ShadeChannel(selected.R, shade),
    ShadeChannel(selected.G, shade),
    ShadeChannel(selected.B, shade),
    byte.MaxValue
  );

  private static byte ShadeChannel(byte selected, int shade) {
    if (shade == SelectedShade) return selected;

    if (shade < SelectedShade) {
      var highlight = Interpolate(byte.MaxValue, selected, 4, 25);
      return Interpolate(highlight, selected, shade, SelectedShade);
    }

    var shadow = Interpolate(0, selected, 1, 5);
    return Interpolate(selected, shadow, shade - SelectedShade,
      ShadesPerBand - SelectedShade - 1);
  }

  private static byte Interpolate(byte start, byte end, int position, int distance) {
    var delta = Convert.ToDouble(Convert.ToInt32(end) - Convert.ToInt32(start));
    var value = Convert.ToDouble(start) + delta * position / distance;
    return Convert.ToByte(Math.Round(value, MidpointRounding.ToEven));
  }
}
