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

/* Phase 1 startup-boundary capture. The startup gate handler runs in the
 * blocked client thread and snapshots the raw call stack into this buffer
 * before it rewrites the temporary CALL return slot. The worker thread later
 * classifies and serializes it. Keep this POD and free of pointers so it can be
 * memcpy'd and inspected without ownership concerns. */
#define CH_GATE_STACK_DWORDS 256u

typedef struct ch_startup_gate_capture {
    volatile LONG valid;
    DWORD thread_id;
    uint32_t eflags;
    uint32_t esp;             /* handler ESP after prologue */
    uint32_t ebp;             /* handler EBP after prologue */
    uint32_t return_slot_va;  /* address of return_address[0] on the stack */
    uint32_t xcookie_return;  /* return_address[0]: XCookie+5 (temp CALL) */
    uint32_t client_return;   /* return_address[1]: caller of XCookie */
    uint32_t stack_count;     /* number of valid entries in stack[] */
    uint32_t stack[CH_GATE_STACK_DWORDS];
} ch_startup_gate_capture;

/* Returns the capture buffer once the handler has marked it valid, else NULL. */
const ch_startup_gate_capture* ch_startup_gate_capture_get(void);

/* One parsed DNF section header, sufficient to classify an RVA. */
typedef struct ch_image_section {
    char name[9];
    uint32_t va;              /* section RVA */
    uint32_t size;            /* virtual size */
    uint32_t characteristics;
} ch_image_section;

/* Pure: index of the section whose [va, va+size) contains rva, or -1. */
int ch_section_index_for_rva(const ch_image_section* sections,
    uint32_t count, uint32_t rva);

/* Serializes the startup-gate capture to a JSON report under <exe>\dumps when
 * CLIENTHACK_STARTUP_TRACE=1. A no-op that returns 1 when the trace is
 * disabled or no capture is available. Runs on the worker thread. */
int ch_startup_trace_write(void);

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
