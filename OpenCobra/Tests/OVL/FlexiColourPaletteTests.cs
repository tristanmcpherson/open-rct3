using OpenCobra.OVL.Files;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class FlexiColourPaletteTests {
  [Test]
  public void Get_ReturnsInstalledGameSwatchesInIndexOrder() {
    var expected = new Rgba32[] {
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
    };

    using (Assert.EnterMultipleScope()) {
      Assert.That(FlexiColourPalette.Count, Is.EqualTo(32));
      foreach (var index in Enumerable.Range(0, expected.Length))
        Assert.That(FlexiColourPalette.Get(index), Is.EqualTo(expected[index]),
          $"Unexpected flexicolour swatch at index {index}.");
    }
  }

  [TestCase(-1)]
  [TestCase(32)]
  [TestCase(int.MinValue)]
  [TestCase(int.MaxValue)]
  public void Get_RejectsInvalidIndex(int index) {
    var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      FlexiColourPalette.Get(index)));

    Assert.That(exception!.ParamName, Is.EqualTo("index"));
  }

  private static Rgba32 Rgb(byte red, byte green, byte blue) =>
    new(red, green, blue, byte.MaxValue);
}
