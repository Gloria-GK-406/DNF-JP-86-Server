/*
 * 0005_AntiAntiDebug — keep the packed DNF client debuggable.
 *
 * The Themida-class protector on DNF.exe kills the process (ExitProcess with
 * code 0xDEADC0DE) a few seconds after ANY external debugger attaches.
 *
 * The actual kill mechanism, established empirically (see below), is ANTI-ATTACH,
 * not steady-state detection: the protector rewrites the first bytes of
 * ntdll!DbgUiRemoteBreakin with a JMP into its own section. When a debugger
 * attaches, Windows injects a remote thread that runs DbgUiRemoteBreakin; the
 * redirect sends that thread straight into the protector's kill routine. This
 * fires on the attach itself, before any of our hooks below are reached, which
 * is why clearing PEB.BeingDebugged and hooking NtQueryInformationProcess did
 * NOT help. ch_install_anti_attach() restores the original DbgUiRemoteBreakin
 * prologue from a clean on-disk copy of ntdll and guards it — that is what lets
 * a debugger attach and stay alive (verified: 120s+ attached vs 5s baseline).
 *
 * The NtQueryInformationProcess / NtSetInformationThread /
 * NtQuerySystemInformation hooks are a ScyllaHide-ported spoof kept as defence
 * for the LATER steady-state detection the client may run once logged in (the
 * classic ProcessDebugPort / ProcessDebugObjectHandle / ProcessDebugFlags
 * checks). They were not the gate that killed us; they were not observed to fire
 * before the anti-attach kill, and their debug-class hits are logged (with the
 * caller RVA) to clienthack-antidebug.log so a real watchdog, if one appears,
 * can be located and byte-patched in 0010_DisableGameGuard.
 */

#include "clienthack/plugin_api.h"

#include <windows.h>
#include <intrin.h>
#include <stdio.h>
#include <wchar.h>

#pragma intrinsic(_ReturnAddress)

/* --- NT types and constants (not all present in winternl.h) --- */

typedef LONG NTSTATUS_;

#define CH_STATUS_SUCCESS_NT      ((NTSTATUS_)0x00000000L)
#define CH_STATUS_PORT_NOT_SET    ((NTSTATUS_)0xC0000353L)

/* PROCESSINFOCLASS values used for debugger detection. */
#define CH_ProcessDebugPort           7u
#define CH_ProcessDebugObjectHandle   0x1Eu
#define CH_ProcessDebugFlags          0x1Fu

/* ProcessDebugFlags reports 1 (PROCESS_DEBUG_INHERIT) when NOT debugged. */
#define CH_PROCESS_DEBUG_INHERIT      1u

/* THREADINFOCLASS: ThreadHideFromDebugger. */
#define CH_ThreadHideFromDebugger     0x11u

/* SYSTEM_INFORMATION_CLASS: SystemKernelDebuggerInformation. */
#define CH_SystemKernelDebuggerInformation 0x23u

#define CH_NT_SUCCESS(s) (((NTSTATUS_)(s)) >= 0)

typedef NTSTATUS_(NTAPI* NtQueryInformationProcess_t)(
    HANDLE, ULONG, PVOID, ULONG, PULONG);
typedef NTSTATUS_(NTAPI* NtSetInformationThread_t)(
    HANDLE, ULONG, PVOID, ULONG);
typedef NTSTATUS_(NTAPI* NtQuerySystemInformation_t)(
    ULONG, PVOID, ULONG, PULONG);

typedef struct system_kernel_debugger_information {
    BOOLEAN KernelDebuggerEnabled;
    BOOLEAN KernelDebuggerNotPresent;
} system_kernel_debugger_information;

static const ch_host_api* g_host;
static NtQueryInformationProcess_t g_real_query_process;
static NtSetInformationThread_t g_real_set_thread;
static NtQuerySystemInformation_t g_real_query_system;

/* --- anti-attach guard state --- */

#define CH_ANTIATTACH_RESTORE_BYTES 16u

typedef struct ch_attach_stub {
    uint8_t* live;                              /* patched export in live ntdll */
    uint8_t  clean[CH_ANTIATTACH_RESTORE_BYTES]; /* original prologue bytes */
} ch_attach_stub;

static ch_attach_stub g_attach_stubs[2];
static uint32_t g_attach_stub_count;

/* --- watchdog-locating log --- */

static volatile LONG g_log_events;
#define CH_MAX_LOG_EVENTS 128

static void ch_build_log_path(wchar_t* out, size_t cap)
{
    DWORD n = GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t* slash = NULL;
    size_t i;
    if (n == 0 || n >= cap) { wcscpy_s(out, cap, L"clienthack-antidebug.log"); return; }
    for (i = 0; out[i]; ++i)
        if (out[i] == L'\\' || out[i] == L'/') slash = out + i;
    if (slash == NULL) { wcscpy_s(out, cap, L"clienthack-antidebug.log"); return; }
    slash[1] = 0;
    wcscat_s(out, cap, L"clienthack-antidebug.log");
}

/* Record one debug-class query and its caller. caller_rva is relative to the
 * DNF module so it maps straight onto the reverse-engineering notes. */
static void ch_log_detection(unsigned info_class, void* caller)
{
    wchar_t path[MAX_PATH];
    FILE* f;
    uintptr_t rva;
    if (InterlockedIncrement(&g_log_events) > CH_MAX_LOG_EVENTS) return;
    rva = (uintptr_t)caller;
    if (g_host != NULL && g_host->dnf_base != 0 && rva >= g_host->dnf_base) {
        rva -= g_host->dnf_base;
    }
    ch_build_log_path(path, MAX_PATH);
    if (_wfopen_s(&f, path, L"a") != 0 || f == NULL) return;
    fprintf(f, "{\"class\":\"0x%02X\",\"caller_rva\":\"0x%08zX\"}\n",
        info_class, (size_t)rva);
    fclose(f);
}

/* --- detours (ScyllaHide-faithful, scoped to detection classes) --- */

static BOOL ch_is_current_process(HANDLE h)
{
    return h == (HANDLE)(LONG_PTR)-1 ||
        (h != NULL && GetProcessId(h) == GetCurrentProcessId());
}

static NTSTATUS_ NTAPI ch_hook_query_process(
    HANDLE process, ULONG info_class, PVOID info, ULONG info_len, PULONG ret_len)
{
    void* caller = _ReturnAddress();
    NTSTATUS_ status;

    /* ProcessDebugObjectHandle: validate via ProcessDebugPort, then report the
     * "no debug object" answer. */
    if (info_class == CH_ProcessDebugObjectHandle && info != NULL &&
        info_len == sizeof(HANDLE) && ch_is_current_process(process)) {
        ch_log_detection(info_class, caller);
        status = g_real_query_process(
            process, CH_ProcessDebugPort, info, sizeof(HANDLE), ret_len);
        if (!CH_NT_SUCCESS(status)) return status;
        *(PHANDLE)info = NULL;
        if (ret_len != NULL) *ret_len = sizeof(HANDLE);
        return CH_STATUS_PORT_NOT_SET;
    }

    if ((info_class == CH_ProcessDebugPort || info_class == CH_ProcessDebugFlags) &&
        ch_is_current_process(process)) {
        ch_log_detection(info_class, caller);
        status = g_real_query_process(process, info_class, info, info_len, ret_len);
        if (CH_NT_SUCCESS(status) && info != NULL && info_len != 0) {
            ULONG backup = (ret_len != NULL) ? *ret_len : 0;
            if (info_class == CH_ProcessDebugPort) {
                *(PHANDLE)info = NULL;
            } else { /* ProcessDebugFlags */
                *(PULONG)info = CH_PROCESS_DEBUG_INHERIT;
            }
            if (ret_len != NULL) *ret_len = backup;
        }
        return status;
    }

    return g_real_query_process(process, info_class, info, info_len, ret_len);
}

static NTSTATUS_ NTAPI ch_hook_set_thread(
    HANDLE thread, ULONG info_class, PVOID info, ULONG info_len)
{
    /* Swallow ThreadHideFromDebugger so the client cannot detach its threads
     * from our debugger. */
    if (info_class == CH_ThreadHideFromDebugger && info_len == 0 &&
        (thread == (HANDLE)(LONG_PTR)-2 ||
         (thread != NULL && GetProcessIdOfThread(thread) == GetCurrentProcessId()))) {
        return CH_STATUS_SUCCESS_NT;
    }
    return g_real_set_thread(thread, info_class, info, info_len);
}

static NTSTATUS_ NTAPI ch_hook_query_system(
    ULONG info_class, PVOID info, ULONG info_len, PULONG ret_len)
{
    NTSTATUS_ status = g_real_query_system(info_class, info, info_len, ret_len);
    if (info_class == CH_SystemKernelDebuggerInformation &&
        CH_NT_SUCCESS(status) && info != NULL &&
        info_len >= sizeof(system_kernel_debugger_information)) {
        system_kernel_debugger_information* sk =
            (system_kernel_debugger_information*)info;
        sk->KernelDebuggerEnabled = FALSE;
        sk->KernelDebuggerNotPresent = TRUE;
    }
    return status;
}

/* --- anti-attach: keep DbgUiRemoteBreakin intact ---
 *
 * The protector rewrites the first bytes of ntdll!DbgUiRemoteBreakin with a JMP
 * into its own section, so the remote thread a debugger injects on attach lands
 * in the protector's kill routine (ExitProcess 0xDEADC0DE) instead of hitting a
 * breakpoint. We restore the original prologue from a clean, image-aligned copy
 * of ntdll mapped from disk, and keep restoring it from a guard thread in case
 * the protector re-applies the patch. */

/* Overwrite `len` bytes at `dst` regardless of page protection. */
static void ch_write_code(uint8_t* dst, const uint8_t* src, SIZE_T len)
{
    DWORD old;
    if (!VirtualProtect(dst, len, PAGE_EXECUTE_READWRITE, &old)) return;
    memcpy(dst, src, len);
    VirtualProtect(dst, len, old, &old);
    FlushInstructionCache(GetCurrentProcess(), dst, len);
}

/* Map the on-disk copy of the running ntdll as raw file bytes. Returns the base
 * of the mapped file (caller leaks the view for process lifetime) or NULL. This
 * is deliberately a fresh file mapping, not LoadLibraryEx: the module is already
 * loaded, so LoadLibraryEx would hand back the live (already-patched) image. */
static uint8_t* ch_map_clean_ntdll_file(HMODULE live_ntdll)
{
    wchar_t path[MAX_PATH];
    HANDLE file, mapping;
    uint8_t* view;
    if (GetModuleFileNameW(live_ntdll, path, MAX_PATH) == 0) return NULL;
    file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return NULL;
    mapping = CreateFileMappingW(file, NULL, PAGE_READONLY, 0, 0, NULL);
    CloseHandle(file);
    if (mapping == NULL) return NULL;
    view = (uint8_t*)MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, 0);
    CloseHandle(mapping);
    return view;
}

/* Convert an RVA to a raw file offset using the PE section table of a raw file
 * mapping. Returns 0 (invalid) if the RVA is not inside any section. */
static uint32_t ch_rva_to_offset(const uint8_t* file_base, uintptr_t rva)
{
    const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)file_base;
    const IMAGE_NT_HEADERS32* nt;
    const IMAGE_SECTION_HEADER* sec;
    WORD i;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    nt = (const IMAGE_NT_HEADERS32*)(file_base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
    sec = IMAGE_FIRST_SECTION(nt);
    for (i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
        uint32_t va = sec[i].VirtualAddress;
        uint32_t vs = sec[i].Misc.VirtualSize;
        if (rva >= va && rva < va + vs) {
            return sec[i].PointerToRawData + (uint32_t)(rva - va);
        }
    }
    return 0;
}

/* Register one attach-related stub: record the live export and capture the
 * original prologue bytes from the clean on-disk image at the same RVA. */
static void ch_register_attach_stub(
    HMODULE live_ntdll, const uint8_t* clean_file, const char* name)
{
    uint8_t* live = (uint8_t*)GetProcAddress(live_ntdll, name);
    uintptr_t rva;
    uint32_t offset;
    char msg[96];
    if (live == NULL || clean_file == NULL || g_attach_stub_count >= 2) return;
    rva = (uintptr_t)live - (uintptr_t)live_ntdll;
    offset = ch_rva_to_offset(clean_file, rva);
    if (offset == 0) return;
    g_attach_stubs[g_attach_stub_count].live = live;
    memcpy(g_attach_stubs[g_attach_stub_count].clean,
        clean_file + offset, CH_ANTIATTACH_RESTORE_BYTES);
    if (g_host != NULL) {
        _snprintf_s(msg, sizeof(msg), _TRUNCATE,
            "%s live=%02X%02X%02X%02X%02X clean=%02X%02X%02X%02X%02X", name,
            live[0], live[1], live[2], live[3], live[4],
            g_attach_stubs[g_attach_stub_count].clean[0],
            g_attach_stubs[g_attach_stub_count].clean[1],
            g_attach_stubs[g_attach_stub_count].clean[2],
            g_attach_stubs[g_attach_stub_count].clean[3],
            g_attach_stubs[g_attach_stub_count].clean[4]);
        g_host->log(3, "AntiAntiDebug", msg);
    }
    ++g_attach_stub_count;
}

/* Restore any stub whose live prologue no longer matches the clean bytes.
 * Returns the number of stubs it had to repair. */
static uint32_t ch_restore_attach_stubs(void)
{
    uint32_t i, repaired = 0;
    for (i = 0; i < g_attach_stub_count; ++i) {
        ch_attach_stub* s = &g_attach_stubs[i];
        if (memcmp(s->live, s->clean, CH_ANTIATTACH_RESTORE_BYTES) != 0) {
            ch_write_code(s->live, s->clean, CH_ANTIATTACH_RESTORE_BYTES);
            ++repaired;
        }
    }
    return repaired;
}

static DWORD WINAPI ch_attach_guard(LPVOID unused)
{
    (void)unused;
    for (;;) {
        ch_restore_attach_stubs();
        Sleep(200);
    }
}

/* Capture clean prologues and install the guard. Safe to call once at init. */
static void ch_install_anti_attach(void)
{
    HMODULE live_ntdll = GetModuleHandleW(L"ntdll.dll");
    uint8_t* clean_file;
    HANDLE thread;
    uint32_t repaired;
    if (live_ntdll == NULL) return;
    clean_file = ch_map_clean_ntdll_file(live_ntdll);
    if (clean_file == NULL) {
        if (g_host != NULL) g_host->log(2, "AntiAntiDebug",
            "anti-attach unavailable: clean ntdll file mapping failed");
        return;
    }
    ch_register_attach_stub(live_ntdll, clean_file, "DbgUiRemoteBreakin");
    ch_register_attach_stub(live_ntdll, clean_file, "DbgBreakPoint");
    repaired = ch_restore_attach_stubs();
    thread = CreateThread(NULL, 0, ch_attach_guard, NULL, 0, NULL);
    if (thread != NULL) CloseHandle(thread);
    if (g_host != NULL) {
        char msg[80];
        _snprintf_s(msg, sizeof(msg), _TRUNCATE,
            "anti-attach installed: %u stub(s), %u repaired at init",
            g_attach_stub_count, repaired);
        g_host->log(3, "AntiAntiDebug", msg);
    }
}

/* --- install --- */

static ch_status ch_hook_ntdll(
    HMODULE ntdll, const char* name, void* detour, void** real)
{
    void* target = (void*)GetProcAddress(ntdll, name);
    if (target == NULL) return CH_STATUS_NOT_FOUND;
    *real = NULL;
    return g_host->hook_create_absolute(target, detour, real);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    HMODULE ntdll;
    ch_status status;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->hook_create_absolute == NULL || host->log == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    g_host = host;

    ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll == NULL) return CH_STATUS_SYSTEM_ERROR;

    status = ch_hook_ntdll(ntdll, "NtQueryInformationProcess",
        (void*)ch_hook_query_process, (void**)&g_real_query_process);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtQueryInformationProcess");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtSetInformationThread",
        (void*)ch_hook_set_thread, (void**)&g_real_set_thread);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtSetInformationThread");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtQuerySystemInformation",
        (void*)ch_hook_query_system, (void**)&g_real_query_system);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtQuerySystemInformation");
        return status;
    }

    /* Neutralize anti-attach so an external debugger can attach at all. This is
     * the gate that actually kills the process today; the NtQIP spoof above
     * guards the later steady-state polling. */
    ch_install_anti_attach();

    host->log(3, "AntiAntiDebug", "debug-object detection neutralized");
    return CH_STATUS_OK;
}
