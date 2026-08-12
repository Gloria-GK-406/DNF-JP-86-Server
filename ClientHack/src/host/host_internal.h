#ifndef CLIENTHACK_HOST_INTERNAL_H
#define CLIENTHACK_HOST_INTERNAL_H

#include <windows.h>

#include "clienthack/plugin_api.h"

#define CH_MAX_PATCHES 128u
#define CH_MAX_HOOKS 64u
#define CH_MAX_SERVICES 32u
#define CH_MAX_PLUGINS 64u
#define CH_MAX_PATCH_BYTES 64u

typedef struct ch_owned_patch {
    uint8_t* target;
    uint8_t original[CH_MAX_PATCH_BYTES];
    uint32_t length;
} ch_owned_patch;

typedef struct ch_owned_hook {
    void* target;
} ch_owned_hook;

typedef struct ch_service_entry {
    char name[64];
    const void* value;
    uint32_t size;
} ch_service_entry;

typedef struct ch_loaded_plugin {
    HMODULE module;
    const ch_plugin_descriptor* descriptor;
} ch_loaded_plugin;

typedef struct ch_host_checkpoint {
    uint32_t patch_count;
    uint32_t hook_count;
    uint32_t service_count;
} ch_host_checkpoint;

extern HMODULE g_ch_module;
extern uintptr_t g_ch_dnf_base;
extern ch_host_api g_ch_host_api;

void ch_log(uint32_t level, const char* component, const char* message);
void ch_log_reset(void);
void ch_fatal(const wchar_t* detail);
int ch_hash_file_sha256(const wchar_t* path, uint8_t digest[32]);
int ch_get_module_directory(wchar_t path[MAX_PATH]);
int ch_wait_for_client_ready(DWORD timeout_ms);
int ch_startup_gate_install(DWORD timeout_ms);
int ch_startup_gate_wait(DWORD timeout_ms);
void ch_startup_gate_release(void);
int ch_wait_for_feature_ready(DWORD timeout_ms);
int ch_repair_client_iat(void);
int ch_host_services_initialize(void);
void ch_host_services_shutdown(void);
void ch_host_checkpoint_capture(ch_host_checkpoint* checkpoint);
ch_status ch_host_rollback_to(const ch_host_checkpoint* checkpoint);
int ch_plugins_load_all(const wchar_t* root_directory);
int ch_plugins_shutdown_all(void);

#endif
