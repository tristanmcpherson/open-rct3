function Get-TrxSummary {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,
    [int]$MinimumRuns = 1,
    [string[]]$ApprovedSkippedTests = @()
  )

  $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse)
  if ($trxFiles.Count -lt $MinimumRuns) {
    throw "Expected at least $MinimumRuns TRX result file(s), found $($trxFiles.Count)."
  }

  $approvedSkips = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
  foreach ($approvedSkip in $ApprovedSkippedTests) { $approvedSkips.Add($approvedSkip) | Out-Null }
  $observedApprovedSkips = @{}
  $totals = @{
    Total = 0
    Executed = 0
    Passed = 0
    ApprovedSkipped = 0
  }
  $rejectedCounters = @(
    'failed',
    'error',
    'timeout',
    'aborted',
    'inconclusive',
    'passedButRunAborted',
    'notRunnable',
    'disconnected',
    'warning',
    'completed',
    'inProgress',
    'pending'
  )

  foreach ($trxFile in $trxFiles) {
    [xml]$trx = Get-Content -Raw -LiteralPath $trxFile.FullName
    $counters = $trx.SelectSingleNode(
      "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"
    )
    if ($null -eq $counters) { throw "TRX counters missing from $($trxFile.FullName)." }

    $runTotal = [int]$counters.GetAttribute('total')
    if ($runTotal -eq 0) { throw "Zero tests ran in $($trxFile.Name)." }
    foreach ($counterName in $rejectedCounters) {
      $counterValue = $counters.GetAttribute($counterName)
      if (-not [string]::IsNullOrWhiteSpace($counterValue) -and [int]$counterValue -ne 0) {
        throw "TRX $($trxFile.Name) reports rejected '$counterName' count $counterValue."
      }
    }

    $definitions = @{}
    $definitionNodes = @($trx.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
    foreach ($definition in $definitionNodes) {
      $testMethod = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
      if ($null -eq $testMethod) { throw "TRX test definition is missing TestMethod in $($trxFile.Name)." }
      $definitions[$definition.GetAttribute('id')] =
        "$($testMethod.GetAttribute('className')).$($testMethod.GetAttribute('name'))"
    }

    $results = @($trx.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    if ($results.Count -ne $runTotal) {
      throw "TRX result count mismatch in $($trxFile.Name): $($results.Count) results for total $runTotal."
    }

    $runPassed = 0
    $runApprovedSkipped = 0
    foreach ($result in $results) {
      $testId = $result.GetAttribute('testId')
      if (-not $definitions.ContainsKey($testId)) {
        throw "TRX result '$($result.GetAttribute('testName'))' has no matching definition."
      }
      $fullName = $definitions[$testId]
      $outcome = $result.GetAttribute('outcome')
      if ($outcome -eq 'Passed') {
        $runPassed++
        continue
      }
      if ($outcome -eq 'NotExecuted' -and $approvedSkips.Contains($fullName)) {
        if ($observedApprovedSkips.ContainsKey($fullName)) {
          throw "Approved skipped test appeared more than once: $fullName"
        }
        $observedApprovedSkips[$fullName] = $trxFile.Name
        $runApprovedSkipped++
        continue
      }
      throw "Rejected TRX outcome '$outcome' for $fullName in $($trxFile.Name)."
    }

    $runExecuted = [int]$counters.GetAttribute('executed')
    $counterPassed = [int]$counters.GetAttribute('passed')
    if ($counterPassed -ne $runPassed) {
      throw "TRX passed count mismatch in $($trxFile.Name): counter=$counterPassed, results=$runPassed."
    }
    if ($runExecuted -ne $runPassed) {
      throw "TRX executed count mismatch in $($trxFile.Name): executed=$runExecuted, passed=$runPassed."
    }
    if (($runPassed + $runApprovedSkipped) -ne $runTotal) {
      throw "TRX outcomes do not account for every test in $($trxFile.Name)."
    }

    $totals.Total += $runTotal
    $totals.Executed += $runExecuted
    $totals.Passed += $runPassed
    $totals.ApprovedSkipped += $runApprovedSkipped
  }

  foreach ($approvedSkip in $ApprovedSkippedTests) {
    if (-not $observedApprovedSkips.ContainsKey($approvedSkip)) {
      throw "Approved skipped test was not reported as NotExecuted: $approvedSkip"
    }
  }
  if ($totals.Executed -eq 0) { throw 'Test discovery succeeded, but no tests executed.' }
  if ($totals.Passed -ne ($totals.Total - $totals.ApprovedSkipped)) {
    throw 'Passed tests plus approved skips do not equal the total test count.'
  }

  return [PSCustomObject]@{
    Runs = $trxFiles.Count
    Total = $totals.Total
    Executed = $totals.Executed
    Passed = $totals.Passed
    Failed = 0
    Skipped = $totals.ApprovedSkipped
    ApprovedSkippedTests = @($ApprovedSkippedTests)
    ResultsDirectory = (Resolve-Path -LiteralPath $ResultsDirectory).Path
  }
}

function Write-TestSummary {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Name,
    [Parameter(Mandatory = $true)]
    [PSCustomObject]$Summary
  )

  $approvedSkips = if ($Summary.ApprovedSkippedTests.Count -eq 0) {
    'none'
  } else {
    $Summary.ApprovedSkippedTests -join ', '
  }
  $message = "$Name test summary: total=$($Summary.Total), executed=$($Summary.Executed), " +
    "passed=$($Summary.Passed), failed=$($Summary.Failed), approved-skipped=$($Summary.Skipped), " +
    "runs=$($Summary.Runs); approved-skips=$approvedSkips; artifacts=$($Summary.ResultsDirectory)"
  Write-Output $message

  if (-not [string]::IsNullOrEmpty($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value @"
### $Name tests

| Total | Executed | Passed | Failed | Approved skipped | Runs |
| ---: | ---: | ---: | ---: | ---: | ---: |
| $($Summary.Total) | $($Summary.Executed) | $($Summary.Passed) | $($Summary.Failed) | $($Summary.Skipped) | $($Summary.Runs) |

Approved skips: $approvedSkips

"@
  }
}
