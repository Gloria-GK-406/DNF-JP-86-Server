#include "clienthack/plugin_api.h"

typedef int (__cdecl *fixture_function)(void);

typedef struct lifecycle_probe {
    fixture_function target;
    int shutdown_called;
    int hook_removed_before_shutdown;
} lifecycle_probe;

static const ch_host_api* g_host;
static lifecycle_probe* g_probe;
static void* g_original;

static int __cdecl detour(void)
{
    return 2;
}

static ch_status CH_CALL initialize(const ch_host_api* host)
{
    static const uint8_t expected[] = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };
    ch_hook_request request;
    ch_status status;
    g_host = host;
    g_probe = (lifecycle_probe*)host->service_query("test.lifecycle", sizeof(*g_probe));
    if (g_probe == NULL) return CH_STATUS_NOT_FOUND;
    request.struct_size = sizeof(request);
    request.target_rva = 64;
    request.expected = expected;
    request.expected_length = sizeof(expected);
    request.detour = (void*)(uintptr_t)detour;
    request.original = &g_original;
    request.name = "fixture-hook";
    status = host->hook_create(&request);
    if (status != CH_STATUS_OK) return status;
    if (g_probe->target() != 2) return CH_STATUS_SYSTEM_ERROR;
    return CH_STATUS_SYSTEM_ERROR;
}

static void CH_CALL shutdown(void)
{
    if (g_probe != NULL) {
        g_probe->shutdown_called = 1;
        g_probe->hook_removed_before_shutdown = g_probe->target() == 1;
    }
    g_host = NULL;
}

static const ch_plugin_descriptor descriptor = {
    sizeof(descriptor), CLIENTHACK_ABI_VERSION, CH_PLUGIN_REQUIRED,
    "DisableGameGuard", initialize, shutdown
};

CH_EXPORT const ch_plugin_descriptor* CH_CALL ClientHackPlugin_Query(void)
{
    return &descriptor;
}
