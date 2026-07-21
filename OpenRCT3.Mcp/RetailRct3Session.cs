using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class RetailRct3Session : IAsyncDisposable {
  internal const string ExpectedSha256 =
    "1c9316e728d67aaa3bfe36d0a1634f3139582927440a5467c351e4c949b5b21d";
  internal const string DefaultExecutablePath =
    @"E:\Games\SteamLibrary\steamapps\common\RollerCoaster Tycoon 3 Complete Edition\RCT3.exe";

  private readonly SemaphoreSlim gate = new(1, 1);
  private Process? process;
  private NamedPipeClientStream? pipe;
  private StreamReader? reader;
  private StreamWriter? writer;
  private string? nonce;
  private long creationFileTime;
  private int requestNumber;

  public async Task<object> LaunchAsync(
    bool buildBridge,
    CancellationToken cancellationToken
  ) {
    await gate.WaitAsync(cancellationToken);
    try {
      if (process is { HasExited: false })
        throw new InvalidOperationException(
          "This MCP server already owns a running retail RCT3 process.");
      if (process != null) await ResetAsync();

      var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
      if (buildBridge) await BuildBridgeAsync(root, cancellationToken);
      var bridgeDirectory = Path.Combine(root, "RCT3Bridge", "build", "Release");
      var launcherPath = Path.Combine(bridgeDirectory, "RCT3Launcher.exe");
      var bridgePath = Path.Combine(bridgeDirectory, "RCT3Bridge.dll");
      if (!File.Exists(launcherPath) || !File.Exists(bridgePath))
        throw new FileNotFoundException(
          "Build RCT3Bridge before launching the retail process.", bridgePath);

      var executablePath = Environment.GetEnvironmentVariable("RCT3_RETAIL_EXE_PATH")
        ?? DefaultExecutablePath;
      executablePath = Path.GetFullPath(executablePath);
      await VerifyExecutableAsync(executablePath, cancellationToken);

      var pipeName = $"rct3-retail-mcp-{Guid.NewGuid():N}";
      nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
      var start = new ProcessStartInfo(launcherPath) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = Path.GetDirectoryName(executablePath)!
      };
      start.ArgumentList.Add(executablePath);
      start.ArgumentList.Add(bridgePath);
      start.ArgumentList.Add(ExpectedSha256);
      start.Environment["RCT3BRIDGE_PIPE"] = pipeName;
      start.Environment["RCT3BRIDGE_NONCE"] = nonce;
      using var launcher = Process.Start(start)
        ?? throw new InvalidOperationException("The retail RCT3 launcher did not start.");
      var receiptLineTask = launcher.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
      var errorTask = launcher.StandardError.ReadToEndAsync(cancellationToken);
      await launcher.WaitForExitAsync(cancellationToken);
      var receiptLine = await receiptLineTask;
      var launcherError = await errorTask;
      if (launcher.ExitCode != 0 || string.IsNullOrWhiteSpace(receiptLine))
        throw new InvalidOperationException(
          $"The retail RCT3 launcher failed: {launcherError.Trim()}");
      var receipt = JsonSerializer.Deserialize<LauncherReceipt>(receiptLine)
        ?? throw new InvalidDataException("The retail launcher returned an empty receipt.");
      creationFileTime = receipt.CreationFileTime;
      process = Process.GetProcessById(receipt.ProcessId);
      EnsureOwnedProcess();

      pipe = new NamedPipeClientStream(
        ".",
        pipeName,
        PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(TimeSpan.FromSeconds(45));
      try {
        await pipe.ConnectAsync(timeout.Token);
      } catch {
        StopOwnedProcess();
        throw;
      }
      reader = new StreamReader(pipe, leaveOpen: true);
      writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
      var state = await SendLockedAsync("hello", cancellationToken);
      return new {
        executable_path = executablePath,
        process_id = process.Id,
        creation_filetime = creationFileTime,
        sha256 = ExpectedSha256,
        file_version = FileVersionInfo.GetVersionInfo(executablePath).FileVersion,
        pe_machine = "0x014c",
        bridge_path = bridgePath,
        bridge_sha256 = await HashFileAsync(bridgePath, cancellationToken),
        launcher_path = launcherPath,
        launcher_sha256 = await HashFileAsync(launcherPath, cancellationToken),
        protocol_version = 1,
        state
      };
    } catch {
      if (process != null) StopOwnedProcess();
      await ResetAsync();
      throw;
    } finally {
      gate.Release();
    }
  }

  public Task<JsonElement> GetStateAsync(CancellationToken cancellationToken) =>
    SendAsync("state", cancellationToken);

  public Task<JsonElement> ClickAsync(
    int x,
    int y,
    CancellationToken cancellationToken
  ) => SendAsync("click", cancellationToken, x, y);

  public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken) {
    var result = await SendAsync("capture", cancellationToken);
    var encoded = result.GetProperty("png_base64").GetString()
      ?? throw new InvalidDataException("The retail bridge returned an empty screenshot.");
    return Convert.FromBase64String(encoded);
  }

  public async Task<JsonElement> ShutdownAsync(CancellationToken cancellationToken) {
    await gate.WaitAsync(cancellationToken);
    try {
      if (process == null)
        return JsonSerializer.SerializeToElement(new { accepted = true, already_stopped = true });
      JsonElement result;
      try {
        result = await SendLockedAsync("shutdown", cancellationToken);
      } finally {
        StopOwnedProcess();
        await ResetAsync();
      }
      return result;
    } finally {
      gate.Release();
    }
  }

  public async ValueTask DisposeAsync() {
    await gate.WaitAsync();
    try {
      if (process != null) StopOwnedProcess();
      await ResetAsync();
    } finally {
      gate.Release();
      gate.Dispose();
    }
  }

  private async Task<JsonElement> SendAsync(
    string method,
    CancellationToken cancellationToken,
    int? x = null,
    int? y = null
  ) {
    await gate.WaitAsync(cancellationToken);
    try {
      return await SendLockedAsync(method, cancellationToken, x, y);
    } finally {
      gate.Release();
    }
  }

  private async Task<JsonElement> SendLockedAsync(
    string method,
    CancellationToken cancellationToken,
    int? x = null,
    int? y = null
  ) {
    EnsureOwnedProcess();
    if (reader == null || writer == null || nonce == null)
      throw new InvalidOperationException("Launch retail RCT3 with retail_rct3_launch first.");
    var id = Convert.ToString(Interlocked.Increment(ref requestNumber));
    var request = new Dictionary<string, object?> {
      ["version"] = 1,
      ["nonce"] = nonce,
      ["id"] = id,
      ["method"] = method
    };
    if (x.HasValue) request["x"] = x.Value;
    if (y.HasValue) request["y"] = y.Value;
    await writer.WriteLineAsync(
      JsonSerializer.Serialize(request).AsMemory(), cancellationToken);
    var line = await reader.ReadLineAsync(cancellationToken)
      ?? throw new EndOfStreamException("The retail bridge closed its automation pipe.");
    var response = JsonSerializer.Deserialize<BridgeResponse>(line)
      ?? throw new InvalidDataException("The retail bridge returned an empty response.");
    if (response.Id != id)
      throw new InvalidDataException("The retail bridge returned a mismatched response id.");
    if (response.Error != null)
      throw new InvalidOperationException($"{response.Error.Code}: {response.Error.Message}");
    return response.Result.Clone();
  }

  private void EnsureOwnedProcess() {
    if (process == null || process.HasExited)
      throw new InvalidOperationException("Launch retail RCT3 with retail_rct3_launch first.");
    if (process.StartTime.ToUniversalTime().ToFileTimeUtc() != creationFileTime)
      throw new InvalidOperationException("The owned retail process identity no longer matches.");
  }

  private void StopOwnedProcess() {
    if (process == null || process.HasExited) return;
    EnsureOwnedProcess();
    process.Kill(entireProcessTree: true);
    process.WaitForExit(10000);
  }

  private async Task ResetAsync() {
    writer?.Dispose();
    reader?.Dispose();
    if (pipe != null) await pipe.DisposeAsync();
    process?.Dispose();
    writer = null;
    reader = null;
    pipe = null;
    process = null;
    nonce = null;
    creationFileTime = 0;
  }

  private static async Task VerifyExecutableAsync(
    string executablePath,
    CancellationToken cancellationToken
  ) {
    if (!File.Exists(executablePath))
      throw new FileNotFoundException("The configured retail RCT3 executable is missing.",
        executablePath);
    var hash = await HashFileAsync(executablePath, cancellationToken);
    if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Retail RCT3 SHA-256 mismatch. Expected {ExpectedSha256}, found {hash}.");
  }

  private static async Task<string> HashFileAsync(
    string path,
    CancellationToken cancellationToken
  ) {
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
      .ToLowerInvariant();
  }

  private static async Task BuildBridgeAsync(
    string root,
    CancellationToken cancellationToken
  ) {
    var script = Path.Combine(root, "RCT3Bridge", "build.ps1");
    var start = new ProcessStartInfo("powershell") {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      WorkingDirectory = root
    };
    start.ArgumentList.Add("-NoProfile");
    start.ArgumentList.Add("-ExecutionPolicy");
    start.ArgumentList.Add("Bypass");
    start.ArgumentList.Add("-File");
    start.ArgumentList.Add(script);
    start.ArgumentList.Add("-Test");
    using var build = Process.Start(start)
      ?? throw new InvalidOperationException("The RCT3Bridge build did not start.");
    var outputTask = build.StandardOutput.ReadToEndAsync(cancellationToken);
    var errorTask = build.StandardError.ReadToEndAsync(cancellationToken);
    await build.WaitForExitAsync(cancellationToken);
    var output = await outputTask;
    var error = await errorTask;
    if (build.ExitCode != 0)
      throw new InvalidOperationException($"RCT3Bridge build failed.\n{output}\n{error}".Trim());
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

  private sealed record LauncherReceipt(
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("creation_filetime")] long CreationFileTime);

  private sealed record BridgeResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("result")] JsonElement Result,
    [property: JsonPropertyName("error")] BridgeError? Error);

  private sealed record BridgeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
}
