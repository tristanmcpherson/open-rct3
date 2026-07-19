[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OPENRCT3_VERIFY_NATIVE -ne '1') {
  Write-Output 'SKIP: set OPENRCT3_VERIFY_NATIVE=1 to run the native map smoke.'
  exit 0
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$results = Join-Path $repo 'TestResults\native-smoke'
$manifestPath = Join-Path $results 'native-smoke.json'
$evidencePath = Join-Path $results 'native-smoke.txt'
if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null

. (Join-Path $PSScriptRoot 'NativeSmokeAssertions.ps1')
. (Join-Path $PSScriptRoot 'NativeSmokeEvidence.ps1')

$manifest = New-NativeSmokeEvidenceRecord -RepoPath $repo
$manifest.artifacts.transcriptPath = $evidencePath

function Write-NativeSmokeSkip {
  param([Parameter(Mandatory = $true)][string]$Reason)

  $manifest.outcome = 'skipped'
  $manifest.reason = $Reason
  Write-NativeSmokeEvidenceManifest -Evidence $manifest -Path $manifestPath
  Write-Output "SKIP: $Reason; manifest=$manifestPath"
}

function Invoke-NativeDriver {
  param([Parameter(Mandatory = $true)][string[]]$Arguments)

  $lines = @()
  $exitCode = 1
  try {
    $lines = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $driver @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
  } catch {
    $lines += $_.Exception.Message
  }
  return [PSCustomObject]@{ ExitCode = $exitCode; Lines = $lines }
}

function Write-DriverEvidence {
  param([Parameter(Mandatory = $true)][PSCustomObject]$Invocation)

  $Invocation.Lines | Tee-Object -FilePath $evidencePath -Append | Write-Output
}

function Get-DriverJson {
  param([Parameter(Mandatory = $true)][PSCustomObject]$Invocation)

  $jsonLine = @($Invocation.Lines | Where-Object { [string]$_ -match '^\s*\{' }) |
    Select-Object -Last 1
  if ($null -eq $jsonLine) { throw 'Native driver produced no JSON evidence object.' }
  return ([string]$jsonLine | ConvertFrom-Json)
}

if ($env:OS -ne 'Windows_NT') {
  Write-NativeSmokeSkip 'native smoke currently requires Windows'
  exit 0
}

$rct3Path = $env:RCT3_PATH
$terrainOvl = if ([string]::IsNullOrWhiteSpace($rct3Path)) {
  $null
} else {
  Join-Path $rct3Path 'terrain\RCT3\Terrain_RCT3.common.ovl'
}
if ([string]::IsNullOrWhiteSpace($rct3Path) -or
    -not (Test-Path -LiteralPath $terrainOvl -PathType Leaf)) {
  Write-NativeSmokeSkip 'RCT3_PATH does not identify a complete installed RCT3 asset directory'
  exit 0
}
$rct3Path = (Resolve-Path -LiteralPath $rct3Path).Path

$mapPath = $env:OPENRCT3_MAP_PATH
if ([string]::IsNullOrWhiteSpace($mapPath)) {
  $mapPath = Join-Path $rct3Path 'Campaigns\Base\BlankLandscape.dat'
} elseif (-not [System.IO.Path]::IsPathRooted($mapPath)) {
  $mapPath = Join-Path $rct3Path $mapPath
}
if (-not (Test-Path -LiteralPath $mapPath -PathType Leaf)) {
  if ([string]::IsNullOrWhiteSpace($env:OPENRCT3_MAP_PATH)) {
    Write-NativeSmokeSkip 'installed RCT3 assets do not contain the default blank landscape'
    exit 0
  }
  $manifest.reason = "OPENRCT3_MAP_PATH does not identify a map file: $mapPath"
  Write-NativeSmokeEvidenceManifest -Evidence $manifest -Path $manifestPath
  throw $manifest.reason
}
$mapPath = (Resolve-Path -LiteralPath $mapPath).Path
$mapHash = (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash
$manifest.requestedMap = [PSCustomObject]@{
  available = $true
  path = $mapPath
  sha256 = $mapHash
}

$driver = Join-Path $repo '.claude\skills\drive-native-app\scripts\AppDriver.ps1'
$isolatedAppData = Join-Path $results 'AppData'
$isolatedTemp = Join-Path $results 'Temp'
$configDirectory = Join-Path $isolatedAppData 'OpenRCT3'
$configPath = Join-Path $configDirectory 'config.json'
$logPath = Join-Path $isolatedAppData 'OpenRCT3\logs\app.log'
$pidFile = Join-Path $isolatedTemp 'openrct3-driver.pid'
$runId = [Guid]::NewGuid().ToString('N')
$timeoutSeconds = 30
if (-not [string]::IsNullOrWhiteSpace($env:OPENRCT3_NATIVE_TIMEOUT_SECONDS)) {
  $parsedTimeout = 0
  if (-not [int]::TryParse($env:OPENRCT3_NATIVE_TIMEOUT_SECONDS, [ref]$parsedTimeout) -or
      $parsedTimeout -lt 5 -or $parsedTimeout -gt 120) {
    $manifest.reason = 'OPENRCT3_NATIVE_TIMEOUT_SECONDS must be an integer from 5 through 120.'
    Write-NativeSmokeEvidenceManifest -Evidence $manifest -Path $manifestPath
    throw $manifest.reason
  }
  $timeoutSeconds = $parsedTimeout
}

New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedTemp -Force | Out-Null
@{
  InstallPath = $rct3Path
  MapPath = $mapPath
  ExtraPaths = @($rct3Path)
  SuppressCrashAlerts = $true
} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
Assert-NativeSmokeConfig -ConfigPath $configPath -InstallPath $rct3Path -MapPath $mapPath

@(
  "run-id=$runId",
  "requested-map=$mapPath",
  "requested-map-sha256=$mapHash",
  "requested-map-bytes=$((Get-Item -LiteralPath $mapPath).Length)",
  "config=$configPath",
  "manifest=$manifestPath"
) | Set-Content -LiteralPath $evidencePath -Encoding UTF8

$originalAppData = $env:APPDATA
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$originalMapPath = $env:OPENRCT3_MAP_PATH
$originalRunId = $env:OPENRCT3_SMOKE_RUN_ID
$env:APPDATA = $isolatedAppData
$env:TEMP = $isolatedTemp
$env:TMP = $isolatedTemp
$env:OPENRCT3_MAP_PATH = $mapPath
$env:OPENRCT3_SMOKE_RUN_ID = $runId

$candidatePid = $null
$candidateMetadata = $null
$nlogPath = $null
$originalNlogBytes = $null
$state = $null
$primaryError = $null
$smokeChecksPassed = $false
$cleanupErrors = [System.Collections.Generic.List[string]]::new()

try {
  $build = Invoke-NativeDriver -Arguments @('-Action', 'Build')
  Write-DriverEvidence -Invocation $build
  if ($build.ExitCode -ne 0) { throw "Native build failed with exit code $($build.ExitCode)." }

  $exe = Get-ChildItem -Path (Join-Path $repo 'OpenRCT3\bin\Debug') -Filter 'OpenRCT3.exe' -Recurse |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($null -eq $exe) { throw 'Native build produced no OpenRCT3.exe.' }
  $executableHash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
  $manifest.executable = [PSCustomObject]@{
    available = $true
    path = $exe.FullName
    sha256 = $executableHash
  }
  Add-Content -LiteralPath $evidencePath -Value @(
    "executable=$($exe.FullName)",
    "pre-launch-executable-sha256=$executableHash"
  )

  $nlogPath = Join-Path $exe.DirectoryName 'nlog.config'
  if (-not (Test-Path -LiteralPath $nlogPath -PathType Leaf)) {
    throw "Native build produced no nlog.config beside $($exe.FullName)."
  }
  Assert-NativeSmokeFileLoggingConfiguration -ConfigPath $nlogPath
  $originalNlogBytes = [System.IO.File]::ReadAllBytes($nlogPath)
  [xml]$nlog = Get-Content -Raw -LiteralPath $nlogPath
  $fileTarget = $nlog.SelectSingleNode("//*[local-name()='target' and @name='file']")
  if ($null -eq $fileTarget) { throw 'Native nlog.config has no file target.' }
  $fileTarget.SetAttribute(
    'layout',
    'run=${environment:variable=OPENRCT3_SMOKE_RUN_ID}|${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}')
  $nlog.Save($nlogPath)

  $launch = Invoke-NativeDriver -Arguments @(
    '-Action', 'Launch', '-TimeoutSec', [string]$timeoutSeconds, '-StrictPid', '-Json')
  Write-DriverEvidence -Invocation $launch
  if (Test-Path -LiteralPath $pidFile -PathType Leaf) {
    $candidateMetadata = Read-NativeDriverPidMetadata -PidFile $pidFile
    $candidatePid = [int]$candidateMetadata.ProcessId
  }
  if ($launch.ExitCode -ne 0) { throw "Native launch failed with exit code $($launch.ExitCode)." }
  if ($null -eq $candidateMetadata) { throw 'Native launch produced no strict PID metadata.' }

  $launchInfo = Get-DriverJson -Invocation $launch
  if ([int]$launchInfo.ProcessId -ne $candidatePid) {
    throw 'Native launch JSON PID does not match strict PID metadata.'
  }
  $manifest.process = [PSCustomObject]@{
    available = $true
    pid = $candidatePid
    hwnd = [long]$launchInfo.WindowHandle
    startTimeUtc = [string]$candidateMetadata.StartTimeUtc
    executablePath = [string]$candidateMetadata.ExecutablePath
  }

  $deadline = (Get-Date).AddSeconds($timeoutSeconds)
  do {
    $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
    if ($null -eq $candidate) {
      throw "Native candidate process $candidatePid exited before world and terrain completion."
    }
    $candidate.Refresh()
    if (-not [string]::IsNullOrWhiteSpace($candidate.MainWindowTitle) -and
        $candidate.MainWindowTitle -notlike '*OpenRCT3*') {
      throw "Native candidate opened an unexpected window '$($candidate.MainWindowTitle)' (picker or ignored config)."
    }

    $state = Get-NativeSmokeLogState `
      -LogPath $logPath `
      -RunId $runId `
      -ExpectedMapPath $mapPath `
      -ExpectedMapSha256 $mapHash
    if ($state.LoadedMapCount -eq 1 -and
        [string]::IsNullOrWhiteSpace($state.LoadedMapParseError)) {
      $manifest.loadedMap = [PSCustomObject]@{
        available = $true
        path = $state.LoadedMapPath
        sha256 = $state.LoadedMapSha256
      }
    }
    if ($state.HasFailure) { Assert-NativeSmokeCompletion -State $state }
    if ($state.HasStartup -and $state.HasWorldLoaded -and $state.HasTerrainMesh -and
        $state.LoadedMapCount -eq 1) { break }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $deadline)

  Assert-NativeSmokeCompletion -State $state
  if ((Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash -ne $mapHash) {
    throw 'The requested map changed during the native smoke run.'
  }

  $info = Invoke-NativeDriver -Arguments @('-Action', 'Info', '-StrictPid', '-Json')
  Write-DriverEvidence -Invocation $info
  if ($info.ExitCode -ne 0) { throw "Native window probe failed with exit code $($info.ExitCode)." }
  $infoObject = Get-DriverJson -Invocation $info
  if ([int]$infoObject.ProcessId -ne $candidatePid -or
      [string]$infoObject.StartTimeUtc -ne [string]$candidateMetadata.StartTimeUtc -or
      -not (Get-NormalizedSmokePath $infoObject.ExecutablePath).Equals(
        (Get-NormalizedSmokePath $candidateMetadata.ExecutablePath),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Native window probe does not match the strict candidate process identity.'
  }
  $manifest.process.hwnd = [long]$infoObject.WindowHandle
  $manifest.window.observedDpi =
    [PSCustomObject]@{ available = $true; value = [int]$infoObject.Dpi }
  $manifest.window.observedClientSize = [PSCustomObject]@{
    available = $true
    width = [int]$infoObject.ClientWidth
    height = [int]$infoObject.ClientHeight
  }

  Add-Content -LiteralPath $evidencePath -Value 'run-correlated-log-markers:'
  $state.Lines | Add-Content -LiteralPath $evidencePath
  $manifest.artifacts.applicationLogPath = $logPath
  $smokeChecksPassed = $true
} catch {
  $primaryError = $_
} finally {
  if ($null -eq $candidatePid -and (Test-Path -LiteralPath $pidFile -PathType Leaf)) {
    try {
      $candidateMetadata = Read-NativeDriverPidMetadata -PidFile $pidFile
      $candidatePid = [int]$candidateMetadata.ProcessId
    } catch {
      $cleanupErrors.Add("Could not recover strict PID metadata: $($_.Exception.Message)")
    }
  }

  if ($null -ne $candidatePid) {
    $manifest.cleanup.attempted = $true
    $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
    if ($null -eq $candidate) {
      $manifest.cleanup.closeResult = 'already-exited'
    } else {
      $close = Invoke-NativeDriver -Arguments @(
        '-Action', 'Close', '-TimeoutSec', '5', '-StrictPid')
      Write-DriverEvidence -Invocation $close
      $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
      if ($close.ExitCode -eq 0 -and $null -eq $candidate) {
        $manifest.cleanup.closeResult = 'graceful'
      } else {
        $cleanupErrors.Add("Candidate process $candidatePid did not exit cleanly through strict Close.")
        $forceClose = Invoke-NativeDriver -Arguments @('-Action', 'Close', '-StrictPid', '-Force')
        Write-DriverEvidence -Invocation $forceClose
        $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
        if ($null -eq $candidate) {
          $manifest.cleanup.closeResult = 'forced'
        } else {
          $manifest.cleanup.closeResult = 'failed'
          try {
            Stop-Process -Id $candidatePid -Force -ErrorAction Stop
          } catch {
            $cleanupErrors.Add("Could not force-stop candidate process ${candidatePid}: $($_.Exception.Message)")
          }
        }
      }
    }
    $manifest.cleanup.processExited =
      $null -eq (Get-Process -Id $candidatePid -ErrorAction SilentlyContinue)
    if ($manifest.cleanup.processExited -ne $true) {
      $cleanupErrors.Add("Native smoke candidate process $candidatePid is still running after cleanup.")
    }
  }

  if ($null -ne $originalNlogBytes -and $null -ne $nlogPath) {
    try {
      [System.IO.File]::WriteAllBytes($nlogPath, $originalNlogBytes)
    } catch {
      $cleanupErrors.Add("Could not restore built nlog.config: $($_.Exception.Message)")
    }
  }

  $env:APPDATA = $originalAppData
  $env:TEMP = $originalTemp
  $env:TMP = $originalTmp
  $env:OPENRCT3_MAP_PATH = $originalMapPath
  $env:OPENRCT3_SMOKE_RUN_ID = $originalRunId
}

if ($cleanupErrors.Count -eq 0 -and $null -eq $primaryError -and $smokeChecksPassed) {
  $manifest.outcome = 'passed'
  $manifest.reason = $null
} else {
  $manifest.outcome = 'failed'
  $reasons = @($cleanupErrors)
  if ($null -ne $primaryError) { $reasons += $primaryError.Exception.Message }
  if ($reasons.Count -eq 0) { $reasons += 'native smoke did not complete its required checks' }
  $manifest.reason = $reasons -join ' '
}

try {
  Write-NativeSmokeEvidenceManifest -Evidence $manifest -Path $manifestPath
} catch {
  $manifest.outcome = 'failed'
  $manifest.reason = "Native evidence validation failed: $($_.Exception.Message)"
  Write-NativeSmokeEvidenceManifest -Evidence $manifest -Path $manifestPath
  throw $manifest.reason
}

if ($manifest.outcome -ne 'passed') { throw $manifest.reason }
Write-Output "Native map smoke passed; run=$runId; manifest=$manifestPath; artifacts=$results" |
  Tee-Object -FilePath $evidencePath -Append
