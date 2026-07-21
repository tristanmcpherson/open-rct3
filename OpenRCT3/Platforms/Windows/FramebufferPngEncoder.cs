using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace OpenRCT3.Platforms.Windows;

internal static class FramebufferPngEncoder {
  public static byte[] EncodeBgraBottomUp(int width, int height, byte[] pixels) {
    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    ArgumentNullException.ThrowIfNull(pixels);
    var stride = checked(width * 4);
    if (pixels.Length != checked(stride * height))
      throw new ArgumentException("Pixel data does not match the framebuffer dimensions.");

    using var bitmap = new Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
    var bounds = new Drawing.Rectangle(0, 0, width, height);
    var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    try {
      foreach (var row in Enumerable.Range(0, height)) {
        var sourceOffset = checked((height - row - 1) * stride);
        var destination = IntPtr.Add(data.Scan0, checked(row * data.Stride));
        Marshal.Copy(pixels, sourceOffset, destination, stride);
      }
    } finally {
      bitmap.UnlockBits(data);
    }

    using var output = new MemoryStream();
    bitmap.Save(output, ImageFormat.Png);
    return output.ToArray();
  }
}
