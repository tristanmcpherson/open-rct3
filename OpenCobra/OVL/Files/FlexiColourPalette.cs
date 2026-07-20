// FlexiColourPalette
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.OVL.Files;

/// <summary>The installed game's 32 scenery flexicolour swatches.</summary>
public static class FlexiColourPalette {
  // The order and values come from the installed game's runtime swatch array. The GUI icon
  // artwork is only an approximation of these canonical colours and must not define the palette.
  private static readonly Rgba32[] Colours = [
    Rgb(47, 67, 67),
    Rgb(131, 151, 151),
    Rgb(211, 219, 219),
    Rgb(83, 83, 139),
    Rgb(139, 139, 191),
    Rgb(155, 103, 199),
    Rgb(27, 83, 203),
    Rgb(91, 163, 231),
    Rgb(175, 231, 251),
    Rgb(71, 167, 163),
    Rgb(171, 231, 231),
    Rgb(39, 143, 7),
    Rgb(99, 155, 119),
    Rgb(115, 155, 67),
    Rgb(91, 191, 63),
    Rgb(159, 183, 111),
    Rgb(151, 155, 79),
    Rgb(255, 243, 95),
    Rgb(243, 203, 27),
    Rgb(167, 111, 7),
    Rgb(255, 139, 51),
    Rgb(219, 79, 0),
    Rgb(191, 151, 87),
    Rgb(143, 99, 39),
    Rgb(143, 127, 107),
    Rgb(215, 151, 115),
    Rgb(199, 103, 103),
    Rgb(171, 0, 0),
    Rgb(255, 7, 0),
    Rgb(163, 31, 95),
    Rgb(239, 91, 171),
    Rgb(255, 171, 163),
  ];

  public static int Count => Colours.Length;

  public static Rgba32 Get(int index) {
    if (index < 0 || index >= Colours.Length)
      throw new ArgumentOutOfRangeException(
        nameof(index), index, $"Flexicolour index must be between 0 and {Colours.Length - 1}.");
    return Colours[index];
  }

  private static Rgba32 Rgb(byte red, byte green, byte blue) =>
    new(red, green, blue, byte.MaxValue);
}
