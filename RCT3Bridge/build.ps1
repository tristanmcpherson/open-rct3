param(
  [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root 'build'

cmake -S $root -B $build -G 'Visual Studio 17 2022' -A Win32
if ($LASTEXITCODE -ne 0) { throw 'RCT3Bridge configure failed.' }

cmake --build $build --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw 'RCT3Bridge build failed.' }

if ($Test) {
  ctest --test-dir $build -C Release --output-on-failure
  if ($LASTEXITCODE -ne 0) { throw 'RCT3Bridge tests failed.' }
}
