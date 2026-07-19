[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'NativeSmokeAssertions.ps1')

function Assert-Throws {
  param(
    [Parameter(Mandatory = $true)]
    [scriptblock]$Action,
    [Parameter(Mandatory = $true)]
    [string]$Name
  )

  try {
    & $Action
  } catch {
    return
  }
  throw "Harness self-test '$Name' expected an error, but the assertion passed."
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$results = Join-Path $repo 'TestResults\harness-self-tests'
if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null

$logPath = Join-Path $results 'app.log'
$runId = 'current-run'
$mapHash = 'CURRENTMAP'
$stalePrefix = 'run=stale-run|map=STALEMAP|2026-01-01|'
$currentPrefix = "run=$runId|map=$mapHash|2026-01-01|"

@(
  "${stalePrefix}INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows...",
  "${stalePrefix}TRACE|OpenRCT3.Game|Game world loaded",
  "${stalePrefix}TRACE|OpenRCT3.Game|Added terrain mesh",
  "${currentPrefix}INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows..."
) | Set-Content -LiteralPath $logPath -Encoding UTF8
$state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } 'stale markers cannot pass a fresh run'

@(
  "run=$runId|map=WRONGMAP|2026-01-01|INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows...",
  "run=$runId|map=WRONGMAP|2026-01-01|TRACE|OpenRCT3.Game|Game world loaded",
  "run=$runId|map=WRONGMAP|2026-01-01|TRACE|OpenRCT3.Game|Added terrain mesh"
) | Add-Content -LiteralPath $logPath
$state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } 'wrong-map markers cannot pass the selected map'

Add-Content -LiteralPath $logPath -Value "${currentPrefix}TRACE|OpenRCT3.Game|Game world loaded"
$state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } 'world load without terrain mesh fails'

Add-Content -LiteralPath $logPath -Value "${currentPrefix}TRACE|OpenRCT3.Game|Added terrain mesh"
$state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
Assert-NativeSmokeCompletion -State $state

Add-Content -LiteralPath $logPath -Value "${currentPrefix}FATAL|OpenRCT3.Program|synthetic failure"
$state = Get-NativeSmokeLogState -LogPath $logPath -RunId $runId -MapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } 'correlated fatal event fails'

$configPath = Join-Path $results 'config.json'
$installPath = Join-Path $results 'install'
$mapPath = Join-Path $results 'selected.dat'
@{
  InstallPath = $installPath
  MapPath = $mapPath
  SuppressCrashAlerts = $true
} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
Assert-NativeSmokeConfig -ConfigPath $configPath -InstallPath $installPath -MapPath $mapPath
Assert-Throws {
  Assert-NativeSmokeConfig -ConfigPath $configPath -InstallPath $installPath -MapPath (Join-Path $results 'ignored.dat')
} 'ignored selected map fails config validation'

Assert-NativeSmokeProcessExited -ProcessId ([int]::MaxValue)
Assert-Throws { Assert-NativeSmokeProcessExited -ProcessId $PID } 'live candidate fails cleanup validation'

Write-Output 'Harness self-tests passed: stale-run, wrong-map, incomplete-world, fatal-log, config, and cleanup guards.'
