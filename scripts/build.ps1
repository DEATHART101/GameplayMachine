param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $root 'GameplayMachine.sln') --configuration $Configuration --nologo -p:GeneratePackageOnBuild=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
