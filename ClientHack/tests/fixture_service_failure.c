#include "clienthack/plugin_api.h"

typedef struct service_probe {
    int shutdown_called;
    int payload;
} service_probe;

static service_probe* g_probe;

static ch_status CH_CALL initialize(const ch_host_api* host)
{
    ch_status status;
    g_probe = (service_probe*)host->service_query("test.service-probe", sizeof(*g_probe));
    if (g_probe == NULL) return CH_STATUS_NOT_FOUND;
    status = host->service_publish("test.dangling", &g_probe->payload,
        sizeof(g_probe->payload));
    if (status != CH_STATUS_OK) return status;
    return CH_STATUS_SYSTEM_ERROR;
}

static void CH_CALL shutdown(void)
{
    if (g_probe != NULL) g_probe->shutdown_called = 1;
}

static const ch_plugin_descriptor descriptor = {
    sizeof(descriptor), CLIENTHACK_ABI_VERSION, 0,
    "ServiceFailureFixture", initialize, shutdown
};

CH_EXPORT const ch_plugin_descriptor* CH_CALL ClientHackPlugin_Query(void)
{
    return &descriptor;
}
