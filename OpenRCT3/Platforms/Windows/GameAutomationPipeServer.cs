using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OpenRCT3.Platforms.Windows;

internal sealed class GameAutomationPipeServer : IDisposable {
  internal const string PipeEnvironmentVariable = "OPENRCT3_AUTOMATION_PIPE";

  private readonly GameWindow window;
  private readonly string pipeName;
  private readonly CancellationTokenSource stopping = new();
  private readonly Task serverTask;

  private GameAutomationPipeServer(GameWindow window, string pipeName) {
    this.window = window;
    this.pipeName = pipeName;
    serverTask = Task.Run(RunAsync);
  }

  public static GameAutomationPipeServer? StartFromEnvironment(GameWindow window) {
    ArgumentNullException.ThrowIfNull(window);
    var pipeName = Environment.GetEnvironmentVariable(PipeEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(pipeName)) return null;
    ValidatePipeName(pipeName);
    return new GameAutomationPipeServer(window, pipeName);
  }

  internal static void ValidatePipeName(string pipeName) {
    ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
    if (pipeName.Length > 128 || pipeName.Any(character =>
        !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
      throw new ArgumentException(
        "Automation pipe names may only contain letters, digits, '-', '_', and '.'.");
  }

  public void Dispose() {
    stopping.Cancel();
    try {
      serverTask.Wait(TimeSpan.FromSeconds(2));
    } catch (AggregateException error) when (
        error.InnerExceptions.All(item => item is OperationCanceledException)) { }
    stopping.Dispose();
  }

  private async Task RunAsync() {
    while (!stopping.IsCancellationRequested) {
      await using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      try {
        await pipe.WaitForConnectionAsync(stopping.Token);
        await ServeClientAsync(pipe);
      } catch (OperationCanceledException) when (stopping.IsCancellationRequested) {
        return;
      } catch (IOException) when (!stopping.IsCancellationRequested) {
        // A disconnected MCP client can reconnect without restarting the game.
      }
    }
  }

  private async Task ServeClientAsync(Stream stream) {
    using var reader = new StreamReader(stream, leaveOpen: true);
    using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
    while (!stopping.IsCancellationRequested) {
      var line = await reader.ReadLineAsync(stopping.Token);
      if (line == null) return;

      GameAutomationRequest? request = null;
      GameAutomationResponse response;
      try {
        request = JsonSerializer.Deserialize<GameAutomationRequest>(line)
          ?? throw new InvalidDataException("The automation request was empty.");
        response = await DispatchAsync(request);
      } catch (Exception error) when (error is JsonException or InvalidDataException
          or ArgumentException or InvalidOperationException) {
        response = new GameAutomationResponse(
          request?.Id, null, new("invalid_request", error.Message));
      } catch (Exception error) {
        response = new GameAutomationResponse(
          request?.Id, null, new("internal_error", error.Message));
      }
      await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
  }

  private async Task<GameAutomationResponse> DispatchAsync(GameAutomationRequest request) {
    if (string.IsNullOrWhiteSpace(request.Id))
      throw new InvalidDataException("Automation requests require an id.");
    if (string.IsNullOrWhiteSpace(request.Method))
      throw new InvalidDataException("Automation requests require a method.");

    var result = request.Method switch {
      "state" => await InvokeAsync(window.GetAutomationState),
      "camera" => await InvokeAsync(() => {
        var camera = request.Parameters.Deserialize<GameAutomationCameraRequest>()
          ?? throw new InvalidDataException("Camera parameters are required.");
        return window.ApplyAutomationCamera(camera);
      }),
      "frame_terrain" => await InvokeAsync(window.FrameAutomationTerrain),
      "pick_terrain" => await InvokeAsync(() => {
        var pick = request.Parameters.Deserialize<GameAutomationTerrainPickRequest>()
          ?? throw new InvalidDataException("Terrain-pick parameters are required.");
        return window.PickAutomationTerrain(pick);
      }),
      "select_terrain" => await InvokeAsync(() => {
        var pick = request.Parameters.Deserialize<GameAutomationTerrainPickRequest>()
          ?? throw new InvalidDataException("Terrain-selection parameters are required.");
        return window.SelectAutomationTerrain(pick);
      }),
      "clear_terrain_selection" => await InvokeAsync(window.ClearAutomationTerrainSelection),
      "screenshot" => await InvokeAsync(() => new GameAutomationScreenshot(
        Convert.ToBase64String(window.CaptureAutomationFrame()))),
      "pause" => await InvokeAsync(() => {
        var pause = request.Parameters.Deserialize<GameAutomationPauseRequest>()
          ?? throw new InvalidDataException("Pause parameters are required.");
        return window.SetAutomationPaused(pause.Paused);
      }),
      "resize" => await InvokeAsync(() => {
        var viewport = request.Parameters.Deserialize<GameAutomationViewportRequest>()
          ?? throw new InvalidDataException("Viewport parameters are required.");
        return window.ResizeAutomationViewport(viewport);
      }),
      "shutdown" => await InvokeAsync(() => {
        window.RequestAutomationShutdown();
        return new GameAutomationShutdown(true);
      }),
      _ => throw new InvalidDataException($"Unknown automation method '{request.Method}'.")
    };
    return new GameAutomationResponse(request.Id, result, null);
  }

  private Task<object> InvokeAsync(Func<object> action) {
    if (window.IsDisposed || window.Disposing)
      throw new InvalidOperationException("The game window is closing.");
    var completion = new TaskCompletionSource<object>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    window.BeginInvoke(() => {
      try {
        completion.SetResult(action());
      } catch (Exception error) {
        completion.SetException(error);
      }
    });
    return completion.Task;
  }
}

internal sealed record GameAutomationRequest(
  [property: JsonPropertyName("id")] string? Id,
  [property: JsonPropertyName("method")] string? Method,
  [property: JsonPropertyName("params")] JsonElement Parameters);

internal sealed record GameAutomationResponse(
  [property: JsonPropertyName("id")] string? Id,
  [property: JsonPropertyName("result")] object? Result,
  [property: JsonPropertyName("error")] GameAutomationError? Error);

internal sealed record GameAutomationError(
  [property: JsonPropertyName("code")] string Code,
  [property: JsonPropertyName("message")] string Message);

internal sealed record GameAutomationCameraRequest(
  [property: JsonPropertyName("pan_right")] float PanRight = 0f,
  [property: JsonPropertyName("pan_forward")] float PanForward = 0f,
  [property: JsonPropertyName("pan_seconds")] float PanSeconds = 0f,
  [property: JsonPropertyName("orbit_degrees")] float OrbitDegrees = 0f,
  [property: JsonPropertyName("elevation_degrees")] float ElevationDegrees = 0f,
  [property: JsonPropertyName("zoom_steps")] float ZoomSteps = 0f,
  [property: JsonPropertyName("target_x")] float? TargetX = null,
  [property: JsonPropertyName("target_y")] float? TargetY = null,
  [property: JsonPropertyName("target_z")] float? TargetZ = null,
  [property: JsonPropertyName("distance")] float? Distance = null);

internal sealed record GameAutomationScreenshot(
  [property: JsonPropertyName("png_base64")] string PngBase64);

internal sealed record GameAutomationPauseRequest(
  [property: JsonPropertyName("paused")] bool Paused);

internal sealed record GameAutomationViewportRequest(
  [property: JsonPropertyName("width")] int Width,
  [property: JsonPropertyName("height")] int Height) {
  private const int MinimumDimension = 64;
  private const int MaximumDimension = 8192;

  internal void Validate() {
    if (Width < MinimumDimension || Width > MaximumDimension)
      throw new ArgumentOutOfRangeException(
        nameof(Width), $"Viewport width must be from {MinimumDimension} to {MaximumDimension}.");
    if (Height < MinimumDimension || Height > MaximumDimension)
      throw new ArgumentOutOfRangeException(
        nameof(Height), $"Viewport height must be from {MinimumDimension} to {MaximumDimension}.");
  }
}

internal sealed record GameAutomationTerrainPickRequest(
  [property: JsonPropertyName("x")] float X,
  [property: JsonPropertyName("y")] float Y) {
  internal void Validate(int width, int height) {
    if (!float.IsFinite(X) || X < 0f || X >= width)
      throw new ArgumentOutOfRangeException(nameof(X), "Terrain-pick X is outside the framebuffer.");
    if (!float.IsFinite(Y) || Y < 0f || Y >= height)
      throw new ArgumentOutOfRangeException(nameof(Y), "Terrain-pick Y is outside the framebuffer.");
  }
}

internal sealed record GameAutomationShutdown(
  [property: JsonPropertyName("accepted")] bool Accepted);
