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

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$driver = Join-Path $repo '.claude\skills\drive-native-app\scripts\AppDriver.ps1'
$results = Join-Path $repo 'TestResults\native-smoke'
$isolatedAppData = Join-Path $results 'AppData'
$isolatedTemp = Join-Path $results 'Temp'
$configDirectory = Join-Path $isolatedAppData 'OpenRCT3'
$configPath = Join-Path $configDirectory 'config.json'
$evidencePath = Join-Path $results 'native-smoke.txt'

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

$env:APPDATA = $isolatedAppData
$env:TEMP = $isolatedTemp
$env:TMP = $isolatedTemp
$launched = $false

try {
  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Build |
    Tee-Object -FilePath $evidencePath
  if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }

  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Launch |
    Tee-Object -FilePath $evidencePath -Append
  if ($LASTEXITCODE -ne 0) { throw "Native launch failed with exit code $LASTEXITCODE." }
  $launched = $true

  Start-Sleep -Seconds 3
  & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Info |
    Tee-Object -FilePath $evidencePath -Append
  if ($LASTEXITCODE -ne 0) { throw "Native window probe failed with exit code $LASTEXITCODE." }

  $logPath = Join-Path $isolatedAppData 'OpenRCT3\logs\app.log'
  if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Native smoke produced no application log at $logPath."
  }
  $log = Get-Content -Raw -LiteralPath $logPath
  if ($log -notmatch 'Starting OpenRCT3 on Windows') {
    throw 'Native smoke log does not contain the expected Windows startup event.'
  }
  if ($log -match '\|(ERROR|FATAL)\|') {
    throw 'Native smoke log contains an ERROR or FATAL event.'
  }

  Write-Output "Native map smoke passed; artifacts=$results" |
    Tee-Object -FilePath $evidencePath -Append
} finally {
  if ($launched) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $driver -Action Close |
      Tee-Object -FilePath $evidencePath -Append
  }
}
