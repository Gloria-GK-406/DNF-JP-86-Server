#include "host_internal.h"

#include <stdio.h>

HMODULE g_ch_module;
uintptr_t g_ch_dnf_base;

typedef struct rollback_failure_probe {
    int shutdown_called;
} rollback_failure_probe;

int main(void)
{
    uint8_t* memory = (uint8_t*)VirtualAlloc(NULL, 8192, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE);
    rollback_failure_probe probe = { 0 };
    if (memory == NULL) return 1;
    g_ch_dnf_base = (uintptr_t)memory;
    if (!ch_host_services_initialize()) return 1;
    if (g_ch_host_api.service_publish("test.rollback-failure", &probe,
        sizeof(probe)) != CH_STATUS_OK) return 1;
    if (ch_plugins_load_all(CLIENTHACK_ROLLBACK_FAILURE_FIXTURE_DIR) ||
        probe.shutdown_called) {
        fprintf(stderr, "shutdown ran after rollback failure\n");
        return 1;
    }
    puts("rollback failure preserves plugin state until process termination");
    return 0;
}
