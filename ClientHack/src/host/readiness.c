#include "host_internal.h"

#include "clienthack/runtime_signatures.h"

#include <intrin.h>
#include <limits.h>
#include <string.h>

static HANDLE g_startup_gate_entered;
static HANDLE g_startup_gate_release;
static uint8_t* g_startup_gate_target;
static uint8_t g_startup_gate_original[5];
static volatile LONG g_startup_gate_restored;

static int ch_bytes_match(uintptr_t rva, const uint8_t* expected, size_t length)
{
    const uint8_t* address = (const uint8_t*)(g_ch_dnf_base + rva);
    MEMORY_BASIC_INFORMATION info;
    if (VirtualQuery(address, &info, sizeof(info)) != sizeof(info) ||
        info.State != MEM_COMMIT ||
        (info.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0 ||
        (uintptr_t)address + length >
            (uintptr_t)info.BaseAddress + info.RegionSize) {
        return 0;
    }
    return memcmp(address, expected, length) == 0;
}

int ch_wait_for_client_ready(DWORD timeout_ms)
{
    const uintptr_t readiness_rva = 0x0213BBBFu;
    const uint8_t signature[4] = { 0x8B, 0xFF, 0x55, 0x8B };
    DWORD elapsed = 0;
    while (elapsed < timeout_ms) {
        if (ch_bytes_match(readiness_rva, signature, sizeof(signature))) {
            return 1;
        }
        Sleep(10);
        elapsed += 10;
    }
    return 0;
}

static __declspec(noinline) void __cdecl ch_startup_gate_handler(void)
{
    void** return_address = (void**)_AddressOfReturnAddress();
    DWORD old_protection;
    int restored = 0;

    *return_address = g_startup_gate_target;
    if (VirtualProtect(g_startup_gate_target, sizeof(g_startup_gate_original),
        PAGE_EXECUTE_READWRITE, &old_protection)) {
        memcpy(g_startup_gate_target, g_startup_gate_original,
            sizeof(g_startup_gate_original));
        FlushInstructionCache(GetCurrentProcess(), g_startup_gate_target,
            sizeof(g_startup_gate_original));
        restored = VirtualProtect(g_startup_gate_target,
            sizeof(g_startup_gate_original), old_protection, &old_protection) != 0;
    }
    InterlockedExchange(&g_startup_gate_restored, restored);
    SetEvent(g_startup_gate_entered);
    /* A failed worker terminates the process. Never let the client thread
     * outrun required plugin initialization because of a local wait timeout. */
    WaitForSingleObject(g_startup_gate_release, INFINITE);
}

int ch_startup_gate_install(DWORD timeout_ms)
{
    static const uint8_t signature[5] = { 0x8B, 0xFF, 0x55, 0x8B, 0xEC };
    const uintptr_t readiness_rva = 0x0213BBBFu;
    ULONGLONG deadline;
    uint8_t call_patch[5];
    intptr_t relative;
    DWORD old_protection;

    g_startup_gate_entered = CreateEventW(NULL, TRUE, FALSE, NULL);
    g_startup_gate_release = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (g_startup_gate_entered == NULL || g_startup_gate_release == NULL) return 0;
    g_startup_gate_target = (uint8_t*)(g_ch_dnf_base + readiness_rva);
    deadline = GetTickCount64() + timeout_ms;
    while (!ch_bytes_match(readiness_rva, signature, sizeof(signature))) {
        if (GetTickCount64() >= deadline) return 0;
        YieldProcessor();
        SwitchToThread();
    }

    relative = (uint8_t*)ch_startup_gate_handler - (g_startup_gate_target + 5);
    if (relative < INT32_MIN || relative > INT32_MAX) return 0;
    memcpy(g_startup_gate_original, signature, sizeof(signature));
    call_patch[0] = 0xE8;
    {
        int32_t displacement = (int32_t)relative;
        memcpy(call_patch + 1, &displacement, sizeof(displacement));
    }
    if (!VirtualProtect(g_startup_gate_target, sizeof(call_patch),
        PAGE_EXECUTE_READWRITE, &old_protection)) return 0;
    memcpy(g_startup_gate_target, call_patch, sizeof(call_patch));
    FlushInstructionCache(GetCurrentProcess(), g_startup_gate_target,
        sizeof(call_patch));
    if (!VirtualProtect(g_startup_gate_target, sizeof(call_patch),
        old_protection, &old_protection)) return 0;
    return 1;
}

int ch_startup_gate_wait(DWORD timeout_ms)
{
    return g_startup_gate_entered != NULL &&
        WaitForSingleObject(g_startup_gate_entered, timeout_ms) == WAIT_OBJECT_0 &&
        InterlockedCompareExchange(&g_startup_gate_restored, 0, 0) != 0;
}

void ch_startup_gate_release(void)
{
    if (g_startup_gate_release != NULL) SetEvent(g_startup_gate_release);
}

int ch_wait_for_feature_ready(DWORD timeout_ms)
{
    DWORD elapsed = 0;
    while (elapsed < timeout_ms) {
        if (ch_bytes_match(0x048FF9ABu, CH_SIG_SOCKET_WRITE,
                sizeof(CH_SIG_SOCKET_WRITE)) &&
            ch_bytes_match(0x028D5C9Cu, CH_ORIG_AZ_STRING_028D5C9C,
                sizeof(CH_ORIG_AZ_STRING_028D5C9C))) {
            return 1;
        }
        Sleep(10);
        elapsed += 10;
    }
    return 0;
}
