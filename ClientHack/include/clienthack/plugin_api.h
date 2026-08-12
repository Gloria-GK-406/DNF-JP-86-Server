#ifndef CLIENTHACK_PLUGIN_API_H
#define CLIENTHACK_PLUGIN_API_H

#include <stddef.h>
#include <stdint.h>

#include "clienthack/version.h"

#if defined(_MSC_VER)
#define CH_CALL __cdecl
#define CH_EXPORT __declspec(dllexport)
#else
#define CH_CALL
#define CH_EXPORT
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum ch_status {
    CH_STATUS_OK = 0,
    CH_STATUS_INVALID_ARGUMENT = 1,
    CH_STATUS_UNSUPPORTED_ABI = 2,
    CH_STATUS_INCOMPATIBLE_MEMORY = 3,
    CH_STATUS_SYSTEM_ERROR = 4,
    CH_STATUS_ALREADY_EXISTS = 5,
    CH_STATUS_NOT_FOUND = 6
} ch_status;

enum {
    CH_PLUGIN_REQUIRED = 0x00000001u
};

typedef struct ch_patch_request {
    uint32_t struct_size;
    uintptr_t target_rva;
    const uint8_t* expected;
    const uint8_t* replacement;
    uint32_t length;
    const char* name;
} ch_patch_request;

typedef struct ch_hook_request {
    uint32_t struct_size;
    uintptr_t target_rva;
    const uint8_t* expected;
    uint32_t expected_length;
    void* detour;
    void** original;
    const char* name;
} ch_hook_request;

typedef struct ch_host_api {
    uint32_t struct_size;
    uint32_t abi_version;
    uintptr_t dnf_base;
    void (CH_CALL *log)(uint32_t level, const char* component, const char* message);
    ch_status (CH_CALL *patch_apply)(const ch_patch_request* request);
    ch_status (CH_CALL *patch_rollback_all)(void);
    ch_status (CH_CALL *hook_create)(const ch_hook_request* request);
    ch_status (CH_CALL *service_publish)(const char* name, const void* service, uint32_t service_size);
    const void* (CH_CALL *service_query)(const char* name, uint32_t minimum_size);
} ch_host_api;

typedef ch_status (CH_CALL *ch_plugin_initialize_fn)(const ch_host_api* host);
typedef void (CH_CALL *ch_plugin_shutdown_fn)(void);

typedef struct ch_plugin_descriptor {
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t flags;
    const char* name;
    ch_plugin_initialize_fn initialize;
    ch_plugin_shutdown_fn shutdown;
} ch_plugin_descriptor;

typedef const ch_plugin_descriptor* (CH_CALL *ch_plugin_query_fn)(void);

#define CH_PLUGIN_QUERY_EXPORT "ClientHackPlugin_Query"

#ifdef __cplusplus
}
#endif

#endif
