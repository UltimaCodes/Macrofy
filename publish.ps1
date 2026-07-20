# Builds Macrofy as a self-contained single exe plus the native hook DLL beside it,
# zipped for release. Users need nothing installed (the .NET runtime is bundled in).
#
# The DLL ships beside the exe on purpose: extracting it to %LocalAppData% at runtime
# looks like dropper behavior to antivirus heuristics, and compressed single-file exes
# look packed. Both were getting the build flagged. The embedded copy still exists as a
# fallback for anyone who moves the exe on its own.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$pubDir = Join-Path $root 'src\Macrofy.App\bin\Release\net8.0-windows\win-x64\publish'

dotnet publish (Join-Path $root 'src\Macrofy.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'Macrofy.exe'
Copy-Item (Join-Path $pubDir 'Macrofy.App.exe') $exe -Force
Copy-Item (Join-Path $root 'native\MacrofyHook.dll') (Join-Path $dist 'MacrofyHook.dll') -Force

$zip = Join-Path $dist 'Macrofy.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe, (Join-Path $dist 'MacrofyHook.dll') -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
"$hash  Macrofy.zip" | Out-File (Join-Path $dist 'Macrofy.zip.sha256') -Encoding ascii

Write-Host ""
Write-Host "Built: $zip"
Write-Host "SHA256: $hash"
