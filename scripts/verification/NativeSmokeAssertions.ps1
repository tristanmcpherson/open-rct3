function Get-NativeSmokeLogState {
  param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,
    [Parameter(Mandatory = $true)]
    [string]$RunId,
    [Parameter(Mandatory = $true)]
    [string]$MapSha256
  )

  $prefix = "run=$RunId|map=$MapSha256|"
  $lines = if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
    @(Get-Content -LiteralPath $LogPath | Where-Object { $_.StartsWith($prefix) })
  } else {
    @()
  }

  return [PSCustomObject]@{
    Prefix = $prefix
    Lines = $lines
    HasStartup = @($lines | Where-Object {
      $_ -match '\|INFO\|OpenRCT3\.Program\|Starting OpenRCT3 on Windows'
    }).Count -gt 0
    HasWorldLoaded = @($lines | Where-Object {
      $_ -match '\|TRACE\|OpenRCT3\.Game\|Game world loaded'
    }).Count -gt 0
    HasTerrainMesh = @($lines | Where-Object {
      $_ -match '\|TRACE\|OpenRCT3\.Game\|Added terrain mesh'
    }).Count -gt 0
    HasFailure = @($lines | Where-Object { $_ -match '\|(ERROR|FATAL)\|' }).Count -gt 0
  }
}

function Assert-NativeSmokeCompletion {
  param(
    [Parameter(Mandatory = $true)]
    [PSCustomObject]$State
  )

  if ($State.HasFailure) {
    throw 'The run-correlated native log contains an ERROR or FATAL event.'
  }

  $missing = @()
  if (-not $State.HasStartup) { $missing += 'Windows startup' }
  if (-not $State.HasWorldLoaded) { $missing += 'Game world loaded' }
  if (-not $State.HasTerrainMesh) { $missing += 'Added terrain mesh' }
  if ($missing.Count -gt 0) {
    throw "Native smoke did not reach required completion markers: $($missing -join ', ')."
  }
}

function Get-NormalizedSmokePath {
  param([Parameter(Mandatory = $true)][string]$Path)

  return [System.IO.Path]::GetFullPath($Path).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
}

function Assert-NativeSmokeConfig {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)]
    [string]$InstallPath,
    [Parameter(Mandatory = $true)]
    [string]$MapPath
  )

  $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
  $expectedInstall = Get-NormalizedSmokePath $InstallPath
  $expectedMap = Get-NormalizedSmokePath $MapPath
  $actualInstall = Get-NormalizedSmokePath ([string]$config.InstallPath)
  $actualMap = Get-NormalizedSmokePath ([string]$config.MapPath)

  if (-not $actualInstall.Equals($expectedInstall, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Native smoke config InstallPath mismatch: expected '$expectedInstall', got '$actualInstall'."
  }
  if (-not $actualMap.Equals($expectedMap, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Native smoke config MapPath mismatch: expected '$expectedMap', got '$actualMap'."
  }
  if ($config.SuppressCrashAlerts -ne $true) {
    throw 'Native smoke config must suppress modal crash alerts.'
  }
}

function Assert-NativeSmokeProcessExited {
  param([Parameter(Mandatory = $true)][int]$ProcessId)

  if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    throw "Native smoke candidate process $ProcessId is still running after cleanup."
  }
}
