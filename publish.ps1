# Builds the Macrofy installer with Velopack.
#
#   .\publish.ps1                 # version from src\Macrofy.App\Macrofy.App.csproj
#   .\publish.ps1 -Version 1.2.0
#
# Output in dist\:
#   Macrofy-win-Setup.exe      the one file people download. Installs per-user (no admin),
#                              adds Start menu + desktop shortcuts, and installs the .NET 8
#                              desktop runtime first if the PC doesn't have it.
#   Macrofy-win-Portable.zip   a no-install copy, for people who prefer that.
#   *.nupkg, releases.win.json what Macrofy's auto-update reads. Upload these to the GitHub
#                              release together with Setup.exe (the release workflow does it).
#
# Needs the Velopack CLI once:  dotnet tool install -g vpk
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Macrofy.App\Macrofy.App.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "No -Version given and none found in $project" }

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    $env:Path += ";$env:USERPROFILE\.dotnet\tools"
}
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "The Velopack CLI isn't installed. Run: dotnet tool install -g vpk"
}

$publish = Join-Path $root 'build\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# Framework-dependent: the installer brings the .NET runtime when it's missing, so the app
# itself stays a few MB instead of bundling ~150 MB of runtime.
dotnet publish $project -c Release -r win-x64 --self-contained false -o $publish `
    -p:Version=$Version -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path (Join-Path $publish 'MacrofyHook.dll'))) { throw "MacrofyHook.dll is missing from the publish output" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# Notes for this version, if they've been written (release-notes\<version>.md). They go into
# the update package, and the release workflow also puts them on the GitHub release page.
$notes = Join-Path $root "release-notes\$Version.md"
$notesArgs = if (Test-Path $notes) { @('--releaseNotes', $notes) } else { @() }

vpk pack `
    --packId Macrofy `
    --packVersion $Version `
    --packDir $publish `
    --mainExe Macrofy.exe `
    --packTitle Macrofy `
    --packAuthors Macrofy `
    --runtime win-x64 `
    --icon (Join-Path $root 'src\Macrofy.App\Assets\macrofy.ico') `
    --framework net8-x64-desktop `
    --outputDir $dist `
    @notesArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host ""
Write-Host "Built Macrofy $Version in $dist"
Get-ChildItem $dist -File | Where-Object { $_.Name -like "Macrofy*$Version*" -or $_.Name -like 'Macrofy-win-*' -or $_.Name -like 'releases*' } |
    ForEach-Object { Write-Host ("  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
