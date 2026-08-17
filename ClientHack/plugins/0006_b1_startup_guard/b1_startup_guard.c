#include "clienthack/plugin_api.h"

#include <windows.h>
#include <stdio.h>
#include <wchar.h>

/* B1 startup guard -- keeps the unpacked B1 replay alive through a startup
 * ordering hole that the packed client never exposes.
 *
 * The failure being guarded (46 identical crashes over 2026-08-12..17): the B1
 * exe faults 0xC0000005 at RVA 0x01CD12F0, the FIRST instruction of a thiscall
 * thunk, so `this` was already invalid on entry:
 *
 *     0x01CD12F0  mov ecx, dword ptr [ecx+8]   <- faults, ecx == NULL
 *     0x01CD12F3  push 0
 *     0x01CD12F5  push 1
 *     0x01CD12F7  call 0x020D6730
 *     0x01CD12FC  ret
 *
 * The thunk occurs in no vtable (zero aligned data references to 0x020D12F0),
 * and its only caller is RVA 0x01CD1608, which loads `this` straight out of a
 * singleton pointer with no null test:
 *
 *     0x01CD1602  mov ecx, dword ptr [0x03091FB8]
 *     0x01CD1608  call 0x020D12F0
 *
 * That singleton has exactly one writer -- RVA 0x00D88E98, ordinary
 * `g = new T(0x28)` codegen, so it is not conditional in practice -- and 48
 * readers. The B1 exe is a memory-dump replay whose snapshot is taken at the
 * startup gate, before the game constructs its subsystems, so the value baked
 * into .data is 0. On some runs the consumer reaches 0x01CD1608 before that one
 * writer has run; on others it does not, which is why the same exe both plays a
 * full session and dies during startup. The packed client does not expose the
 * window: the replay changes startup timing.
 *
 * IMPORTANT -- this fault is SECONDARY. Runtime evidence from 2026-08-17 traced
 * the caller: the recorded stack is [0x01CD160D, 0x00CA4CF6, kernel32, ntdll],
 * and the site at 0x00CA4CF6 is the return of an INDIRECT call:
 *
 *     0x00CA4CDB  cmp  dword ptr [0x0307C39C], 0
 *     0x00CA4CE3  je   0x00CA4CF9         ; no callback registered -> skip
 *     0x00CA4CF0  call dword ptr [0x0307C39C]
 *     0x00CA4CF6  add  esp, 4
 *     0x00CA4CF9  mov  eax, 1
 *     0x00CA4CFF  ret  4
 *
 * `LONG WINAPI f(void*)` returning 1 is EXCEPTION_EXECUTE_HANDLER: 0x00CA4Cxx is
 * the client's UNHANDLED-EXCEPTION FILTER, and 0x01CD15E0 is the crash-reporting
 * callback it invokes (its tail formats three strings plus the literal 740 and
 * calls a 5-argument logger). The deliberate abort stub `mov dword ptr [0],0` at
 * 0x00CA4D10 sits in the same block.
 *
 * So the order of events on a bad run is:
 *   1. some PRIMARY fault happens ~2 s into startup;
 *   2. the client's filter runs and invokes its reporting callback;
 *   3. the reporter dereferences a singleton that startup never constructed,
 *      producing the SECONDARY fault at 0x01CD12F0 -- the one WER recorded,
 *      because it faults inside the filter before it can return.
 *
 * Guarding step 3 works and is worth keeping: it stops the reporter from
 * crashing. But it cannot save the process, because the filter then returns
 * EXECUTE_HANDLER and the client exits with the PRIMARY exception code -- which
 * is why WER logs no APPCRASH once the guard is in (a deliberate exit, not an
 * unhandled fault) and why the earlier 11-12 s lifetimes were the same ~2 s
 * fault plus WER collection time.
 *
 * Waiting for the singleton is therefore pointless: the process is already
 * dying and startup will never construct it. [B1Guard] SingletonWaitMs defaults
 * to 0 (skip immediately) and is kept only as a knob.
 *
 * The primary fault is invisible to WER because the client's own filter swallows
 * it, so this plugin also installs a first-chance exception recorder to capture
 * it at its origin. That record -- not the secondary fault -- is what identifies
 * the actual startup failure.
 *
 * Deliberately NOT a required plugin (CLIENTHACK_PLUGIN_FLAGS=0u): if the byte
 * signature ever stops matching, the client must still start unguarded rather
 * than refuse to boot.
 *
 * Addresses are image-relative against the fixed-SHA-256 DNF.exe baseline; see
 * ClientHack/docs/address-provenance.md.
 */

enum {
    GUARD_THUNK_RVA = 0x01CD12F0u,
    /* VA 0x03091FB8 - image base 0x00400000. Read only for diagnostics. */
    GUARD_SINGLETON_RVA = 0x02C91FB8u,

    /* Frame 0 is RtlCaptureStackBackTrace's own caller (the reporter); the
     * detour sits at ~frame 1 and the game's path starts at ~frame 2, so the
     * caller at 0x01CD1608 is visible near the front of the recorded stack. */
    GUARD_STACK_SKIP = 1u,
    GUARD_STACK_FRAMES = 16u,

    /* Cap the detailed records: a startup path must not write an unbounded log.
     * The counters keep accumulating past the cap. */
    GUARD_MAX_LOGGED = 16,

    /* Waiting is off by default: by the time the reporter runs, the process is
     * already unwinding a primary fault and the singleton will never appear.
     * Kept as a knob only. */
    GUARD_DEFAULT_WAIT_MS = 0,
    GUARD_POLL_MS = 1,

    /* First-chance records to keep. The interesting one is the earliest fatal
     * exception, so a small cap is enough and keeps the handler cheap. */
    GUARD_MAX_EXCEPTIONS = 12
};

static const uint8_t GUARD_SIG_THUNK[13] = {
    0x8B, 0x49, 0x08,             /* mov ecx, dword ptr [ecx+8] */
    0x6A, 0x00,                   /* push 0                     */
    0x6A, 0x01,                   /* push 1                     */
    0xE8, 0x34, 0x54, 0x00, 0x00, /* call 0x020D6730            */
    0xC3                          /* ret                        */
};

typedef int(__fastcall* guard_thunk_fn)(void* self, void* edx_value);

static const ch_host_api* g_host;
static guard_thunk_fn g_original_thunk;
static wchar_t g_log_path[MAX_PATH];
static DWORD g_wait_ms = GUARD_DEFAULT_WAIT_MS;
static PVOID g_veh_handle;
static volatile LONG g_recovered_hits;   /* waited, then forwarded normally */
static volatile LONG g_skipped_hits;     /* singleton absent, call dropped */
static volatile LONG g_pass_hits;        /* singleton already published */
static volatile LONG g_exceptions;       /* fatal first-chance exceptions seen */

/* Build "<game dir>\<file_name>". Falls back to the bare file name (current
 * directory) when the game directory does not fit. */
static void build_log_path(wchar_t* out, size_t cap, const wchar_t* file_name)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash = NULL;
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, file_name); return; }
    for (size_t i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash) slash[1] = L'\0';
    else out[0] = L'\0';
    wcscat_s(out, cap, file_name);
}

static void guard_log(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "B1StartupGuard", message);
}

/* Resolve [B1Guard] from ClientHack.ini next to the game exe. An absent section
 * keeps the default wait, so a client with no [B1Guard] section still recovers
 * the ordering; 0 disables waiting and restores skip-only behaviour. */
static void load_guard_config(void)
{
    wchar_t ini[MAX_PATH];
    build_log_path(ini, MAX_PATH, L"ClientHack.ini");
    g_wait_ms = (DWORD)GetPrivateProfileIntW(
        L"B1Guard", L"SingletonWaitMs", (INT)GUARD_DEFAULT_WAIT_MS, ini);
}

/* Truncate the log once per process. ClientHack.log is per-run, and an
 * append-only guard log made an archived run appear to contain earlier runs'
 * hits, so this keeps the two consistent. */
static void reset_log(void)
{
    FILE* f = NULL;
    if (_wfopen_s(&f, g_log_path, L"w") != 0 || f == NULL) return;
    fprintf(f, "{\"event\":\"session-start\",\"pid\":%lu,\"wait_ms\":%lu}\n",
        (unsigned long)GetCurrentProcessId(), (unsigned long)g_wait_ms);
    fclose(f);
}

/* Read the singleton the faulting caller loads. Volatile because the publishing
 * write happens on another thread and the wait loop must observe it. */
static void* read_singleton(void)
{
    uintptr_t base = (g_host != NULL) ? g_host->dnf_base : 0u;
    if (base == 0u) return NULL;
    return *(void* volatile*)(base + GUARD_SINGLETON_RVA);
}

/* Bounded wait for the client thread to publish the singleton. Returns the
 * pointer, or NULL on timeout; `waited_ms` receives how long it actually took,
 * which is the number that says whether the window is tight or the writer never
 * runs at all on a bad run. */
static void* wait_for_singleton(DWORD* waited_ms)
{
    DWORD start = GetTickCount();
    void* value = read_singleton();

    while (value == NULL) {
        DWORD elapsed = GetTickCount() - start;
        if (elapsed >= g_wait_ms) {
            *waited_ms = elapsed;
            return NULL;
        }
        Sleep(GUARD_POLL_MS);
        value = read_singleton();
    }
    *waited_ms = GetTickCount() - start;
    return value;
}

/* One JSON line per recorded event, shaped like the NetworkHook capture logs:
 * stack entries are RVAs relative to the DNF base, so they paste straight into
 * a disassembler. `singleton` is the live value of the global at the time of the
 * record, which distinguishes "not constructed yet" from "constructed but this
 * caller received something else". */
static void report_hit(const char* event, const void* self, DWORD waited_ms)
{
    void* frames[GUARD_STACK_FRAMES];
    uintptr_t base = (g_host != NULL) ? g_host->dnf_base : 0u;
    USHORT n, i;
    FILE* f = NULL;

    n = RtlCaptureStackBackTrace(
        (ULONG)GUARD_STACK_SKIP, (ULONG)GUARD_STACK_FRAMES, frames, NULL);
    if (_wfopen_s(&f, g_log_path, L"a") != 0 || f == NULL) return;
    fprintf(f,
        "{\"event\":\"%s\",\"this\":\"0x%08lX\",\"singleton\":\"0x%08lX\","
        "\"waited_ms\":%lu,\"recovered\":%ld,\"skipped\":%ld,\"passed\":%ld,"
        "\"stack\":[",
        event,
        (unsigned long)(uintptr_t)self,
        (unsigned long)(uintptr_t)read_singleton(),
        (unsigned long)waited_ms,
        (long)g_recovered_hits,
        (long)g_skipped_hits,
        (long)g_pass_hits);
    for (i = 0; i < n; ++i) {
        uintptr_t a = (uintptr_t)frames[i];
        uintptr_t rva = (base != 0u && a >= base) ? (a - base) : a;
        fprintf(f, "%s%lu", (i == 0 ? "" : ","), (unsigned long)rva);
    }
    fputs("]}\n", f);
    fclose(f);
}

/* The target is thiscall with no stack arguments (`ret`, not `ret N`), so `this`
 * arrives in ecx and a two-argument fastcall detour receives it as `self`; edx
 * is untouched by the target. The return value is whatever the target's callee
 * leaves in eax, and the sole caller ignores it, so the skip path returns 0. */
static int __fastcall guard_thunk(void* self, void* edx_value)
{
    if (self == NULL) {
        DWORD waited = 0;
        void* recovered = (g_wait_ms != 0u) ? wait_for_singleton(&waited) : NULL;

        if (recovered != NULL) {
            LONG hits = InterlockedIncrement(&g_recovered_hits);
            if (hits <= GUARD_MAX_LOGGED)
                report_hit("waited-then-forwarded", recovered, waited);
            if (hits == 1) {
                char message[160];
                _snprintf_s(message, sizeof(message), _TRUNCATE,
                    "singleton was NULL; waited %lums for the client thread to "
                    "publish it, then forwarded the call", (unsigned long)waited);
                guard_log(2u, message);
            }
            /* Forward with the pointer the caller should have loaded. */
            return g_original_thunk(recovered, edx_value);
        }
        {
            LONG hits = InterlockedIncrement(&g_skipped_hits);
            if (hits <= GUARD_MAX_LOGGED)
                report_hit("skipped-no-singleton", self, waited);
            if (hits == 1) {
                guard_log(2u,
                    "crash reporter ran before startup built its singleton: "
                    "skipped the call that used to fault at 0x01CD12F0. The "
                    "client is already handling a PRIMARY fault -- see the "
                    "first-chance records in clienthack-b1-guard.log");
            }
        }
        return 0;
    }
    if (InterlockedIncrement(&g_pass_hits) == 1) {
        guard_log(3u, "singleton was already published on first use; "
                      "guard passing calls through");
    }
    return g_original_thunk(self, edx_value);
}

/* Record fatal first-chance exceptions at their origin. The client's own
 * unhandled-exception filter returns EXECUTE_HANDLER, so the PRIMARY fault never
 * reaches WER; this is the only place it is observable.
 *
 * Diagnostics only: always returns EXCEPTION_CONTINUE_SEARCH, so game code that
 * relies on exceptions (including its anti-debug paths) is unaffected. Benign
 * codes the runtime raises constantly -- C++ EH, thread naming, breakpoints --
 * are skipped so the earliest genuine fault stays easy to find. */
static int is_fatal_code(DWORD code)
{
    switch (code) {
    case 0xE06D7363u:   /* C++ EH  */
    case 0x406D1388u:   /* SetThreadName */
    case 0x80000003u:   /* breakpoint */
    case 0x80000004u:   /* single step */
    case 0x4001000Au:   /* debug string */
        return 0;
    default:
        return (code & 0xC0000000u) == 0xC0000000u;
    }
}

static LONG CALLBACK first_chance_recorder(PEXCEPTION_POINTERS info)
{
    static volatile LONG reentry;
    EXCEPTION_RECORD* rec;
    uintptr_t base;
    FILE* f = NULL;
    LONG seen;

    if (info == NULL || info->ExceptionRecord == NULL)
        return EXCEPTION_CONTINUE_SEARCH;
    rec = info->ExceptionRecord;
    if (!is_fatal_code(rec->ExceptionCode)) return EXCEPTION_CONTINUE_SEARCH;

    seen = InterlockedIncrement(&g_exceptions);
    if (seen > GUARD_MAX_EXCEPTIONS) return EXCEPTION_CONTINUE_SEARCH;
    /* A fault raised while writing the record would recurse; bail instead. */
    if (InterlockedCompareExchange(&reentry, 1, 0) != 0)
        return EXCEPTION_CONTINUE_SEARCH;

    base = (g_host != NULL) ? g_host->dnf_base : 0u;
    if (_wfopen_s(&f, g_log_path, L"a") == 0 && f != NULL) {
        uintptr_t at = (uintptr_t)rec->ExceptionAddress;
        int in_image = (base != 0u && at >= base);
        fprintf(f,
            "{\"event\":\"first-chance\",\"code\":\"0x%08lX\",\"at\":\"0x%08lX\","
            "\"at_is_rva\":%d,\"thread\":%lu,\"nth\":%ld",
            (unsigned long)rec->ExceptionCode,
            (unsigned long)(in_image ? at - base : at),
            in_image,
            (unsigned long)GetCurrentThreadId(),
            (long)seen);
        if (rec->ExceptionCode == (DWORD)EXCEPTION_ACCESS_VIOLATION &&
            rec->NumberParameters >= 2) {
            static const char* kind[3] = { "read", "write", "execute" };
            ULONG_PTR op = rec->ExceptionInformation[0];
            fprintf(f, ",\"access\":\"%s\",\"target\":\"0x%08lX\"",
                (op <= 2u) ? kind[op] : "?",
                (unsigned long)rec->ExceptionInformation[1]);
        }
        fputs("}\n", f);
        fclose(f);
    }
    InterlockedExchange(&reentry, 0);
    return EXCEPTION_CONTINUE_SEARCH;
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    ch_hook_request request;
    ch_status status;

    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->hook_create == NULL || host->log == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    g_host = host;
    build_log_path(g_log_path, MAX_PATH, L"clienthack-b1-guard.log");
    load_guard_config();
    reset_log();

    /* Install the recorder before the hook: the primary fault it exists to
     * capture happens after the gate releases the client thread, but installing
     * first keeps the ordering obvious and costs nothing. A failure here is not
     * fatal -- the guard is still worth having on its own. */
    g_veh_handle = AddVectoredExceptionHandler(1UL, first_chance_recorder);
    if (g_veh_handle == NULL)
        guard_log(1u, "could not install the first-chance exception recorder");

    request.struct_size = sizeof(request);
    request.target_rva = GUARD_THUNK_RVA;
    request.expected = GUARD_SIG_THUNK;
    request.expected_length = (uint32_t)sizeof(GUARD_SIG_THUNK);
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable : 4054 4055)
#endif
    request.detour = (void*)guard_thunk;
    request.original = (void**)&g_original_thunk;
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    request.name = "b1-null-singleton-guard";

    status = host->hook_create(&request);
    if (status != CH_STATUS_OK) {
        guard_log(1u, "could not hook 0x01CD12F0: client runs unguarded");
        return status;
    }
    guard_log(3u, "null-singleton guard installed at 0x01CD12F0");
    return CH_STATUS_OK;
}

void CH_CALL ch_plugin_shutdown(void)
{
    char message[192];

    if (g_veh_handle != NULL) {
        RemoveVectoredExceptionHandler(g_veh_handle);
        g_veh_handle = NULL;
    }
    if (g_recovered_hits == 0 && g_skipped_hits == 0 && g_pass_hits == 0 &&
        g_exceptions == 0) {
        guard_log(3u, "guard never hit and no fatal exception seen: "
                      "this run started cleanly");
        return;
    }
    _snprintf_s(message, sizeof(message), _TRUNCATE,
        "guard totals: %ld forwarded after waiting, %ld skipped, %ld passed "
        "through; %ld fatal first-chance exception(s) recorded",
        (long)g_recovered_hits, (long)g_skipped_hits, (long)g_pass_hits,
        (long)g_exceptions);
    guard_log((g_skipped_hits > 0 || g_exceptions > 0) ? 2u : 3u, message);
}
