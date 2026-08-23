#include "host_internal.h"

#include "clienthack/runtime_signatures.h"
#include "clienthack/client_profile.h"

#include <limits.h>
#include <string.h>

#if defined(_MSC_VER)
#include <intrin.h>
#define CH_NOINLINE __declspec(noinline)
/* The gate handler captures the client's GP registers at entry, so it must not
 * carry a /GS cookie prologue (which would clobber EAX before the capture). */
#define CH_GATE_HANDLER __declspec(noinline) __declspec(safebuffers)
#elif defined(__GNUC__)
#define CH_NOINLINE __attribute__((noinline, optimize("no-omit-frame-pointer")))
#define CH_GATE_HANDLER CH_NOINLINE
#else
#error Unsupported compiler
#endif

static HANDLE g_startup_gate_entered;
static HANDLE g_startup_gate_release;
static uint8_t* g_startup_gate_target;
static uint8_t g_startup_gate_original[5];
static volatile LONG g_startup_gate_restored;
static ch_startup_gate_capture g_startup_gate_capture;

const ch_startup_gate_capture* ch_startup_gate_capture_get(void)
{
    return InterlockedCompareExchange(&g_startup_gate_capture.valid, 1, 1) != 0
        ? &g_startup_gate_capture
        : NULL;
}

/* Snapshot the incoming call stack before the gate rewrites the return slot.
 * return_address[0] is the temporary CALL's return (XCookie+5) and
 * return_address[1] is the original client's return address into the caller of
 * XCookie -- the Phase 1 anchor for recovering the real startup chain. Reading
 * upward from the return slot stays inside already-committed stack, so no
 * VirtualQuery or fault handling is needed in this sensitive window.
 *
 * esp/ebp/eflags are the handler frame's registers; the client's ESP just
 * before it executed CALL XCookie is return_slot_va + sizeof(void*) (the slot
 * the original CALL pushed client_return into), recoverable offline. The eax..edi
 * values are the client GP registers as seen at handler entry. */
static void ch_startup_gate_snapshot(void** return_address,
    const uint32_t regs[8], uint32_t eflags)
{
    ch_startup_gate_capture* cap = &g_startup_gate_capture;
    size_t i;
    cap->thread_id = GetCurrentThreadId();
    cap->eflags = eflags;
    /* regs order: eax, ecx, edx, ebx, esp, ebp, esi, edi (pushad order). */
    cap->eax = regs[0];
    cap->ecx = regs[1];
    cap->edx = regs[2];
    cap->ebx = regs[3];
    cap->esp = regs[4];
    cap->ebp = regs[5];
    cap->esi = regs[6];
    cap->edi = regs[7];
    cap->return_slot_va = (uint32_t)(uintptr_t)return_address;
    cap->xcookie_return = (uint32_t)(uintptr_t)return_address[0];
    cap->client_return = (uint32_t)(uintptr_t)return_address[1];
    cap->stack_count = CH_GATE_STACK_DWORDS;
    for (i = 0; i < CH_GATE_STACK_DWORDS; ++i) {
        cap->stack[i] = (uint32_t)(uintptr_t)return_address[i];
    }
    InterlockedExchange(&cap->valid, 1);
}

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
    const uintptr_t readiness_rva = CH_CLIENT_COOKIE_RVA;
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

static CH_GATE_HANDLER void __cdecl ch_startup_gate_handler(void)
{
    /* regs order matches pushad: eax, ecx, edx, ebx, esp, ebp, esi, edi. */
    uint32_t regs[8] = { 0, 0, 0, 0, 0, 0, 0, 0 };
    uint32_t captured_eflags = 0;
    void** return_address;
    DWORD old_protection;
    int restored = 0;

#if defined(_MSC_VER) && defined(_M_IX86)
    __asm {
        mov regs[0], eax
        mov regs[4], ecx
        mov regs[8], edx
        mov regs[12], ebx
        mov regs[16], esp
        mov regs[20], ebp
        mov regs[24], esi
        mov regs[28], edi
        pushfd
        pop eax
        mov captured_eflags, eax
    }
    return_address = (void**)_AddressOfReturnAddress();
#elif defined(_MSC_VER)
    return_address = (void**)_AddressOfReturnAddress();
#else
    /* The GCC function attribute above preserves EBP, so EBP+pointer-size is
     * the CALL return slot that the startup gate must redirect. */
    return_address = (void**)((uint8_t*)__builtin_frame_address(0) +
        sizeof(void*));
#endif
    ch_startup_gate_snapshot(return_address, regs, captured_eflags);
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
    static const uint8_t signature[5] = CH_CLIENT_COOKIE_PROLOGUE;
    const uintptr_t readiness_rva = CH_CLIENT_COOKIE_RVA;
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

/* A second identity check layered on top of the SHA-256 gate: byte sequences
 * only the expected build carries at these exact addresses. The addresses have
 * not been measured on every supported client, and an UNMEASURED anchor is
 * worse than none -- a wrong address either rejects the correct client or, if
 * the bytes happen to match, asserts something untrue. A profile without
 * anchors therefore relies on the hash gate alone. */
int ch_wait_for_feature_ready(DWORD timeout_ms)
{
#if !defined(CH_CLIENT_HAS_FEATURE_ANCHORS)
    (void)timeout_ms;
    return 1;
#else
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
#endif
}
