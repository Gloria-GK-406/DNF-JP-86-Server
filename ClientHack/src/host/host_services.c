#include "host_internal.h"

#include "MinHook.h"

#include <stdio.h>
#include <string.h>
#include <wchar.h>

static CRITICAL_SECTION g_lock;
static int g_initialized;
static ch_owned_patch g_patches[CH_MAX_PATCHES];
static uint32_t g_patch_count;
static ch_owned_hook g_hooks[CH_MAX_HOOKS];
static uint32_t g_hook_count;
static ch_service_entry g_services[CH_MAX_SERVICES];
static uint32_t g_service_count;

static int ch_log_path(wchar_t path[MAX_PATH])
{
    wchar_t* separator;
    DWORD length = GetModuleFileNameW(g_ch_module, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 0;
    separator = wcsrchr(path, L'\\');
    if (separator == NULL) return 0;
    return _snwprintf_s(separator + 1,
        MAX_PATH - (size_t)(separator + 1 - path), _TRUNCATE,
        L"ClientHack.log") > 0;
}

void ch_log_reset(void)
{
    wchar_t path[MAX_PATH];
    HANDLE file;
    if (!ch_log_path(path)) return;
    file = CreateFileW(path, GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
}

void ch_log(uint32_t level, const char* component, const char* message)
{
    char line[512];
    wchar_t path[MAX_PATH];
    HANDLE file;
    DWORD written;
    size_t length;
    _snprintf_s(line, sizeof(line), _TRUNCATE, "[ClientHack:%lu:%s] %s\r\n",
        (unsigned long)level, component != NULL ? component : "host",
        message != NULL ? message : "");
    OutputDebugStringA(line);
    if (!ch_log_path(path)) return;
    file = CreateFileW(path, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    length = strlen(line);
    (void)WriteFile(file, line, (DWORD)length, &written, NULL);
    CloseHandle(file);
}

static ch_status CH_CALL ch_patch_apply_impl(const ch_patch_request* request)
{
    uint8_t* target;
    DWORD old_protection;
    ch_owned_patch* record;
    MEMORY_BASIC_INFORMATION memory;

    if (request == NULL || request->struct_size < sizeof(*request) ||
        request->expected == NULL || request->replacement == NULL ||
        request->length == 0 || request->length > CH_MAX_PATCH_BYTES ||
        request->target_rva > 0x06693000u - request->length) {
        return CH_STATUS_INVALID_ARGUMENT;
    }
    target = (uint8_t*)(g_ch_dnf_base + request->target_rva);
    if (VirtualQuery(target, &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT ||
        (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0 ||
        (uintptr_t)target + request->length >
            (uintptr_t)memory.BaseAddress + memory.RegionSize) {
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    }
    EnterCriticalSection(&g_lock);
    if (memcmp(target, request->replacement, request->length) == 0) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_OK;
    }
    if (memcmp(target, request->expected, request->length) != 0) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    }
    if (g_patch_count == CH_MAX_PATCHES) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_SYSTEM_ERROR;
    }
    record = &g_patches[g_patch_count];
    if (!VirtualProtect(target, request->length, PAGE_EXECUTE_READWRITE, &old_protection)) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_SYSTEM_ERROR;
    }
    record->target = target;
    record->length = request->length;
    memcpy(record->original, target, request->length);
    memcpy(target, request->replacement, request->length);
    FlushInstructionCache(GetCurrentProcess(), target, request->length);
    VirtualProtect(target, request->length, old_protection, &old_protection);
    ++g_patch_count;
    LeaveCriticalSection(&g_lock);
    return CH_STATUS_OK;
}

static ch_status CH_CALL ch_patch_rollback_all_impl(void)
{
    ch_host_checkpoint checkpoint = { 0, 0, 0 };
    return ch_host_rollback_to(&checkpoint);
}

void ch_host_checkpoint_capture(ch_host_checkpoint* checkpoint)
{
    if (checkpoint == NULL) return;
    EnterCriticalSection(&g_lock);
    checkpoint->patch_count = g_patch_count;
    checkpoint->hook_count = g_hook_count;
    checkpoint->service_count = g_service_count;
    LeaveCriticalSection(&g_lock);
}

ch_status ch_host_rollback_to(const ch_host_checkpoint* checkpoint)
{
    if (checkpoint == NULL || checkpoint->patch_count > g_patch_count ||
        checkpoint->hook_count > g_hook_count ||
        checkpoint->service_count > g_service_count) return CH_STATUS_INVALID_ARGUMENT;
    EnterCriticalSection(&g_lock);
    while (g_hook_count > checkpoint->hook_count) {
        void* target = g_hooks[g_hook_count - 1].target;
        MH_STATUS status = MH_DisableHook(target);
        if (status != MH_OK && status != MH_ERROR_DISABLED) {
            LeaveCriticalSection(&g_lock);
            return CH_STATUS_SYSTEM_ERROR;
        }
        if (MH_RemoveHook(target) != MH_OK) {
            LeaveCriticalSection(&g_lock);
            return CH_STATUS_SYSTEM_ERROR;
        }
        --g_hook_count;
    }
    while (g_patch_count > checkpoint->patch_count) {
        DWORD old_protection;
        ch_owned_patch* record = &g_patches[g_patch_count - 1];
        if (!VirtualProtect(record->target, record->length, PAGE_EXECUTE_READWRITE, &old_protection)) {
            LeaveCriticalSection(&g_lock);
            return CH_STATUS_SYSTEM_ERROR;
        }
        memcpy(record->target, record->original, record->length);
        FlushInstructionCache(GetCurrentProcess(), record->target, record->length);
        if (!VirtualProtect(record->target, record->length, old_protection, &old_protection)) {
            LeaveCriticalSection(&g_lock);
            return CH_STATUS_SYSTEM_ERROR;
        }
        --g_patch_count;
    }
    while (g_service_count > checkpoint->service_count) {
        --g_service_count;
        memset(&g_services[g_service_count], 0, sizeof(g_services[g_service_count]));
    }
    LeaveCriticalSection(&g_lock);
    return CH_STATUS_OK;
}

static ch_status CH_CALL ch_hook_create_impl(const ch_hook_request* request)
{
    void* target;
    MH_STATUS status;
    MEMORY_BASIC_INFORMATION memory;
    if (request == NULL || request->struct_size < sizeof(*request) ||
        request->expected == NULL || request->expected_length < 5 ||
        request->expected_length > CH_MAX_PATCH_BYTES || request->detour == NULL ||
        request->original == NULL ||
        request->target_rva > 0x06693000u - request->expected_length) {
        return CH_STATUS_INVALID_ARGUMENT;
    }
    target = (void*)(g_ch_dnf_base + request->target_rva);
    if (VirtualQuery(target, &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT || (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0 ||
        (uintptr_t)target + request->expected_length >
            (uintptr_t)memory.BaseAddress + memory.RegionSize ||
        memcmp(target, request->expected, request->expected_length) != 0) {
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    }
    EnterCriticalSection(&g_lock);
    if (g_hook_count == CH_MAX_HOOKS) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_SYSTEM_ERROR;
    }
    status = MH_CreateHook(target, request->detour, request->original);
    if (status == MH_OK) status = MH_EnableHook(target);
    if (status != MH_OK) {
        MH_RemoveHook(target);
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_SYSTEM_ERROR;
    }
    g_hooks[g_hook_count++].target = target;
    LeaveCriticalSection(&g_lock);
    return CH_STATUS_OK;
}

static ch_status CH_CALL ch_service_publish_impl(
    const char* name, const void* service, uint32_t service_size)
{
    uint32_t i;
    size_t length;
    if (name == NULL || service == NULL || service_size == 0) return CH_STATUS_INVALID_ARGUMENT;
    length = strlen(name);
    if (length == 0 || length >= sizeof(g_services[0].name)) return CH_STATUS_INVALID_ARGUMENT;
    EnterCriticalSection(&g_lock);
    for (i = 0; i < g_service_count; ++i) {
        if (_stricmp(g_services[i].name, name) == 0) {
            LeaveCriticalSection(&g_lock);
            return CH_STATUS_ALREADY_EXISTS;
        }
    }
    if (g_service_count == CH_MAX_SERVICES) {
        LeaveCriticalSection(&g_lock);
        return CH_STATUS_SYSTEM_ERROR;
    }
    strcpy_s(g_services[g_service_count].name, sizeof(g_services[g_service_count].name), name);
    g_services[g_service_count].value = service;
    g_services[g_service_count].size = service_size;
    ++g_service_count;
    LeaveCriticalSection(&g_lock);
    return CH_STATUS_OK;
}

static const void* CH_CALL ch_service_query_impl(const char* name, uint32_t minimum_size)
{
    uint32_t i;
    const void* result = NULL;
    if (name == NULL) return NULL;
    EnterCriticalSection(&g_lock);
    for (i = 0; i < g_service_count; ++i) {
        if (_stricmp(g_services[i].name, name) == 0 && g_services[i].size >= minimum_size) {
            result = g_services[i].value;
            break;
        }
    }
    LeaveCriticalSection(&g_lock);
    return result;
}

ch_host_api g_ch_host_api = {
    sizeof(ch_host_api), CLIENTHACK_ABI_VERSION, 0,
    ch_log, ch_patch_apply_impl, ch_patch_rollback_all_impl,
    ch_hook_create_impl, ch_service_publish_impl, ch_service_query_impl
};

int ch_host_services_initialize(void)
{
    if (g_initialized) return 1;
    InitializeCriticalSection(&g_lock);
    if (MH_Initialize() != MH_OK) {
        DeleteCriticalSection(&g_lock);
        return 0;
    }
    g_ch_host_api.dnf_base = g_ch_dnf_base;
    g_initialized = 1;
    return 1;
}

void ch_host_services_shutdown(void)
{
    if (!g_initialized) return;
    ch_patch_rollback_all_impl();
    MH_Uninitialize();
    DeleteCriticalSection(&g_lock);
    g_initialized = 0;
}
