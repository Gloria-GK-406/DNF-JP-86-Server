/* Capture real `.vmp1` VM entries and their per-function keys.
 *
 * Why live capture: enumerating the VM statically does not work. The bytes
 * around `.vmp1` are covered by Themida's mutation layer, so pattern matching
 * measures the mutator's output distribution rather than program structure --
 * three separate attempts failed the same way. Bias scanning found 1,199
 * candidate handlers of which only 34% end in a dispatch; shape clustering
 * collapsed 1,199 addresses into 1,107 shapes, so they are not clones of a
 * small handler set; and walking back from the 14,760 direct references to a
 * vm_entry clone found 15 candidate key pushes, exactly one of which produced
 * a VIP inside `.vmp1`. The path into vm_entry is itself a chain of mutated
 * jumps, so there is no stub shape to match.
 *
 * What the VM does give us is a fixed point. All 53 vm_entry clones open with
 * `e8 00 00 00 00` (call $+5, the self-relocation probe), and by then the
 * caller has already pushed the key followed by eight saved dwords, so at
 * clone+0 the key sits at [esp+0x20] -- the save order the earlier analysis
 * measured and found invariant across every entry.
 *
 * So: trap byte 0 of each clone, read the key, and EMULATE the call instead of
 * restoring the byte. `call $+5` is just "push clone+5; go to clone+5", so the
 * trap survives its own hit and every later entry is observed too. One trap
 * covers a whole session instead of a single call -- which matters because one
 * clone serves many VM functions, so a one-shot trap would yield one key.
 *
 * Cost control: an exception per VM invocation is not free and VM functions run
 * in hot paths. Once MaxKeys distinct keys have been seen the traps are removed
 * and the client runs at full speed for the rest of the session.
 *
 * A measurement, never required: off unless [VmEntryTrace] Enabled=1.
 */
#include "clienthack/plugin_api.h"
#include "vm_entry_table.h"

#include <windows.h>
#include <stdio.h>
#include <string.h>

#define KEY_SLOTS 8192u          /* power of two: open-addressed dedup table */

static const ch_host_api* g_host;
static uintptr_t g_site[VM_ENTRY_SITE_COUNT];
static uint32_t g_sites;
static PVOID g_veh;

static uint32_t g_keys[KEY_SLOTS];
static uint8_t g_used[KEY_SLOTS];
static volatile LONG g_distinct;
static volatile LONG g_hits;
static uint32_t g_max_keys = 2048;
static volatile LONG g_disarmed;

static void log_line(uint32_t level, const char* message)
{
    if (g_host != NULL && g_host->log != NULL)
        g_host->log(level, "VmEntryTrace", message);
}

/* Returns 1 the first time a key is seen. Runs inside the exception handler,
 * so it must not allocate and must not take a lock the faulting thread may
 * already hold. */
static int key_is_new(uint32_t key)
{
    uint32_t h = (key * 2654435761u) & (KEY_SLOTS - 1u);
    uint32_t i;
    for (i = 0; i < KEY_SLOTS; ++i) {
        uint32_t s = (h + i) & (KEY_SLOTS - 1u);
        if (!g_used[s]) {
            g_used[s] = 1;
            g_keys[s] = key;
            return 1;
        }
        if (g_keys[s] == key)
            return 0;
    }
    return 0;
}

static int write_byte(uintptr_t at, uint8_t value)
{
    DWORD old;
    SIZE_T n = 0;
    if (VirtualProtect((void*)at, 1, PAGE_EXECUTE_READWRITE, &old)) {
        *(volatile uint8_t*)at = value;
        VirtualProtect((void*)at, 1, old, &old);
        FlushInstructionCache(GetCurrentProcess(), (void*)at, 1);
        return 1;
    }
    if (WriteProcessMemory(GetCurrentProcess(), (void*)at, &value, 1, &n) && n == 1) {
        FlushInstructionCache(GetCurrentProcess(), (void*)at, 1);
        return 1;
    }
    return 0;
}

static void disarm_all(void)
{
    char line[160];
    uint32_t i;
    if (InterlockedExchange(&g_disarmed, 1) != 0)
        return;
    for (i = 0; i < g_sites; ++i)
        write_byte(g_site[i], 0xE8);
    _snprintf(line, sizeof(line),
        "key budget reached: %ld distinct keys over %ld entries; traps removed",
        g_distinct, g_hits);
    log_line(3, line);
}

static LONG CALLBACK vm_entry_veh(EXCEPTION_POINTERS* ep)
{
    uintptr_t at;
    uint32_t i;

    if (ep == NULL || ep->ExceptionRecord == NULL || ep->ContextRecord == NULL)
        return EXCEPTION_CONTINUE_SEARCH;
    if (ep->ExceptionRecord->ExceptionCode != EXCEPTION_BREAKPOINT)
        return EXCEPTION_CONTINUE_SEARCH;

    at = (uintptr_t)ep->ExceptionRecord->ExceptionAddress;
    for (i = 0; i < g_sites; ++i) {
        if (at != g_site[i] && (uintptr_t)ep->ContextRecord->Eip != g_site[i] + 1)
            continue;

        /* The caller pushed the key, then ecx, edx, ebx, eax, esi, EFLAGS, ebp
         * and edi. Eight dwords sit above the key, so it is at [esp+0x20]; the
         * trapped instruction has not executed, so nothing else is on top. */
        {
            uint32_t key = *(volatile uint32_t*)(ep->ContextRecord->Esp + 0x20);
            InterlockedIncrement(&g_hits);
            if (key_is_new(key)) {
                char line[160];
                LONG n = InterlockedIncrement(&g_distinct);
                _snprintf(line, sizeof(line), "VMKEY entry=%08X key=%08X n=%ld",
                    (unsigned)g_site[i], (unsigned)key, n);
                log_line(3, line);
                if ((uint32_t)n >= g_max_keys)
                    disarm_all();
            }
        }

        /* Emulate `call $+5`: push the address of the next instruction and go
         * there, leaving the trap byte in place for the next entry. */
        ep->ContextRecord->Esp -= 4;
        *(volatile uint32_t*)ep->ContextRecord->Esp = (uint32_t)(g_site[i] + 5);
        ep->ContextRecord->Eip = (DWORD)(g_site[i] + 5);
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
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

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    wchar_t ini[MAX_PATH];
    char line[160];
    uint32_t i, armed = 0, mismatch = 0;

    g_host = host;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->dnf_base == 0) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    if (ini_path(ini, MAX_PATH) == NULL ||
        GetPrivateProfileIntW(L"VmEntryTrace", L"Enabled", 0, ini) == 0) {
        log_line(3, "disabled ([VmEntryTrace] Enabled=0)");
        return CH_STATUS_OK;
    }
    g_max_keys = (uint32_t)GetPrivateProfileIntW(L"VmEntryTrace", L"MaxKeys",
                                                 2048, ini);

    g_veh = AddVectoredExceptionHandler(1UL, vm_entry_veh);
    if (g_veh == NULL) {
        log_line(1, "AddVectoredExceptionHandler failed; not arming");
        return CH_STATUS_OK;
    }

    for (i = 0; i < VM_ENTRY_SITE_COUNT; ++i) {
        uintptr_t va = host->dnf_base + VM_ENTRY_SITES[i].rva;
        if (*(volatile uint8_t*)va != VM_ENTRY_SITES[i].original) {
            ++mismatch;
            continue;
        }
        g_site[g_sites] = va;
        if (!write_byte(va, 0xCC)) {
            _snprintf(line, sizeof(line), "arm FAILED at %08X", (unsigned)va);
            log_line(2, line);
            continue;
        }
        ++g_sites;
        ++armed;
    }
    _snprintf(line, sizeof(line),
        "armed %u/%u vm_entry clones (%u byte mismatches), key budget %u",
        armed, (unsigned)VM_ENTRY_SITE_COUNT, mismatch, g_max_keys);
    log_line(3, line);
    return CH_STATUS_OK;
}
