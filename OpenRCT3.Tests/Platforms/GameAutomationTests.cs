using OpenRCT3.Platforms.Windows;
using System.Drawing;

namespace OpenRCT3.Tests.Platforms;

[TestFixture]
public class GameAutomationTests {
  [TestCase("openrct3-mcp-0123456789abcdef")]
  [TestCase("open_rct3.test-1")]
  public void ValidatePipeName_AcceptsLocalGeneratedNames(string pipeName) {
    Assert.DoesNotThrow(new Action(() =>
      GameAutomationPipeServer.ValidatePipeName(pipeName)));
  }

  [TestCase("bad/name")]
  [TestCase("bad\\name")]
  [TestCase("bad name")]
  public void ValidatePipeName_RejectsNamesOutsideTheLocalProtocol(string pipeName) {
    Assert.Throws<ArgumentException>(new Action(() =>
      GameAutomationPipeServer.ValidatePipeName(pipeName)));
  }

  [TestCase(64, 64)]
  [TestCase(1280, 720)]
  [TestCase(8192, 8192)]
  public void ViewportRequest_Validate_AcceptsBoundedDimensions(int width, int height) {
    var request = new GameAutomationViewportRequest(width, height);

    Assert.DoesNotThrow(new Action(request.Validate));
  }

  [TestCase(63, 720)]
  [TestCase(1280, 63)]
  [TestCase(8193, 720)]
  [TestCase(1280, 8193)]
  public void ViewportRequest_Validate_RejectsUnsafeDimensions(int width, int height) {
    var request = new GameAutomationViewportRequest(width, height);

    Assert.Throws<ArgumentOutOfRangeException>(new Action(request.Validate));
  }

  [TestCase(0f, 0f)]
  [TestCase(1279.999f, 719.999f)]
  [TestCase(640f, 360f)]
  public void TerrainPickRequest_Validate_AcceptsFramebufferCoordinates(float x, float y) {
    var request = new GameAutomationTerrainPickRequest(x, y);

    Assert.DoesNotThrow(new Action(() => request.Validate(1280, 720)));
  }

  [TestCase(-1f, 0f)]
  [TestCase(0f, -1f)]
  [TestCase(1280f, 0f)]
  [TestCase(0f, 720f)]
  [TestCase(float.NaN, 0f)]
  [TestCase(0f, float.PositiveInfinity)]
  public void TerrainPickRequest_Validate_RejectsCoordinatesOutsideFramebuffer(float x, float y) {
    var request = new GameAutomationTerrainPickRequest(x, y);

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() => request.Validate(1280, 720)));
  }

  [Test]
  public void EncodeBgraBottomUp_FlipsOpenGlRowsIntoPngCoordinates() {
    byte[] pixels = [
      255, 0, 0, 255,
      255, 255, 255, 255,
      0, 0, 255, 255,
      0, 255, 0, 255
    ];

    var png = FramebufferPngEncoder.EncodeBgraBottomUp(2, 2, pixels);

    using var stream = new MemoryStream(png);
    using var bitmap = new Bitmap(stream);
    Assert.Multiple(new Action(() => {
      Assert.That(bitmap.GetPixel(0, 0).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
      Assert.That(bitmap.GetPixel(1, 0).ToArgb(), Is.EqualTo(Color.Lime.ToArgb()));
      Assert.That(bitmap.GetPixel(0, 1).ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
      Assert.That(bitmap.GetPixel(1, 1).ToArgb(), Is.EqualTo(Color.White.ToArgb()));
    }));
  }

  [Test]
  public void EncodeBgraBottomUp_RejectsMismatchedPixelCount() {
    Assert.Throws<ArgumentException>(new Action(() =>
      FramebufferPngEncoder.EncodeBgraBottomUp(2, 2, new byte[4])));
  }
}
