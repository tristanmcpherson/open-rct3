using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class FlexiTextureRecolorerTests {
  [Test]
  public void Recolor_ReplacesOnlyActiveBandsAndPreservesSourceAlpha() {
    var sourcePixels = new[] {
      new Rgba32(1, 2, 3, 0),
      new Rgba32(4, 5, 6, 64),
      new Rgba32(7, 8, 9, 128),
      new Rgba32(10, 11, 12, 255),
    };
    using var texture = Image.LoadPixelData<Rgba32>(sourcePixels, 2, 2);
    var frame = new FlexiTexture(Recolorable.First | Recolorable.Third, texture) {
      PaletteBgra = CreatePalette(),
      IndexedPixels = new byte[] { 0, 42, 127, 212 },
    };

    using var result = FlexiTextureRecolorer.Recolor(
      frame,
      new Rgba32(10, 20, 30, 1),
      new Rgba32(110, 120, 130, 2),
      new Rgba32(210, 220, 230, 3));

    var actual = new Rgba32[4];
    result.CopyPixelDataTo(actual);
    var sourceAfter = new Rgba32[4];
    texture.CopyPixelDataTo(sourceAfter);
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual[0], Is.EqualTo(new Rgba32(0, 255, 0, 0)));
      Assert.That(actual[1], Is.EqualTo(new Rgba32(10, 20, 30, 64)));
      Assert.That(actual[2], Is.EqualTo(new Rgba32(125, 128, 127, 128)));
      Assert.That(actual[3], Is.EqualTo(new Rgba32(210, 220, 230, 255)));
      Assert.That(sourceAfter, Is.EqualTo(sourcePixels), "The source image must remain immutable.");
    }
  }

  [Test]
  public void Recolor_UsesDocumentedSoftRampAroundSelectedShade() {
    var sourcePixels = new[] {
      new Rgba32(0, 0, 0, 10),
      new Rgba32(0, 0, 0, 20),
      new Rgba32(0, 0, 0, 30),
      new Rgba32(0, 0, 0, 40),
    };
    using var texture = Image.LoadPixelData<Rgba32>(sourcePixels, 2, 2);
    var frame = new FlexiTexture(Recolorable.First, texture) {
      PaletteBgra = CreatePalette(),
      IndexedPixels = new byte[] { 1, 42, 43, 85 },
    };

    using var result = FlexiTextureRecolorer.Recolor(
      frame,
      new Rgba32(100, 150, 200, 1),
      new Rgba32(0, 0, 0, 2),
      new Rgba32(0, 0, 0, 3));

    var actual = new Rgba32[4];
    result.CopyPixelDataTo(actual);
    Assert.That(actual, Is.EqualTo(new[] {
      new Rgba32(230, 238, 246, 10),
      new Rgba32(100, 150, 200, 20),
      new Rgba32(98, 147, 196, 30),
      new Rgba32(20, 30, 40, 40),
    }));
  }

  [Test]
  public void Recolor_MapsEachBandToItsSelectedColour() {
    using var texture = new Image<Rgba32>(3, 1, new Rgba32(0, 0, 0, 77));
    var frame = new FlexiTexture(
      Recolorable.First | Recolorable.Second | Recolorable.Third, texture) {
      PaletteBgra = CreatePalette(),
      IndexedPixels = new byte[] { 42, 127, 212 },
    };

    using var result = FlexiTextureRecolorer.Recolor(
      frame,
      new Rgba32(10, 20, 30, 1),
      new Rgba32(40, 50, 60, 2),
      new Rgba32(70, 80, 90, 3));

    var actual = new Rgba32[3];
    result.CopyPixelDataTo(actual);
    Assert.That(actual, Is.EqualTo(new[] {
      new Rgba32(10, 20, 30, 77),
      new Rgba32(40, 50, 60, 77),
      new Rgba32(70, 80, 90, 77),
    }));
  }

  [TestCase(1023, 1)]
  [TestCase(1025, 1)]
  [TestCase(1024, 0)]
  [TestCase(1024, 2)]
  public void Recolor_RejectsMalformedBackingData(int paletteLength, int indexLength) {
    using var texture = new Image<Rgba32>(1, 1);
    var frame = new FlexiTexture(Recolorable.First, texture) {
      PaletteBgra = new byte[paletteLength],
      IndexedPixels = new byte[indexLength],
    };

    Assert.Throws<InvalidDataException>(new Action(() => {
      using var result = FlexiTextureRecolorer.Recolor(
        frame, default, default, default);
    }));
  }

  [Test]
  public void Recolor_RejectsUnknownFlags() {
    using var texture = new Image<Rgba32>(1, 1);
    var frame = new FlexiTexture((Recolorable)8, texture) {
      PaletteBgra = CreatePalette(),
      IndexedPixels = new byte[1],
    };

    Assert.Throws<InvalidDataException>(new Action(() => {
      using var result = FlexiTextureRecolorer.Recolor(
        frame, default, default, default);
    }));
  }

  [Test]
  public void Recolor_RejectsMissingDecodedTexture() {
    Assert.Throws<InvalidDataException>(new Action(() => {
      using var result = FlexiTextureRecolorer.Recolor(
        default, default, default, default);
    }));
  }

  private static byte[] CreatePalette() {
    var palette = new byte[256 * 4];
    foreach (var index in Enumerable.Range(0, 256)) {
      var offset = index * 4;
      palette[offset] = Convert.ToByte(index);
      palette[offset + 1] = Convert.ToByte(255 - index);
      palette[offset + 2] = Convert.ToByte(index * 3 % 256);
      palette[offset + 3] = byte.MaxValue;
    }
    return palette;
  }
}
