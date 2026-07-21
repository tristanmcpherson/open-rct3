using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

[McpServerToolType]
internal sealed class OpenRct3Tools(OpenRct3AutomationSession session) {
  [McpServerTool(Name = "openrct3_launch", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false)]
  [Description("Build and launch an MCP-owned OpenRCT3 process without activating its window.")]
  public Task<object> Launch(
    [Description("Optional absolute path to an RCT3 .dat park file.")] string? mapPath = null,
    [Description("Build the exact worktree executable before launch.")] bool build = true,
    [Description("Hide in-game UI so framebuffer captures show the scene unobstructed.")]
    bool hideUi = true,
    [Description("Optional framebuffer width; supply viewportHeight too.")]
    int? viewportWidth = null,
    [Description("Optional framebuffer height; supply viewportWidth too.")]
    int? viewportHeight = null,
    CancellationToken cancellationToken = default
  ) => session.LaunchAsync(
    mapPath,
    build,
    hideUi,
    viewportWidth,
    viewportHeight,
    cancellationToken);

  [McpServerTool(Name = "openrct3_get_state", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Read the owned game's map, framebuffer, camera, and scene state.")]
  public Task<JsonElement> GetState(CancellationToken cancellationToken = default) =>
    session.GetStateAsync(cancellationToken);

  [McpServerTool(Name = "openrct3_camera", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false)]
  [Description("Move the owned game's camera without using desktop keyboard or mouse input.")]
  public Task<JsonElement> Camera(
    [Description("Right/left pan axis from -1 to 1.")] float panRight = 0f,
    [Description("Forward/back pan axis from -1 to 1.")] float panForward = 0f,
    [Description("Pan duration in seconds; one request is capped at 0.25 seconds.")]
    float panSeconds = 0f,
    [Description("Horizontal orbit in degrees.")] float orbitDegrees = 0f,
    [Description("Vertical orbit in degrees.")] float elevationDegrees = 0f,
    [Description("Positive zooms in and negative zooms out.")] float zoomSteps = 0f,
    [Description("Optional absolute world target X; supply all three target coordinates.")]
    float? targetX = null,
    [Description("Optional absolute world target Y; supply all three target coordinates.")]
    float? targetY = null,
    [Description("Optional absolute world target Z; supply all three target coordinates.")]
    float? targetZ = null,
    [Description("Optional absolute eye-to-target distance.")] float? distance = null,
    CancellationToken cancellationToken = default
  ) => session.MoveCameraAsync(
    panRight,
    panForward,
    panSeconds,
    orbitDegrees,
    elevationDegrees,
    zoomSteps,
    targetX,
    targetY,
    targetZ,
    distance,
    cancellationToken);

  [McpServerTool(Name = "openrct3_screenshot", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Capture the owned game's OpenGL framebuffer directly as PNG.")]
  public async Task<IEnumerable<ContentBlock>> Screenshot(
    CancellationToken cancellationToken = default
  ) {
    var png = await session.CaptureScreenshotAsync(cancellationToken);
    return [ImageContentBlock.FromBytes(png, "image/png")];
  }

  [McpServerTool(Name = "openrct3_set_paused", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Pause or resume the owned game for deterministic visual inspection.")]
  public Task<JsonElement> SetPaused(
    [Description("True pauses simulation; false resumes it.")] bool paused,
    CancellationToken cancellationToken = default
  ) => session.SetPausedAsync(paused, cancellationToken);

  [McpServerTool(Name = "openrct3_resize", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Resize the owned game's framebuffer without activating its window.")]
  public Task<JsonElement> Resize(
    [Description("Framebuffer width in pixels (64 to 8192).")] int width,
    [Description("Framebuffer height in pixels (64 to 8192).")] int height,
    CancellationToken cancellationToken = default
  ) => session.ResizeViewportAsync(width, height, cancellationToken);

  [McpServerTool(Name = "openrct3_frame_terrain", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Reset the owned camera to frame the complete loaded terrain.")]
  public Task<JsonElement> FrameTerrain(CancellationToken cancellationToken = default) =>
    session.FrameTerrainAsync(cancellationToken);

  [McpServerTool(Name = "openrct3_pick_terrain", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Resolve a framebuffer pixel to the nearest rendered terrain tile and world point.")]
  public Task<JsonElement> PickTerrain(
    [Description("Framebuffer X coordinate measured from the left edge.")] float x,
    [Description("Framebuffer Y coordinate measured from the top edge.")] float y,
    CancellationToken cancellationToken = default
  ) => session.PickTerrainAsync(x, y, cancellationToken);

  [McpServerTool(Name = "openrct3_select_terrain", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Select the terrain tile under a framebuffer pixel without desktop input.")]
  public Task<JsonElement> SelectTerrain(
    [Description("Framebuffer X coordinate measured from the left edge.")] float x,
    [Description("Framebuffer Y coordinate measured from the top edge.")] float y,
    CancellationToken cancellationToken = default
  ) => session.SelectTerrainAsync(x, y, cancellationToken);

  [McpServerTool(Name = "openrct3_clear_terrain_selection", ReadOnly = false,
    Destructive = false, Idempotent = true, OpenWorld = false)]
  [Description("Clear the current terrain selection without desktop input.")]
  public Task<JsonElement> ClearTerrainSelection(
    CancellationToken cancellationToken = default
  ) => session.ClearTerrainSelectionAsync(cancellationToken);

  [McpServerTool(Name = "openrct3_shutdown", ReadOnly = false, Destructive = false,
    Idempotent = true, OpenWorld = false)]
  [Description("Gracefully stop only the OpenRCT3 process launched by this MCP server.")]
  public Task<JsonElement> Shutdown(CancellationToken cancellationToken = default) =>
    session.ShutdownAsync(cancellationToken);
}
