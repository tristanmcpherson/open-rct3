param(
  [string]$ExecutablePath = 'E:\Games\SteamLibrary\steamapps\common\RollerCoaster Tycoon 3 Complete Edition\RCT3.exe',
  [string]$ExpectedSha256 = '1c9316e728d67aaa3bfe36d0a1634f3139582927440a5467c351e4c949b5b21d',
  [string]$OutputPath,
  [int]$RenderWaitSeconds = 10
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (!$OutputPath) {
  $OutputPath = Join-Path $root 'build\retail-smoke.png'
}
$launcherPath = Join-Path $root 'build\Release\RCT3Launcher.exe'
$bridgePath = Join-Path $root 'build\Release\RCT3Bridge.dll'
$pipeName = "rct3-retail-smoke-$([Guid]::NewGuid().ToString('N'))"
$nonceBytes = [byte[]]::new(32)
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$random.GetBytes($nonceBytes)
$random.Dispose()
$nonce = ([BitConverter]::ToString($nonceBytes) -replace '-', '').ToLowerInvariant()
$retail = $null
$pipe = $null
$reader = $null
$writer = $null

function Send-BridgeRequest([string]$Method, [int]$Id) {
  $request = @{
    version = 1
    nonce = $nonce
    id = [Convert]::ToString($Id)
    method = $Method
  } | ConvertTo-Json -Compress
  $writer.WriteLine($request)
  $response = $reader.ReadLine()
  if (!$response) { throw 'The retail bridge closed its pipe.' }
  $parsed = $response | ConvertFrom-Json
  if ($parsed.id -ne [Convert]::ToString($Id)) { throw 'The retail bridge returned a mismatched id.' }
  if ($parsed.error) { throw "$($parsed.error.code): $($parsed.error.message)" }
  return $parsed.result
}

try {
  $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExecutablePath).Hash.ToLowerInvariant()
  if ($actualHash -ne $ExpectedSha256) { throw "Retail SHA-256 mismatch: $actualHash" }
  if (!(Test-Path -LiteralPath $launcherPath) -or !(Test-Path -LiteralPath $bridgePath)) {
    throw 'Build the bridge before running the smoke test.'
  }

  $start = [Diagnostics.ProcessStartInfo]::new($launcherPath)
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $start.WorkingDirectory = Split-Path -Parent $ExecutablePath
  $start.Arguments = '"' + $ExecutablePath + '" "' + $bridgePath + '" ' + $ExpectedSha256
  $start.Environment['RCT3BRIDGE_PIPE'] = $pipeName
  $start.Environment['RCT3BRIDGE_NONCE'] = $nonce
  $launcher = [Diagnostics.Process]::Start($start)
  $receiptLine = $launcher.StandardOutput.ReadLine()
  $launcher.WaitForExit()
  if ($launcher.ExitCode -ne 0) { throw $launcher.StandardError.ReadToEnd() }
  $receipt = $receiptLine | ConvertFrom-Json
  $retail = [Diagnostics.Process]::GetProcessById($receipt.pid)
  if ($retail.StartTime.ToUniversalTime().ToFileTimeUtc() -ne $receipt.creation_filetime) {
    throw 'Retail process identity mismatch.'
  }

  $options = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly
  $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName,
    [IO.Pipes.PipeDirection]::InOut, $options)
  $pipe.Connect(45000)
  $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8, $false, 1024, $true)
  $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
  $writer.AutoFlush = $true

  $state = Send-BridgeRequest 'hello' 1
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  $id = 2
  while (!$state.device_ready -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $state = Send-BridgeRequest 'state' $id
    ++$id
  }
  if (!$state.device_ready) { throw 'The retail D3D9 device did not become ready within 60 seconds.' }
  Start-Sleep -Seconds $RenderWaitSeconds
  $state = Send-BridgeRequest 'state' $id
  ++$id
  $capture = Send-BridgeRequest 'capture' $id
  ++$id
  $bytes = [Convert]::FromBase64String($capture.png_base64)
  if ($bytes.Length -lt 1000) { throw "Retail capture was unexpectedly small: $($bytes.Length) bytes." }
  $directory = Split-Path -Parent $OutputPath
  New-Item -ItemType Directory -Force -Path $directory | Out-Null
  [IO.File]::WriteAllBytes($OutputPath, $bytes)
  Send-BridgeRequest 'shutdown' $id | Out-Null
  [pscustomobject]@{
    process_id = $receipt.pid
    executable_sha256 = $actualHash
    capture_path = [IO.Path]::GetFullPath($OutputPath)
    capture_bytes = $bytes.Length
    device_ready = $state.device_ready
    view_observed = $state.view_observed
    projection_observed = $state.projection_observed
    vertex_shader_constants_observed = $state.vertex_shader_constants_observed
  } | ConvertTo-Json -Compress
} finally {
  if ($writer) { $writer.Dispose() }
  if ($reader) { $reader.Dispose() }
  if ($pipe) { $pipe.Dispose() }
  if ($retail -and !$retail.HasExited) {
    $retail.Refresh()
    if ($retail.StartTime.ToUniversalTime().ToFileTimeUtc() -eq $receipt.creation_filetime) {
      $retail.Kill()
      $retail.WaitForExit(10000)
    }
  }
  if ($retail) { $retail.Dispose() }
}
