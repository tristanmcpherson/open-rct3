// PaletteConverterTests
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using NUnit.Framework;
using OpenCobra.OVL.Files;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class PaletteConverterTests {
  [Test]
  public void ConvertIndexedBgraToRgba_CreatesImage() {
    var indexedPixels = new byte[] { 0, 1, 2, 0 };
    var width = 2u;
    var height = 2u;
    // BGRA palette
    var palette = new byte[256 * 4];
    // Index 0: Red
    palette[0] = 0; palette[1] = 0; palette[2] = 255; palette[3] = 255;
    // Index 1: Green
    palette[4] = 0; palette[5] = 255; palette[6] = 0; palette[7] = 255;
    // Index 2: Blue
    palette[8] = 255; palette[9] = 0; palette[10] = 0; palette[11] = 255;
    byte[] alphaPixels = [255, 255, 255, 255];
    var outputRgba = PaletteConverter.ConvertIndexedBgraToRgba(width, height, palette, indexedPixels, alphaPixels);

    Assert.That(outputRgba[0], Is.EqualTo(new Rgba32(255, 0, 0, 255)));
    Assert.That(outputRgba[1], Is.EqualTo(new Rgba32(0, 255, 0, 255)));
    Assert.That(outputRgba[2], Is.EqualTo(new Rgba32(0, 0, 255, 255)));
    Assert.That(outputRgba[3], Is.EqualTo(new Rgba32(255, 0, 0, 255)));
  }

  [Test]
  public void ConvertIndexedBgraToRgba_OrsAlphaPlaneWithPaletteAlpha() {
    var palette = new byte[256 * 4];
    palette[0] = 10;
    palette[1] = 20;
    palette[2] = 30;
    palette[3] = 80;
    byte[] indexedPixels = [0];
    byte[] alphaPixels = [15];

    var outputRgba = PaletteConverter.ConvertIndexedBgraToRgba(
      1, 1, palette, indexedPixels, alphaPixels);

    Assert.That(outputRgba[0], Is.EqualTo(new Rgba32(30, 20, 10, 95)));
  }

  [Test]
  public void ConvertIndexedBgraToRgba_WithoutAlphaPlaneForcesOpaqueAlpha() {
    var palette = new byte[256 * 4];
    palette[3] = 7;
    byte[] indexedPixels = [0];

    var outputRgba = PaletteConverter.ConvertIndexedBgraToRgba(
      1, 1, palette, indexedPixels, []);

    Assert.That(outputRgba[0].A, Is.EqualTo(byte.MaxValue));
  }
}
