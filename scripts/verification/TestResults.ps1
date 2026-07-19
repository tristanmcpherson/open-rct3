function Get-TrxSummary {
  param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,
    [int]$MinimumRuns = 1
  )

  $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse)
  if ($trxFiles.Count -lt $MinimumRuns) {
    throw "Expected at least $MinimumRuns TRX result file(s), found $($trxFiles.Count)."
  }

  $totals = @{
    Total = 0
    Executed = 0
    Passed = 0
    Failed = 0
    Error = 0
    Timeout = 0
    Aborted = 0
    NotExecuted = 0
  }

  foreach ($trxFile in $trxFiles) {
    [xml]$trx = Get-Content -Raw -LiteralPath $trxFile.FullName
    $counters = $trx.SelectSingleNode(
      "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"
    )
    if ($null -eq $counters) { throw "TRX counters missing from $($trxFile.FullName)." }

    $runTotal = [int]$counters.GetAttribute('total')
    if ($runTotal -eq 0) { throw "Zero tests ran in $($trxFile.Name)." }
    $runExecuted = [int]$counters.GetAttribute('executed')
    if ($runExecuted -ne $runTotal) {
      throw "Not all discovered tests executed in $($trxFile.Name): $runExecuted of $runTotal."
    }

    foreach ($key in @($totals.Keys)) {
      $attribute = $key.Substring(0, 1).ToLowerInvariant() + $key.Substring(1)
      $value = $counters.GetAttribute($attribute)
      if (-not [string]::IsNullOrEmpty($value)) {
        $totals[$key] += [int]$value
      }
    }
  }

  if ($totals.Executed -eq 0) { throw 'Test discovery succeeded, but no tests executed.' }
  if (($totals.Failed + $totals.Error + $totals.Timeout + $totals.Aborted) -gt 0) {
    throw "TRX reports failed or incomplete tests: $($totals | ConvertTo-Json -Compress)"
  }

  return [PSCustomObject]@{
    Runs = $trxFiles.Count
    Total = $totals.Total
    Executed = $totals.Executed
    Passed = $totals.Passed
    Failed = $totals.Failed
    Skipped = $totals.NotExecuted
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

  $message = "$Name test summary: total=$($Summary.Total), executed=$($Summary.Executed), " +
    "passed=$($Summary.Passed), failed=$($Summary.Failed), skipped=$($Summary.Skipped), " +
    "runs=$($Summary.Runs); artifacts=$($Summary.ResultsDirectory)"
  Write-Output $message

  if (-not [string]::IsNullOrEmpty($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value @"
### $Name tests

| Total | Executed | Passed | Failed | Skipped | Runs |
| ---: | ---: | ---: | ---: | ---: | ---: |
| $($Summary.Total) | $($Summary.Executed) | $($Summary.Passed) | $($Summary.Failed) | $($Summary.Skipped) | $($Summary.Runs) |

"@
  }
}
