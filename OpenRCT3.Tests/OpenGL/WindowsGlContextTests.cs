using OpenRCT3.OpenGL;

namespace OpenRCT3.Tests.OpenGL;

[TestFixture]
public class WindowsGlContextTests {
  [Test]
  public void RequestedPixelFormat_RequiresDoubleBufferedOpenGlWindow() {
    var descriptor = GLContext.CreatePixelFormatDescriptor();

    Assert.That(
      descriptor.dwFlags & GLContext.RequiredPixelFormatFlags,
      Is.EqualTo(GLContext.RequiredPixelFormatFlags));
  }

  [Test]
  public void SelectedPixelFormat_WithEveryRequiredCapabilityIsAccepted() {
    Assert.DoesNotThrow(new Action(() =>
      GLContext.ValidateSelectedPixelFormat(
        described: 1,
        GLContext.RequiredPixelFormatFlags)));
  }

  [TestCase(GLContext.PfdDoubleBuffer)]
  [TestCase(GLContext.PfdDrawToWindow)]
  [TestCase(GLContext.PfdSupportOpenGl)]
  public void SelectedPixelFormat_MissingRequiredCapabilityIsRejected(
    uint missingFlag
  ) {
    var flags = GLContext.RequiredPixelFormatFlags & ~missingFlag;

    var error = Assert.Throws<PlatformNotSupportedException>(new Action(() =>
      GLContext.ValidateSelectedPixelFormat(described: 1, flags)));

    Assert.That(error!.Message, Does.Contain($"0x{missingFlag:X8}"));
  }

  [Test]
  public void SelectedPixelFormat_WhenDescriptionFailsIsRejected() {
    var error = Assert.Throws<Exception>(new Action(() =>
      GLContext.ValidateSelectedPixelFormat(
        described: 0,
        GLContext.RequiredPixelFormatFlags)));

    Assert.That(
      error!.Message,
      Is.EqualTo("Could not describe the selected OpenGL pixel format."));
  }

  [Test]
  public void BufferSwap_NativeFailureIsRejected() {
    var error = Assert.Throws<Exception>(new Action(() =>
      GLContext.EnsureBufferSwapSucceeded(false)));

    Assert.That(error!.Message, Is.EqualTo("Could not swap graphics buffers."));
  }

  [Test]
  public void BufferSwap_NativeSuccessIsAccepted() {
    Assert.DoesNotThrow(new Action(() =>
      GLContext.EnsureBufferSwapSucceeded(true)));
  }
}
