#include "host_internal.h"

#include <stdio.h>
#include <string.h>

HMODULE g_ch_module;
uintptr_t g_ch_dnf_base;

typedef int (__cdecl *fixture_function)(void);

typedef struct lifecycle_probe {
    fixture_function target;
    int shutdown_called;
    int hook_removed_before_shutdown;
} lifecycle_probe;

typedef struct service_probe {
    int shutdown_called;
    int payload;
} service_probe;

int main(void)
{
    static const uint8_t function_code[] = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };
    uint8_t* memory = (uint8_t*)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE);
    lifecycle_probe probe;
    int loaded;
    service_probe service;
    if (memory == NULL) return 1;
    memcpy(memory + 64, function_code, sizeof(function_code));
    memset(&probe, 0, sizeof(probe));
    probe.target = (fixture_function)(void*)(memory + 64);
    g_ch_dnf_base = (uintptr_t)memory;
    if (!ch_host_services_initialize()) return 1;
    if (g_ch_host_api.service_publish("test.lifecycle", &probe, sizeof(probe)) != CH_STATUS_OK) return 1;

    loaded = ch_plugins_load_all(CLIENTHACK_LOADER_FIXTURE_DIR);
    if (loaded || !probe.shutdown_called || !probe.hook_removed_before_shutdown ||
        probe.target() != 1) {
        fprintf(stderr, "loader lifecycle rollback order failed\n");
        return 1;
    }
    ch_host_services_shutdown();

    memset(&service, 0, sizeof(service));
    if (!ch_host_services_initialize()) return 1;
    if (g_ch_host_api.service_publish("test.service-probe", &service,
        sizeof(service)) != CH_STATUS_OK) return 1;
    loaded = ch_plugins_load_all(CLIENTHACK_SERVICE_FIXTURE_DIR);
    if (loaded || !service.shutdown_called ||
        g_ch_host_api.service_query("test.dangling", 1) != NULL) {
        fprintf(stderr, "failed plugin service rollback failed\n");
        return 1;
    }
    ch_host_services_shutdown();
    VirtualFree(memory, 0, MEM_RELEASE);
    puts("plugin loader lifecycle test passed");
    return 0;
}
