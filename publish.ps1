# Builds Macrofy as a single self-contained .exe. Users need nothing installed (the .NET
# runtime is bundled in). Output goes to dist\Macrofy.exe plus a SHA256 to post on the release.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pubDir = Join-Path $root 'src\Macrofy.App\bin\Release\net8.0-windows\win-x64\publish'

dotnet publish (Join-Path $root 'src\Macrofy.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$out = Join-Path $dist 'Macrofy.exe'
Copy-Item (Join-Path $pubDir 'Macrofy.App.exe') $out -Force

$hash = (Get-FileHash $out -Algorithm SHA256).Hash
"$hash  Macrofy.exe" | Out-File (Join-Path $dist 'Macrofy.exe.sha256') -Encoding ascii

Write-Host ""
Write-Host "Built: $out"
Write-Host "SHA256: $hash"
