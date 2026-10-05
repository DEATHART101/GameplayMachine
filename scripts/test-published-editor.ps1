param([Parameter(Mandatory)][string]$EditorDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = (Resolve-Path -LiteralPath $EditorDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $directory 'GameplayMachine.Editor.exe'))) {
    throw 'Published editor executable not found.'
}
$previous = $env:GAMEPLAYMACHINE_PUBLISHED_EDITOR
try {
    $env:GAMEPLAYMACHINE_PUBLISHED_EDITOR = $directory
    dotnet test (Join-Path $root 'od-manipulator/ODManipulator/ODStudio.Tests/ODStudio.Tests.csproj') --configuration Release --no-build --no-restore --nologo --filter 'FullyQualifiedName~CommandLineBuildReportsPublishProgress'
    if ($LASTEXITCODE -ne 0) { throw 'Published editor could not build a command-line game.' }
}
finally {
    $env:GAMEPLAYMACHINE_PUBLISHED_EDITOR = $previous
}
