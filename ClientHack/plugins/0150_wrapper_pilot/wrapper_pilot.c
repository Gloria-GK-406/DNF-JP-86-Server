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

#include <windows.h>
#include <stdio.h>
#include <string.h>

static const ch_host_api* g_host;

static void log_line(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "WrapperPilot", message);
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

    for (i = 0; i < WRAPPER_PILOT_SAMPLE_COUNT; ++i) {
        const wrapper_pilot_sample_t* sample = &WRAPPER_PILOT_SAMPLES[i];
        HMODULE module;
        FARPROC api;
        uint8_t replacement[10];
        uint32_t length;
        ch_patch_request request;
        ch_status status;

        if (!group_enabled(groups, "ABCD"[sample->convention])) {
            continue;
        }

        module = GetModuleHandleA(sample->module);
        if (module == NULL) {
            _snprintf(line, sizeof(line), "skip %s!%s at %08X: module not loaded",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva));
            log_line(2, line);
            ++skipped;
            continue;
        }
        api = GetProcAddress(module, sample->function);
        if (api == NULL || (uintptr_t)api > 0xFFFFFFFFu) {
            _snprintf(line, sizeof(line), "skip %s!%s at %08X: proc not found",
                sample->module, sample->function,
                (unsigned)(host->dnf_base + sample->entry_rva));
            log_line(2, line);
            ++skipped;
            continue;
        }
        length = build_replacement(sample->convention, (uintptr_t)api,
            replacement);
        if (length == 0) {
            ++skipped;
            continue;
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

    _snprintf(line, sizeof(line), "pilot armed: %u/%u entries rewritten",
        (unsigned)applied, (unsigned)(applied + skipped));
    log_line(3, line);
    return CH_STATUS_OK;
}
