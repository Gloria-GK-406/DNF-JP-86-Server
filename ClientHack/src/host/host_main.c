#include "host_internal.h"
#include "early_antidebug.h"

#include "clienthack/gate.h"
#include "clienthack/startup.h"

#include <stdio.h>

HMODULE g_ch_module;
uintptr_t g_ch_dnf_base;

#define CH_NATIVE_TRACE_DWORDS (3u + 8192u)

static int ch_native_trace_mapping(void)
{
    wchar_t requested[8];
    HANDLE mapping;
    volatile LONG* state;
    DWORD length = GetEnvironmentVariableW(
        L"CLIENTHACK_NATIVE_TRACE", requested,
        sizeof(requested) / sizeof(requested[0]));
    if (length != 1 || requested[0] != L'1') return 1;
    mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
        0, sizeof(LONG) * CH_NATIVE_TRACE_DWORDS,
        L"Local\\ClientHackNativeTrace");
    if (mapping == NULL) return 0;
    state = (volatile LONG*)MapViewOfFile(
        mapping, FILE_MAP_ALL_ACCESS, 0, 0,
        sizeof(LONG) * CH_NATIVE_TRACE_DWORDS);
    if (state == NULL) {
        CloseHandle(mapping);
        return 0;
    }
    ZeroMemory((void*)state, sizeof(LONG) * CH_NATIVE_TRACE_DWORDS);
    ch_early_antidebug_set_native_trace_state(state);
    return 1;
}

static int ch_debug_attach_handshake(void)
{
    wchar_t requested[8];
    HANDLE mapping;
    volatile LONG* state;
    DWORD length = GetEnvironmentVariableW(
        L"CLIENTHACK_DEBUG_HANDSHAKE", requested,
        sizeof(requested) / sizeof(requested[0]));
    if (length != 1 || requested[0] != L'1') {
        length = GetEnvironmentVariableW(
            L"CLIENTHACK_PRE_NATIVE_HANDSHAKE", requested,
            sizeof(requested) / sizeof(requested[0]));
        if (length != 1 || requested[0] != L'1') return 1;
    }
    mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE,
        0, sizeof(LONG) * 3u, L"Local\\ClientHackDebugHandshake");
    if (mapping == NULL) return 0;
    state = (volatile LONG*)MapViewOfFile(
        mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(LONG) * 3u);
    if (state == NULL) {
        CloseHandle(mapping);
        return 0;
    }
    InterlockedExchange(&state[0], 0);
    InterlockedExchange(&state[1], 0);
    InterlockedExchange(&state[2], 0);
    ch_early_antidebug_set_debug_handshake(state);
    if (!ch_early_antidebug_arm_pre_native_call()) return 0;
    return 1;
}

static const uint8_t g_supported_dnf_hash[32] = {
    0xF7, 0x8F, 0x0A, 0xEA, 0x51, 0x37, 0xDF, 0x06,
    0xB6, 0x3E, 0xD0, 0xF7, 0x47, 0x0B, 0x66, 0xF3,
    0xA2, 0x4D, 0x06, 0x1B, 0x69, 0xD4, 0xDA, 0x13,
    0x75, 0x8E, 0x65, 0x57, 0x4A, 0x7F, 0xC0, 0x4C
};

void ch_fatal(const wchar_t* detail)
{
    wchar_t message[768];
    char narrow_detail[512];
    if (detail != NULL && WideCharToMultiByte(CP_UTF8, 0, detail, -1,
        narrow_detail, sizeof(narrow_detail), NULL, NULL) > 0) {
        ch_log(0, "fatal", narrow_detail);
    } else {
        ch_log(0, "fatal", "ClientHack startup failed");
    }
    _snwprintf_s(message, sizeof(message) / sizeof(message[0]), _TRUNCATE,
        L"ClientHack refused to start.\n\n%s\n\n"
        L"This build only supports DNF.exe SHA-256:\n"
        L"F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C\n\n"
        L"The process will now close.", detail != NULL ? detail : L"Unknown error.");
    MessageBoxW(NULL, message, L"Unsupported DNF client", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
    TerminateProcess(GetCurrentProcess(), ERROR_BAD_EXE_FORMAT);
}

static int ch_continue_startup(void* context)
{
    const wchar_t* root = (const wchar_t*)context;
    ch_early_antidebug_state early;
    g_ch_dnf_base = (uintptr_t)GetModuleHandleW(NULL);
    if (g_ch_dnf_base == 0 || !ch_startup_gate_install(120000u)) return 0;
    ch_log(3, "host", "startup gate installed");
    if (!ch_startup_gate_wait(120000u)) return 0;
    ch_log(3, "host", "startup gate intercepted the client thread");
    ch_early_antidebug_snapshot(&early);
    {
        char message[192];
        char stack_message[1024];
        size_t offset = 0;
        size_t i;
        _snprintf_s(message, sizeof(message), _TRUNCATE,
            "early anti-debug installed=%ld service=0x%lX gate=%p "
            "fs_transition=%p fs_hooked=%ld "
            "install_thread=%lu hidden_at_install=%ld "
            "blocked_hide_calls=%ld last_thread=0x%08lX "
            "service_return=0x%08lX caller=0x%08lX",
            (long)early.installed, (unsigned long)early.service_id,
            early.gateway, early.fs_transition,
            (long)early.fs_transition_hooked,
            (unsigned long)early.install_thread_id,
            (long)early.hidden_at_install, (long)early.blocked_hide_calls,
            (unsigned long)early.last_thread_handle,
            (unsigned long)early.last_service_return,
            (unsigned long)early.last_caller);
        ch_log(3, "AntiAntiDebugEarly", message);
        {
            unsigned __int64 hidden_filetime =
                ((unsigned __int64)early.hidden_filetime_high << 32) |
                early.hidden_filetime_low;
            _snprintf_s(message, sizeof(message), _TRUNCATE,
                "hidden observed filetime_100ns=%I64u",
                hidden_filetime);
            ch_log(3, "AntiAntiDebugEarly", message);
        }
        offset += (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "hidden transition stack:");
        for (i = 0; i < sizeof(early.monitor_stack) /
                sizeof(early.monitor_stack[0]) &&
                offset < sizeof(stack_message); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, " %08lX",
                (unsigned long)early.monitor_stack[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
        offset = (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "pre-native target=%08lX code:",
            (unsigned long)early.pre_native_call_target);
        for (i = 0; i < sizeof(early.pre_native_target_code) &&
                offset < sizeof(stack_message); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, "%02X",
                (unsigned)early.pre_native_target_code[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
        offset = (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "set-process returns=%ld stack:",
            (long)early.set_process_returns);
        for (i = 0; i < sizeof(early.set_process_stack) /
                sizeof(early.set_process_stack[0]); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, " %08lX",
                (unsigned long)early.set_process_stack[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
        _snprintf_s(message, sizeof(message), _TRUNCATE,
            "heavens-gate trace armed=%ld captured=%ld eip=%08lX esp=%08lX",
            (long)early.trace_armed, (long)early.trace_captured,
            (unsigned long)early.transition_eip,
            (unsigned long)early.transition_esp);
        ch_log(3, "AntiAntiDebugEarly", message);
        offset = (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "heavens-gate stack:");
        for (i = 0; i < sizeof(early.transition_stack) /
                sizeof(early.transition_stack[0]); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, " %08lX",
                (unsigned long)early.transition_stack[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
        for (i = 0; i < 4; ++i) {
            size_t j;
            offset = (size_t)_snprintf_s(
                stack_message, sizeof(stack_message), _TRUNCATE,
                "heavens-gate pcs %03u:", (unsigned)(i * 32));
            for (j = 0; j < 32; ++j) {
                int written = _snprintf_s(
                    stack_message + offset, sizeof(stack_message) - offset,
                    _TRUNCATE, " %08lX",
                    (unsigned long)early.instruction_pcs[i * 32 + j]);
                if (written < 0) break;
                offset += (size_t)written;
            }
            ch_log(3, "AntiAntiDebugEarly", stack_message);
        }
        offset = (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "instrumentation status=%08lX recent-pcs:",
            (unsigned long)early.instrumentation_status);
        for (i = 0; i < sizeof(early.syscall_return_pcs) /
                sizeof(early.syscall_return_pcs[0]) &&
                offset < sizeof(stack_message); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, " %08lX",
                (unsigned long)early.syscall_return_pcs[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
        offset = (size_t)_snprintf_s(
            stack_message, sizeof(stack_message), _TRUNCATE,
            "hide-query caller=%08lX live-code:",
            (unsigned long)early.query_caller);
        for (i = 0; i < sizeof(early.query_code) &&
                offset < sizeof(stack_message); ++i) {
            int written = _snprintf_s(
                stack_message + offset, sizeof(stack_message) - offset,
                _TRUNCATE, "%02X", (unsigned)early.query_code[i]);
            if (written < 0) break;
            offset += (size_t)written;
        }
        ch_log(3, "AntiAntiDebugEarly", stack_message);
    }
    if (!ch_repair_client_iat()) return 0;
    if (!ch_wait_for_feature_ready(30000u)) {
        ch_log(0, "host", "feature runtime signatures did not become ready");
        return 0;
    }
    ch_log(3, "host", "feature runtime signatures verified");
    if (!ch_host_services_initialize()) return 0;
    if (!ch_plugins_load_all(root)) {
        (void)ch_plugins_shutdown_all();
        ch_host_services_shutdown();
        return 0;
    }
    ch_log(3, "host", "ordered plugin initialization completed");
    ch_startup_gate_release();
    ch_log(3, "host", "client thread released after startup initialization");
    return 1;
}

static DWORD WINAPI ch_worker_thread(void* parameter)
{
    wchar_t root[MAX_PATH];
    wchar_t dnf_path[MAX_PATH];
    uint8_t actual_hash[32];
    int readable;
    (void)parameter;

    if (!ch_get_module_directory(root) ||
        _snwprintf_s(dnf_path, MAX_PATH, _TRUNCATE, L"%s\\DNF.exe", root) <= 0) {
        ch_fatal(L"Could not resolve the Host directory or DNF.exe path.");
        return 1;
    }

    ch_log_reset();
    ch_log(3, "host", "Az worker started");

    readable = ch_hash_file_sha256(dnf_path, actual_hash);
    ch_log(readable ? 3u : 0u, "gate", readable
        ? "DNF.exe SHA-256 calculated"
        : "DNF.exe could not be read");
    switch (ch_startup_after_identity(readable, actual_hash, g_supported_dnf_hash,
        ch_continue_startup, root)) {
    case CH_STARTUP_COMPLETED:
        return 0;
    case CH_STARTUP_EXE_MISMATCH:
        ch_fatal(L"DNF.exe exists, but its file hash does not match the supported client.");
        return 1;
    case CH_STARTUP_EXE_UNREADABLE:
        ch_fatal(L"DNF.exe is missing or unreadable.");
        return 1;
    default:
        ch_fatal(L"Readiness, IAT repair, or required plugin initialization failed.");
        return 1;
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void* reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        HANDLE thread;
        g_ch_module = instance;
        if (!ch_early_antidebug_install()) return FALSE;
        if (!ch_early_antidebug_start_monitor()) return FALSE;
        if (!ch_native_trace_mapping()) return FALSE;
        if (!ch_debug_attach_handshake()) return FALSE;
        DisableThreadLibraryCalls(instance);
        thread = CreateThread(NULL, 0, ch_worker_thread, NULL, 0, NULL);
        if (thread == NULL) return FALSE;
        CloseHandle(thread);
    }
    return TRUE;
}
