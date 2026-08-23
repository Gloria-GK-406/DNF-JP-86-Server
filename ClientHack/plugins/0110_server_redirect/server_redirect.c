#include "clienthack/endpoint_redirect.h"
#include "clienthack/plugin_api.h"

#include <winsock2.h>
#include <windows.h>

#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

enum {
#if defined(CLIENTHACK_CLIENT_A21)
    /* ws2_32!inet_addr, VA 0x0302962C in iat_table_a21.h */
    DNF_INET_ADDR_IAT_RVA = 0x02C2962Cu
#else
    DNF_INET_ADDR_IAT_RVA = 0x023D3630u
#endif
};

typedef unsigned long (WSAAPI *inet_addr_fn)(const char* value);

static inet_addr_fn g_original_inet_addr;
static void (CH_CALL *g_log)(uint32_t level, const char* component, const char* message);
static char g_server_address[16] = "127.0.0.1";
static LONG g_logged_redirect;

static int configuration_path(wchar_t path[MAX_PATH])
{
    HMODULE az = GetModuleHandleW(L"Az.dll");
    wchar_t* separator;
    DWORD length;
    if (az == NULL) return 0;
    length = GetModuleFileNameW(az, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    separator = wcsrchr(path, L'\\');
    if (separator == NULL) return 0;
    return _snwprintf_s(separator + 1,
        MAX_PATH - (size_t)(separator + 1 - path), _TRUNCATE,
        L"ClientHack.ini") > 0;
}

static unsigned long WSAAPI redirect_inet_addr(const char* requested_address)
{
    char selected[32];
    const int replaced = ch_endpoint_redirect_select(1, g_server_address,
        requested_address, selected, sizeof(selected));
    if (replaced && InterlockedCompareExchange(&g_logged_redirect, 1, 0) == 0) {
        if (g_log != NULL) {
            g_log(3, "ServerRedirect", "loopback endpoint replacement was used");
        }
    }
    return g_original_inet_addr != NULL
        ? g_original_inet_addr(replaced ? selected : requested_address)
        : INADDR_NONE;
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    wchar_t path[MAX_PATH];
    wchar_t configured[32];
    HMODULE winsock;
    FARPROC resolved;
    uintptr_t expected;
    uintptr_t replacement;
    ch_patch_request request;
    int enabled;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->patch_apply == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    if (!configuration_path(path)) return CH_STATUS_SYSTEM_ERROR;
    g_log = host->log;
    enabled = GetPrivateProfileIntW(L"ServerRedirect", L"Enabled", 0, path) != 0;
    if (!enabled) {
        if (host->log != NULL) host->log(3, "ServerRedirect", "disabled by configuration");
        return CH_STATUS_OK;
    }
    if (GetPrivateProfileStringW(L"ServerRedirect", L"Address", L"",
        configured, (DWORD)(sizeof(configured) / sizeof(configured[0])), path) == 0 ||
        WideCharToMultiByte(CP_UTF8, 0, configured, -1, g_server_address,
            sizeof(g_server_address), NULL, NULL) <= 0 ||
        !ch_ipv4_address_is_valid(g_server_address)) {
        if (host->log != NULL) {
            host->log(0, "ServerRedirect", "configured IPv4 address is invalid");
        }
        return CH_STATUS_INVALID_ARGUMENT;
    }
    winsock = GetModuleHandleW(L"ws2_32.dll");
    if (winsock == NULL) {
        winsock = LoadLibraryExW(L"ws2_32.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    }
    resolved = winsock != NULL ? GetProcAddress(winsock, "inet_addr") : NULL;
    if (resolved == NULL) return CH_STATUS_SYSTEM_ERROR;
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055 4054)
#endif
    g_original_inet_addr = (inet_addr_fn)(void*)resolved;
    expected = (uintptr_t)(void*)resolved;
    replacement = (uintptr_t)(void*)redirect_inet_addr;
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    request.struct_size = sizeof(request);
    request.target_rva = DNF_INET_ADDR_IAT_RVA;
    request.expected = (const uint8_t*)&expected;
    request.replacement = (const uint8_t*)&replacement;
    request.length = sizeof(uintptr_t);
    request.name = "redirect-loopback-server-endpoint";
    if (host->log != NULL) {
        char message[96];
        _snprintf_s(message, sizeof(message), _TRUNCATE,
            "redirecting 127.0.0.1 to %s", g_server_address);
        host->log(3, "ServerRedirect", message);
    }
    return host->patch_apply(&request);
}
