param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$')][string]$Version,
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = "GameplayMachine-Editor-$Version-$Runtime"
$stage = Join-Path $root "artifacts/$name"
$zip = Join-Path $root "artifacts/$name.zip"
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) {
    throw "Output already exists. Move the previous artifacts before packaging $Version again."
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$editor = Join-Path $root 'od-manipulator/ODManipulator/ODStudio.Editor/ODStudio.Editor.csproj'
dotnet publish $editor --configuration Release --runtime $Runtime --self-contained true --nologo -p:GeneratePackageOnBuild=false -o (Join-Path $stage 'editor')
if ($LASTEXITCODE -ne 0) { throw 'Editor publish failed.' }

# Keep runtime source beside editor so Godot export can resolve its project without private checkouts.
$sourceArchive = Join-Path $stage 'runtime-source.zip'
git -C $root archive --format=zip "--output=$sourceArchive" HEAD -- Directory.Build.props global.json NuGet.Config LICENSE README.md THIRD_PARTY_NOTICES.md licenses gameplaymachine_core/main/GameplayMachineCore gameplaymachine_core/main/GameplayMachineGodot gameplaymachine_core/LICENSE od-core/main/ODCore od-core/LICENSE xlockstep/src xlockstep/LICENSE dependencies
if ($LASTEXITCODE -ne 0) { throw 'Runtime source archive failed. Commit release files before packaging.' }
Expand-Archive -LiteralPath $sourceArchive -DestinationPath $stage -Force
Remove-Item -LiteralPath $sourceArchive

$assetsPath = Join-Path (Split-Path $editor -Parent) 'obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$metadata = foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $packageRoot = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageRoot = $candidate; break }
    }
    if (-not $packageRoot) { throw "Package not found: $($library.Name)" }
    $destination = Join-Path $stage ('licenses/packages/' + $library.Name)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' | Select-Object -First 1
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    [xml]$spec = Get-Content -LiteralPath $nuspec.FullName -Raw
    Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object { $_.Name -match '(?i)licen[cs]e|copyright|notice|^OFL' } |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($packageRoot, $_.FullName)
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target
        }
    [pscustomobject]@{
        Package = $library.Name
        License = $spec.package.metadata.license.InnerText
        ProjectUrl = $spec.package.metadata.projectUrl
        Repository = $spec.package.metadata.repository.url
    }
}
$metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'licenses/packages.json') -Encoding utf8NoBOM
$commit = git -C $root rev-parse HEAD
[pscustomobject]@{ Version=$Version; Runtime=$Runtime; Commit=$commit; SelfContained=$true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8NoBOM
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Output "Release archive: $zip"
