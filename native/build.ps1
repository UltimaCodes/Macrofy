# Builds the native WH_KEYBOARD hook DLL (MacrofyHook.dll) with MinGW-w64 gcc.
# Install gcc once with:  winget install BrechtSanders.WinLibs.POSIX.UCRT
#
# Built with no C runtime (-nostdlib, DllMain as the entry point): the DLL gets mapped
# into every process that reads keyboard input, so it imports only kernel32 and user32.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$gcc = (Get-Command gcc -ErrorAction SilentlyContinue).Source
if (-not $gcc) {
    $env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")
    $gcc = (Get-Command gcc -ErrorAction SilentlyContinue).Source
}
if (-not $gcc) { throw "gcc not found. Install: winget install BrechtSanders.WinLibs.POSIX.UCRT" }

& $gcc -shared -O2 -s -nostdlib -fno-stack-protector "-Wl,--entry=DllMain" `
    "-Wl,--dynamicbase" "-Wl,--nxcompat" "-Wl,--high-entropy-va" `
    -o (Join-Path $here "MacrofyHook.dll") (Join-Path $here "hook.c") -lkernel32 -luser32
if ($LASTEXITCODE -ne 0) { throw "gcc build failed" }
Write-Host "Built MacrofyHook.dll"
