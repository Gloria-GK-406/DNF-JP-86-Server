# TU 49 — `DNFClient\RDAR\DumpReport\HangDumpTrace.cpp`

Briefing span `0x004CC660`–`0x004CCA40`. **Real span is `0x004CC600`–`0x004CCB60`** (see "Span correction").

## One-line answer

`CHangDumpTrace` — a **main-thread watchdog**. A dedicated thread wakes every 10 s, reads a
heartbeat DWORD that the game loop stamps with `GetTickCount()`, and if that value is unchanged
**12 consecutive times (~120 s)** it declares the client hung: it writes `NetworkDump.dmp`
(`MiniDumpWriteDump`), **suspends the watched thread**, reads its `CONTEXT`, walks its EBP chain
into an XML `<CrashDump>` buffer, and hands that buffer to
`CCrashReporter::reportNetworkMinidump` for HTTP upload. No packet parsing — `parsers = 0` is
correct.

Class name is ground truth from the decrypted literals `CHangDumpTrace::Create` (`0x03061A64`)
and `CHangDumpTrace::checkThread` (`0x030619C0`), both paired with the `__FILE__`
`D:\Work\1229\Source\DNFClient\RDAR\DumpReport\HangDumpTrace.cpp` (`0x03061938`).

---

## 1. Functions

| VA | Role | Convention |
| --- | --- | --- |
| `0x004CC600` | `writeMiniDump(const wchar_t* path, _, _)` | `__cdecl`, 3 args (2 ignored) |
| `0x004CC660` | `seedFrameFromSelf()` — seeds the walker from *checkThread's own* frame | `__cdecl`, 0 args |
| `0x004CC690` | `CHangDumpTrace::checkThread(void* this)` — the watchdog thread proc | `__stdcall` (`ret 4`) |
| `0x004CC9F0` | `CHangDumpTrace::stop()` | `__thiscall` |
| `0x004CCA10` | `CHangDumpTrace::tick()` — the heartbeat stamp | `__thiscall` |
| `0x004CCA40` | `CHangDumpTrace::Create()` — spawns the watchdog thread | `__thiscall` |
| `0x004CCAE0` | `CHangDumpTrace::getInstance()` — lazy singleton | `__cdecl`, 0 args |

`functions.csv` folds `0x004CC660` and `0x004CC690` into one 912-byte record; they are two
functions separated by `int3` padding at `0x004CC684`–`0x004CC68F`.

---

## 2. Object layout — `CHangDumpTrace`, sizeof = 0x0C

Proof: `0x004CCAE0` allocates exactly `0xC` and zeroes three dwords.

```
004CCB0D  push 0xc
004CCB0F  call 0x423460            ; operator new(12)
004CCB29  mov  dword ptr [esi],     0
004CCB2F  mov  dword ptr [esi+4],   0
004CCB36  mov  dword ptr [esi+8],   0
004CCB3D  call 0x4cca40            ; this->Create()   (ecx = esi)
004CCB48  mov  dword ptr [0x39a2a58], eax
```

| Offset | Type | Meaning | Proof |
| --- | --- | --- | --- |
| `+0x00` | `unsigned` | watchdog thread's own TID | `_beginthreadex` arg6 = `esi` (`push esi` at `0x004CCA51`) writes `*(unsigned*)this` |
| `+0x04` | `HANDLE` | watchdog thread handle | `0x004CCA6B mov [esi+4], eax`; tested `== -1` at `0x004CCA6E` |
| `+0x08` | `DWORD` | **watched thread id** (the thread that first touched the singleton) | `0x004CCA93 mov [esi+8], eax` after `GetCurrentThreadId`; read at `0x004CC7EF mov eax,[edx+8]` and fed to `OpenThread` |

`+0x08` is cleared to 0 by `stop()` (`0x004CC9FF mov dword ptr [esi+8], 0`).

### Globals

| VA | Meaning | Proof |
| --- | --- | --- |
| `0x039A2A58` | `CHangDumpTrace*` singleton | `0x004CCB04` read / `0x004CCB48` write |
| `0x039A2A5C` | heartbeat DWORD (`GetTickCount` snapshot) | `InterlockedExchange(0x39a2a5c, …)` at `0x004CCA2E`; read at `0x004CC6D0` / `0x004CC6E8` / `0x004CC86E` |
| `0x039A2A60` | `HANDLE` manual-reset stop event | created `0x004CCA4B`, waited `0x004CC6C5`, signalled `0x004CC9F9` |
| `0x03A45C98` | the shared XML report buffer (lives in `DumpCommon.cpp`) | `mov ecx, 0x3a45c98` at 8 sites |
| `0x03B84690` | `CCrashReporter` instance | `mov ecx, 0x3b84690; call 0x279cd60` at `0x004CC9C3` |

---

## 3. `CHangDumpTrace::Create()` — `0x004CCA40`

```
004CCA41  push 0 / push 0 / push 1 / push 0
004CCA4B  call [0x30291b8]      ; CreateEventW(NULL, bManualReset=TRUE, bInitialState=FALSE, NULL)
004CCA5E  mov  [0x39a2a60], eax
          push esi / push 4 / push esi / push 0x4cc690 / push 0 / push 0
004CCA63  call 0x2c656cd        ; _beginthreadex(NULL, 0, checkThread, this, CREATE_SUSPENDED, &this->tid)
004CCA6B  mov  [esi+4], eax
004CCA6E  cmp  eax, -1          ; on failure: CloseHandle(event); g_event = 0
004CCA89  call [0x30291b4]      ; GetCurrentThreadId()
004CCA93  mov  [esi+8], eax     ; <- watched thread
004CCA96  call [0x3029194]      ; ResumeThread(this->hThread)
          log(0, __FILE__, "CHangDumpTrace::Create", 73, L"------------- Hang Check Start!!")
```

`0x02C656CD` is `_beginthreadex` — it validates arg3 (start address) against `EINVAL = 0x16`
and allocates a `0x214`-byte `_tiddata`.

**Source line numbers recovered** (immediate pushed as the logger's 4th arg):
`Create` logs at **line 73** (`0x49`), `checkThread` stop-log at **line 115** (`0x73`),
hang-detected log at **line 129** (`0x81`).

---

## 4. `CHangDumpTrace::tick()` — `0x004CCA10` (the heartbeat)

```
004CCA17  push &local; call 0x27be550   ; local = GetTickCount()   (thunk: call [0x3029608]; *arg = eax)
004CCA23  lea ecx, &local; call 0xe46b20 ; mov eax,[ecx]; ret      (read it back)
004CCA2E  call [0x30291b0]              ; InterlockedExchange(&g_lastTick(0x39a2a5c), tick)
```

Called **once per main-loop iteration** from `WinMain` (`Main.cpp`, TU 89):

```
02793BE4  call 0x1102c30       ; get engine object
02793BF0  call eax             ; vtable[+8]  -> per-frame step
02793BF2  call 0x4ccae0        ; CHangDumpTrace::getInstance()
02793BF9  call 0x4cca10        ; ->tick()
02793C0F  jne  0x2793bc8       ; loop
02793C13  call 0x4ccae0
02793C1A  call 0x4cc9f0        ; ->stop()  on loop exit
```

Because `getInstance()` is first reached from inside that loop, `this+8` is the **main/game
thread**, and the watchdog measures *main-loop liveness*, not process liveness.

---

## 5. `CHangDumpTrace::checkThread` — `0x004CC690` (the state machine)

```
esi = 0 (stall count), edi = 0 (last seen tick), ebx = &WaitForSingleObject
loop:
  r = WaitForSingleObject(g_stopEvent, 10000)          ; 0x2710 ms
  if (r == WAIT_OBJECT_0) goto shutdown                ; je 0x4cc883
  t = g_lastTick
  if (t != edi) { edi = t; esi = 0; goto loop; }        ; 0x4CC6D8 jne 0x4cc866
  if (++esi < 12)          goto loop;                   ; 0x4CC6DF cmp esi,0xC / jl
  ---- HANG ----
shutdown:
  log(0, __FILE__, "CHangDumpTrace::checkThread", 115, L"------------- Stop Hang Dump Trace!!")
  return 0
```

**Threshold: 12 consecutive unchanged 10-second polls = ~120 s of frozen main loop.**
The dump path falls straight into the epilogue afterwards, so the thread performs **exactly one
hang dump per process lifetime** — which is why the "Count[%d]" argument is the folded constant
`1` (`push 1` at `0x004CC6F0`).

### Hang path, in order

1. `log(0, file, "CHangDumpTrace::checkThread", 129, L"------------- HangDump!! Count[%d] [%d == %d]", 1, lastTick, curTick)`
2. `writeMiniDump(L"NetworkDump.dmp", 0, 0)` — `0x004CC73D call 0x4cc600`
3. `buf.reset()` — `0x004CC74A call 0x15476a0`
4. `buf.appendf(L"<CrashDump version='1'> ")`
5. **PE build-stamp probe** (`0x004CC764`–`0x004CC7B5`):
   ```
   mov  eax, [0x40003c]                  ; IMAGE_DOS_HEADER.e_lfanew
   cmp  byte [eax+0x400000], 'P' / 'E' / 0 / 0
   mov  eax, [eax+0x400008]              ; IMAGE_NT_HEADERS.FileHeader.TimeDateStamp
   buf.appendf(L"  <Processs timestamp='0x%08x'/> ", eax)
   ```
   If the `PE\0\0` check fails the function **jumps straight to the epilogue at `0x004CC9D9`** —
   no report at all. (`jne 0x4cc9d9`, four times.) The `<Processs>` typo is the client's.
6. `buf.appendf(L"  <Exception code='None Exception' where='None Exception' dumpType='hang'")`
   then `buf.appendf(L"/> ")` — so **a hang report is distinguished from a crash report solely by
   `dumpType='hang'` plus the sentinel `code='None Exception'`**.
7. `hT = OpenThread(0x1F03FF /*THREAD_ALL_ACCESS*/, FALSE, this->watchedTid)` — `0x004CC7FD`
8. if `hT`:
   ```
   memset(&ctx, 0, 0x2CC);  ctx.ContextFlags = 0x10007   ; CONTEXT_FULL
   SuspendThread(hT);       GetThreadContext(hT, &ctx)
   ebp = *(DWORD*)((char*)&ctx + 0xB4)                   ; CONTEXT.Ebp
   if (ebp == 0) ebp = <checkThread's own ebp>           ; 0x004CC856
   valid = (ebp & 3) == 0 && *(DWORD*)ebp > ebp && !IsBadWritePtr(*(DWORD*)ebp, 8)
   if (valid) { buf.setFP(*(DWORD*)ebp); buf.setIP(*(DWORD*)(ebp+4)); }
   else       { ResumeThread(hT); CloseHandle(hT); goto self; }
   ```
   `0x2CC` = `sizeof(CONTEXT)` on x86; `0x2d0-0x21c = 0xB4` = `CONTEXT.Ebp`. Proven by the
   `memset` size at `0x004CC80F` and the local at `ebp-0x2d0`.
9. `self:` `0x004CC92D call 0x4cc660` — seeds FP/IP from **checkThread's own** frame, so the
   report is still emitted (of a useless stack) when the target thread cannot be inspected.
10. `writeCallStack(&buf, buf.getFP(), buf.getIP())` — `0x004CC94D call 0x1547eb0`
11. if suspended: `ResumeThread(hT); CloseHandle(hT)` (`0x004CC959`/`0x004CC961`) — the walk runs
    **while the target is suspended**.
12. `buf.appendf(L"</CrashDump> ")`
13. `SnapShot::loggingSnapShot()` — `0x004CC99B call 0x12d1bd0`
14. `DumpLogger::instance()->lock()` / `->flush()` / … / `->unlock()`
    (`0x27FD7D0` instance, `0x27FA100` `EnterCriticalSection(0x3BB2500)`, `0x27FBBF0` flush,
    `0x27FA110` `LeaveCriticalSection`)
15. `CCrashReporter(0x3B84690)->reportNetworkMinidump(buf.c_str())` — `0x004CC9C3`

### IAT naming caveat (important)

`DNF-iat.csv` names `0x030291A0` **OpenThread** and `0x03029194` **GetExitCodeThread**; both are
**wrong** for this code. `0x030291A0` and `0x030291A4` hold the *same* pointer `0x75A0D7E0` in the
dump — the reconstruction duplicated one entry. Arity refutes the names:

* `0x004CC830 call [0x30291a0]` — **one** `push esi` (a HANDLE), immediately before
  `GetThreadContext`, with `ContextFlags` just set → **`SuspendThread`**.
* `0x004CC920` / `0x004CC961` / `0x004CCA96 call [0x3029194]` — **one** `push` of a HANDLE,
  followed by `CloseHandle`, and in `Create` immediately after a `CREATE_SUSPENDED`
  `_beginthreadex` → **`ResumeThread`**. `GetExitCodeThread` takes two arguments.
* `0x004CC7FD call [0x30291a4]` — three pushes (`0x1F03FF, 0, tid`) → genuinely `OpenThread`.

Treat `0x030291A0`/`0x03029194` as `SuspendThread`/`ResumeThread` anywhere else in the image.

---

## 6. `writeMiniDump` — `0x004CC600`

```
CreateFileW(path, GENERIC_WRITE 0x40000000, FILE_SHARE_WRITE 2, NULL,
            CREATE_ALWAYS 2, FILE_ATTRIBUTE_NORMAL 0x80, NULL)
0x2c8ddb4( GetCurrentProcess(), GetCurrentProcessId(), hFile, 0, 0, 0, 0 )   ; MiniDumpWriteDump
CloseHandle(hFile);  return (ret == 1)
```

Seven-argument shape and the `(hProcess, pid, hFile, DumpType, …)` order identify `0x02C8DDB4`
as the `MiniDumpWriteDump` thunk. **`DumpType = 0` (`MiniDumpNormal`)** — hang dumps are
*normal* minidumps (no full memory), unlike the crash path in `DumpCommon.cpp`, which has both
`minidump.dmp` and `fulldump.dmp` literals.

Two extra arguments are pushed at the call site (`push 0; push 0;` at `0x004CC72B`) and never
read — a defaulted signature.

---

## 7. The report buffer `0x03A45C98` (owned by `DumpCommon.cpp`, layout proved here)

`0x01547E00` carries `__FILE__ = D:\Work\1229\Source\DNFClient\RDAR\DumpReport\DumpCommon.cpp`,
so the whole `0x01547xxx` accessor family belongs there. Layout, from the accessors:

| Offset | Field | Proof |
| --- | --- | --- |
| `+0x00000` | `wchar_t buffer[0x8000]` (64 KiB) | `cmp eax, 0x8000` after `sub eax,esi; sar eax,1` at `0x015476C1` |
| `+0x10000` | `wchar_t* pWrite` | `0x015476A0 mov [ecx+0x10000], ecx` (reset); `0x015476E5 add [esi+0x10000], eax` |
| `+0x10004` | `DWORD framePointer` | `0x015476F6` set / `0x01547700` get |
| `+0x10008` | `DWORD instructionPointer` | `0x01547716` set / `0x01547720` get |

Methods: `0x015476A0` `reset()`, `0x015476B0` `appendf(this, fmt, …)` (bounded `_vsnwprintf` at
`0x02C65906`, advances `pWrite` by `2*ret`), `0x015476F0/0x01547700` FP set/get,
`0x01547710/0x01547720` IP set/get, `0x02C426E0` `c_str()` (`mov eax,ecx; ret`).

`0x01547EB0` `writeCallStack(buf, fp, ip)` emits
`  <CallStack eip='0x%08x' checksum='0x%08x'> ` then one
`    <Call addr='0x%08x' module='…'/> ` per frame — `IsBadReadPtr(fp,8)` + `VirtualQuery` +
`GetModuleFileNameW` into the shared `MAX_PATH` wide scratch at `0x03A55CA8`, with `&`→`&amp;`
and `'`→`&apos;` XML escaping — closing with `  </CallStack> `.

**The buffer is wide.** `appendf` divides the byte delta by 2, and the literal decryptor
`0x027BACC0` tests a **WORD** magic (`cmp cx, 0x2CA1`) and returns `ptr+4`. Literal spacing
confirms it: `0x030618D4` (`"<CrashDump version='1'> "`, 24 chars) to `0x03061910` is `0x3C`
bytes — UTF-16, not ANSI.

---

## 8. Public surface (who calls in, from outside the file)

| Function | External callers | Context |
| --- | --- | --- |
| `getInstance()` `0x004CCAE0` | `0x02793870` ×2 (`Main.cpp` `WinMain`), `0x01547850`, `0x01547BB0` (`DumpCommon.cpp` crash paths) | lazy-constructs + `Create()`s the watchdog |
| `tick()` `0x004CCA10` | `0x02793870` (main loop body, `0x02793BF9`) | one heartbeat per frame |
| `stop()` `0x004CC9F0` | `0x02793870` (`0x02793C13`, normal shutdown), `0x01547850` (`0x01547A40`), `0x01547BB0` | **the crash handler disarms the hang watchdog before writing its own dump** — otherwise both would race to produce a report |
| `checkThread` `0x004CC690` | 0 direct callers — reached only as the `_beginthreadex` start address | |

`writeMiniDump` `0x004CC600` and `seedFrameFromSelf` `0x004CC660` are file-local.

---

## 9. Span correction

The briefing's `0x004CC660`–`0x004CCA40` is **too narrow at both ends**. The `__FILE__` anchors
sit at `0x004CC660` and `0x004CCA40`, but two more functions belong to the same object file:

* `0x004CC600` `writeMiniDump` — called only from `checkThread` (`0x004CC73D`), adjacent, no
  `__FILE__` of its own.
* `0x004CCAE0` `getInstance` — constructs the class and calls `0x004CCA40 Create()`; `in_degree 3`
  is understated in the briefing table (it lists `0x004CCA40` with 1 caller, which is
  `getInstance`).

**True TU: `0x004CC600` – `0x004CCB60`, 7 functions.** Everything from `0x004CCB60` onward is
unrelated `std::vector` destructor COMDATs (element strides `0x4C` and `0xBC`), and the next
`__FILE__` anchor in the region is `0x004D1360` (`IntegrateEventDataManager.cpp`). The literals
inside the span all agree with the file name — no contradiction.

---

## 10. Server relevance

There is **no packet code here** (`parsers = 0`, no `read_u8/16/32/bytes` reachability), so
nothing to mirror on the wire. What a server/private-server operator should still know:

1. **A frozen client self-reports after ~120 s.** If the main loop stalls — which on a private
   server most often means *the client is blocked waiting on a server response* — the client
   spawns a minidump, gathers `SnapShot::loggingSnapShot()` state (`0x012D1BD0`: character
   name/level/job, party roster with each member's last skill index, last map index, last dungeon
   index, fps, memory usage, OS bitness, `Memory Control Level`), and hands the package to
   `CCrashReporter`. Sibling function `CCrashReporter::reportBugToHTTPServer` (`0x0279BFB0`)
   shows the transport: shell out to `7z.exe` to build `DNF_DUMP%04d%02d%02d%02d%02d%02d.zip`
   from `<base>.dmp` / `.trc` / `.jpg` / `.cmp` (+ `HShield/hshield.log`), then HTTP POST.
   On a private server that endpoint is unreachable, so the visible symptom of a 2-minute server
   stall is a stray `7z.exe` and a `NetworkDump.dmp` in the client directory, plus a long hang
   before the reporter gives up.
2. **The report identifies the client build by PE `TimeDateStamp`**, emitted as
   `<Processs timestamp='0x%08x'/>` — the only build identifier in the XML.
3. **`dumpType='hang'` + `code='None Exception'`** is the discriminator between a watchdog report
   and a real exception report, if you ever ingest these.
4. The name `NetworkDump.dmp` is a **hard-coded handoff**: this file writes it,
   `CCrashReporter::reportNetworkMinidump` (`0x0279CD60`, literal `0x03061910`) reads/renames it.

---

## 11. Loose ends / behaviour worth flagging

* `stop()` only `SetEvent`s and zeroes `this+8`. It does **not** join the thread, **not**
  `CloseHandle` the thread handle or the event, and **not** delete the singleton. The event is
  manual-reset, so once stopped the watchdog can never be restarted.
* If `WaitForSingleObject` times out at the same instant `stop()` runs, `checkThread` can still
  proceed into the full dump path with `this->watchedTid == 0`; `OpenThread(…, 0)` fails and it
  falls back to walking its own stack, producing a bogus but well-formed report.
* If `CreateEventW` fails, `g_stopEvent` is `NULL` and `WaitForSingleObject(NULL, 10000)` returns
  `WAIT_FAILED (0xFFFFFFFF)` — non-zero, so the loop treats it as a timeout and runs forever.

## Evidence index

| Claim | VA |
| --- | --- |
| class/file names | literals `0x03061938`, `0x03061A64`, `0x030619C0` |
| 10 s poll | `0x004CC6B5` `push 0x2710` |
| 12-strike threshold | `0x004CC6DF` `cmp esi, 0xC` |
| heartbeat write | `0x004CCA2E` `InterlockedExchange` on `0x039A2A5C` |
| heartbeat source | `0x027BE550` → `call [0x3029608]` (`GetTickCount`) |
| tied to main loop | `0x02793BF2`/`0x02793BF9` in `Main.cpp` |
| watched tid = `this+8` | `0x004CCA93` write, `0x004CC7EF` read |
| CONTEXT.Ebp | `ebp-0x21c` vs base `ebp-0x2d0` = `+0xB4`; `memset` `0x2CC` at `0x004CC80F` |
| suspend/resume | `[0x030291A0]` @ `0x004CC830`, `[0x03029194]` @ `0x004CC920`/`0x004CC961`/`0x004CCA96` |
| minidump | `0x004CC61A` `CreateFileW`, `0x004CC63F` → `0x02C8DDB4` |
| PE timestamp | `0x004CC764`–`0x004CC7A0` |
| buffer layout | `0x015476A0`, `0x015476C1`, `0x015476F6`, `0x01547716` |
| report handoff | `0x004CC9C3` `mov ecx,0x3b84690; call 0x279cd60` |
