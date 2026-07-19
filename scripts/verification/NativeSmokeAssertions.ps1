function Get-NormalizedSmokePath {
  param([Parameter(Mandatory = $true)][string]$Path)

  return [System.IO.Path]::GetFullPath($Path).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
}

function Get-NativeSmokeLogState {
  param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,
    [Parameter(Mandatory = $true)]
    [string]$RunId,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedMapPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedMapSha256
  )

  $prefix = "run=$RunId|"
  $lines = if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
    @(Get-Content -LiteralPath $LogPath | Where-Object { $_.StartsWith($prefix) })
  } else {
    @()
  }
  $loadedMapLines = @($lines | Where-Object {
    $_ -match '\|INFO\|OpenRCT3\.Simulation\.Terrain\|Native smoke loaded map '
  })
  $loadedMapPath = $null
  $loadedMapSha256 = $null
  $loadedMapParseError = $null
  if ($loadedMapLines.Count -eq 1) {
    if ($loadedMapLines[0] -match 'Native smoke loaded map (?<identity>\{.+\})\s*$') {
      try {
        $identity = $Matches.identity | ConvertFrom-Json
        $loadedMapPath = [string]$identity.path
        $loadedMapSha256 = [string]$identity.sha256
      } catch {
        $loadedMapParseError = $_.Exception.Message
      }
    } else {
      $loadedMapParseError = 'The loaded-map marker does not contain a JSON identity.'
    }
  }

  $mapPathMatches = $false
  if (-not [string]::IsNullOrWhiteSpace($loadedMapPath)) {
    $mapPathMatches = (Get-NormalizedSmokePath $loadedMapPath).Equals(
      (Get-NormalizedSmokePath $ExpectedMapPath),
      [StringComparison]::OrdinalIgnoreCase)
  }

  return [PSCustomObject]@{
    Prefix = $prefix
    Lines = $lines
    HasStartup = @($lines | Where-Object {
      $_ -match '\|INFO\|OpenRCT3\.Program\|Starting OpenRCT3 on Windows'
    }).Count -gt 0
    HasWorldLoaded = @($lines | Where-Object {
      $_ -match '\|DEBUG\|OpenRCT3\.Game\|Game world loaded'
    }).Count -gt 0
    HasTerrainMesh = @($lines | Where-Object {
      $_ -match '\|DEBUG\|OpenRCT3\.Game\|Added terrain mesh'
    }).Count -gt 0
    HasFailure = @($lines | Where-Object { $_ -match '\|(ERROR|FATAL)\|' }).Count -gt 0
    LoadedMapCount = $loadedMapLines.Count
    LoadedMapPath = $loadedMapPath
    LoadedMapSha256 = $loadedMapSha256
    LoadedMapParseError = $loadedMapParseError
    LoadedMapPathMatches = $mapPathMatches
    LoadedMapHashMatches = $loadedMapSha256 -eq $ExpectedMapSha256
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
  if ($State.LoadedMapCount -ne 1) { $missing += 'exactly one application loaded-map identity' }
  if ($missing.Count -gt 0) {
    throw "Native smoke did not reach required completion markers: $($missing -join ', ')."
  }
  if (-not [string]::IsNullOrWhiteSpace($State.LoadedMapParseError)) {
    throw "Native smoke loaded-map marker is invalid: $($State.LoadedMapParseError)"
  }
  if (-not $State.LoadedMapPathMatches) {
    throw "Application loaded a different map path: '$($State.LoadedMapPath)'."
  }
  if (-not $State.LoadedMapHashMatches) {
    throw "Application loaded a different map hash: '$($State.LoadedMapSha256)'."
  }
}

function Assert-NativeSmokeFileLoggingConfiguration {
  param([Parameter(Mandatory = $true)][string]$ConfigPath)

  [xml]$config = Get-Content -Raw -LiteralPath $ConfigPath
  $rules = @($config.SelectNodes("//*[local-name()='rules']/*[local-name()='logger']"))
  $levelOrder = @{
    Trace = 0
    Debug = 1
    Info = 2
    Warn = 3
    Error = 4
    Fatal = 5
  }
  $fileRule = $rules | Where-Object {
    @([string]$_.GetAttribute('writeTo') -split ',' | ForEach-Object { $_.Trim() }) -contains 'file'
  } | Where-Object {
    $minimumValue = $_.GetAttribute('minlevel')
    $maximumValue = $_.GetAttribute('maxlevel')
    $minimum = if ([string]::IsNullOrWhiteSpace($minimumValue)) { 'Trace' } else { $minimumValue }
    $maximum = if ([string]::IsNullOrWhiteSpace($maximumValue)) { 'Fatal' } else { $maximumValue }
    $levelOrder[$minimum] -le $levelOrder.Debug -and $levelOrder[$maximum] -ge $levelOrder.Info
  } | Select-Object -First 1

  if ($null -eq $fileRule) {
    throw 'nlog.config does not route the required Debug and Info smoke markers to the file target.'
  }
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

function Read-NativeDriverPidMetadata {
  param([Parameter(Mandatory = $true)][string]$PidFile)

  if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) {
    throw "Native driver PID metadata is missing: $PidFile"
  }
  $metadata = Get-Content -Raw -LiteralPath $PidFile | ConvertFrom-Json
  if ([int]$metadata.ProcessId -le 0) { throw 'Native driver PID metadata has no valid ProcessId.' }
  if ([string]::IsNullOrWhiteSpace([string]$metadata.ExecutablePath) -or
      -not [System.IO.Path]::IsPathRooted([string]$metadata.ExecutablePath)) {
    throw 'Native driver PID metadata has no absolute ExecutablePath.'
  }
  $parsedStartTime = [DateTime]::MinValue
  if (-not [DateTime]::TryParse(
      [string]$metadata.StartTimeUtc,
      [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind,
      [ref]$parsedStartTime)) {
    throw 'Native driver PID metadata has no valid StartTimeUtc.'
  }
  return $metadata
}

function Assert-NativeSmokeProcessExited {
  param([Parameter(Mandatory = $true)][int]$ProcessId)

  if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    throw "Native smoke candidate process $ProcessId is still running after cleanup."
  }
}
