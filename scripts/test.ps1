param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
dotnet test (Join-Path $root 'GameplayMachine.sln') --configuration $Configuration --no-build --nologo --blame-hang-timeout 180s --logger trx --results-directory (Join-Path $root 'artifacts/test-results')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
