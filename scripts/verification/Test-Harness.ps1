[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'NativeSmokeAssertions.ps1')
. (Join-Path $PSScriptRoot 'NativeSmokeEvidence.ps1')
. (Join-Path $PSScriptRoot 'TestResults.ps1')

function Assert-Throws {
  param(
    [Parameter(Mandatory = $true)][scriptblock]$Action,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$MessagePattern = $null
  )

  try {
    & $Action
  } catch {
    if (-not [string]::IsNullOrWhiteSpace($MessagePattern) -and
        $_.Exception.Message -notmatch $MessagePattern) {
      throw "Harness self-test '$Name' failed for the wrong reason: $($_.Exception.Message)"
    }
    return
  }
  throw "Harness self-test '$Name' expected an error, but the assertion passed."
}

function Write-SyntheticTrx {
  param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [Parameter(Mandatory = $true)][string]$Outcome,
    [string]$RejectedTest = 'Harness.Tests.RejectedCase',
    [switch]$CorruptPassedCounter
  )

  New-Item -ItemType Directory -Path $Directory -Force | Out-Null
  $counterNames = @(
    'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
    'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')
  $counterValues = @{}
  foreach ($counterName in $counterNames) { $counterValues[$counterName] = 0 }
  $counterNameByOutcome = @{
    Failed = 'failed'; Error = 'error'; Timeout = 'timeout'; Aborted = 'aborted'
    Inconclusive = 'inconclusive'; PassedButRunAborted = 'passedButRunAborted'
    NotRunnable = 'notRunnable'; Disconnected = 'disconnected'; Warning = 'warning'
    Completed = 'completed'; InProgress = 'inProgress'; Pending = 'pending'
  }
  if ($counterNameByOutcome.ContainsKey($Outcome)) {
    $counterValues[$counterNameByOutcome[$Outcome]] = 1
  }
  $rejectedExecuted = if ($Outcome -eq 'NotExecuted') { 0 } else { 1 }
  $passedCounter = if ($CorruptPassedCounter) { 0 } else { 1 }
  $counterAttributes = ($counterNames | ForEach-Object {
    "$_=`"$($counterValues[$_])`""
  }) -join ' '
  $rejectedParts = $RejectedTest.Split('.')
  $rejectedMethod = $rejectedParts[-1]
  $rejectedClass = $RejectedTest.Substring(0, $RejectedTest.Length - $rejectedMethod.Length - 1)
  @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testId="pass-id" testName="PassedCase" outcome="Passed" />
    <UnitTestResult testId="rejected-id" testName="$rejectedMethod" outcome="$Outcome" />
  </Results>
  <TestDefinitions>
    <UnitTest id="pass-id"><TestMethod className="Harness.Tests" name="PassedCase" /></UnitTest>
    <UnitTest id="rejected-id"><TestMethod className="$rejectedClass" name="$rejectedMethod" /></UnitTest>
  </TestDefinitions>
  <ResultSummary>
    <Counters total="2" executed="$($rejectedExecuted + 1)" passed="$passedCounter" $counterAttributes />
  </ResultSummary>
</TestRun>
"@ | Set-Content -LiteralPath (Join-Path $Directory 'synthetic.trx') -Encoding UTF8
}

function New-PassedEvidence {
  param(
    [Parameter(Mandatory = $true)][string]$Repo,
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$Map
  )

  $hash = 'A' * 64
  $evidence = New-NativeSmokeEvidenceRecord -RepoPath $Repo
  $evidence.outcome = 'passed'
  $evidence.executable = [PSCustomObject]@{ available = $true; path = $Executable; sha256 = $hash }
  $evidence.requestedMap = [PSCustomObject]@{ available = $true; path = $Map; sha256 = $hash }
  $evidence.loadedMap = [PSCustomObject]@{ available = $true; path = $Map; sha256 = $hash }
  $evidence.process = [PSCustomObject]@{
    available = $true
    pid = 42
    hwnd = 84
    startTimeUtc = '2026-01-01T00:00:00.0000000Z'
    executablePath = $Executable
  }
  $evidence.window.observedDpi = [PSCustomObject]@{ available = $true; value = 96 }
  $evidence.window.observedClientSize =
    [PSCustomObject]@{ available = $true; width = 1280; height = 720 }
  $evidence.cleanup.attempted = $true
  $evidence.cleanup.closeResult = 'graceful'
  $evidence.cleanup.processExited = $true
  return $evidence
}

function Invoke-LoadedMapProbe {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$ConfiguredMapPath,
    [string]$EnvironmentMapPath = $null
  )

  $caseDirectory = Join-Path $results "loaded-map-$Name"
  $appData = Join-Path $caseDirectory 'AppData'
  $configDirectory = Join-Path $appData 'OpenRCT3'
  $installPath = Join-Path $caseDirectory 'install'
  New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
  @{
    InstallPath = $installPath
    MapPath = $ConfiguredMapPath
    SuppressCrashAlerts = $true
  } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $configDirectory 'config.json') -Encoding UTF8

  $probeNlog = Join-Path $caseDirectory 'nlog.config'
  Copy-Item -LiteralPath $builtNlogPath -Destination $probeNlog
  [xml]$probeConfig = Get-Content -Raw -LiteralPath $probeNlog
  $probeFileTarget = $probeConfig.SelectSingleNode("//*[local-name()='target' and @name='file']")
  $probeLogPath = Join-Path $caseDirectory 'app.log'
  $probeFileTarget.SetAttribute('fileName', $probeLogPath)
  $probeFileTarget.SetAttribute(
    'layout',
    'run=${environment:variable=OPENRCT3_SMOKE_RUN_ID}|${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}')
  $probeConfig.Save($probeNlog)

  $originalAppData = $env:APPDATA
  $originalMapPath = $env:OPENRCT3_MAP_PATH
  $originalRunId = $env:OPENRCT3_SMOKE_RUN_ID
  $env:APPDATA = $appData
  $env:OPENRCT3_MAP_PATH = $EnvironmentMapPath
  $env:OPENRCT3_SMOKE_RUN_ID = "probe-$Name"
  try {
    $probeOutput = & powershell -NoProfile -ExecutionPolicy Bypass `
      -File (Join-Path $PSScriptRoot 'Test-LoadedMapMarker.ps1') `
      -RepoPath $repo `
      -NlogConfigPath $probeNlog `
      -InstallPath $installPath `
      -ConfiguredMapPath $ConfiguredMapPath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
      throw "Loaded-map application probe '$Name' failed: $probeOutput"
    }
  } finally {
    $env:APPDATA = $originalAppData
    $env:OPENRCT3_MAP_PATH = $originalMapPath
    $env:OPENRCT3_SMOKE_RUN_ID = $originalRunId
  }
  return [PSCustomObject]@{
    LogPath = $probeLogPath
    RunId = "probe-$Name"
  }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$results = Join-Path $repo 'TestResults\harness-self-tests'
if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null

$logPath = Join-Path $results 'app.log'
$runId = 'current-run'
$mapPath = Join-Path $results 'fallback-map.dat'
$wrongMapPath = Join-Path $results 'wrong-map.dat'
$mapHash = 'A' * 64
$wrongMapHash = 'B' * 64
$stalePrefix = 'run=stale-run|2026-01-01|'
$currentPrefix = "run=$runId|2026-01-01|"
$mapJson = @{ path = $mapPath; sha256 = $mapHash } | ConvertTo-Json -Compress
$wrongMapJson = @{ path = $wrongMapPath; sha256 = $wrongMapHash } | ConvertTo-Json -Compress

@(
  "${stalePrefix}INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows...",
  "${stalePrefix}DEBUG|OpenRCT3.Game|Game world loaded",
  "${stalePrefix}DEBUG|OpenRCT3.Game|Added terrain mesh",
  "${stalePrefix}INFO|OpenRCT3.Simulation.Terrain|Native smoke loaded map $mapJson",
  "${currentPrefix}INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows..."
) | Set-Content -LiteralPath $logPath -Encoding UTF8
$state = Get-NativeSmokeLogState `
  -LogPath $logPath -RunId $runId -ExpectedMapPath $mapPath -ExpectedMapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } `
  'stale markers cannot pass a fresh run' 'required completion markers'

Add-Content -LiteralPath $logPath -Value @(
  "${currentPrefix}DEBUG|OpenRCT3.Game|Game world loaded",
  "${currentPrefix}DEBUG|OpenRCT3.Game|Added terrain mesh",
  "${currentPrefix}INFO|OpenRCT3.Simulation.Terrain|Native smoke loaded map $wrongMapJson"
)
$state = Get-NativeSmokeLogState `
  -LogPath $logPath -RunId $runId -ExpectedMapPath $mapPath -ExpectedMapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } `
  'application wrong-map identity fails' 'different map path'

@(
  "${currentPrefix}INFO|OpenRCT3.Program|Starting OpenRCT3 on Windows...",
  "${currentPrefix}DEBUG|OpenRCT3.Game|Game world loaded",
  "${currentPrefix}DEBUG|OpenRCT3.Game|Added terrain mesh",
  "${currentPrefix}INFO|OpenRCT3.Simulation.Terrain|Native smoke loaded map $mapJson"
) | Set-Content -LiteralPath $logPath -Encoding UTF8
$state = Get-NativeSmokeLogState `
  -LogPath $logPath -RunId $runId -ExpectedMapPath $mapPath -ExpectedMapSha256 $mapHash
Assert-NativeSmokeCompletion -State $state

Add-Content -LiteralPath $logPath -Value "${currentPrefix}FATAL|OpenRCT3.Program|synthetic failure"
$state = Get-NativeSmokeLogState `
  -LogPath $logPath -RunId $runId -ExpectedMapPath $mapPath -ExpectedMapSha256 $mapHash
Assert-Throws { Assert-NativeSmokeCompletion -State $state } `
  'correlated fatal event fails' 'ERROR or FATAL'

Assert-NativeSmokeFileLoggingConfiguration -ConfigPath (Join-Path $repo 'OpenRCT3\nlog.config')

$openRct3Assembly = Get-ChildItem -Path (Join-Path $repo 'OpenRCT3\bin\Debug') `
  -Filter 'OpenRCT3.dll' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1 `
  -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($openRct3Assembly)) {
  throw 'The loaded-map application probe requires the make test-build output.'
}
$builtNlogPath = Join-Path (Split-Path $openRct3Assembly -Parent) 'nlog.config'
$parkFixture = Join-Path $repo 'OpenCobra\Tests\Fixtures\Parks\Fun Valley Amusment park.dat'
$parkHash = (Get-FileHash -LiteralPath $parkFixture -Algorithm SHA256).Hash
$fallbackProbe = Invoke-LoadedMapProbe -Name 'fallback' -ConfiguredMapPath $parkFixture
$fallbackState = Get-NativeSmokeLogState `
  -LogPath $fallbackProbe.LogPath `
  -RunId $fallbackProbe.RunId `
  -ExpectedMapPath $parkFixture `
  -ExpectedMapSha256 $parkHash
if ($fallbackState.LoadedMapCount -ne 1 -or -not $fallbackState.LoadedMapPathMatches -or
    -not $fallbackState.LoadedMapHashMatches) {
  throw 'Application fallback-map marker did not identify the config-selected fixture.'
}

$wrongApplicationMap = Join-Path $results 'environment-selected-wrong-map.dat'
Copy-Item -LiteralPath $parkFixture -Destination $wrongApplicationMap
$wrongProbe = Invoke-LoadedMapProbe `
  -Name 'wrong' `
  -ConfiguredMapPath $parkFixture `
  -EnvironmentMapPath $wrongApplicationMap
$wrongState = Get-NativeSmokeLogState `
  -LogPath $wrongProbe.LogPath `
  -RunId $wrongProbe.RunId `
  -ExpectedMapPath $parkFixture `
  -ExpectedMapSha256 $parkHash
if ($wrongState.LoadedMapCount -ne 1 -or $wrongState.LoadedMapPathMatches) {
  throw 'Application wrong-map probe did not expose environment-path precedence.'
}

$configPath = Join-Path $results 'config.json'
$installPath = Join-Path $results 'install'
@{
  InstallPath = $installPath
  MapPath = $mapPath
  SuppressCrashAlerts = $true
} | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
Assert-NativeSmokeConfig -ConfigPath $configPath -InstallPath $installPath -MapPath $mapPath
Assert-Throws {
  Assert-NativeSmokeConfig -ConfigPath $configPath -InstallPath $installPath -MapPath $wrongMapPath
} 'ignored selected map fails config validation' 'MapPath mismatch'

$approvedSkip = 'Dumper.Tests.TruncatedLabelTests.TestVeryLongPath_PreservesFilename'
$approvedTrx = Join-Path $results 'trx-approved-skip'
Write-SyntheticTrx -Directory $approvedTrx -Outcome NotExecuted -RejectedTest $approvedSkip
$approvedSummary = Get-TrxSummary `
  -ResultsDirectory $approvedTrx -ApprovedSkippedTests @($approvedSkip)
if ($approvedSummary.Passed -ne 1 -or $approvedSummary.Skipped -ne 1) {
  throw 'Approved TRX skip was not reported honestly.'
}

$rejectedOutcomes = @(
  'NotExecuted', 'Failed', 'Error', 'Timeout', 'Aborted', 'Inconclusive',
  'PassedButRunAborted', 'NotRunnable', 'Disconnected', 'Warning', 'Completed',
  'InProgress', 'Pending')
foreach ($outcome in $rejectedOutcomes) {
  $outcomeDirectory = Join-Path $results "trx-rejected-$outcome"
  Write-SyntheticTrx -Directory $outcomeDirectory -Outcome $outcome
  Assert-Throws { Get-TrxSummary -ResultsDirectory $outcomeDirectory } `
    "TRX rejects $outcome" 'Rejected'
}
$countMismatch = Join-Path $results 'trx-passed-count-mismatch'
Write-SyntheticTrx -Directory $countMismatch -Outcome Passed -CorruptPassedCounter
Assert-Throws { Get-TrxSummary -ResultsDirectory $countMismatch } `
  'TRX rejects passed counter mismatch' 'passed count mismatch'

$manifestPath = Join-Path $results 'native-smoke.json'
$executablePath = Join-Path $results 'OpenRCT3.exe'
$passedEvidence = New-PassedEvidence -Repo $repo -Executable $executablePath -Map $mapPath
Write-NativeSmokeEvidenceManifest -Evidence $passedEvidence -Path $manifestPath
$roundTrip = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($roundTrip.outcome -ne 'passed' -or $roundTrip.loadedMap.sha256 -ne $mapHash) {
  throw 'Native evidence manifest did not preserve its required content.'
}
$missingLoadedMap = New-PassedEvidence -Repo $repo -Executable $executablePath -Map $mapPath
$missingLoadedMap.loadedMap = [PSCustomObject]@{ available = $false; reason = 'marker missing' }
Assert-Throws { Assert-NativeSmokeEvidenceRecord -Evidence $missingLoadedMap } `
  'manifest rejects missing loaded map' 'Loaded map identity is unavailable'
$wrongLoadedMap = New-PassedEvidence -Repo $repo -Executable $executablePath -Map $mapPath
$wrongLoadedMap.loadedMap.sha256 = $wrongMapHash
Assert-Throws { Assert-NativeSmokeEvidenceRecord -Evidence $wrongLoadedMap } `
  'manifest rejects wrong loaded map hash' 'map hashes do not match'
$wrongExecutable = New-PassedEvidence -Repo $repo -Executable $executablePath -Map $mapPath
$wrongExecutable.process.executablePath = Join-Path $results 'foreign.exe'
Assert-Throws { Assert-NativeSmokeEvidenceRecord -Evidence $wrongExecutable } `
  'manifest rejects foreign process binding' 'executable binding'
$missingCaptureHash = New-PassedEvidence -Repo $repo -Executable $executablePath -Map $mapPath
$missingCaptureHash.capture.mode = 'screenshot'
$missingCaptureHash.capture.artifacts = @(
  [PSCustomObject]@{ available = $true; path = (Join-Path $results 'screen.png'); sha256 = 'bad' })
Assert-Throws { Assert-NativeSmokeEvidenceRecord -Evidence $missingCaptureHash } `
  'manifest rejects unhashed screenshot' 'SHA-256'

Assert-NativeSmokeProcessExited -ProcessId ([int]::MaxValue)
Assert-Throws { Assert-NativeSmokeProcessExited -ProcessId $PID } `
  'live candidate fails cleanup validation' 'still running'

& (Join-Path $PSScriptRoot 'Test-AppDriver.ps1')

Write-Output 'Harness self-tests passed: native identity/logging, strict TRX, manifest, and driver guards.'
