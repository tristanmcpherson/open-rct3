[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OPENRCT3_VERIFY_NATIVE -ne '1') {
  Write-Output 'SKIP: set OPENRCT3_VERIFY_NATIVE=1 to run the native map smoke.'
  exit 0
}
if ($env:OS -ne 'Windows_NT') {
  Write-Output 'SKIP: native smoke currently requires Windows.'
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
  Write-Output 'SKIP: RCT3_PATH does not identify a complete installed RCT3 asset directory.'
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
    Write-Output 'SKIP: the installed RCT3 assets do not contain the default blank landscape.'
    exit 0
  }
  throw "OPENRCT3_MAP_PATH does not identify a map file: $mapPath"
}
$mapPath = (Resolve-Path -LiteralPath $mapPath).Path

. (Join-Path $PSScriptRoot 'NativeSmokeAssertions.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$driver = Join-Path $repo '.claude\skills\drive-native-app\scripts\AppDriver.ps1'
$results = Join-Path $repo 'TestResults\native-smoke'
$isolatedAppData = Join-Path $results 'AppData'
$isolatedTemp = Join-Path $results 'Temp'
$configDirectory = Join-Path $isolatedAppData 'OpenRCT3'
$configPath = Join-Path $configDirectory 'config.json'
$logPath = Join-Path $isolatedAppData 'OpenRCT3\logs\app.log'
$evidencePath = Join-Path $results 'native-smoke.txt'
$pidFile = Join-Path $isolatedTemp 'openrct3-driver.pid'
$runId = [Guid]::NewGuid().ToString('N')
$mapHash = (Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash
$timeoutSeconds = 30
if (-not [string]::IsNullOrWhiteSpace($env:OPENRCT3_NATIVE_TIMEOUT_SECONDS)) {
  $parsedTimeout = 0
  if (-not [int]::TryParse($env:OPENRCT3_NATIVE_TIMEOUT_SECONDS, [ref]$parsedTimeout) -or
      $parsedTimeout -lt 5 -or $parsedTimeout -gt 120) {
    throw 'OPENRCT3_NATIVE_TIMEOUT_SECONDS must be an integer from 5 through 120.'
  }
  $timeoutSeconds = $parsedTimeout
}

if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
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
  "selected-map=$mapPath",
  "selected-map-sha256=$mapHash",
  "selected-map-bytes=$((Get-Item -LiteralPath $mapPath).Length)",
  "config=$configPath"
) | Set-Content -LiteralPath $evidencePath -Encoding UTF8

$originalAppData = $env:APPDATA
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$originalMapPath = $env:OPENRCT3_MAP_PATH
$originalRunId = $env:OPENRCT3_SMOKE_RUN_ID
$originalMapHash = $env:OPENRCT3_SMOKE_MAP_SHA256
$env:APPDATA = $isolatedAppData
$env:TEMP = $isolatedTemp
$env:TMP = $isolatedTemp
$env:OPENRCT3_MAP_PATH = $mapPath
$env:OPENRCT3_SMOKE_RUN_ID = $runId
$env:OPENRCT3_SMOKE_MAP_SHA256 = $mapHash

$candidatePid = $null
$nlogPath = $null
$originalNlogBytes = $null
$primaryError = $null
$cleanupErrors = [System.Collections.Generic.List[string]]::new()

try {
  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Build |
    Tee-Object -FilePath $evidencePath -Append
  if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }

  $exe = Get-ChildItem -Path (Join-Path $repo 'OpenRCT3\bin\Debug') -Filter 'OpenRCT3.exe' -Recurse |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($null -eq $exe) { throw 'Native build produced no OpenRCT3.exe.' }
  $nlogPath = Join-Path $exe.DirectoryName 'nlog.config'
  if (-not (Test-Path -LiteralPath $nlogPath -PathType Leaf)) {
    throw "Native build produced no nlog.config beside $($exe.FullName)."
  }

  $originalNlogBytes = [System.IO.File]::ReadAllBytes($nlogPath)
  [xml]$nlog = Get-Content -Raw -LiteralPath $nlogPath
  $fileTarget = $nlog.SelectSingleNode("//*[local-name()='target' and @name='file']")
  if ($null -eq $fileTarget) { throw 'Native nlog.config has no file target.' }
  $fileTarget.SetAttribute(
    'layout',
    'run=${environment:variable=OPENRCT3_SMOKE_RUN_ID}|map=${environment:variable=OPENRCT3_SMOKE_MAP_SHA256}|${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}')
  $nlog.Save($nlogPath)

  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Launch -TimeoutSec $timeoutSeconds |
    Tee-Object -FilePath $evidencePath -Append
  if ($LASTEXITCODE -ne 0) { throw "Native launch failed with exit code $LASTEXITCODE." }
  if (-not (Test-Path -LiteralPath $pidFile -PathType Leaf)) {
    throw 'Native launch did not record its candidate process id.'
  }
  $candidatePid = [int](Get-Content -Raw -LiteralPath $pidFile).Trim()

  $deadline = (Get-Date).AddSeconds($timeoutSeconds)
  $state = $null
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

    $state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
    if ($state.HasFailure) { Assert-NativeSmokeCompletion -State $state }
    if ($state.HasStartup -and $state.HasWorldLoaded -and $state.HasTerrainMesh) { break }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $deadline)

  Assert-NativeSmokeCompletion -State $state
  if ((Get-FileHash -LiteralPath $mapPath -Algorithm SHA256).Hash -ne $mapHash) {
    throw 'The selected map changed during the native smoke run.'
  }

  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Info |
    Tee-Object -FilePath $evidencePath -Append
  if ($LASTEXITCODE -ne 0) { throw "Native window probe failed with exit code $LASTEXITCODE." }

  Add-Content -LiteralPath $evidencePath -Value 'run-correlated-log-markers:'
  $state.Lines | Add-Content -LiteralPath $evidencePath
} catch {
  $primaryError = $_
} finally {
  if ($null -ne $candidatePid) {
    $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
    if ($null -ne $candidate) {
      $closeError = $null
      try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Close -TimeoutSec 5 2>&1 |
          Tee-Object -FilePath $evidencePath -Append
      } catch {
        $closeError = $_
      }

      $candidate = Get-Process -Id $candidatePid -ErrorAction SilentlyContinue
      if ($null -ne $candidate) {
        if ($null -ne $closeError) {
          $cleanupErrors.Add("Native close driver failed: $($closeError.Exception.Message)")
        }
        $cleanupErrors.Add("Candidate process $candidatePid did not exit within 5 seconds after WM_CLOSE.")
        try {
          Stop-Process -Id $candidatePid -Force -ErrorAction Stop
        } catch {
          $cleanupErrors.Add("Could not force-stop candidate process ${candidatePid}: $($_.Exception.Message)")
        }

        $forceDeadline = (Get-Date).AddSeconds(5)
        while ((Get-Process -Id $candidatePid -ErrorAction SilentlyContinue) -and
               (Get-Date) -lt $forceDeadline) {
          Start-Sleep -Milliseconds 100
        }
      }
    }

    try {
      Assert-NativeSmokeProcessExited -ProcessId $candidatePid
    } catch {
      $cleanupErrors.Add($_.Exception.Message)
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
  $env:OPENRCT3_SMOKE_MAP_SHA256 = $originalMapHash
}

if ($cleanupErrors.Count -gt 0) {
  $message = "Native smoke cleanup failed: $($cleanupErrors -join ' ')"
  if ($null -ne $primaryError) { $message += " Original failure: $($primaryError.Exception.Message)" }
  throw $message
}
if ($null -ne $primaryError) { throw $primaryError }

Write-Output "Native map smoke passed; run=$runId; map-sha256=$mapHash; artifacts=$results" |
  Tee-Object -FilePath $evidencePath -Append
