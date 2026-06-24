# Builds the native WH_KEYBOARD hook DLL (MacrofyHook.dll) with MinGW-w64 gcc.
# Install gcc once with:  winget install BrechtSanders.WinLibs.POSIX.UCRT
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gcc = (Get-Command gcc -ErrorAction SilentlyContinue).Source
if (-not $gcc) {
    $env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")
    $gcc = (Get-Command gcc -ErrorAction SilentlyContinue).Source
}
if (-not $gcc) { throw "gcc not found. Install: winget install BrechtSanders.WinLibs.POSIX.UCRT" }

& $gcc -shared -O2 -o (Join-Path $here "MacrofyHook.dll") (Join-Path $here "hook.c") -luser32
if ($LASTEXITCODE -ne 0) { throw "gcc build failed" }
Write-Host "Built MacrofyHook.dll"
