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
#define CH_THREAD_CREATE_FLAGS_HIDE_FROM_DEBUGGER 0x00000004u

/* The hardware execution breakpoint used for the 00AB serializer. */
#define CH_00AB_SERIALIZER_RVA        0x01CD1120u
#define CH_EFLAGS_RESUME_FLAG         0x00010000u
#define CH_MAX_TARGET_STEP_LOGS       64

/* SYSTEM_INFORMATION_CLASS: SystemKernelDebuggerInformation. */
#define CH_SystemKernelDebuggerInformation 0x23u

#define CH_NT_SUCCESS(s) (((NTSTATUS_)(s)) >= 0)

#ifndef CLIENTHACK_PROTECT_DRX
#define CLIENTHACK_PROTECT_DRX 1
#endif

typedef NTSTATUS_(NTAPI* NtQueryInformationProcess_t)(
    HANDLE, ULONG, PVOID, ULONG, PULONG);
typedef NTSTATUS_(NTAPI* NtSetInformationThread_t)(
    HANDLE, ULONG, PVOID, ULONG);
typedef NTSTATUS_(NTAPI* NtQuerySystemInformation_t)(
    ULONG, PVOID, ULONG, PULONG);
typedef NTSTATUS_(NTAPI* NtGetContextThread_t)(HANDLE, PCONTEXT);
typedef NTSTATUS_(NTAPI* NtSetContextThread_t)(HANDLE, PCONTEXT);
typedef NTSTATUS_(NTAPI* NtTerminateProcess_t)(HANDLE, NTSTATUS_);
typedef NTSTATUS_(NTAPI* NtCreateThreadEx_t)(
    PHANDLE, ACCESS_MASK, PVOID, HANDLE, PVOID, PVOID, ULONG,
    SIZE_T, SIZE_T, SIZE_T, PVOID);
typedef NTSTATUS_(NTAPI* NtRaiseException_t)(
    PEXCEPTION_RECORD, PCONTEXT, BOOLEAN);
typedef VOID(WINAPI* ExitProcess_t)(UINT);
typedef VOID(NTAPI* RtlExitUserProcess_t)(NTSTATUS_);

typedef struct system_kernel_debugger_information {
    BOOLEAN KernelDebuggerEnabled;
    BOOLEAN KernelDebuggerNotPresent;
} system_kernel_debugger_information;

static const ch_host_api* g_host;
static NtQueryInformationProcess_t g_real_query_process;
static NtSetInformationThread_t g_real_set_thread;
static NtQuerySystemInformation_t g_real_query_system;
static NtGetContextThread_t g_real_get_context;
static NtSetContextThread_t g_real_set_context;
static NtTerminateProcess_t g_real_terminate_process;
static NtCreateThreadEx_t g_real_create_thread_ex;
static NtRaiseException_t g_real_raise_exception;
static ExitProcess_t g_real_exit_process;
static RtlExitUserProcess_t g_real_rtl_exit_user_process;
static PVOID g_exception_handler;
static DWORD g_nt_set_info_service;
static void* g_real_wow64_gate;

static volatile DWORD g_last_exception_code;
static volatile DWORD g_last_exception_address;
static volatile DWORD g_last_exception_eip;
static volatile DWORD g_last_exception_thread_id;
static volatile LONG g_last_exception_handled = -1;
static volatile LONG g_last_exception_first_chance = -1;
static volatile LONG g_target_step_logs;
static volatile LONG g_exception_log_active;
static volatile LONG g_hide_log_active;

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
static volatile LONG g_context_log_events;
#define CH_MAX_LOG_EVENTS 2048

/* Every ch_log_* below is reached from ntdll/kernel32 detours that fire on
 * arbitrary client threads with no ordering. Concurrent _wfopen_s/fclose on the
 * shared CRT stdio stream table from those threads races and faults -- observed
 * as a startup-flaky access violation inside the CRT stream-table walk. Serialize
 * all diagnostic file I/O behind one lock. The CRITICAL_SECTION is recursive on
 * the owning thread, so a detour that re-enters logging on the same thread cannot
 * self-deadlock. */
static CRITICAL_SECTION g_log_lock;
static volatile LONG g_log_lock_ready;

/* The CRT stdio machinery (_wfopen_s/_getstream walking __piob) faults from these
 * detours -- observed crashing inside _wfopen_s even single-threaded under the
 * lock above, i.e. the stdio stream table itself is invalid (freed during CRT
 * teardown while a hook thread still logs, or never initialized in this loaded
 * DLL). The forensic anti-debug log is not needed to run or debug the client, so
 * it is OFF unless CLIENTHACK_ANTIDEBUG_LOG=1 is set in the environment. This
 * removes the crash source without touching any anti-debug behavior. */
static volatile LONG g_log_enabled;

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

/* Open the shared log with the diagnostic lock held. Returns NULL WITHOUT the
 * lock held on failure (or before the lock is initialized); on success the caller
 * owns the lock and must release it via ch_log_unlock_close(). */
static FILE* ch_log_lock_open(void)
{
    wchar_t path[MAX_PATH];
    FILE* f;
    if (!g_log_lock_ready || !g_log_enabled) return NULL;
    EnterCriticalSection(&g_log_lock);
    ch_build_log_path(path, MAX_PATH);
    if (_wfopen_s(&f, path, L"a") != 0 || f == NULL) {
        LeaveCriticalSection(&g_log_lock);
        return NULL;
    }
    return f;
}

static void ch_log_unlock_close(FILE* f)
{
    fclose(f);
    LeaveCriticalSection(&g_log_lock);
}

/* Record one debug-class query and its caller. caller_rva is relative to the
 * DNF module so it maps straight onto the reverse-engineering notes. */
static void ch_log_detection(unsigned info_class, void* caller)
{
    FILE* f;
    uintptr_t rva;
    if (InterlockedIncrement(&g_log_events) > CH_MAX_LOG_EVENTS) return;
    rva = (uintptr_t)caller;
    if (g_host != NULL && g_host->dnf_base != 0 && rva >= g_host->dnf_base) {
        rva -= g_host->dnf_base;
    }
    f = ch_log_lock_open();
    if (f == NULL) return;
    fprintf(f, "{\"class\":\"0x%02X\",\"caller_rva\":\"0x%08zX\"}\n",
        info_class, (size_t)rva);
    ch_log_unlock_close(f);
}

static void ch_log_context(
    const char* api, HANDLE thread, DWORD flags, NTSTATUS_ status,
    const CONTEXT* ctx, void* caller)
{
    FILE* f;
    uintptr_t address = (uintptr_t)caller;
    DWORD thread_id = thread == (HANDLE)(LONG_PTR)-2
        ? GetCurrentThreadId() : GetThreadId(thread);
    LONG context_index = InterlockedIncrement(&g_context_log_events);
    if (context_index > 32 && (context_index % 100) != 0) return;
    if (InterlockedIncrement(&g_log_events) > CH_MAX_LOG_EVENTS) return;
    if (g_host != NULL && g_host->dnf_base != 0 && address >= g_host->dnf_base) {
        address -= g_host->dnf_base;
    }
    f = ch_log_lock_open();
    if (f == NULL) return;
    fprintf(f,
        "{\"api\":\"%s\",\"thread_id\":%lu,\"flags\":\"0x%08lX\","
        "\"debug_requested\":%s,\"status\":\"0x%08lX\","
        "\"dr0\":\"0x%08lX\",\"dr7\":\"0x%08lX\","
        "\"caller_rva\":\"0x%08zX\"}\n",
        api, (unsigned long)thread_id, (unsigned long)flags,
        (flags & CONTEXT_DEBUG_REGISTERS) == CONTEXT_DEBUG_REGISTERS
            ? "true" : "false",
        (unsigned long)status,
        (unsigned long)(ctx != NULL ? ctx->Dr0 : 0),
        (unsigned long)(ctx != NULL ? ctx->Dr7 : 0),
        (size_t)address);
    ch_log_unlock_close(f);
}

static void ch_log_exit(const char* api, NTSTATUS_ status, void* caller)
{
    FILE* f;
    void* frames[16];
    USHORT count;
    USHORT i;
    uintptr_t caller_address = (uintptr_t)caller;
    uintptr_t caller_rva = 0;
    BOOL caller_in_dnf = FALSE;
    if (g_host != NULL && g_host->dnf_base != 0) {
        const IMAGE_DOS_HEADER* dos = (const IMAGE_DOS_HEADER*)g_host->dnf_base;
        if (dos->e_magic == IMAGE_DOS_SIGNATURE) {
            const IMAGE_NT_HEADERS32* nt = (const IMAGE_NT_HEADERS32*)(
                g_host->dnf_base + (uintptr_t)dos->e_lfanew);
            if (nt->Signature == IMAGE_NT_SIGNATURE &&
                caller_address >= g_host->dnf_base &&
                caller_address < g_host->dnf_base + nt->OptionalHeader.SizeOfImage) {
                caller_in_dnf = TRUE;
                caller_rva = caller_address - g_host->dnf_base;
            }
        }
    }
    count = RtlCaptureStackBackTrace(1, 16, frames, NULL);
    f = ch_log_lock_open();
    if (f == NULL) return;
    fprintf(f,
        "{\"api\":\"%s\",\"exit_status\":\"0x%08lX\","
        "\"caller_va\":\"0x%08zX\",\"caller_dnf_rva\":",
        api, (unsigned long)status, (size_t)caller_address);
    if (caller_in_dnf) fprintf(f, "\"0x%08zX\"", (size_t)caller_rva);
    else fputs("null", f);
    fputs(",\"stack\":[", f);
    for (i = 0; i < count; ++i) {
        if (i != 0) fputc(',', f);
        fprintf(f, "\"0x%08zX\"", (size_t)(uintptr_t)frames[i]);
    }
    fprintf(f,
        "],\"last_exception_code\":\"0x%08lX\","
        "\"last_exception_address\":\"0x%08lX\","
        "\"last_exception_eip\":\"0x%08lX\","
        "\"last_exception_thread_id\":%lu,"
        "\"last_exception_handled\":%ld,"
        "\"last_exception_first_chance\":%ld}\n",
        (unsigned long)g_last_exception_code,
        (unsigned long)g_last_exception_address,
        (unsigned long)g_last_exception_eip,
        (unsigned long)g_last_exception_thread_id,
        (long)g_last_exception_handled,
        (long)g_last_exception_first_chance);
    ch_log_unlock_close(f);
}

static void ch_log_thread_create(
    ULONG original_flags, ULONG effective_flags, PVOID start_routine,
    NTSTATUS_ status, void* caller)
{
    FILE* f;
    uintptr_t caller_address = (uintptr_t)caller;
    f = ch_log_lock_open();
    if (f == NULL) return;
    fprintf(f,
        "{\"api\":\"NtCreateThreadEx\","
        "\"original_flags\":\"0x%08lX\","
        "\"effective_flags\":\"0x%08lX\","
        "\"hide_removed\":%s,\"start_routine\":\"0x%08zX\","
        "\"status\":\"0x%08lX\",\"caller_va\":\"0x%08zX\"}\n",
        (unsigned long)original_flags, (unsigned long)effective_flags,
        original_flags != effective_flags ? "true" : "false",
        (size_t)(uintptr_t)start_routine, (unsigned long)status,
        (size_t)caller_address);
    ch_log_unlock_close(f);
}

static void ch_log_thread_hide(
    const char* source, HANDLE thread, void* service_return, void* caller)
{
    FILE* f;
    DWORD thread_id = thread == (HANDLE)(LONG_PTR)-2
        ? GetCurrentThreadId() : GetThreadId(thread);
    if (InterlockedCompareExchange(&g_hide_log_active, 1, 0) != 0) return;
    f = ch_log_lock_open();
    if (f != NULL) {
        fprintf(f,
            "{\"api\":\"ThreadHideFromDebugger\","
            "\"source\":\"%s\",\"blocked\":true,"
            "\"current_thread_id\":%lu,\"target_thread_id\":%lu,"
            "\"thread_handle\":\"0x%08zX\","
            "\"service_return\":\"0x%08zX\","
            "\"caller\":\"0x%08zX\"}\n",
            source, (unsigned long)GetCurrentThreadId(),
            (unsigned long)thread_id, (size_t)(uintptr_t)thread,
            (size_t)(uintptr_t)service_return, (size_t)(uintptr_t)caller);
        ch_log_unlock_close(f);
    }
    InterlockedExchange(&g_hide_log_active, 0);
}

static BOOL ch_decode_wow64_syscall_stub(
    const uint8_t* stub, DWORD* service_id, void** gateway)
{
    DWORD decoded_service;
    DWORD decoded_gateway;
    if (stub == NULL || service_id == NULL || gateway == NULL ||
        stub[0] != 0xB8 || stub[5] != 0xBA ||
        stub[10] != 0xFF || stub[11] != 0xD2) {
        return FALSE;
    }
    memcpy(&decoded_service, stub + 1, sizeof(decoded_service));
    memcpy(&decoded_gateway, stub + 6, sizeof(decoded_gateway));
    if (decoded_gateway == 0) return FALSE;
    *service_id = decoded_service;
    *gateway = (void*)(uintptr_t)decoded_gateway;
    return TRUE;
}

static SIZE_T ch_read_context_words(
    DWORD address, DWORD* words, SIZE_T capacity)
{
    SIZE_T bytes_read = 0;
    if (address == 0 || words == NULL || capacity == 0) return 0;
    if (!ReadProcessMemory(
            GetCurrentProcess(), (const void*)(uintptr_t)address, words,
            capacity * sizeof(*words), &bytes_read)) {
        return 0;
    }
    return bytes_read / sizeof(*words);
}

static SIZE_T ch_walk_ebp_returns(
    DWORD initial_ebp, DWORD* returns, SIZE_T capacity)
{
    DWORD frame = initial_ebp;
    SIZE_T count = 0;
    while (frame != 0 && count < capacity) {
        DWORD pair[2];
        SIZE_T read = ch_read_context_words(frame, pair, 2);
        if (read != 2 || pair[1] == 0) break;
        returns[count++] = pair[1];
        if (pair[0] <= frame || pair[0] - frame > 0x00100000u) break;
        frame = pair[0];
    }
    return count;
}

static void ch_log_target_single_step(
    const EXCEPTION_RECORD* record, const CONTEXT* context,
    DWORD eflags_before)
{
    FILE* f;
    DWORD stack_words[24];
    DWORD frame_returns[16];
    DWORD ecx_words[16];
    SIZE_T stack_count;
    SIZE_T frame_count;
    SIZE_T ecx_count;
    SIZE_T i;
    LONG log_index = InterlockedIncrement(&g_target_step_logs);
    if (log_index > CH_MAX_TARGET_STEP_LOGS || record == NULL || context == NULL)
        return;
    if (InterlockedCompareExchange(&g_exception_log_active, 1, 0) != 0)
        return;

    stack_count = ch_read_context_words(
        context->Esp, stack_words, sizeof(stack_words) / sizeof(stack_words[0]));
    frame_count = ch_walk_ebp_returns(
        context->Ebp, frame_returns,
        sizeof(frame_returns) / sizeof(frame_returns[0]));
    ecx_count = ch_read_context_words(
        context->Ecx, ecx_words, sizeof(ecx_words) / sizeof(ecx_words[0]));

    f = ch_log_lock_open();
    if (f != NULL) {
        fprintf(f,
            "{\"api\":\"VEH_SINGLE_STEP_CONSUMED\","
            "\"thread_id\":%lu,\"target_rva\":\"0x%08lX\","
            "\"exception_address\":\"0x%08zX\","
            "\"eip\":\"0x%08lX\",\"esp\":\"0x%08lX\","
            "\"ebp\":\"0x%08lX\",\"ecx\":\"0x%08lX\","
            "\"eflags_before\":\"0x%08lX\","
            "\"eflags_after\":\"0x%08lX\","
            "\"dr0\":\"0x%08lX\",\"dr6\":\"0x%08lX\","
            "\"dr7\":\"0x%08lX\",\"stack_words\":[",
            (unsigned long)GetCurrentThreadId(),
            (unsigned long)CH_00AB_SERIALIZER_RVA,
            (size_t)(uintptr_t)record->ExceptionAddress,
            (unsigned long)context->Eip, (unsigned long)context->Esp,
            (unsigned long)context->Ebp, (unsigned long)context->Ecx,
            (unsigned long)eflags_before, (unsigned long)context->EFlags,
            (unsigned long)context->Dr0, (unsigned long)context->Dr6,
            (unsigned long)context->Dr7);
        for (i = 0; i < stack_count; ++i) {
            if (i != 0) fputc(',', f);
            fprintf(f, "\"0x%08lX\"", (unsigned long)stack_words[i]);
        }
        fputs("],\"ebp_returns\":[", f);
        for (i = 0; i < frame_count; ++i) {
            if (i != 0) fputc(',', f);
            fprintf(f, "\"0x%08lX\"", (unsigned long)frame_returns[i]);
        }
        fputs("],\"ecx_words\":[", f);
        for (i = 0; i < ecx_count; ++i) {
            if (i != 0) fputc(',', f);
            fprintf(f, "\"0x%08lX\"", (unsigned long)ecx_words[i]);
        }
        fputs("]}\n", f);
        ch_log_unlock_close(f);
    }
    InterlockedExchange(&g_exception_log_active, 0);
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
        ch_log_thread_hide(
            "ntdll_export", thread, NULL, _ReturnAddress());
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

/* True when the thread handle refers to a thread of THIS process (so we only
 * sanitize the protector's self-inspection, never cross-process debugger use).
 * A debugger's own GetThreadContext runs in the debugger's process and goes
 * through the kernel debug object, not through this in-process ntdll hook, so
 * hiding the debug registers here does not disturb the debugger's breakpoints. */
static BOOL ch_thread_is_ours(HANDLE thread)
{
    return thread == (HANDLE)(LONG_PTR)-2 ||
        (thread != NULL && GetProcessIdOfThread(thread) == GetCurrentProcessId());
}

/* frame is the original WOW64 gateway stack:
 *   [0] return to syscall wrapper, [1..4] NtSetInformationThread arguments,
 *   [5] wrapper caller (when the caller uses a conventional four-arg stub). */
static BOOL ch_should_block_wow64_hide(
    DWORD service_id, const DWORD* frame)
{
    HANDLE thread;
    if (frame == NULL || service_id != g_nt_set_info_service ||
        frame[2] != CH_ThreadHideFromDebugger || frame[4] != 0) {
        return FALSE;
    }
    thread = (HANDLE)(uintptr_t)frame[1];
    return ch_thread_is_ours(thread);
}

static BOOL __cdecl ch_handle_wow64_set_thread(const DWORD* frame)
{
    HANDLE thread;
    if (!ch_should_block_wow64_hide(g_nt_set_info_service, frame))
        return FALSE;
    thread = (HANDLE)(uintptr_t)frame[1];
    ch_log_thread_hide(
        "wow64_gate", thread, (void*)(uintptr_t)frame[0],
        (void*)(uintptr_t)frame[5]);
    return TRUE;
}

/* The WOW64 syscall gateway is entered by CALL with EAX holding the service
 * number and the native arguments immediately above the return address. Keep
 * this detour naked so every non-target syscall reaches the original gateway
 * with an identical register/stack image. */
static __declspec(naked) void ch_wow64_gate_detour(void)
{
    __asm {
        cmp eax, dword ptr [g_nt_set_info_service]
        jne pass_through
        pushfd
        pushad
        lea eax, [esp + 36]
        push eax
        call ch_handle_wow64_set_thread
        add esp, 4
        test eax, eax
        jz restore_and_pass
        popad
        popfd
        xor eax, eax
        ret
restore_and_pass:
        popad
        popfd
pass_through:
        jmp dword ptr [g_real_wow64_gate]
    }
}

/* Protect DRx: when the client reads a thread context that includes the debug
 * registers, hand back zeros so its DR-register scan never sees our hardware
 * breakpoints. */
static NTSTATUS_ NTAPI ch_hook_get_context(HANDLE thread, PCONTEXT ctx)
{
    void* caller = _ReturnAddress();
    DWORD original_flags = 0;
    BOOL debug_registers_requested = FALSE;
    BOOL sanitized = FALSE;
    NTSTATUS_ status;
    if (ctx != NULL && ch_thread_is_ours(thread)) {
        original_flags = ctx->ContextFlags;
        debug_registers_requested =
            (original_flags & CONTEXT_DEBUG_REGISTERS) == CONTEXT_DEBUG_REGISTERS;
#if CLIENTHACK_PROTECT_DRX
        ctx->ContextFlags &= ~(DWORD)CONTEXT_DEBUG_REGISTERS;
#endif
        sanitized = TRUE;
    }
    status = g_real_get_context(thread, ctx);
#if CLIENTHACK_PROTECT_DRX
    if (sanitized) ctx->ContextFlags = original_flags;
#endif
    if (sanitized) {
        ch_log_context(
            "NtGetContextThread", thread, original_flags, status, ctx, caller);
    }
#if CLIENTHACK_PROTECT_DRX
    if (CH_NT_SUCCESS(status) && ctx != NULL && debug_registers_requested) {
        ctx->Dr0 = 0; ctx->Dr1 = 0; ctx->Dr2 = 0; ctx->Dr3 = 0;
        ctx->Dr6 = 0; ctx->Dr7 = 0;
    }
#else
    (void)debug_registers_requested;
#endif
    return status;
}

/* Prevent the client from clearing our hardware breakpoints: drop the debug
 * register portion of any self-directed SetContext so it cannot overwrite DRx. */
static NTSTATUS_ NTAPI ch_hook_set_context(HANDLE thread, PCONTEXT ctx)
{
    void* caller = _ReturnAddress();
    DWORD original_flags = 0;
    BOOL sanitized = FALSE;
    NTSTATUS_ status;
    if (ctx != NULL && ch_thread_is_ours(thread)) {
        original_flags = ctx->ContextFlags;
#if CLIENTHACK_PROTECT_DRX
        ctx->ContextFlags &= ~(DWORD)CONTEXT_DEBUG_REGISTERS;
#endif
        sanitized = TRUE;
    }
    status = g_real_set_context(thread, ctx);
#if CLIENTHACK_PROTECT_DRX
    if (sanitized) ctx->ContextFlags = original_flags;
#endif
    if (sanitized) {
        ch_log_context(
            "NtSetContextThread", thread, original_flags, status, ctx, caller);
    }
    return status;
}

static NTSTATUS_ NTAPI ch_hook_terminate_process(
    HANDLE process, NTSTATUS_ exit_status)
{
    if (process == NULL || ch_is_current_process(process)) {
        ch_log_exit("NtTerminateProcess", exit_status, _ReturnAddress());
    }
    return g_real_terminate_process(process, exit_status);
}

static NTSTATUS_ NTAPI ch_hook_create_thread_ex(
    PHANDLE thread_handle, ACCESS_MASK desired_access, PVOID object_attributes,
    HANDLE process, PVOID start_routine, PVOID argument, ULONG create_flags,
    SIZE_T zero_bits, SIZE_T stack_size, SIZE_T maximum_stack_size,
    PVOID attribute_list)
{
    void* caller = _ReturnAddress();
    ULONG effective_flags = create_flags;
    NTSTATUS_ status;
    if (ch_is_current_process(process)) {
        effective_flags &= ~CH_THREAD_CREATE_FLAGS_HIDE_FROM_DEBUGGER;
    }
    status = g_real_create_thread_ex(
        thread_handle, desired_access, object_attributes, process,
        start_routine, argument, effective_flags, zero_bits, stack_size,
        maximum_stack_size, attribute_list);
    if (ch_is_current_process(process)) {
        ch_log_thread_create(
            create_flags, effective_flags, start_routine, status, caller);
    }
    return status;
}

static void ch_capture_exception(
    PEXCEPTION_RECORD record, PCONTEXT context, LONG first_chance)
{
    if (record == NULL) return;
    if (record->ExceptionCode != STATUS_SINGLE_STEP &&
        record->ExceptionCode != STATUS_BREAKPOINT) return;
    g_last_exception_code = record->ExceptionCode;
    g_last_exception_address = (DWORD)(uintptr_t)record->ExceptionAddress;
    g_last_exception_eip = context != NULL ? context->Eip : 0;
    g_last_exception_thread_id = GetCurrentThreadId();
    g_last_exception_handled = -1;
    g_last_exception_first_chance = first_chance;
}

static BOOL ch_prepare_target_single_step_resume(
    const EXCEPTION_RECORD* record, PCONTEXT context)
{
    uintptr_t target;
    if (record == NULL || context == NULL || g_host == NULL ||
        g_host->dnf_base == 0 || record->ExceptionCode != STATUS_SINGLE_STEP) {
        return FALSE;
    }
    target = g_host->dnf_base + CH_00AB_SERIALIZER_RVA;
    if ((uintptr_t)record->ExceptionAddress != target ||
        (uintptr_t)context->Eip != target) {
        return FALSE;
    }
    context->EFlags |= CH_EFLAGS_RESUME_FLAG;
    return TRUE;
}

static LONG CALLBACK ch_exception_veh(PEXCEPTION_POINTERS pointers)
{
    DWORD eflags_before = 0;
    if (pointers != NULL) {
        ch_capture_exception(
            pointers->ExceptionRecord, pointers->ContextRecord, 1);
        if (pointers->ContextRecord != NULL)
            eflags_before = pointers->ContextRecord->EFlags;
        if (ch_prepare_target_single_step_resume(
                pointers->ExceptionRecord, pointers->ContextRecord)) {
            g_last_exception_handled = 1;
            ch_log_target_single_step(
                pointers->ExceptionRecord, pointers->ContextRecord,
                eflags_before);
            return EXCEPTION_CONTINUE_EXECUTION;
        }
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

static NTSTATUS_ NTAPI ch_hook_raise_exception(
    PEXCEPTION_RECORD record, PCONTEXT context, BOOLEAN first_chance)
{
    ch_capture_exception(record, context, first_chance ? 1 : 0);
    return g_real_raise_exception(record, context, first_chance);
}

static VOID NTAPI ch_hook_rtl_exit_user_process(NTSTATUS_ exit_status)
{
    ch_log_exit("RtlExitUserProcess", exit_status, _ReturnAddress());
    g_real_rtl_exit_user_process(exit_status);
}

static VOID WINAPI ch_hook_exit_process(UINT exit_code)
{
    ch_log_exit("ExitProcess", (NTSTATUS_)exit_code, _ReturnAddress());
    g_real_exit_process(exit_code);
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
    HMODULE kernel32;
    void* set_thread_export;
    void* wow64_gate;
    ch_status status;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->hook_create_absolute == NULL || host->log == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    g_host = host;

    /* Ready the diagnostic-log lock before installing any detour, so the first
     * hook hit from a foreign thread already finds it initialized. The forensic
     * file log stays off unless explicitly opted in (see g_log_enabled). */
    {
        wchar_t opt[8];
        DWORD n = GetEnvironmentVariableW(L"CLIENTHACK_ANTIDEBUG_LOG", opt, 8);
        g_log_enabled = (n > 0 && n < 8 && opt[0] == L'1') ? 1 : 0;
    }
    InitializeCriticalSection(&g_log_lock);
    g_log_lock_ready = 1;

    ntdll = GetModuleHandleW(L"ntdll.dll");
    kernel32 = GetModuleHandleW(L"kernel32.dll");
    if (ntdll == NULL || kernel32 == NULL) return CH_STATUS_SYSTEM_ERROR;

    set_thread_export = (void*)GetProcAddress(ntdll, "NtSetInformationThread");
    if (!ch_decode_wow64_syscall_stub(
            (const uint8_t*)set_thread_export,
            &g_nt_set_info_service, &wow64_gate)) {
        host->log(0, "AntiAntiDebug", "failed to resolve WOW64 syscall gateway");
        return CH_STATUS_NOT_FOUND;
    }
    status = host->hook_create_absolute(
        wow64_gate, (void*)ch_wow64_gate_detour, &g_real_wow64_gate);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook WOW64 syscall gateway");
        return status;
    }
    {
        char msg[96];
        _snprintf_s(msg, sizeof(msg), _TRUNCATE,
            "WOW64 ThreadHideFromDebugger filter installed: service=0x%lX gate=%p",
            (unsigned long)g_nt_set_info_service, wow64_gate);
        host->log(3, "AntiAntiDebug", msg);
    }

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
    /* Protect DRx: hide hardware breakpoints from the client's DR-register scan,
     * which otherwise kills the process within seconds of any hardware bp. */
    status = ch_hook_ntdll(ntdll, "NtGetContextThread",
        (void*)ch_hook_get_context, (void**)&g_real_get_context);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtGetContextThread");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtSetContextThread",
        (void*)ch_hook_set_context, (void**)&g_real_set_context);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtSetContextThread");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtTerminateProcess",
        (void*)ch_hook_terminate_process, (void**)&g_real_terminate_process);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtTerminateProcess");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtCreateThreadEx",
        (void*)ch_hook_create_thread_ex, (void**)&g_real_create_thread_ex);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtCreateThreadEx");
        return status;
    }
    status = ch_hook_ntdll(ntdll, "NtRaiseException",
        (void*)ch_hook_raise_exception, (void**)&g_real_raise_exception);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook NtRaiseException");
        return status;
    }
    g_exception_handler = AddVectoredExceptionHandler(1, ch_exception_veh);
    if (g_exception_handler == NULL) {
        host->log(0, "AntiAntiDebug", "failed to install exception recorder");
        return CH_STATUS_SYSTEM_ERROR;
    }
    status = ch_hook_ntdll(ntdll, "RtlExitUserProcess",
        (void*)ch_hook_rtl_exit_user_process,
        (void**)&g_real_rtl_exit_user_process);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook RtlExitUserProcess");
        return status;
    }
    status = ch_hook_ntdll(kernel32, "ExitProcess",
        (void*)ch_hook_exit_process, (void**)&g_real_exit_process);
    if (status != CH_STATUS_OK) {
        host->log(0, "AntiAntiDebug", "failed to hook ExitProcess");
        return status;
    }

    /* Neutralize anti-attach so an external debugger can attach at all. This is
     * the gate that actually kills the process today; the NtQIP spoof above
     * guards the later steady-state polling. */
    ch_install_anti_attach();

    host->log(3, "AntiAntiDebug", "debug-object detection neutralized");
    return CH_STATUS_OK;
}
