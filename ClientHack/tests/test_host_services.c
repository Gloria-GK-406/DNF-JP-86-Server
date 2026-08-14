#include "host_internal.h"

#include <stdio.h>
#include <string.h>

HMODULE g_ch_module;
uintptr_t g_ch_dnf_base;

static int failures;
#define CHECK(value) do { if (!(value)) { \
    fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #value); ++failures; \
} } while (0)

typedef int (__cdecl *test_function)(void);

static int __cdecl detour_function(void)
{
    return 2;
}

int main(void)
{
    const SIZE_T readiness_size = 0x0213BBBFu + 4096u;
    uint8_t* memory = (uint8_t*)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE);
    static const uint8_t expected_a[] = { 1, 2, 3 };
    static const uint8_t replacement_a[] = { 4, 5, 6 };
    static const uint8_t expected_b[] = { 7, 8, 9 };
    static const uint8_t replacement_b[] = { 10, 11, 12 };
    static const uint8_t function_code[] = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3 };
    ch_patch_request patch;
    ch_hook_request hook;
    ch_host_checkpoint checkpoint;
    void* original = NULL;
    test_function function;
    uint8_t* readiness_memory;

    CHECK(memory != NULL);
    if (memory == NULL) return 1;
    g_ch_dnf_base = (uintptr_t)memory;
    memcpy(memory, expected_a, sizeof(expected_a));
    memcpy(memory + 16, expected_b, sizeof(expected_b));
    memcpy(memory + 64, function_code, sizeof(function_code));
    CHECK(ch_host_services_initialize());

    patch.struct_size = sizeof(patch);
    patch.target_rva = 0;
    patch.expected = expected_a;
    patch.replacement = replacement_a;
    patch.length = sizeof(expected_a);
    patch.name = "patch-a";
    CHECK(g_ch_host_api.patch_apply(&patch) == CH_STATUS_OK);
    ch_host_checkpoint_capture(&checkpoint);

    patch.target_rva = 16;
    patch.expected = expected_b;
    patch.replacement = replacement_b;
    patch.length = sizeof(expected_b);
    patch.name = "patch-b";
    CHECK(g_ch_host_api.patch_apply(&patch) == CH_STATUS_OK);
    CHECK(ch_host_rollback_to(&checkpoint) == CH_STATUS_OK);
    CHECK(memcmp(memory, replacement_a, sizeof(replacement_a)) == 0);
    CHECK(memcmp(memory + 16, expected_b, sizeof(expected_b)) == 0);

    hook.struct_size = sizeof(hook);
    hook.target_rva = 64;
    hook.expected = function_code;
    hook.expected_length = sizeof(function_code);
    hook.detour = (void*)(uintptr_t)detour_function;
    hook.original = &original;
    hook.name = "test-hook";
    ch_host_checkpoint_capture(&checkpoint);
    CHECK(g_ch_host_api.hook_create(&hook) == CH_STATUS_OK);
    function = (test_function)(void*)(memory + 64);
    CHECK(function() == 2);
    CHECK(ch_host_rollback_to(&checkpoint) == CH_STATUS_OK);
    CHECK(function() == 1);

    memory[64] = 0x90;
    CHECK(g_ch_host_api.hook_create(&hook) == CH_STATUS_INCOMPATIBLE_MEMORY);
    CHECK(g_ch_host_api.patch_rollback_all() == CH_STATUS_OK);
    CHECK(memcmp(memory, expected_a, sizeof(expected_a)) == 0);

    ch_host_services_shutdown();
    VirtualFree(memory, 0, MEM_RELEASE);

    readiness_memory = (uint8_t*)VirtualAlloc(NULL, readiness_size,
        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    CHECK(readiness_memory != NULL);
    if (readiness_memory != NULL) {
        static const uint8_t ready[] = { 0x8B, 0xFF, 0x55, 0x8B };
        g_ch_dnf_base = (uintptr_t)readiness_memory;
        CHECK(!ch_wait_for_client_ready(20));
        memcpy(readiness_memory + 0x0213BBBFu, ready, sizeof(ready));
        CHECK(ch_wait_for_client_ready(20));
        readiness_memory[0x0213BBBFu] = 0;
        CHECK(!ch_wait_for_client_ready(20));
        VirtualFree(readiness_memory, 0, MEM_RELEASE);
    }

    {
        static const ch_image_section sections[] = {
            { ".text", 0x00001000u, 0x00010000u, 0x60000020u },
            { ".data", 0x00020000u, 0x00001000u, 0xC0000040u },
            { ".empty", 0x00030000u, 0x00000000u, 0x40000040u },
            { ".j`-", 0x06000000u, 0x00522000u, 0xE0000020u }
        };
        const uint32_t count = (uint32_t)(sizeof(sections) / sizeof(sections[0]));
        /* Below the first section, in the header gap. */
        CHECK(ch_section_index_for_rva(sections, count, 0x00000500u) == -1);
        /* Start, middle, and last byte of .text. */
        CHECK(ch_section_index_for_rva(sections, count, 0x00001000u) == 0);
        CHECK(ch_section_index_for_rva(sections, count, 0x00008000u) == 0);
        CHECK(ch_section_index_for_rva(sections, count, 0x00010FFFu) == 0);
        /* One past .text is not in any section (gap before .data). */
        CHECK(ch_section_index_for_rva(sections, count, 0x00011000u) == -1);
        CHECK(ch_section_index_for_rva(sections, count, 0x00020000u) == 1);
        /* Zero-size sections never match. */
        CHECK(ch_section_index_for_rva(sections, count, 0x00030000u) == -1);
        /* Inside the protected section. */
        CHECK(ch_section_index_for_rva(sections, count, 0x06176731u) == 3);
        CHECK(ch_section_index_for_rva(sections, count, 0x06522000u) == -1);
        CHECK(ch_section_index_for_rva(NULL, count, 0x00001000u) == -1);
    }

    if (failures != 0) return 1;
    puts("all host service tests passed");
    return 0;
}
