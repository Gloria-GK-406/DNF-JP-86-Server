# Debug / Play split — operator runbook

Two isolated setups that share one game-data install:

- **Play** — the original packed `DNF.exe` with the lean ClientHack (server
  redirect + anti-cheat + client-check bypass). What you actually play on.
- **Debug** — the unpacked, AEP-free rebuild `DNF.unpacked-b1-debug.exe` (B1) with
  the full debug ClientHack (adds anti-anti-debug + 00AB capture/trace). What you
  attach a debugger to when reverse-engineering the protocol.

Reverse a protocol on **Debug**, then play it on **Play**.

---

## 1. Branch model (source)

| Branch | Role | ClientHack shape |
|---|---|---|
| `main` (`ca7feb3`+) | **Play** config | 5 required plugins, `CH_REQUIRED_PLUGIN_COUNT=5`, no `0005`, no `startup_trace.c`/`early_antidebug.c`. Static CRT (`/MT`). |
| `client-debug-lab` (`6d4a542`+) | **Debug** config | 6 required plugins (adds `0005_AntiAntiDebug`), B1 PE-rebuild toolchain, gate-in-DllMain, frame capture + startup trace. |

`client-debug-lab` was branched from `main`, so every play patch is already in the
debug branch. The reverse is **not** true — the debug apparatus stays off `main`.
The old `codex/*` branches were renamed/cleaned; the debug lineage is now
`client-debug-lab`.

> Play-critical fix reminder: `8525279 "finalize packet totalLength in cipher
> encrypt passthrough"` lives on `origin/main`. Local `main` must be at
> `origin/main` (or later) or the play client misframes the first short login
> follow-up packet and the connection drops. It is already in `client-debug-lab`.

## 2. Folder layout (runtime, outside the repo)

```
D:\project\DNF\
├─ DNFClient-ClientHackLab-20260812\   <- DATA ROOT + archive (real game data,
│                                          original DNF.exe, dumps, old logs)
├─ DNFClient-play\                      <- PLAY: packed DNF.exe + play Az + 5 plugins
└─ DNFClient-debug\                     <- DEBUG: B1 exe + rebased DLLs + 6 plugins
```

`DNFClient-play` and `DNFClient-debug` are the two operational folders. Both pull
the multi-GB game data (`ImagePacks2`, `SoundPacks`, …) from the data root via
**junctions**, and the shipped game files via **hardlinks** — the data is stored
once. Cleaning old logs/experiments out of the data root is safe as long as the
game *data* dirs and the original `DNF.exe` stay.

## 3. Run PLAY

Nothing to rebuild after a reboot — the play DLLs load with normal ASLR.

```
DNFClient-play\启动DNF-play.bat
```

Launches `DNF.exe` (packed) with `CLIENTHACK_B1_MODE` unset. Az does the full
packed-exe path (IAT repair, ~342 fixups) and loads exactly the 5 play plugins.
Server target is `ClientHack.ini` → `[ServerRedirect] Address`.

Healthy boot log ends with: `IAT policy accepted`, 5 `plugin initialized`,
`ordered plugin initialization completed`, `client thread released`.

## 4. Run DEBUG

### 4a. After a reboot — one click to rebuild the B1 exe

```
DNFClient-debug\重建debug-exe.bat      then      DNFClient-debug\启动DNF-debug.bat
```

**A reboot DOES break the old B1 exe — this is confirmed, not hypothetical.** The
dump replay bakes absolute pointers to KnownDLLs (kernel32/ntdll/**shell32**/...)
whose ASLR base changes every boot; after a reboot the old exe crashes in real
client code (e.g. a baked shell32 pointer lands in `int 3` padding, seen as
`call dword ptr [eax+54h]` → dead). The 7 app-local DLLs survive (fixed rebased
bases, `DYNAMICBASE:NO`), but KnownDLLs cannot be pinned that way.

`重建debug-exe.bat` fixes it in ~10-15 s, fully automated (self-contained in
`DNFClient-debug\_rebuild\`, path-relative so a folder rename doesn't break it):

1. self-heals the 7 game-data junctions (a folder rename/move breaks them);
2. drops the `0000_MemoryDump` plugin in, runs the **packed** `DNF.exe` (non-B1) so
   it self-dumps **this boot's** KnownDLL bases (< 5 s), removes the plugin again;
3. runs `rebuild_runnable_pe.py` on the fresh dump → new `DNF.unpacked-b1-debug.exe`;
4. keeps only the newest dump (~107 MB each).

It reuses the rebased DLLs already on disk (they persist across reboots), so no
recompile / editbin / `prepare_b1_runtime.ps1` is needed after a reboot.

First-time setup only (not per reboot): `prepare_b1_runtime.ps1` rebases the 7
DLLs to their fixed dump bases and deploys Az + 6 plugins + the `DNF.exe`
SHA-identity hardlink into `DNFClient-debug`. Verify the 7 bases with §6.

### 4b. Launch + attach

```
DNFClient-debug\启动DNF-debug.bat     (sets CLIENTHACK_B1_MODE=1, runs the B1 exe)
```

Then attach a debugger to `DNF.unpacked-b1-debug.exe`. The `0005_AntiAntiDebug`
plugin hides DRx and neutralizes anti-attach, so hardware breakpoints are not
detected and the client keeps running.

Healthy boot log: `startup gate intercepted the client thread`, 6 `plugin
initialized` (incl. `AntiAntiDebug`), `client thread released`.

### 4c. 00AB protocol breakpoint (worked example)

Image base is `0x00400000`; 00AB serializer RVA `0x01CD1120` → VA `0x020D1120`.

```
ba e1 0x020d1120 ".echo GOT_00AB_ECX; dd @ecx L14; gc"
```

On channel login this hits once, dumps the `this` state-report object (4 slots),
and auto-continues; the client survives. `NetworkHook` independently logs
`frame-capture: send type=0x00AB ...` to `clienthack-frame-capture.log`.

### 4d. Frame capture ([FrameCapture] in ClientHack.ini)

`NetworkHook` records one JSON line per Game packet, both directions, to
`clienthack-frame-capture.log` next to the exe. Defaults capture every type
with call stacks, which is what a first pass on an unknown message needs;
narrow `Types=` once the message of interest is known.

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `1` | Master switch for the recorder |
| `Types` | `*` | Hex type list (`00AB,01DD`) or `*`; empty stays `*` |
| `Stack` | `*` | Hex type list to capture call stacks for; empty = none |
| `MaxBodyBytes` | `4096` | Hex-dump cap per frame; `0` = unlimited |
| `WriteTrace` | `0` | Also trace `ISocket::Write` staging to `clienthack-write-trace.log` |
| `LogFile` | `clienthack-frame-capture.log` | Capture log file name |

The two directions record different fields, because they see different memory:

- `send` (`Cipher::Encrypt`): `hdr:1`, full 13-byte header + body in `hex`, with
  `cmd`/`type`/`length`/`seq` read from that header. `totalLength` is finalized
  before the dump, so the bytes match the wire.
- `recv` (`Cipher::Decrypt`): `hdr:0`, body only. The decrypt input is **not**
  preceded by the frame header, so the identity comes from the `packet_type`
  argument alone (recorded as both `arg_type` and `type`). Never read behind
  that pointer — an earlier version did and corrupted client behavior.

`dumped` reports how many bytes the `hex` field actually contains; it is lower
than `body_len` (recv) or `length` (send) when `MaxBodyBytes` truncated it.

## 5. Rebuild an Az from source (only when the branch changes)

Play (from `main`, isolated worktree so the debug tree is untouched):

```
git worktree add ..\_play-build-main main
cmake --preset win32-release            # in _play-build-main\ClientHack
cmake --build out\win32-release --config Release --target clienthack_stage
# copy out\win32-release\stage\{Az.dll,ijl15.dll,plugins\*} into DNFClient-play,
# then: git worktree remove ..\_play-build-main --force
```

Debug (from `client-debug-lab`, its stage feeds prepare_b1_runtime.ps1):

```
cmake --preset win32-debug              # or the existing out\build-trace
cmake --build out\build-trace --config Debug --target clienthack_stage
# then re-run prepare_b1_runtime.ps1 to redeploy+rebase into DNFClient-debug
```

## 6. Quick checks

Rebased debug DLL bases (all must match):

```
msvcp60=0x607A0000 tbbmalloc=0x60820000 fmodex=0x60390000 D3DX9_43=0x604E0000
dinput8=0x608A0000 logoncli=0x60350000 Az=0x60860000
```

Play plugin count = 5 (no `0005`); Debug plugin count = 6 (incl. `0005`).
