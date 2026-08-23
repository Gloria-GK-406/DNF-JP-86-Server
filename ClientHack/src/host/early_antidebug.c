#include "early_antidebug.h"

#include <stdint.h>
#include <string.h>
#include <intrin.h>

#define CH_THREAD_HIDE_FROM_DEBUGGER 0x11u
#define CH_STATUS_WX86_SINGLE_STEP ((DWORD)0x4000001Eu)

typedef LONG(NTAPI* ch_nt_query_information_thread_t)(
    HANDLE, ULONG, PVOID, ULONG, PULONG);
typedef LONG(NTAPI* ch_nt_set_information_process_t)(
    HANDLE, ULONG, PVOID, ULONG);

typedef struct ch_process_instrumentation_callback_information {
    ULONG_PTR Version;
    ULONG_PTR Reserved;
    PVOID Callback;
} ch_process_instrumentation_callback_information;

static volatile LONG g_early_installed;
static volatile LONG g_early_blocked;
static DWORD g_early_service_id;
static void* g_early_gateway;
static void* g_early_original_transition;
static void* g_early_fs_transition;
static volatile LONG g_early_fs_hooked;
static DWORD g_early_install_thread_id;
static volatile LONG g_early_hidden_at_install = -1;
static volatile DWORD g_early_last_thread_handle;
static volatile DWORD g_early_last_service_return;
static volatile DWORD g_early_last_caller;
static volatile LONG g_monitor_captured;
static volatile DWORD g_monitor_eip;
static volatile DWORD g_monitor_esp;
static volatile DWORD g_monitor_ebp;
static volatile DWORD g_monitor_stack[16];
static volatile DWORD g_monitor_hidden_filetime_low;
static volatile DWORD g_monitor_hidden_filetime_high;
static ch_nt_query_information_thread_t g_early_real_query_thread;
static volatile DWORD g_early_query_caller;
static volatile BYTE g_early_query_code[128];
static volatile LONG g_instrumentation_count;
static volatile DWORD g_instrumentation_pcs[256];
static volatile LONG g_instrumentation_status = (LONG)0xC0000001L;
static volatile DWORD g_monitor_syscall_pcs[64];
static DWORD g_query_syscall_return;
static DWORD g_set_process_syscall_return;
static DWORD g_trace_arm_caller;
static volatile LONG g_trace_enabled;
static volatile LONG g_trace_armed;
static volatile LONG g_trace_captured;
static volatile LONG g_trace_count;
static volatile DWORD g_trace_pcs[8192];
static volatile DWORD g_trace_transition_eip;
static volatile DWORD g_trace_transition_esp;
static volatile DWORD g_trace_transition_stack[8];
static volatile LONG g_set_process_returns;
static volatile DWORD g_set_process_stack[8];
static volatile LONG g_debug_trace_requested;
static volatile LONG g_native_trace_requested;
static volatile LONG* g_debug_handshake_state;
static volatile LONG* g_native_trace_state;
static volatile LONG g_pre_native_breakpoint_armed;
static uint8_t* g_pre_native_breakpoint_address;
static BYTE g_pre_native_original_byte;
static DWORD g_pre_native_old_protection;
static volatile DWORD g_pre_native_call_target;
static volatile BYTE g_pre_native_target_code[64];

static LONG WINAPI ch_instruction_trace_veh(EXCEPTION_POINTERS* exception)
{
    CONTEXT* context;
    DWORD eip;
    BYTE opcode;
    LONG index;
    size_t i;
    if (exception == NULL || exception->ExceptionRecord == NULL ||
        exception->ContextRecord == NULL) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    context = exception->ContextRecord;
    if (exception->ExceptionRecord->ExceptionCode == STATUS_BREAKPOINT &&
        InterlockedCompareExchange(
            &g_pre_native_breakpoint_armed, 0, 0) != 0 &&
        g_pre_native_breakpoint_address != NULL &&
        context->Eip ==
            (DWORD)(uintptr_t)(g_pre_native_breakpoint_address + 1) &&
        __readfsdword(0x24) == g_early_install_thread_id) {
        DWORD ignored;
        *g_pre_native_breakpoint_address = g_pre_native_original_byte;
        FlushInstructionCache(GetCurrentProcess(),
            g_pre_native_breakpoint_address, 1);
        VirtualProtect(g_pre_native_breakpoint_address, 1,
            g_pre_native_old_protection, &ignored);
        InterlockedExchange(&g_pre_native_breakpoint_armed, 0);
        g_pre_native_call_target = context->Eax;
        context->Eip = (DWORD)(uintptr_t)g_pre_native_breakpoint_address;
        if (g_debug_handshake_state != NULL) {
            g_debug_handshake_state[2] = (LONG)context->Eax;
            g_debug_handshake_state[0] = 1;
            while (g_debug_handshake_state[1] == 0) YieldProcessor();
        }
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    if (
        (exception->ExceptionRecord->ExceptionCode != STATUS_SINGLE_STEP &&
         exception->ExceptionRecord->ExceptionCode !=
            CH_STATUS_WX86_SINGLE_STEP) ||
        InterlockedCompareExchange(&g_trace_enabled, 0, 0) == 0 ||
        __readfsdword(0x24) != g_early_install_thread_id) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    eip = context->Eip;
    index = InterlockedIncrement(&g_trace_count) - 1;
    g_trace_pcs[index & 8191] = eip;
    if (g_native_trace_state != NULL) {
        g_native_trace_state[3 + (index & 8191)] = (LONG)eip;
        InterlockedExchange(&g_native_trace_state[0], index + 1);
    }
    opcode = *(const volatile BYTE*)(uintptr_t)eip;
    if (opcode == 0xCB || opcode == 0xCA ||
        opcode == 0xEA || opcode == 0x9A) {
        g_trace_transition_eip = eip;
        g_trace_transition_esp = context->Esp;
        for (i = 0; i < sizeof(g_trace_transition_stack) /
                sizeof(g_trace_transition_stack[0]); ++i) {
            g_trace_transition_stack[i] =
                ((const volatile DWORD*)(uintptr_t)context->Esp)[i];
        }
        InterlockedExchange(&g_trace_captured, 1);
        if (g_native_trace_state != NULL) {
            g_native_trace_state[2] = (LONG)eip;
            InterlockedExchange(&g_native_trace_state[1], 1);
        }
        if (InterlockedCompareExchange(
                &g_debug_trace_requested, 0, 0) != 0 &&
            g_debug_handshake_state != NULL) {
            g_debug_handshake_state[0] = 1;
            while (g_debug_handshake_state[1] == 0) YieldProcessor();
            context->EFlags |= 0x100u;
        } else {
            InterlockedExchange(&g_trace_enabled, 0);
            context->EFlags &= ~0x100u;
        }
    } else {
        context->EFlags |= 0x100u;
    }
    return EXCEPTION_CONTINUE_EXECUTION;
}

static __declspec(naked) void ch_instrumentation_callback(void)
{
    __asm {
        pushfd
        cmp ecx, dword ptr [g_set_process_syscall_return]
        jne not_trace_arm
        push eax
        lock inc dword ptr [g_set_process_returns]
        mov eax, dword ptr [esp + 8]
        mov dword ptr [g_set_process_stack], eax
        mov eax, dword ptr [esp + 0Ch]
        mov dword ptr [g_set_process_stack + 4], eax
        mov eax, dword ptr [esp + 10h]
        mov dword ptr [g_set_process_stack + 8], eax
        mov eax, dword ptr [esp + 14h]
        mov dword ptr [g_set_process_stack + 12], eax
        mov eax, dword ptr [esp + 18h]
        mov dword ptr [g_set_process_stack + 16], eax
        mov eax, dword ptr [esp + 1Ch]
        mov dword ptr [g_set_process_stack + 20], eax
        mov eax, dword ptr [esp + 20h]
        mov dword ptr [g_set_process_stack + 24], eax
        mov eax, dword ptr [esp + 24h]
        mov dword ptr [g_set_process_stack + 28], eax
        cmp dword ptr [esp + 10h], 23h
        jne trace_arm_done
        cmp dword ptr [g_native_trace_requested], 0
        je debugger_trace_arm
        cmp dword ptr [g_set_process_returns], 2
        jl trace_arm_done
        mov dword ptr [g_trace_enabled], 1
        or dword ptr [esp + 4], 100h
        lock inc dword ptr [g_trace_armed]
        jmp trace_arm_done
debugger_trace_arm:
        cmp dword ptr [g_debug_trace_requested], 0
        je trace_arm_done
        mov eax, dword ptr [g_debug_handshake_state]
        test eax, eax
        je trace_arm_done
        mov dword ptr [eax], 1
trace_attach_wait:
        pause
        cmp dword ptr [eax + 4], 0
        je trace_attach_wait
        mov dword ptr [g_trace_enabled], 1
        or dword ptr [esp + 4], 100h
        lock inc dword ptr [g_trace_armed]
trace_arm_done:
        pop eax
not_trace_arm:
        cmp ecx, dword ptr [g_query_syscall_return]
        je skip_record
        push eax
        mov eax, 1
        lock xadd dword ptr [g_instrumentation_count], eax
        and eax, 0FFh
        mov dword ptr [g_instrumentation_pcs + eax * 4], ecx
        pop eax
skip_record:
        popfd
        jmp ecx
    }
}

void ch_early_antidebug_set_debug_handshake(volatile LONG* state)
{
    InterlockedExchangePointer(
        (PVOID volatile*)&g_debug_handshake_state, (PVOID)state);
}

void ch_early_antidebug_set_native_trace_state(volatile LONG* state)
{
    InterlockedExchangePointer(
        (PVOID volatile*)&g_native_trace_state, (PVOID)state);
}

int ch_early_antidebug_arm_pre_native_call(void)
{
    uint8_t* address = (uint8_t*)GetModuleHandleW(NULL) + 0x04ECD542u;
    DWORD protection;
    if (address == NULL || *address != 0xFF ||
        InterlockedCompareExchange(
            &g_pre_native_breakpoint_armed, 0, 0) != 0) {
        return 0;
    }
    if (!VirtualProtect(address, 1, PAGE_EXECUTE_READWRITE, &protection))
        return 0;
    g_pre_native_breakpoint_address = address;
    g_pre_native_original_byte = *address;
    g_pre_native_old_protection = protection;
    *address = 0xCC;
    FlushInstructionCache(GetCurrentProcess(), address, 1);
    InterlockedExchange(&g_pre_native_breakpoint_armed, 1);
    return 1;
}

static void ch_install_instrumentation_callback(HMODULE ntdll)
{
    ch_nt_set_information_process_t set_process;
    ch_process_instrumentation_callback_information info;
    set_process = (ch_nt_set_information_process_t)GetProcAddress(
        ntdll, "NtSetInformationProcess");
    if (set_process == NULL) return;
    g_set_process_syscall_return =
        (DWORD)(uintptr_t)((uint8_t*)(void*)set_process + 12);
    memset(&info, 0, sizeof(info));
    /* On 32-bit/WOW64 Windows 10+, Version carries the x86 callback address. */
    info.Version = (ULONG_PTR)(void*)ch_instrumentation_callback;
    g_instrumentation_status = set_process(
        (HANDLE)(LONG_PTR)-1, 40u, &info, sizeof(info));
}

static LONG NTAPI ch_early_query_thread_hook(
    HANDLE thread, ULONG info_class, PVOID info, ULONG info_length,
    PULONG return_length)
{
    void* caller = _ReturnAddress();
    uintptr_t dnf_base = (uintptr_t)GetModuleHandleW(NULL);
    if (info_class == CH_THREAD_HIDE_FROM_DEBUGGER &&
        (uintptr_t)caller >= dnf_base &&
        (uintptr_t)caller < dnf_base + 0x06693000u &&
        InterlockedCompareExchange(
            (LONG volatile*)&g_early_query_caller,
            (LONG)(uintptr_t)caller, 0) == 0) {
        SIZE_T bytes_read = 0;
        uintptr_t start = (uintptr_t)caller - 64u;
        ReadProcessMemory(
            GetCurrentProcess(), (void*)start,
            (void*)g_early_query_code, sizeof(g_early_query_code),
            &bytes_read);
    }
    return g_early_real_query_thread(
        thread, info_class, info, info_length, return_length);
}

static int ch_install_early_query_hook(uint8_t* query_thread)
{
    uint8_t* trampoline;
    uint8_t jump[5];
    DWORD old_protection;
    intptr_t displacement;
    if (query_thread == NULL || query_thread[0] != 0xB8) return 0;
    trampoline = (uint8_t*)VirtualAlloc(
        NULL, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (trampoline == NULL) return 0;
    memcpy(trampoline, query_thread, 5);
    trampoline[5] = 0xE9;
    displacement = (query_thread + 5) - (trampoline + 10);
    if (displacement < INT32_MIN || displacement > INT32_MAX) return 0;
    {
        int32_t relative = (int32_t)displacement;
        memcpy(trampoline + 6, &relative, sizeof(relative));
    }
    g_early_real_query_thread =
        (ch_nt_query_information_thread_t)(void*)trampoline;
    jump[0] = 0xE9;
    displacement = (uint8_t*)ch_early_query_thread_hook - (query_thread + 5);
    if (displacement < INT32_MIN || displacement > INT32_MAX) return 0;
    {
        int32_t relative = (int32_t)displacement;
        memcpy(jump + 1, &relative, sizeof(relative));
    }
    if (!VirtualProtect(
            query_thread, sizeof(jump), PAGE_EXECUTE_READWRITE,
            &old_protection)) return 0;
    memcpy(query_thread, jump, sizeof(jump));
    FlushInstructionCache(GetCurrentProcess(), query_thread, sizeof(jump));
    return VirtualProtect(
        query_thread, sizeof(jump), old_protection, &old_protection) != 0;
}

static int ch_decode_syscall_stub(
    const uint8_t* stub, DWORD* service_id, uint8_t** gateway)
{
    DWORD decoded_service;
    DWORD decoded_gateway;
    if (stub == NULL || service_id == NULL || gateway == NULL ||
        stub[0] != 0xB8 || stub[5] != 0xBA ||
        stub[10] != 0xFF || stub[11] != 0xD2) {
        return 0;
    }
    memcpy(&decoded_service, stub + 1, sizeof(decoded_service));
    memcpy(&decoded_gateway, stub + 6, sizeof(decoded_gateway));
    if (decoded_gateway == 0) return 0;
    *service_id = decoded_service;
    *gateway = (uint8_t*)(uintptr_t)decoded_gateway;
    return 1;
}

static int ch_decode_transition_slot(uint8_t* gateway, void*** slot)
{
    DWORD decoded_slot;
    if (gateway == NULL || slot == NULL ||
        gateway[0] != 0xFF || gateway[1] != 0x25) {
        return 0;
    }
    memcpy(&decoded_slot, gateway + 2, sizeof(decoded_slot));
    if (decoded_slot == 0) return 0;
    *slot = (void**)(uintptr_t)decoded_slot;
    return 1;
}

/* Preserve the complete machine state for every unrelated syscall. For the
 * exact NtSetInformationThread(ThreadHideFromDebugger) shape, remember the
 * early call site and emulate STATUS_SUCCESS without entering the kernel. */
static __declspec(naked) void ch_early_wow64_transition(void)
{
    __asm {
        pushfd
        cmp eax, dword ptr [g_early_service_id]
        jne pass_through
        cmp dword ptr [esp + 0Ch], CH_THREAD_HIDE_FROM_DEBUGGER
        jne pass_through
        cmp dword ptr [esp + 14h], 0
        jne pass_through

        mov ecx, dword ptr [esp + 08h]
        mov dword ptr [g_early_last_thread_handle], ecx
        mov ecx, dword ptr [esp + 04h]
        mov dword ptr [g_early_last_service_return], ecx
        mov ecx, dword ptr [esp + 18h]
        mov dword ptr [g_early_last_caller], ecx
        lock inc dword ptr [g_early_blocked]
        popfd
        xor eax, eax
        ret

pass_through:
        popfd
        jmp dword ptr [g_early_original_transition]
    }
}

int ch_early_antidebug_install(void)
{
    HMODULE ntdll;
    uint8_t* set_thread;
    ch_nt_query_information_thread_t query_thread;
    uint8_t* gateway;
    void** transition_slot;
    void* original;
    DWORD old_protection;

    if (InterlockedCompareExchange(&g_early_installed, 0, 0) != 0) return 1;
    ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll == NULL) return 0;
    {
        wchar_t handshake[8];
        wchar_t native_trace[8];
        DWORD length = GetEnvironmentVariableW(
            L"CLIENTHACK_DEBUG_HANDSHAKE", handshake,
            sizeof(handshake) / sizeof(handshake[0]));
        InterlockedExchange(&g_debug_trace_requested,
            length == 1 && handshake[0] == L'1' ? 1 : 0);
        length = GetEnvironmentVariableW(
            L"CLIENTHACK_NATIVE_TRACE", native_trace,
            sizeof(native_trace) / sizeof(native_trace[0]));
        InterlockedExchange(&g_native_trace_requested,
            length == 1 && native_trace[0] == L'1' ? 1 : 0);
    }
    g_early_install_thread_id = GetCurrentThreadId();
    g_trace_arm_caller =
        (DWORD)(uintptr_t)GetModuleHandleW(NULL) + 0x04ECD544u;
    if (AddVectoredExceptionHandler(1, ch_instruction_trace_veh) == NULL)
        return 0;
    query_thread = (ch_nt_query_information_thread_t)GetProcAddress(
        ntdll, "NtQueryInformationThread");
    if (query_thread != NULL) {
        BYTE hidden = 0;
        if (query_thread(
                (HANDLE)(LONG_PTR)-2, CH_THREAD_HIDE_FROM_DEBUGGER,
                &hidden, sizeof(hidden), NULL) >= 0) {
            InterlockedExchange(&g_early_hidden_at_install, hidden ? 1 : 0);
        }
    }
    if (!ch_install_early_query_hook((uint8_t*)(void*)query_thread)) return 0;
    g_query_syscall_return = (DWORD)(uintptr_t)((uint8_t*)query_thread + 12);
    ch_install_instrumentation_callback(ntdll);
    set_thread = (uint8_t*)GetProcAddress(ntdll, "NtSetInformationThread");
    if (!ch_decode_syscall_stub(
            set_thread, &g_early_service_id, &gateway) ||
        !ch_decode_transition_slot(gateway, &transition_slot)) {
        return 0;
    }
    original = *transition_slot;
    if (original == NULL || original == (void*)ch_early_wow64_transition)
        return 0;
    if (!VirtualProtect(
            transition_slot, sizeof(*transition_slot), PAGE_READWRITE,
            &old_protection)) {
        return 0;
    }
    g_early_gateway = gateway;
    g_early_original_transition = original;
    InterlockedExchangePointer(
        (PVOID volatile*)transition_slot, (void*)ch_early_wow64_transition);
    if (!VirtualProtect(
            transition_slot, sizeof(*transition_slot), old_protection,
            &old_protection)) {
        return 0;
    }
    g_early_fs_transition = (void*)(uintptr_t)__readfsdword(0xC0);
    if (g_early_fs_transition == original) {
        __writefsdword(
            0xC0, (DWORD)(uintptr_t)(void*)ch_early_wow64_transition);
        InterlockedExchange(&g_early_fs_hooked, 1);
    }
    {
        wchar_t probe[8];
        DWORD length = GetEnvironmentVariableW(
            L"CLIENTHACK_NATIVE_CALL_PROBE", probe,
            sizeof(probe) / sizeof(probe[0]));
        if (length == 1 && probe[0] == L'1' &&
            !ch_early_antidebug_arm_pre_native_call()) {
            return 0;
        }
    }
    InterlockedExchange(&g_early_installed, 1);
    return 1;
}

static DWORD WINAPI ch_early_hide_monitor(void* parameter)
{
    HANDLE target = (HANDLE)parameter;
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    ch_nt_query_information_thread_t query_thread =
        (ch_nt_query_information_thread_t)GetProcAddress(
            ntdll, "NtQueryInformationThread");
    if (query_thread == NULL) return 0;
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    for (;;) {
        BYTE hidden = 0;
        if (query_thread(
                target, CH_THREAD_HIDE_FROM_DEBUGGER, &hidden,
                sizeof(hidden), NULL) >= 0 && hidden) {
            CONTEXT context;
            FILETIME hidden_filetime;
            SIZE_T bytes_read = 0;
            LONG end_index;
            LONG i;
            GetSystemTimeAsFileTime(&hidden_filetime);
            g_monitor_hidden_filetime_low = hidden_filetime.dwLowDateTime;
            g_monitor_hidden_filetime_high = hidden_filetime.dwHighDateTime;
            end_index = InterlockedCompareExchange(
                &g_instrumentation_count, 0, 0);
            for (i = 0; i < 64; ++i) {
                LONG source = end_index - 64 + i;
                g_monitor_syscall_pcs[i] = source >= 0
                    ? g_instrumentation_pcs[source & 0xFF] : 0;
            }
            memset(&context, 0, sizeof(context));
            context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER;
            if (SuspendThread(target) != (DWORD)-1) {
                if (GetThreadContext(target, &context)) {
                    g_monitor_eip = context.Eip;
                    g_monitor_esp = context.Esp;
                    g_monitor_ebp = context.Ebp;
                    ReadProcessMemory(
                        GetCurrentProcess(),
                        (void*)(uintptr_t)context.Esp,
                        (void*)g_monitor_stack, sizeof(g_monitor_stack),
                        &bytes_read);
                    InterlockedExchange(&g_monitor_captured, 1);
                }
                ResumeThread(target);
            }
            break;
        }
        YieldProcessor();
    }
    CloseHandle(target);
    return 0;
}

int ch_early_antidebug_start_monitor(void)
{
    HANDLE target;
    HANDLE monitor;
    if (g_early_install_thread_id == 0) return 0;
    target = OpenThread(
        THREAD_QUERY_INFORMATION | THREAD_SUSPEND_RESUME |
        THREAD_GET_CONTEXT, FALSE, g_early_install_thread_id);
    if (target == NULL) return 0;
    monitor = CreateThread(NULL, 0, ch_early_hide_monitor, target, 0, NULL);
    if (monitor == NULL) {
        CloseHandle(target);
        return 0;
    }
    CloseHandle(monitor);
    return 1;
}

void ch_early_antidebug_snapshot(ch_early_antidebug_state* state)
{
    if (state == NULL) return;
    state->installed = InterlockedCompareExchange(&g_early_installed, 0, 0);
    state->blocked_hide_calls =
        InterlockedCompareExchange(&g_early_blocked, 0, 0);
    state->service_id = g_early_service_id;
    state->gateway = g_early_gateway;
    state->fs_transition = g_early_fs_transition;
    state->fs_transition_hooked =
        InterlockedCompareExchange(&g_early_fs_hooked, 0, 0);
    state->install_thread_id = g_early_install_thread_id;
    state->hidden_at_install =
        InterlockedCompareExchange(&g_early_hidden_at_install, 0, 0);
    state->last_thread_handle = g_early_last_thread_handle;
    state->last_service_return = g_early_last_service_return;
    state->last_caller = g_early_last_caller;
    state->hidden_filetime_low = g_monitor_hidden_filetime_low;
    state->hidden_filetime_high = g_monitor_hidden_filetime_high;
    if (InterlockedCompareExchange(&g_monitor_captured, 0, 0) != 0) {
        size_t i;
        state->last_service_return = g_monitor_eip;
        state->last_caller = g_monitor_esp;
        state->last_thread_handle = g_monitor_ebp;
        for (i = 0; i < sizeof(state->monitor_stack) /
                sizeof(state->monitor_stack[0]); ++i) {
            state->monitor_stack[i] = g_monitor_stack[i];
        }
    }
    state->query_caller = g_early_query_caller;
    state->instrumentation_status = g_instrumentation_status;
    {
        size_t i;
        for (i = 0; i < sizeof(state->query_code); ++i)
            state->query_code[i] = g_early_query_code[i];
        for (i = 0; i < sizeof(state->syscall_return_pcs) /
                sizeof(state->syscall_return_pcs[0]); ++i)
            state->syscall_return_pcs[i] = g_monitor_syscall_pcs[i];
        state->trace_armed =
            InterlockedCompareExchange(&g_trace_armed, 0, 0);
        state->trace_captured =
            InterlockedCompareExchange(&g_trace_captured, 0, 0);
        state->transition_eip = g_trace_transition_eip;
        state->transition_esp = g_trace_transition_esp;
        for (i = 0; i < sizeof(state->transition_stack) /
                sizeof(state->transition_stack[0]); ++i)
            state->transition_stack[i] = g_trace_transition_stack[i];
        {
            LONG end = InterlockedCompareExchange(&g_trace_count, 0, 0);
            for (i = 0; i < sizeof(state->instruction_pcs) /
                    sizeof(state->instruction_pcs[0]); ++i) {
                LONG source = end - (LONG)(sizeof(state->instruction_pcs) /
                    sizeof(state->instruction_pcs[0])) + (LONG)i;
                state->instruction_pcs[i] = source >= 0
                    ? g_trace_pcs[source & 8191] : 0;
            }
        }
        state->set_process_returns =
            InterlockedCompareExchange(&g_set_process_returns, 0, 0);
        for (i = 0; i < sizeof(state->set_process_stack) /
                sizeof(state->set_process_stack[0]); ++i)
            state->set_process_stack[i] = g_set_process_stack[i];
        state->pre_native_call_target = g_pre_native_call_target;
        for (i = 0; i < sizeof(state->pre_native_target_code); ++i)
            state->pre_native_target_code[i] = g_pre_native_target_code[i];
    }
}
