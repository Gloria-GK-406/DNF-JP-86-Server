<#
  rebuild_debug_exe.ps1 — one-click rebuild of the B1 debug exe after a reboot.

  Why this is needed: the B1 exe (DNF.unpacked-b1-debug.exe) is a memory-dump
  replay. The dump bakes absolute pointers to KnownDLLs (kernel32/ntdll/shell32/
  ...) whose ASLR base changes every boot. So after a reboot the old B1 exe
  crashes in real client code (a baked shell32 pointer lands in the wrong place).
  The 7 app-local DLLs survive (fixed rebased bases) but KnownDLLs cannot be
  pinned that way.

  Fix: re-dump the packed DNF.exe in the CURRENT boot (bakes this boot's KnownDLL
  bases) and rebuild the B1 exe from that fresh dump. Then launch as usual.

  This script is fully self-contained and path-relative (survives folder renames):
    <debug>\_rebuild\rebuild_debug_exe.ps1   (this file)
    <debug>\_rebuild\0000_MemoryDump.dll     (the in-process dump plugin)
    <debug>\_rebuild\rebuild_runnable_pe.py  (PE rebuilder)
  It only needs Python + the packed DNF.exe hardlink + the rebased DLLs already
  in <debug> (all present). No compiler / editbin needed (DLLs stay rebased on
  disk across reboots).

  Run this, then launch 启动DNF-debug.bat.
#>
$ErrorActionPreference = "Stop"
$rebuildDir = $PSScriptRoot
$dbg        = Split-Path -Parent $rebuildDir                 # the debug folder
$root       = Split-Path -Parent $dbg
$dataRoot   = Join-Path $root "DNFClient-ClientHackLab-20260812"
$arg        = "99?127.0.0.1?7001?10038?de509f65e9ccaae621cb7278fc2b8e6c?01?1?0?0?0?0?1?9n2b1c8r3w7y?0?0?19847"

function Say($m) { Write-Host "[rebuild] $m" }

# --- locate python ---
$py = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $py -or -not (Test-Path $py)) { $py = "C:\Users\10239\AppData\Local\Programs\Python\Python314\python.exe" }
if (-not (Test-Path $py)) { throw "Python not found. Edit `$py in this script." }
Say "python: $py"

# --- 0) stop any running client ---
Get-Process | Where-Object { $_.ProcessName -match 'unpacked|DNF' } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }

# --- 1) self-heal the game-data junctions (a folder rename/move breaks them) ---
$dirs = 'Fonts','ImagePacks2','Music','Replay','ScreenShot','SoundPacks','Video'
foreach ($d in $dirs) {
    $link = Join-Path $dbg $d
    $target = Join-Path $dataRoot $d
    if (-not (Test-Path -LiteralPath $target)) { Say "WARN data dir missing: $target"; continue }
    $ok = $false
    $it = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
    if ($it -and $it.Target) { $ok = ($it.Target -eq $target) -and (Test-Path -LiteralPath (Join-Path $link '.')) }
    if (-not $ok) {
        if ($it) { $it.Delete() }
        New-Item -ItemType Junction -Path $link -Target $target | Out-Null
        Say "junction repaired: $d"
    }
}

# --- 2) drop in the dump plugin ---
$pluginDst = Join-Path $dbg 'plugins\0000_MemoryDump.dll'
Copy-Item (Join-Path $rebuildDir '0000_MemoryDump.dll') $pluginDst -Force

# --- 3) run the PACKED DNF.exe (non-B1) so it self-dumps this boot's state ---
Say "dumping packed client (this boot's KnownDLL bases)..."
$env:CLIENTHACK_B1_MODE = ""
$before = Get-Date
$dumpsDir = Join-Path $dbg 'dumps'
$p = Start-Process -FilePath (Join-Path $dbg 'DNF.exe') -ArgumentList $arg -WorkingDirectory $dbg -PassThru
$dumpDir = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $c = Get-ChildItem $dumpsDir -Recurse -Filter COMPLETE -ErrorAction SilentlyContinue |
         Where-Object { $_.LastWriteTime -gt $before } | Select-Object -First 1
    if ($c) { $dumpDir = $c.Directory.FullName; break }
}
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Remove-Item $pluginDst -Force -ErrorAction SilentlyContinue      # never dump on normal runs
if (-not $dumpDir) { throw "dump did not complete within 60s (COMPLETE marker not found)" }
Say "dump complete: $dumpDir"

# --- 4) rebuild the B1 exe from the fresh dump ---
$exe = Join-Path $dbg 'DNF.unpacked-b1-debug.exe'
$bin = Join-Path $dumpDir 'DNF-memory-rva.bin'
$csv = Join-Path $dumpDir 'DNF-iat.csv'
if (-not (Test-Path $bin) -or -not (Test-Path $csv)) { throw "dump missing DNF-memory-rva.bin / DNF-iat.csv" }
if (Test-Path $exe) { Copy-Item $exe "$exe.prev" -Force }
Say "rebuilding B1 exe..."
& $py (Join-Path $rebuildDir 'rebuild_runnable_pe.py') `
    --original (Join-Path $dbg 'DNF.exe') --memory $bin --stage b1 --iat-csv $csv --output "$exe.new"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path "$exe.new")) { throw "rebuild_runnable_pe.py failed" }
Move-Item "$exe.new" $exe -Force

# --- 5) tidy: keep only the newest dump (each is ~107 MB) ---
Get-ChildItem $dumpsDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $dumpDir } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Say "DONE. Fresh B1 exe: $exe"
Say "Now launch:  启动DNF-debug.bat"
