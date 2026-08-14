<#
.SYNOPSIS
  Prepare the B1 debuggable-client runtime under the DNFClient-debug folder.

.DESCRIPTION
  B1 is the runnable, unpacked, AEP-free rebuild of DNF.exe (see
  Docs/runnable-rebuild-phase3.md). It replays the dump's memory, so every DLL
  whose base was ASLR-randomized between dump time and run time carries stale
  inline references. The fix is to force those DLLs to load at their dump-time
  bases (rebased app-local copies, ASLR off). Only the 7 non-KnownDLL,
  non-fixed-base DLLs below actually move; KnownDLLs (kernel32/ntdll/...) share a
  per-boot base and are handled by the loader.

  This script rebases those 7 DLLs into the DNFClient-debug folder and deploys
  the ClientHack files (run with CLIENTHACK_B1_MODE=1 so the host skips the now
  pointless anti-debug + IAT repair). It assumes the B1 exe and the game-data
  junctions/hardlinks are already present in DNFClient-debug (one-time setup;
  they persist across reboots).

  Reboot note: the rebased DLLs keep their fixed bases across reboots, so a
  reboot usually needs only a relaunch. The only reboot risk is inline KnownDLL
  references (if any) -- those cannot be scripted around and would require a
  fresh dump in the new boot. You find out the first reboot: if B1 still reaches
  channel select, you are set; if it faults on a kernel32/ntdll address, re-dump.

.EXAMPLE
  powershell -File prepare_b1_runtime.ps1 -Launch
#>
param(
  # Source of the original (non-rebased) game DLLs to copy+rebase from.
  [string]$Client  = "D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNFClient-ClientHackLab-20260812",
  # The B1 DEBUG runtime folder to (re)build. Formerly <Client>\rebuilt; now a
  # clean sibling. This is the folder 启动DNF.bat / a debugger launches from.
  [string]$Debug   = "D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNFClient-debug",
  # Debug-config ClientHack build stage (branch: client-debug-lab).
  [string]$Stage   = "D:\project\DNF-AntiDebug-Isolated-Root-20260813\DNF-JP-86-Server\ClientHack\out\build-trace\stage",
  [string]$Editbin = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Tools\MSVC\14.50.35717\bin\Hostx86\x86\editbin.exe",
  [string]$Python  = "C:\Users\10239\AppData\Local\Programs\Python\Python314\python.exe",
  [switch]$Launch
)

$ErrorActionPreference = "Stop"
$rb = $Debug
$strip = Join-Path $PSScriptRoot "strip_sig.py"
if (-not (Test-Path $rb)) { throw "debug runtime dir not found: $rb (run the one-time B1 setup first)" }

# name, source path, dump-time base (from the dump; see phase3 doc)
$dlls = @(
  @{ n="msvcp60.dll";  src="C:\Windows\SysWOW64\msvcp60.dll";  base="0x607A0000" },
  @{ n="tbbmalloc.dll";src=(Join-Path $Client "tbbmalloc.dll");base="0x60820000" },
  @{ n="fmodex.dll";   src=(Join-Path $Client "fmodex.dll");   base="0x60390000" },
  @{ n="D3DX9_43.dll"; src=(Join-Path $Client "D3DX9_43.dll"); base="0x604E0000" },
  @{ n="dinput8.dll";  src="C:\Windows\SysWOW64\dinput8.dll";  base="0x608A0000" },
  @{ n="logoncli.dll"; src="C:\Windows\SysWOW64\logoncli.dll"; base="0x60350000" }
)

Get-Process | Where-Object { $_.ProcessName -like "*unpacked*" } | Stop-Process -Force -ErrorAction SilentlyContinue

function Rebase($dst, $base) {
  & $Editbin /NOLOGO /REBASE:BASE=$base /DYNAMICBASE:NO $dst | Out-Null
  if ($LASTEXITCODE -ne 0) {
    # LNK1282: signed image -> strip signature and retry
    & $Python $strip $dst | Out-Null
    & $Editbin /NOLOGO /REBASE:BASE=$base /DYNAMICBASE:NO $dst | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rebase failed for $dst" }
  }
}

foreach ($d in $dlls) {
  $dst = Join-Path $rb $d.n
  Copy-Item $d.src $dst -Force
  Rebase $dst $d.base
  Write-Output ("rebased {0,-14} -> {1}" -f $d.n, $d.base)
}

# ClientHack: Az is a moved DLL too (its PutRSInfo is at RVA 0x1000, matching
# the dump), so rebase it to the dump az base as well.
Copy-Item (Join-Path $Stage "Az.dll") (Join-Path $rb "Az.dll") -Force
Rebase (Join-Path $rb "Az.dll") "0x60860000"
Write-Output "rebased Az.dll        -> 0x60860000"
Copy-Item (Join-Path $Stage "ijl15.dll") (Join-Path $rb "ijl15.dll") -Force
if (-not (Test-Path (Join-Path $rb "plugins"))) { New-Item -ItemType Directory (Join-Path $rb "plugins") | Out-Null }
Copy-Item (Join-Path $Stage "plugins\*.dll") (Join-Path $rb "plugins\") -Force
# Az validates a DNF.exe next to it by SHA-256 -> hardlink the original packed exe.
if (-not (Test-Path (Join-Path $rb "DNF.exe"))) {
  New-Item -ItemType HardLink -Path (Join-Path $rb "DNF.exe") -Target (Join-Path $Client "DNF.exe") | Out-Null
}
Write-Output "deployed Az.dll, ijl15.dll, 6 plugins, DNF.exe hardlink"

if ($Launch) {
  $env:CLIENTHACK_B1_MODE = "1"
  $arg = "99?127.0.0.1?7001?10038?de509f65e9ccaae621cb7278fc2b8e6c?01?1?0?0?0?0?1?9n2b1c8r3w7y?0?0?19847"
  Start-Process -FilePath (Join-Path $rb "DNF.unpacked-b1-debug.exe") -ArgumentList $arg -WorkingDirectory $rb | Out-Null
  Write-Output "launched B1 (CLIENTHACK_B1_MODE=1). Attach a debugger to explore runtime code."
}
