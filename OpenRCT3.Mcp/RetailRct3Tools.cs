using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

[McpServerToolType]
internal sealed class RetailRct3Tools(
  RetailRct3Session retailSession,
  OpenRct3AutomationSession openSession
) {
  [McpServerTool(Name = "retail_rct3_launch", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false)]
  [Description("Build the bridge and launch only the exact hash-pinned retail RCT3.exe suspended, inject the bridge, then resume it.")]
  public Task<object> Launch(
    [Description("Build and test the 32-bit bridge before launch.")] bool buildBridge = true,
    CancellationToken cancellationToken = default
  ) => retailSession.LaunchAsync(buildBridge, cancellationToken);

  [McpServerTool(Name = "retail_rct3_get_state", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Read the owned retail process D3D9 device and final view/projection telemetry.")]
  public Task<JsonElement> GetState(CancellationToken cancellationToken = default) =>
    retailSession.GetStateAsync(cancellationToken);

  [McpServerTool(Name = "retail_rct3_click", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false)]
  [Description("Click a client-area pixel in the owned retail process without moving the desktop cursor.")]
  public Task<JsonElement> Click(
    [Description("Client X coordinate measured from the left edge.")] int x,
    [Description("Client Y coordinate measured from the top edge.")] int y,
    CancellationToken cancellationToken = default
  ) => retailSession.ClickAsync(x, y, cancellationToken);

  [McpServerTool(Name = "retail_rct3_screenshot", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Capture the owned retail process backbuffer on its D3D9 render thread.")]
  public async Task<IEnumerable<ContentBlock>> Screenshot(
    CancellationToken cancellationToken = default
  ) {
    var png = await retailSession.CaptureScreenshotAsync(cancellationToken);
    return [ImageContentBlock.FromBytes(png, "image/png")];
  }

  [McpServerTool(Name = "rct3_compare_frames", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Capture both owned processes and return, in order: retail, OpenRCT3, side-by-side, and absolute RGB diff PNGs.")]
  public async Task<IEnumerable<ContentBlock>> CompareFrames(
    CancellationToken cancellationToken = default
  ) {
    var retailTask = retailSession.CaptureScreenshotAsync(cancellationToken);
    var openTask = openSession.CaptureScreenshotAsync(cancellationToken);
    await Task.WhenAll(retailTask, openTask);
    var retail = await retailTask;
    var open = await openTask;
    var (sideBySide, difference) = CreateComparison(retail, open);
    return [
      ImageContentBlock.FromBytes(retail, "image/png"),
      ImageContentBlock.FromBytes(open, "image/png"),
      ImageContentBlock.FromBytes(sideBySide, "image/png"),
      ImageContentBlock.FromBytes(difference, "image/png")
    ];
  }

  [McpServerTool(Name = "retail_rct3_shutdown", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Stop only the exact retail RCT3 process launched by this MCP server.")]
  public Task<JsonElement> Shutdown(CancellationToken cancellationToken = default) =>
    retailSession.ShutdownAsync(cancellationToken);

  private static (byte[] SideBySide, byte[] Difference) CreateComparison(
    byte[] retailBytes,
    byte[] openBytes
  ) {
    using var retailStream = new MemoryStream(retailBytes);
    using var openStream = new MemoryStream(openBytes);
    using var retail = new Bitmap(retailStream);
    using var open = new Bitmap(openStream);
    using var sideBySide = new Bitmap(retail.Width + open.Width, Math.Max(retail.Height, open.Height));
    using (var graphics = Graphics.FromImage(sideBySide)) {
      graphics.Clear(Color.Black);
      graphics.DrawImageUnscaled(retail, 0, 0);
      graphics.DrawImageUnscaled(open, retail.Width, 0);
    }

    var width = Math.Min(retail.Width, open.Width);
    var height = Math.Min(retail.Height, open.Height);
    using var difference = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var retailPixel = retail.GetPixel(x, y);
        var openPixel = open.GetPixel(x, y);
        difference.SetPixel(x, y, Color.FromArgb(
          Math.Abs(retailPixel.R - openPixel.R),
          Math.Abs(retailPixel.G - openPixel.G),
          Math.Abs(retailPixel.B - openPixel.B)));
      }
    }
    return (EncodePng(sideBySide), EncodePng(difference));
  }

  private static byte[] EncodePng(Image image) {
    using var stream = new MemoryStream();
    image.Save(stream, ImageFormat.Png);
    return stream.ToArray();
  }
}
