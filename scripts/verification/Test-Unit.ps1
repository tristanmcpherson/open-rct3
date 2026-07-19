[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestResults.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solutionFilter = Join-Path $repo 'OpenRCT3.tests.slnf'
$settings = Join-Path $PSScriptRoot 'verification.runsettings'
$dumperProject = 'Dumper\Dumper.Tests\Dumper.Tests.csproj'
$approvedDumperSkip = 'Dumper.Tests.TruncatedLabelTests.TestVeryLongPath_PreservesFilename'
$results = Join-Path $repo 'TestResults\unit'
$solutionDirectory = $repo.Replace('\', '/') + '/'

if (Test-Path -LiteralPath $results) {
  Remove-Item -LiteralPath $results -Recurse -Force
}
New-Item -ItemType Directory -Path $results -Force | Out-Null

$filter = Get-Content -Raw -LiteralPath $solutionFilter | ConvertFrom-Json
$testProjects = @()
foreach ($project in $filter.solution.projects) {
  $projectPath = Join-Path $repo $project
  $isTestProject = (& dotnet msbuild $projectPath -nologo -getProperty:IsTestProject | Out-String).Trim()
  if ($LASTEXITCODE -ne 0) {
    throw "MSBuild could not inspect IsTestProject for $project."
  }
  if ($isTestProject -eq 'true') { $testProjects += $project }
}
if ($testProjects.Count -eq 0) { throw 'The unit solution filter contains no test projects.' }

$dumperProjectPath = Join-Path $repo $dumperProject
$dumperIsTestProject = (& dotnet msbuild $dumperProjectPath -nologo -getProperty:IsTestProject | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
  throw "MSBuild could not inspect IsTestProject for $dumperProject."
}
if ($dumperIsTestProject -ne 'true') {
  throw "$dumperProject is a required unit project, but MSBuild does not mark it as a test project."
}
if ($filter.solution.projects -notcontains $dumperProject) {
  $testProjects += $dumperProject
}

Push-Location $repo
try {
  & (Join-Path $PSScriptRoot 'Test-Harness.ps1')

  & deno check clients/desktop/main.ts
  if ($LASTEXITCODE -ne 0) { throw "Deno check failed with exit code $LASTEXITCODE." }

  $testArgs = @(
    'test',
    $solutionFilter,
    '--no-build',
    '--no-restore',
    '--settings',
    $settings,
    '--logger',
    'trx;LogFilePrefix=unit',
    '--results-directory',
    $results,
    '--verbosity',
    'normal',
    "-p:SolutionDir=$solutionDirectory"
  )
  if ($env:COLLECT_COVERAGE -eq '1') {
    $testArgs += '--collect:XPlat Code Coverage'
  }

  & dotnet @testArgs
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed with exit code $LASTEXITCODE." }

  if ($filter.solution.projects -notcontains $dumperProject) {
    $dumperArgs = @(
      'test',
      $dumperProjectPath,
      '--no-build',
      '--no-restore',
      '--settings',
      $settings,
      '--logger',
      'trx;LogFilePrefix=unit-dumper',
      '--results-directory',
      $results,
      '--verbosity',
      'normal',
      '-p:TestingPlatformDotnetTestSupport=false',
      "-p:SolutionDir=$solutionDirectory"
    )
    if ($env:COLLECT_COVERAGE -eq '1') {
      $dumperArgs += '--collect:XPlat Code Coverage'
    }

    & dotnet @dumperArgs
    if ($LASTEXITCODE -ne 0) { throw "Dumper unit tests failed with exit code $LASTEXITCODE." }
  }
} finally {
  Pop-Location
}

$summary = Get-TrxSummary `
  -ResultsDirectory $results `
  -MinimumRuns $testProjects.Count `
  -ApprovedSkippedTests @($approvedDumperSkip)
Write-TestSummary -Name 'Unit' -Summary $summary
