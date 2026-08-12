#include "host_internal.h"

#include "clienthack/gate.h"
#include "clienthack/startup.h"

#include <stdio.h>

HMODULE g_ch_module;
uintptr_t g_ch_dnf_base;

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
    g_ch_dnf_base = (uintptr_t)GetModuleHandleW(NULL);
    if (g_ch_dnf_base == 0 || !ch_startup_gate_install(120000u)) return 0;
    ch_log(3, "host", "startup gate installed");
    if (!ch_startup_gate_wait(120000u)) return 0;
    ch_log(3, "host", "startup gate intercepted the client thread");
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
        DisableThreadLibraryCalls(instance);
        thread = CreateThread(NULL, 0, ch_worker_thread, NULL, 0, NULL);
        if (thread == NULL) return FALSE;
        CloseHandle(thread);
    }
    return TRUE;
}
