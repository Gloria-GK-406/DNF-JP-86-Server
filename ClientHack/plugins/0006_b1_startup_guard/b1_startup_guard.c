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
 * What this does: hook the thunk; when `this` is NULL, record the call stack and
 * return instead of dereferencing. Skipping a startup notification is not
 * obviously free, so every skip is recorded in clienthack-b1-guard.log -- that
 * log is the evidence for whether the client stays healthy afterwards, and for
 * how often the race is actually lost.
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
    GUARD_MAX_LOGGED = 16
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
static volatile LONG g_null_hits;
static volatile LONG g_pass_hits;

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

/* One JSON line per recorded event, shaped like the NetworkHook capture logs:
 * stack entries are RVAs relative to the DNF base, so they paste straight into
 * a disassembler. `singleton` is the live value of the global at the time of the
 * hit, which distinguishes "not constructed yet" from "constructed but this
 * caller received something else". */
static void report_hit(const char* event, const void* self)
{
    void* frames[GUARD_STACK_FRAMES];
    const void* singleton = NULL;
    uintptr_t base = (g_host != NULL) ? g_host->dnf_base : 0u;
    USHORT n, i;
    FILE* f = NULL;

    if (base != 0u) singleton = *(void* const*)(base + GUARD_SINGLETON_RVA);
    n = RtlCaptureStackBackTrace(
        (ULONG)GUARD_STACK_SKIP, (ULONG)GUARD_STACK_FRAMES, frames, NULL);
    if (_wfopen_s(&f, g_log_path, L"a") != 0 || f == NULL) return;
    fprintf(f,
        "{\"event\":\"%s\",\"this\":\"0x%08lX\",\"singleton\":\"0x%08lX\","
        "\"null_hits\":%ld,\"passed\":%ld,\"stack\":[",
        event,
        (unsigned long)(uintptr_t)self,
        (unsigned long)(uintptr_t)singleton,
        (long)g_null_hits,
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
        LONG hits = InterlockedIncrement(&g_null_hits);
        if (hits <= GUARD_MAX_LOGGED) report_hit("skipped-null-this", self);
        if (hits == 1) {
            guard_log(2u,
                "singleton 0x03091FB8 was NULL: skipped the call that used to "
                "fault at 0x01CD12F0 (details in clienthack-b1-guard.log)");
        }
        return 0;
    }
    if (InterlockedIncrement(&g_pass_hits) == 1) {
        guard_log(3u, "singleton was already constructed on first use; "
                      "guard passing calls through");
    }
    return g_original_thunk(self, edx_value);
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
    char message[128];
    if (g_null_hits == 0 && g_pass_hits == 0) {
        guard_log(3u, "guard never hit: the startup path did not run");
        return;
    }
    _snprintf_s(message, sizeof(message), _TRUNCATE,
        "guard totals: %ld skipped (NULL), %ld passed through",
        (long)g_null_hits, (long)g_pass_hits);
    guard_log(g_null_hits > 0 ? 2u : 3u, message);
}
