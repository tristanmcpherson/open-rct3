[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DriverProbe {
  param([Parameter(Mandatory = $true)][string[]]$Arguments)

  $originalPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $driver @Arguments 2>&1 |
      Out-String
    $exitCode = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $originalPreference
  }
  return [PSCustomObject]@{ ExitCode = $exitCode; Output = $output }
}

function Get-ProcessByExecutablePath {
  param([Parameter(Mandatory = $true)][string]$ExecutablePath)

  $expected = [System.IO.Path]::GetFullPath($ExecutablePath)
  return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    try {
      [System.IO.Path]::GetFullPath($_.Path).Equals($expected, [StringComparison]::OrdinalIgnoreCase)
    } catch {
      $false
    }
  })
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$driver = Join-Path $repo '.claude\skills\drive-native-app\scripts\AppDriver.ps1'
$results = Join-Path $repo 'TestResults\app-driver-self-tests'
if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null
$pidFile = Join-Path $results 'openrct3-driver.pid'

$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$originalSelfTest = $env:OPENRCT3_DRIVER_SELF_TEST
$env:TEMP = $results
$env:TMP = $results
$fakeExe = $null

try {
  $missingPid = Invoke-DriverProbe -Arguments @('-Action', 'Info', '-StrictPid', '-Json')
  if ($missingPid.ExitCode -eq 0 -or $missingPid.Output -notmatch 'Strict PID mode requires process metadata') {
    throw 'Strict PID Info did not fail closed when metadata was absent.'
  }

  [PSCustomObject]@{
    ProcessId = $PID
    ExecutablePath = Join-Path $results 'foreign-instance.exe'
    StartTimeUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
  } | ConvertTo-Json -Compress | Set-Content -LiteralPath $pidFile -Encoding UTF8
  $foreignClose = Invoke-DriverProbe -Arguments @('-Action', 'Close', '-StrictPid', '-Force')
  if ($foreignClose.ExitCode -eq 0 -or $foreignClose.Output -notmatch 'executable mismatch') {
    throw 'Strict PID Close did not reject a foreign executable binding.'
  }
  if ($null -eq (Get-Process -Id $PID -ErrorAction SilentlyContinue)) {
    throw 'Strict PID Close terminated the harness process after rejecting its identity.'
  }

  $currentProcess = Get-Process -Id $PID
  [PSCustomObject]@{
    ProcessId = $PID
    ExecutablePath = $currentProcess.Path
    StartTimeUtc = '2000-01-01T00:00:00.0000000Z'
  } | ConvertTo-Json -Compress | Set-Content -LiteralPath $pidFile -Encoding UTF8
  $reusedPid = Invoke-DriverProbe -Arguments @('-Action', 'Close', '-StrictPid', '-Force')
  if ($reusedPid.ExitCode -eq 0 -or $reusedPid.Output -notmatch 'start-time mismatch') {
    throw 'Strict PID Info did not reject a reused-PID start-time binding.'
  }

  Remove-Item -LiteralPath $pidFile -Force
  $fakeExe = Join-Path $results 'SyntheticNativeCandidate.exe'
  Add-Type -TypeDefinition @'
using System;
using System.Threading;

internal static class SyntheticNativeCandidate {
  [STAThread]
  private static void Main() {
    Thread.Sleep(TimeSpan.FromSeconds(15));
  }
}
'@ -Language CSharp -OutputAssembly $fakeExe -OutputType WindowsApplication

  $env:OPENRCT3_DRIVER_SELF_TEST = '1'
  $launchFailure = Invoke-DriverProbe -Arguments @(
    '-Action', 'Launch',
    '-StrictPid',
    '-Json',
    '-ExecutablePath', $fakeExe,
    '-TestFailAfterSpawn'
  )
  if ($launchFailure.ExitCode -eq 0 -or
      $launchFailure.Output -notmatch 'Synthetic post-spawn launch failure') {
    throw 'The driver self-test did not exercise the post-spawn failure path.'
  }
  if (Test-Path -LiteralPath $pidFile) {
    throw 'Transactional launch left stale PID metadata after a post-spawn failure.'
  }
  if (@(Get-ProcessByExecutablePath -ExecutablePath $fakeExe).Count -ne 0) {
    throw 'Transactional launch leaked the candidate after a post-spawn failure.'
  }

  $retainedCleanup = Invoke-DriverProbe -Arguments @(
    '-Action', 'Launch',
    '-StrictPid',
    '-Json',
    '-ExecutablePath', $fakeExe,
    '-TestFailAfterSpawn',
    '-TestFailCleanup'
  )
  if ($retainedCleanup.ExitCode -eq 0 -or
      $retainedCleanup.Output -notmatch 'could not be terminated') {
    throw 'The driver self-test did not exercise failed post-spawn cleanup.'
  }
  if (-not (Test-Path -LiteralPath $pidFile -PathType Leaf)) {
    throw 'Failed launch cleanup removed PID metadata before proving candidate termination.'
  }
  $retainedMetadata = Get-Content -Raw -LiteralPath $pidFile | ConvertFrom-Json
  if (-not [System.IO.Path]::GetFullPath([string]$retainedMetadata.ExecutablePath).Equals(
      [System.IO.Path]::GetFullPath($fakeExe),
      [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Retained failed-cleanup metadata does not name the launched candidate executable.'
  }
  $strictRetainedClose = Invoke-DriverProbe -Arguments @(
    '-Action', 'Close', '-StrictPid', '-Force')
  if ($strictRetainedClose.ExitCode -ne 0) {
    throw "Strict driver cleanup could not validate and terminate the retained candidate: " +
      $strictRetainedClose.Output
  }
  if (Test-Path -LiteralPath $pidFile) {
    throw 'Strict driver cleanup left retained PID metadata after proven termination.'
  }
  if ($null -ne (Get-Process -Id ([int]$retainedMetadata.ProcessId) -ErrorAction SilentlyContinue)) {
    throw 'Strict driver cleanup reported success while the retained candidate PID was still running.'
  }
} finally {
  if ($null -ne $fakeExe -and (Test-Path -LiteralPath $pidFile -PathType Leaf)) {
    Invoke-DriverProbe -Arguments @('-Action', 'Close', '-StrictPid', '-Force') | Out-Null
  }
  $env:TEMP = $originalTemp
  $env:TMP = $originalTmp
  $env:OPENRCT3_DRIVER_SELF_TEST = $originalSelfTest
}

Write-Output 'AppDriver self-tests passed: strict PID identity and transactional cleanup retention.'
