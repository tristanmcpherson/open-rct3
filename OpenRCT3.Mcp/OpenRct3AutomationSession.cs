using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class OpenRct3AutomationSession : IAsyncDisposable {
  private readonly SemaphoreSlim gate = new(1, 1);
  private Process? process;
  private NamedPipeClientStream? pipe;
  private StreamReader? reader;
  private StreamWriter? writer;
  private int requestNumber;

  public async Task<object> LaunchAsync(
    string? mapPath,
    bool build,
    bool hideUi,
    int? viewportWidth,
    int? viewportHeight,
    CancellationToken cancellationToken
  ) {
    await gate.WaitAsync(cancellationToken);
    try {
      if (process is { HasExited: false })
        throw new InvalidOperationException(
          "This MCP server already owns a running OpenRCT3 process.");
      if (process != null) await ResetAsync();
      var viewportValues = new[] { viewportWidth, viewportHeight };
      if (viewportValues.Any(value => value.HasValue)
          && viewportValues.Any(value => !value.HasValue))
        throw new ArgumentException(
          "viewportWidth and viewportHeight must be supplied together.");

      var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
      var projectPath = Path.Combine(root, "OpenRCT3", "OpenRCT3.csproj");
      if (build) await BuildAsync(projectPath, root, cancellationToken);

      var executablePath = Path.Combine(
        root,
        "OpenRCT3",
        "bin",
        "Debug",
        "net8.0-windows10.0.17763.0",
        "OpenRCT3.exe");
      if (!File.Exists(executablePath))
        throw new FileNotFoundException("Build OpenRCT3 before launching it.", executablePath);
      if (!string.IsNullOrWhiteSpace(mapPath) && !File.Exists(mapPath))
        throw new FileNotFoundException("The requested park file does not exist.", mapPath);

      var pipeName = $"openrct3-mcp-{Guid.NewGuid():N}";
      var start = new ProcessStartInfo(executablePath) {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(executablePath)!
      };
      start.Environment["OPENRCT3_AUTOMATION_PIPE"] = pipeName;
      if (hideUi) start.Environment["OPENRCT3_HIDE_UI"] = "1";
      if (!string.IsNullOrWhiteSpace(mapPath))
        start.Environment["OPENRCT3_MAP_PATH"] = Path.GetFullPath(mapPath);
      try {
        process = Process.Start(start)
          ?? throw new InvalidOperationException("OpenRCT3 did not start.");

        pipe = new NamedPipeClientStream(
          ".",
          pipeName,
          PipeDirection.InOut,
          PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        await pipe.ConnectAsync(timeout.Token);
        reader = new StreamReader(pipe, leaveOpen: true);
        writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var state = viewportValues.All(value => value.HasValue)
          ? await ResizeViewportLockedAsync(
            viewportWidth!.Value, viewportHeight!.Value, cancellationToken)
          : await SendLockedAsync("state", new { }, cancellationToken);
        using var executable = File.OpenRead(executablePath);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(
          executable, cancellationToken)).ToLowerInvariant();
        var assemblyPath = Path.ChangeExtension(executablePath, ".dll");
        using var assembly = File.OpenRead(assemblyPath);
        var assemblySha256 = Convert.ToHexString(await SHA256.HashDataAsync(
          assembly, cancellationToken)).ToLowerInvariant();
        return new {
          executable_path = executablePath,
          process_id = process.Id,
          sha256,
          assembly_path = assemblyPath,
          assembly_sha256 = assemblySha256,
          state
        };
      } catch {
        await TerminateOwnedProcessAsync();
        await ResetAsync();
        throw;
      }
    } finally {
      gate.Release();
    }
  }

  public Task<JsonElement> GetStateAsync(CancellationToken cancellationToken) =>
    SendAsync("state", new { }, cancellationToken);

  public Task<JsonElement> MoveCameraAsync(
    float panRight,
    float panForward,
    float panSeconds,
    float orbitDegrees,
    float elevationDegrees,
    float zoomSteps,
    float? targetX,
    float? targetY,
    float? targetZ,
    float? distance,
    CancellationToken cancellationToken
  ) => SendAsync("camera", new {
    pan_right = panRight,
    pan_forward = panForward,
    pan_seconds = panSeconds,
    orbit_degrees = orbitDegrees,
    elevation_degrees = elevationDegrees,
    zoom_steps = zoomSteps,
    target_x = targetX,
    target_y = targetY,
    target_z = targetZ,
    distance
  }, cancellationToken);

  public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken) {
    var result = await SendAsync("screenshot", new { }, cancellationToken);
    var encoded = result.GetProperty("png_base64").GetString()
      ?? throw new InvalidDataException("OpenRCT3 returned an empty screenshot.");
    return Convert.FromBase64String(encoded);
  }

  public Task<JsonElement> SetPausedAsync(
    bool paused,
    CancellationToken cancellationToken
  ) => SendAsync("pause", new { paused }, cancellationToken);

  public Task<JsonElement> ResizeViewportAsync(
    int width,
    int height,
    CancellationToken cancellationToken
  ) => SendAsync("resize", new { width, height }, cancellationToken);

  public Task<JsonElement> FrameTerrainAsync(CancellationToken cancellationToken) =>
    SendAsync("frame_terrain", new { }, cancellationToken);

  public Task<JsonElement> PickTerrainAsync(
    float x,
    float y,
    CancellationToken cancellationToken
  ) => SendAsync("pick_terrain", new { x, y }, cancellationToken);

  public Task<JsonElement> SelectTerrainAsync(
    float x,
    float y,
    CancellationToken cancellationToken
  ) => SendAsync("select_terrain", new { x, y }, cancellationToken);

  public Task<JsonElement> ClearTerrainSelectionAsync(CancellationToken cancellationToken) =>
    SendAsync("clear_terrain_selection", new { }, cancellationToken);

  public async Task<JsonElement> ShutdownAsync(CancellationToken cancellationToken) {
    await gate.WaitAsync(cancellationToken);
    try {
      if (process == null)
        return JsonSerializer.SerializeToElement(new { accepted = true, already_stopped = true });
      var result = await SendLockedAsync("shutdown", new { }, cancellationToken);
      await process.WaitForExitAsync(cancellationToken);
      await ResetAsync();
      return result;
    } finally {
      gate.Release();
    }
  }

  public async ValueTask DisposeAsync() {
    await gate.WaitAsync();
    try {
      if (process is { HasExited: false }) {
        try {
          await SendLockedAsync("shutdown", new { }, CancellationToken.None);
          await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        } catch {
          if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
      }
      await ResetAsync();
    } finally {
      gate.Release();
      gate.Dispose();
    }
  }

  private async Task<JsonElement> SendAsync(
    string method,
    object parameters,
    CancellationToken cancellationToken
  ) {
    await gate.WaitAsync(cancellationToken);
    try {
      return await SendLockedAsync(method, parameters, cancellationToken);
    } finally {
      gate.Release();
    }
  }

  private async Task<JsonElement> SendLockedAsync(
    string method,
    object parameters,
    CancellationToken cancellationToken
  ) {
    if (process == null || process.HasExited || reader == null || writer == null)
      throw new InvalidOperationException("Launch OpenRCT3 with openrct3_launch first.");
    var id = Convert.ToString(Interlocked.Increment(ref requestNumber));
    await writer.WriteLineAsync(JsonSerializer.Serialize(new {
      id,
      method,
      @params = parameters
    }).AsMemory(), cancellationToken);
    var line = await reader.ReadLineAsync(cancellationToken)
      ?? throw new EndOfStreamException("OpenRCT3 closed its automation pipe.");
    var response = JsonSerializer.Deserialize<AutomationResponse>(line)
      ?? throw new InvalidDataException("OpenRCT3 returned an empty response.");
    if (response.Id != id)
      throw new InvalidDataException("OpenRCT3 returned a mismatched response id.");
    if (response.Error != null)
      throw new InvalidOperationException(response.Error.Message);
    return response.Result.Clone();
  }

  private Task<JsonElement> ResizeViewportLockedAsync(
    int width,
    int height,
    CancellationToken cancellationToken
  ) => SendLockedAsync("resize", new { width, height }, cancellationToken);

  private async Task ResetAsync() {
    writer?.Dispose();
    reader?.Dispose();
    if (pipe != null) await pipe.DisposeAsync();
    process?.Dispose();
    writer = null;
    reader = null;
    pipe = null;
    process = null;
  }

  private async Task TerminateOwnedProcessAsync() {
    if (process is not { HasExited: false }) return;
    try {
      process.Kill(entireProcessTree: true);
      await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    } catch {
      // Preserve the launch failure that required cleanup.
    }
  }

  private static async Task BuildAsync(
    string projectPath,
    string workingDirectory,
    CancellationToken cancellationToken
  ) {
    var start = new ProcessStartInfo("dotnet") {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = workingDirectory
    };
    start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
    start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    start.ArgumentList.Add("build");
    start.ArgumentList.Add(projectPath);
    start.ArgumentList.Add("--nologo");
    start.ArgumentList.Add("-m:1");
    start.ArgumentList.Add("-p:UseSharedCompilation=false");
    using var build = Process.Start(start)
      ?? throw new InvalidOperationException("The OpenRCT3 build did not start.");
    try {
      var outputTask = build.StandardOutput.ReadToEndAsync(cancellationToken);
      var errorTask = build.StandardError.ReadToEndAsync(cancellationToken);
      await build.WaitForExitAsync(cancellationToken);
      var output = await outputTask;
      var error = await errorTask;
      if (build.ExitCode != 0)
        throw new InvalidOperationException(
          $"OpenRCT3 build failed.\n{output}\n{error}".Trim());
    } catch {
      if (!build.HasExited) {
        build.Kill(entireProcessTree: true);
        await build.WaitForExitAsync();
      }
      throw;
    }
  }

  private static string FindRepositoryRoot(string startPath) {
    var directory = new DirectoryInfo(Path.GetFullPath(startPath));
    while (directory != null) {
      if (File.Exists(Path.Combine(directory.FullName, "OpenRCT3.sln")))
        return directory.FullName;
      directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Could not find the OpenRCT3 repository root.");
  }

  private sealed record AutomationResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("result")] JsonElement Result,
    [property: JsonPropertyName("error")] AutomationError? Error);

  private sealed record AutomationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
}
