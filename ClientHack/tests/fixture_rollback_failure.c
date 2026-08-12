#include "clienthack/plugin_api.h"

#include <windows.h>

typedef struct rollback_failure_probe {
    int shutdown_called;
} rollback_failure_probe;

static rollback_failure_probe* g_probe;

static ch_status CH_CALL initialize(const ch_host_api* host)
{
    static const uint8_t expected[] = { 0, 0, 0 };
    static const uint8_t replacement[] = { 1, 2, 3 };
    ch_patch_request request = {
        sizeof(request), 4096u, expected, replacement, sizeof(expected), "rollback-failure"
    };
    ch_status status;
    g_probe = (rollback_failure_probe*)host->service_query(
        "test.rollback-failure", sizeof(*g_probe));
    if (g_probe == NULL) return CH_STATUS_NOT_FOUND;
    status = host->patch_apply(&request);
    if (status != CH_STATUS_OK) return status;
    if (!VirtualFree((void*)(host->dnf_base + 4096u), 4096u, MEM_DECOMMIT)) {
        return CH_STATUS_SYSTEM_ERROR;
    }
    return CH_STATUS_SYSTEM_ERROR;
}

static void CH_CALL shutdown(void)
{
    if (g_probe != NULL) g_probe->shutdown_called = 1;
}

static const ch_plugin_descriptor descriptor = {
    sizeof(descriptor), CLIENTHACK_ABI_VERSION, CH_PLUGIN_REQUIRED,
    "DisableGameGuard", initialize, shutdown
};

CH_EXPORT const ch_plugin_descriptor* CH_CALL ClientHackPlugin_Query(void)
{
    return &descriptor;
}
