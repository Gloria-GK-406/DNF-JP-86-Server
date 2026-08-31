/* Wrapper-entry rewrite pilot: do the mutated .-wI layer and the runtime it
 * reads (.eI4) tolerate having a wrapper replaced by a two-instruction
 * transfer to the real API?
 *
 * ANSWERED 2026-08-31: yes. 16 hot entries (B/A/D conventions -- message
 * loop, GDI draw, input polling, UI geometry) survived a 24-minute session
 * through a full dungeon clear and the complete exit flush; the first-chance
 * log matched a zero-patch control run entry for entry. The protector does
 * not integrity-check the wrapper bytes. The one group that did NOT survive,
 * C ("esp+4/a1"), was never a transfer wrapper: those entries are
 * register-loader stubs that compute the API address into a register and
 * return to the caller (R+1), the caller then pushes args and calls the
 * register -- see Docs/themida-api-wrapper-recovery.md, "C 组不是转移型
 * 包装器". Rewriting them as transfers makes the API return into a stack
 * buffer; the Conventions= ini knob exists so that mistake can never load
 * unnoticed: keep C out of the set.
 *
 * What it does: at plugin init (inside the startup gate, before the client
 * thread is released) it rewrites the entries compiled into
 * wrapper_pilot_samples.h in place, using patch_apply so every target is
 * byte-verified against the dump first. The per-convention templates
 * reproduce the stack shape symeval measured (see build_replacement).
 *
 * API addresses are resolved at runtime (GetModuleHandle/GetProcAddress), so
 * the table never bakes in dump-time module bases. Modules are limited to
 * kernel32/user32/gdi32, which are loaded before the gate arms.
 *
 * Samples were chosen by tools/generate_wrapper_pilot.py from the safety
 * census (tools/wrapper_rewrite_safety.py): no still-running code lands in
 * any sampled entry's first 16 bytes, and no neighbouring named entry sits
 * within the patched bytes.
 *
 * A measurement, never required: off unless [WrapperPilot] Enabled=1 in
 * ClientHack.ini, and every patch failure is logged and skipped rather than
 * failing the host. Delete the DLL (or clear the flag) to disable.
 */
#include "clienthack/plugin_api.h"
#include "wrapper_pilot_samples.h"
#include "wrapper_pilot_census_d.h"
#include "wrapper_pilot_full.h"

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>
#include <string.h>

static const ch_host_api* g_host;

static void log_line(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "WrapperPilot", message);
}

static uint32_t build_replacement(wrapper_pilot_convention_t convention,
    uintptr_t api, uint8_t out[10]);
static uintptr_t stub_emit(const uint8_t* code, uint32_t length);
static uint32_t build_trampoline(uintptr_t entry, uintptr_t stub, uint8_t out[5]);

/* ---- Witness mode ----------------------------------------------------
 *
 * Answers a question the transfer pilot cannot: did the patched wrapper's
 * call site ever EXECUTE during the session? "The client stayed up" only
 * proves a wrapper is safe to rewrite if the rewritten code actually ran,
 * and each wrapper serves exactly one call site, so a cold path yields a
 * clean run that proves nothing.
 *
 * [WrapperPilot] Witness=1 replaces the transfer template with a single
 * 0xCC at the entry. The first execution traps; this handler restores the
 * original byte, rewinds EIP onto it and continues, so the wrapper then
 * runs untouched and the session carries on. One shot per entry, one log
 * line per entry, no behavioural change beyond the first trap.
 *
 * Our VEH is registered last (plugin 0150 loads after 0001/0005/0006/0007)
 * and AddVectoredExceptionHandler(1, ...) prepends, so we see the trap
 * first and the other handlers never observe it.
 */
#define WRAPPER_PILOT_WITNESS_MAX 1280

typedef struct wrapper_pilot_witness {
    uintptr_t address;
    uint8_t original;
    const char* module;
    const char* function;
    wrapper_pilot_convention_t convention;
    uintptr_t api;
} wrapper_pilot_witness_t;

static wrapper_pilot_witness_t g_witness[WRAPPER_PILOT_WITNESS_MAX];
static uint32_t g_witness_count;
static PVOID g_witness_veh;
static int g_witness_then_rewrite;
static int g_trampoline;

/* Write one byte ourselves rather than through host->patch_apply.
 *
 * patch_apply refused all 16 targets with SYSTEM_ERROR, which it only returns
 * when its VirtualProtect fails (a byte mismatch would have been status 3, so
 * the sample table does agree with the live image). Doing the write here lets
 * us report GetLastError and fall back to WriteProcessMemory, which performs
 * its own protection dance in the kernel and so survives a hooked or filtered
 * user-mode VirtualProtect. */
static int witness_write_bytes(uintptr_t address, const uint8_t* bytes,
    uint32_t count, char* why, size_t why_size)
{
    DWORD old_protect = 0;
    DWORD protect_error = 0;
    SIZE_T written = 0;

    if (VirtualProtect((void*)address, count, PAGE_EXECUTE_READWRITE, &old_protect)) {
        memcpy((void*)address, bytes, count);
        VirtualProtect((void*)address, count, old_protect, &old_protect);
        FlushInstructionCache(GetCurrentProcess(), (void*)address, count);
        return 1;
    }
    protect_error = GetLastError();

    if (WriteProcessMemory(GetCurrentProcess(), (void*)address, bytes, count, &written)
        && written == count) {
        FlushInstructionCache(GetCurrentProcess(), (void*)address, count);
        _snprintf(why, why_size, "VirtualProtect err=%lu, WriteProcessMemory ok",
            (unsigned long)protect_error);
        return 1;
    }
    _snprintf(why, why_size, "VirtualProtect err=%lu, WriteProcessMemory err=%lu",
        (unsigned long)protect_error, (unsigned long)GetLastError());
    return 0;
}

static int witness_write(uintptr_t address, uint8_t value, char* why, size_t why_size)
{
    return witness_write_bytes(address, &value, 1, why, why_size);
}

static int witness_restore(uintptr_t address, uint8_t original)
{
    char why[96];
    why[0] = 0;
    return witness_write(address, original, why, sizeof(why));
}

static LONG CALLBACK witness_veh(EXCEPTION_POINTERS* ep)
{
    uint32_t i;
    uintptr_t at;
    char line[160];

    if (ep == NULL || ep->ExceptionRecord == NULL || ep->ContextRecord == NULL)
        return EXCEPTION_CONTINUE_SEARCH;
    if (ep->ExceptionRecord->ExceptionCode != EXCEPTION_BREAKPOINT)
        return EXCEPTION_CONTINUE_SEARCH;

    at = (uintptr_t)ep->ExceptionRecord->ExceptionAddress;
    for (i = 0; i < g_witness_count; ++i) {
        /* the kernel reports the int3's own address; Eip sits one past it */
        if (at != g_witness[i].address &&
            (uintptr_t)ep->ContextRecord->Eip != g_witness[i].address + 1) {
            continue;
        }
        if (g_witness_then_rewrite) {
            uint8_t replacement[10];
            uint32_t length = build_replacement(g_witness[i].convention,
                g_witness[i].api, replacement);
            char why[96];
            why[0] = 0;
            if (length != 0 && g_trampoline) {
                uintptr_t stub = stub_emit(replacement, length);
                length = (stub != 0)
                    ? build_trampoline(g_witness[i].address, stub, replacement)
                    : 0;
            }
            if (length == 0 || !witness_write_bytes(g_witness[i].address,
                    replacement, length, why, sizeof(why))) {
                _snprintf(line, sizeof(line),
                    "WITNESS %s!%s at %08X: rewrite FAILED (%s)",
                    g_witness[i].module, g_witness[i].function,
                    (unsigned)g_witness[i].address, why);
                log_line(1, line);
                /* fall back to the original byte so the session survives */
                witness_restore(g_witness[i].address, g_witness[i].original);
            } else {
                _snprintf(line, sizeof(line),
                    "WITNESS HIT %s!%s entry %08X thread %lu -- call site DID "
                    "execute; wrapper NOW REWRITTEN as transfer (conv %d, %s), "
                    "this very call runs it",
                    g_witness[i].module, g_witness[i].function,
                    (unsigned)g_witness[i].address,
                    (unsigned long)GetCurrentThreadId(),
                    (int)g_witness[i].convention,
                    g_trampoline ? "5-byte E9 trampoline" : "inline template");
                log_line(3, line);
            }
            ep->ContextRecord->Eip = (DWORD)g_witness[i].address;
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        if (!witness_restore(g_witness[i].address, g_witness[i].original)) {
            _snprintf(line, sizeof(line),
                "WITNESS %s!%s at %08X: restore FAILED, leaving the trap in place",
                g_witness[i].module, g_witness[i].function,
                (unsigned)g_witness[i].address);
            log_line(1, line);
            return EXCEPTION_CONTINUE_SEARCH;
        }
        _snprintf(line, sizeof(line),
            "WITNESS HIT %s!%s entry %08X thread %lu -- call site DID execute",
            g_witness[i].module, g_witness[i].function,
            (unsigned)g_witness[i].address,
            (unsigned long)GetCurrentThreadId());
        log_line(3, line);
        ep->ContextRecord->Eip = (DWORD)g_witness[i].address;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

/* Per-convention templates. `api` is the resolved absolute address; the
 * trailing push+ret transfers to it without needing a rel32 that fits
 * (app-local DLLs rebase every launch) or a writable data slot.
 *
 * The byte counts reproduce what resolve_wrappers.py measured, in its exact
 * terms: convention = (API-entry esp - wrapper-entry esp) / what sits at the
 * API's [esp]. The A template is the subtle one -- the original moves the
 * caller's return address ONTO the swallowed argument's slot (esp+4, R there),
 * it does not keep the entry stack shape (esp+0). Keeping it at +0 leaks four
 * bytes of stack per call and walks esp into the stack: the first pilot run
 * died exactly there, executing a stack address ~40 s in. */
static uint32_t build_replacement(wrapper_pilot_convention_t convention,
    uintptr_t api, uint8_t out[10])
{
    memset(out, 0x90, 10);
    switch (convention) {
    case WRAPPER_PILOT_CONV_B:
        out[0] = 0xFF; out[1] = 0x04; out[2] = 0x24;      /* inc [esp]     */
        break;
    case WRAPPER_PILOT_CONV_A:
        out[0] = 0x58;                                   /* pop eax       */
        out[1] = 0x89; out[2] = 0x04; out[3] = 0x24;      /* mov [esp],eax */
        break;
    case WRAPPER_PILOT_CONV_C:
        out[0] = 0x83; out[1] = 0xC4; out[2] = 0x04;      /* add esp,4     */
        break;
    case WRAPPER_PILOT_CONV_D:
        out[0] = 0x83; out[1] = 0xC4; out[2] = 0x08;      /* add esp,8     */
        break;
    default:
        return 0;
    }
    {
        const uint32_t head = (convention == WRAPPER_PILOT_CONV_A) ? 4 : 3;
        out[head] = 0x68;                                 /* push imm32    */
        memcpy(out + head + 1, &api, 4);
        out[head + 5] = 0xC3;                             /* ret           */
        return head + 6;
    }
}

/* ---- Trampoline mode -------------------------------------------------
 *
 * The inline templates need 9-10 bytes at the entry, but 521 census-verified
 * transfer wrappers have only 5-8 bytes of headroom -- 389 of them exactly 6,
 * the shared "already-adjusted" entry point the recovery doc describes -- and
 * 29 more have a neighbouring named entry inside the template.
 *
 * So put the template on a stub and leave a 5-byte `E9 rel32` at the entry.
 * The template is position-independent (`inc [esp]` / `pop eax; mov [esp],eax`
 * then `push imm32; ret`), so relocating it changes nothing semantically.
 */
#define WRAPPER_PILOT_STUB_SLOT  16u
#define WRAPPER_PILOT_STUB_MAX   1536u

static uint8_t* g_stub_pool;
static uint32_t g_stub_used;

static uintptr_t stub_emit(const uint8_t* code, uint32_t length)
{
    uint8_t* slot;
    if (g_stub_pool == NULL) {
        g_stub_pool = (uint8_t*)VirtualAlloc(NULL,
            WRAPPER_PILOT_STUB_SLOT * WRAPPER_PILOT_STUB_MAX,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        if (g_stub_pool == NULL) return 0;
    }
    if (g_stub_used >= WRAPPER_PILOT_STUB_MAX || length > WRAPPER_PILOT_STUB_SLOT)
        return 0;
    slot = g_stub_pool + (size_t)g_stub_used * WRAPPER_PILOT_STUB_SLOT;
    memset(slot, 0xCC, WRAPPER_PILOT_STUB_SLOT);
    memcpy(slot, code, length);
    ++g_stub_used;
    FlushInstructionCache(GetCurrentProcess(), slot, WRAPPER_PILOT_STUB_SLOT);
    return (uintptr_t)slot;
}

/* E9 rel32 from `entry` to `stub`; rel32 is measured from the end of the jump. */
static uint32_t build_trampoline(uintptr_t entry, uintptr_t stub, uint8_t out[5])
{
    int32_t rel = (int32_t)((intptr_t)stub - (intptr_t)(entry + 5));
    out[0] = 0xE9;
    memcpy(out + 1, &rel, 4);
    return 5;
}

static wchar_t* ini_path(wchar_t* buffer, DWORD cap)
{
    wchar_t* slash;
    if (GetModuleFileNameW(NULL, buffer, cap) == 0) return NULL;
    slash = wcsrchr(buffer, L'\\');
    if (slash == NULL) return NULL;
    wcscpy_s(slash + 1, cap - (DWORD)(slash + 1 - buffer), L"ClientHack.ini");
    return buffer;
}

/* [WrapperPilot] Conventions=B (letters among ABCD, default all) selects
 * which convention groups take part -- the bisect knob for finding a bad
 * group without rebuilding. */
static int group_enabled(const wchar_t* text, char letter)
{
    wchar_t needle[2] = { (wchar_t)letter, 0 };
    return wcsstr(text, needle) != NULL;
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    wchar_t ini[MAX_PATH];
    wchar_t groups[8] = L"ABCD";
    char line[160];
    uint32_t applied = 0;
    uint32_t skipped = 0;
    uint32_t i;
    int witness;
    int census_d;
    uint32_t only_entry;
    uint32_t skip_entry;
    int skip_loaders;
    int trampoline;
    int full_table;
    int dry_run;
    wchar_t only_module_w[64];
    char only_module[64];
    const uint8_t* modes = NULL;
    uint32_t dry_ok = 0, dry_nomodule = 0, dry_noproc = 0, dry_bytes = 0;
    const wrapper_pilot_sample_t* samples = WRAPPER_PILOT_SAMPLES;
    uint32_t sample_count = WRAPPER_PILOT_SAMPLE_COUNT;

    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->patch_apply == NULL || host->dnf_base == 0) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    if (ini_path(ini, MAX_PATH) == NULL ||
        GetPrivateProfileIntW(L"WrapperPilot", L"Enabled", 0, ini) == 0) {
        log_line(3, "disabled ([WrapperPilot] Enabled=0)");
        return CH_STATUS_OK;
    }
    GetPrivateProfileStringW(L"WrapperPilot", L"Conventions", L"ABCD",
        groups, 8, ini);
    witness = GetPrivateProfileIntW(L"WrapperPilot", L"Witness", 0, ini) != 0;
    g_witness_then_rewrite = GetPrivateProfileIntW(L"WrapperPilot",
        L"WitnessThenRewrite", 0, ini) != 0;
    /* CensusD=1 swaps the 20 hand-picked samples for every group-D wrapper the
     * census found, so one session maps which D call sites are reachable at
     * all instead of relying on four lucky picks. */
    census_d = GetPrivateProfileIntW(L"WrapperPilot", L"CensusD", 0, ini) != 0;
    /* Single-variable controls: OnlyEntry arms exactly one entry VA, SkipEntry
     * arms everything except one. Both take the VA as printed in the log
     * (decimal-free hex without 0x), so a suspected entry can be isolated or
     * excluded without rebuilding. */
    /* SkipLoaders=1 excludes every group-D entry the census flagged as a
     * register-loader shape. One of them (0x059D1D26) is live-confirmed lethal;
     * the others share its byte shape and are excluded on that evidence. */
    skip_loaders = GetPrivateProfileIntW(L"WrapperPilot", L"SkipLoaders", 0, ini) != 0;
    /* Trampoline=1 writes a 5-byte E9 at the entry and puts the convention
     * template on an allocated stub, so entries with only 5-8 bytes of
     * headroom become patchable. */
    trampoline = GetPrivateProfileIntW(L"WrapperPilot", L"Trampoline", 0, ini) != 0;
    g_trampoline = trampoline;
    /* FullTable=1 uses every entry the rewrite plan cleared (1158), with the
     * per-entry patch mode the plan decided; Trampoline=1 overrides it and
     * puts every entry on a stub. DryRun=1 performs every check -- module
     * loaded, export resolved, dump bytes still match -- and writes nothing,
     * so coverage can be measured before anything is risked. */
    full_table = GetPrivateProfileIntW(L"WrapperPilot", L"FullTable", 0, ini) != 0;
    dry_run = GetPrivateProfileIntW(L"WrapperPilot", L"DryRun", 0, ini) != 0;
    /* OnlyModule=user32.dll arms just one module, so the full plan can be
     * validated live in batches instead of all at once -- the lesson from the
     * group-D run is that a single crash in a 1000-entry set tells you almost
     * nothing about which entry caused it. */
    GetPrivateProfileStringW(L"WrapperPilot", L"OnlyModule", L"",
        only_module_w, 64, ini);
    only_module[0] = 0;
    if (only_module_w[0] != 0) {
        WideCharToMultiByte(CP_ACP, 0, only_module_w, -1, only_module,
            sizeof(only_module), NULL, NULL);
    }
    only_entry = (uint32_t)GetPrivateProfileIntW(L"WrapperPilot", L"OnlyEntry", 0, ini);
    skip_entry = (uint32_t)GetPrivateProfileIntW(L"WrapperPilot", L"SkipEntry", 0, ini);
    {
        wchar_t hexbuf[16];
        GetPrivateProfileStringW(L"WrapperPilot", L"OnlyEntryHex", L"", hexbuf, 16, ini);
        if (hexbuf[0] != 0) only_entry = (uint32_t)wcstoul(hexbuf, NULL, 16);
        GetPrivateProfileStringW(L"WrapperPilot", L"SkipEntryHex", L"", hexbuf, 16, ini);
        if (hexbuf[0] != 0) skip_entry = (uint32_t)wcstoul(hexbuf, NULL, 16);
    }
    if (census_d) {
        samples = WRAPPER_PILOT_CENSUS_D;
        sample_count = WRAPPER_PILOT_CENSUS_D_COUNT;
        log_line(3, "sample table: full census group-D set");
    } else if (full_table) {
        samples = WRAPPER_PILOT_FULL;
        sample_count = WRAPPER_PILOT_FULL_COUNT;
        modes = WRAPPER_PILOT_FULL_MODE;
        log_line(3, "sample table: full rewrite plan");
    }
    if (dry_run) log_line(3, "DRY RUN: checking only, nothing is written");
    if (witness) {
        g_witness_veh = AddVectoredExceptionHandler(1UL, witness_veh);
        if (g_witness_veh == NULL) {
            log_line(1, "witness mode: AddVectoredExceptionHandler failed; not arming");
            return CH_STATUS_OK;
        }
        log_line(3, g_witness_then_rewrite
            ? "witness mode: entries trap once on first execution, then the "
              "transfer rewrite is applied and that same call runs it"
            : "witness mode: entries trap once on first execution, "
              "then run unmodified");
    }

    for (i = 0; i < sample_count; ++i) {
        const wrapper_pilot_sample_t* sample = &samples[i];
        HMODULE module;
        FARPROC api;
        uint8_t replacement[10];
        uint32_t length;
        ch_patch_request request;
        ch_status status;

        if (!group_enabled(groups, "ABCD"[sample->convention])) {
            continue;
        }
        {
            uint32_t entry_va = (uint32_t)(host->dnf_base + sample->entry_rva);
            if (only_entry != 0 && entry_va != only_entry) continue;
            if (only_module[0] != 0 && _stricmp(sample->module, only_module) != 0)
                continue;
            if (skip_loaders) {
                uint32_t k;
                int is_loader = 0;
                for (k = 0; k < WRAPPER_PILOT_CENSUS_D_LOADER_COUNT; ++k) {
                    if (WRAPPER_PILOT_CENSUS_D_LOADERS[k] == sample->entry_rva) {
                        is_loader = 1;
                        break;
                    }
                }
                if (is_loader) {
                    _snprintf(line, sizeof(line),
                        "skipping %s!%s entry %08X: census register-loader shape",
                        sample->module, sample->function, (unsigned)entry_va);
                    log_line(3, line);
                    continue;
                }
            }
            if (skip_entry != 0 && entry_va == skip_entry) {
                _snprintf(line, sizeof(line), "skipping %s!%s entry %08X by SkipEntryHex",
                    sample->module, sample->function, (unsigned)entry_va);
                log_line(3, line);
                continue;
            }
        }

        module = GetModuleHandleA(sample->module);
        if (module == NULL && dry_run) { ++dry_nomodule; continue; }
        if (module == NULL) {
            _snprintf(line, sizeof(line), "skip %s!%s at %08X: module not loaded",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva));
            log_line(2, line);
            ++skipped;
            continue;
        }
        api = GetProcAddress(module, sample->function);
        if (dry_run) {
            uintptr_t entry = host->dnf_base + sample->entry_rva;
            if (api == NULL || (uintptr_t)api > 0xFFFFFFFFu) { ++dry_noproc; continue; }
            if (*(volatile uint8_t*)entry != sample->expected[0]) { ++dry_bytes; continue; }
            ++dry_ok;
            continue;
        }
        if ((api == NULL || (uintptr_t)api > 0xFFFFFFFFu)
            && !(witness && !g_witness_then_rewrite)) {
            _snprintf(line, sizeof(line), "skip %s!%s at %08X: proc not found",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva));
            log_line(2, line);
            ++skipped;
            continue;
        }
        if (witness) {
            uintptr_t entry = host->dnf_base + sample->entry_rva;
            uint8_t live = 0;
            char why[96];

            if (g_witness_count == WRAPPER_PILOT_WITNESS_MAX) {
                ++skipped;
                continue;
            }
            why[0] = 0;
            live = *(volatile uint8_t*)entry;
            if (live != sample->expected[0]) {
                _snprintf(line, sizeof(line),
                    "witness SKIP %s!%s entry %08X: live byte %02X != dump %02X",
                    sample->module, sample->function, (unsigned)entry,
                    (unsigned)live, (unsigned)sample->expected[0]);
                log_line(2, line);
                ++skipped;
                continue;
            }
            if (!witness_write(entry, 0xCC, why, sizeof(why))) {
                _snprintf(line, sizeof(line),
                    "witness FAILED %s!%s entry %08X: %s",
                    sample->module, sample->function, (unsigned)entry, why);
                log_line(2, line);
                ++skipped;
                continue;
            }
            g_witness[g_witness_count].address = entry;
            g_witness[g_witness_count].original = sample->expected[0];
            g_witness[g_witness_count].module = sample->module;
            g_witness[g_witness_count].function = sample->function;
            g_witness[g_witness_count].convention = sample->convention;
            g_witness[g_witness_count].api = (uintptr_t)api;
            ++g_witness_count;
            _snprintf(line, sizeof(line),
                "witness armed %s!%s entry %08X (caller %08X)%s%s",
                sample->module, sample->function, (unsigned)entry,
                (unsigned)(host->dnf_base + sample->call_site_rva),
                why[0] ? " via " : "", why[0] ? why : "");
            log_line(3, line);
            ++applied;
            continue;
        }
        {
            length = build_replacement(sample->convention, (uintptr_t)api,
                replacement);
            if (length == 0) {
                ++skipped;
                continue;
            }
            if (trampoline || (modes != NULL && modes[i])) {
                uintptr_t entry = host->dnf_base + sample->entry_rva;
                uintptr_t stub = stub_emit(replacement, length);
                if (stub == 0) {
                    _snprintf(line, sizeof(line),
                        "skip %s!%s at %08X: stub pool exhausted",
                        sample->module, sample->function, (unsigned)entry);
                    log_line(2, line);
                    ++skipped;
                    continue;
                }
                length = build_trampoline(entry, stub, replacement);
            }
        }
        memset(&request, 0, sizeof(request));
        request.struct_size = sizeof(request);
        request.target_rva = sample->entry_rva;
        request.expected = sample->expected;
        request.replacement = replacement;
        request.length = length;
        request.name = sample->function;
        status = host->patch_apply(&request);
        if (status == CH_STATUS_OK) {
            _snprintf(line, sizeof(line),
                "patched %s!%s entry %08X conv %d -> api %08X (caller %08X)",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva),
                (int)sample->convention, (unsigned)(uintptr_t)api,
                (unsigned)(host->dnf_base + sample->call_site_rva));
            log_line(3, line);
            ++applied;
        } else {
            /* INCOMPATIBLE_MEMORY here means live bytes differ from the dump:
             * either the wrapper self-decodes at runtime (a finding) or the
             * table is stale. Either way, log and stay hands-off. */
            _snprintf(line, sizeof(line),
                "FAILED %s!%s entry %08X status %d",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva), (int)status);
            log_line(2, line);
            ++skipped;
        }
    }

    if (dry_run) {
        _snprintf(line, sizeof(line),
            "DRY RUN over %u entries: ready %u | module not loaded %u | "
            "export missing %u | entry bytes differ from dump %u",
            (unsigned)sample_count, (unsigned)dry_ok, (unsigned)dry_nomodule,
            (unsigned)dry_noproc, (unsigned)dry_bytes);
        log_line(3, line);
        return CH_STATUS_OK;
    }
    _snprintf(line, sizeof(line), "pilot armed: %u/%u entries %s",
        (unsigned)applied, (unsigned)(applied + skipped),
        witness ? "trapped for witness" : "rewritten");
    log_line(3, line);
    return CH_STATUS_OK;
}
