#include "clienthack/abi.h"
#include "clienthack/gate.h"
#include "clienthack/iat_state.h"
#include "clienthack/patch_state.h"
#include "clienthack/plugin_name.h"
#include "clienthack/required_plugins.h"
#include "clienthack/sha256.h"
#include "clienthack/startup.h"
#include "iat_gate_policy.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#include <windows.h>

static int g_failures;

#define CHECK(expression) do { \
    if (!(expression)) { \
        fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__, #expression); \
        ++g_failures; \
    } \
} while (0)

static void test_sha256(void)
{
    static const uint8_t expected[32] = {
        0xBA, 0x78, 0x16, 0xBF, 0x8F, 0x01, 0xCF, 0xEA,
        0x41, 0x41, 0x40, 0xDE, 0x5D, 0xAE, 0x22, 0x23,
        0xB0, 0x03, 0x61, 0xA3, 0x96, 0x17, 0x7A, 0x9C,
        0xB4, 0x10, 0xFF, 0x61, 0xF2, 0x00, 0x15, 0xAD
    };
    uint8_t digest[32];
    uint8_t parsed[32];
    ch_sha256("abc", 3, digest);
    CHECK(memcmp(digest, expected, sizeof(expected)) == 0);
    CHECK(ch_sha256_from_hex(
        "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
        parsed));
    CHECK(memcmp(parsed, expected, sizeof(expected)) == 0);
    CHECK(!ch_sha256_from_hex("xyz", parsed));
}

static void test_gate(void)
{
    uint8_t expected[32] = { 0 };
    uint8_t actual[32] = { 0 };
    CHECK(ch_gate_evaluate(1, actual, expected) == CH_GATE_ACCEPTED);
    actual[7] = 1;
    CHECK(ch_gate_evaluate(1, actual, expected) == CH_GATE_HASH_MISMATCH);
    CHECK(ch_gate_evaluate(0, actual, expected) == CH_GATE_UNREADABLE);
}

static void test_iat_state(void)
{
    CHECK(ch_iat_slot_evaluate(0x1234u, 0x1234u) == CH_IAT_SLOT_RESOLVED);
    CHECK(ch_iat_slot_evaluate(0u, 0x1234u) == CH_IAT_SLOT_CAPTURED_ZERO);
    CHECK(ch_iat_slot_evaluate(0u, 0u) == CH_IAT_SLOT_CAPTURED_ZERO);
    CHECK(ch_iat_slot_evaluate(0x5678u, 0x1234u) == CH_IAT_SLOT_UNVERIFIED);
}

typedef struct fake_iat_writer {
    uintptr_t values[4];
    size_t calls;
    size_t fail_index;
    int fail_once;
} fake_iat_writer;

static int fake_iat_write(void* context, size_t index, uintptr_t value)
{
    fake_iat_writer* writer = (fake_iat_writer*)context;
    writer->values[index] = value;
    ++writer->calls;
    if (writer->fail_once && index == writer->fail_index) {
        writer->fail_once = 0;
        return 0;
    }
    return 1;
}

static void test_iat_policy_and_transaction(void)
{
    const uintptr_t original[4] = { 0x1111u, 0x2222u, 0x3333u, 0x4444u };
    const uintptr_t resolved[4] = { 0xAAAAu, 0xBBBBu, 0xCCCCu, 0xDDDDu };
    const uint8_t mixed_repairs[4] = { 1, 0, 1, 0 };
    const uint8_t no_repairs[4] = { 0, 0, 0, 0 };
    fake_iat_writer writer;

    CHECK(ch_iat_policy_evaluate(0x1111u, 0xAAAAu, 0x1111u, 0, 0) ==
        CH_IAT_POLICY_NEEDS_REPAIR);
    CHECK(ch_iat_policy_evaluate(0xAAAAu, 0xAAAAu, 0x1111u, 0, 0) ==
        CH_IAT_POLICY_RESOLVED);
    CHECK(ch_iat_policy_evaluate(0x9999u, 0xAAAAu, 0x1111u, 0, 0) ==
        CH_IAT_POLICY_REJECTED);
    CHECK(ch_iat_policy_evaluate(0x5060F0u, 0, 0x60F0u, 0x500000u, 1) ==
        CH_IAT_POLICY_RETAIN_UNRESOLVED);
    CHECK(ch_iat_policy_evaluate(0x5060F1u, 0, 0x60F0u, 0x500000u, 1) ==
        CH_IAT_POLICY_REJECTED);

    memset(&writer, 0, sizeof(writer));
    memcpy(writer.values, original, sizeof(original));
    CHECK(ch_iat_repair_transaction(original, resolved, no_repairs, 4,
        fake_iat_write, &writer));
    CHECK(writer.calls == 0);

    memset(&writer, 0, sizeof(writer));
    memcpy(writer.values, original, sizeof(original));
    CHECK(ch_iat_repair_transaction(original, resolved, mixed_repairs, 4,
        fake_iat_write, &writer));
    CHECK(writer.values[0] == resolved[0] && writer.values[1] == original[1] &&
        writer.values[2] == resolved[2] && writer.values[3] == original[3]);

    memset(&writer, 0, sizeof(writer));
    memcpy(writer.values, original, sizeof(original));
    writer.fail_index = 2;
    writer.fail_once = 1;
    CHECK(!ch_iat_repair_transaction(original, resolved, mixed_repairs, 4,
        fake_iat_write, &writer));
    CHECK(memcmp(writer.values, original, sizeof(original)) == 0);
}

static void test_production_iat_gate_policy(void)
{
    const uintptr_t module_base = 0x500000u;
    size_t index;
    for (index = 0; index < CH_IAT_GATE_ENTRY_COUNT; ++index) {
        const uintptr_t resolved = index == CH_IAT_GATE_RELATIVE_INDEX
            ? 0u : 0x10000000u + index;
        uintptr_t captured;
        int relative = 0;
        ch_iat_policy_state state;

        if (index < CH_IAT_GATE_EXACT_COUNT) {
            captured = g_ch_iat_gate_exact[index];
        } else if (index == CH_IAT_GATE_RELATIVE_INDEX) {
            captured = CH_IAT_GATE_RELATIVE_RVA;
            relative = 1;
        } else if (index == CH_IAT_GATE_EXTRA_EXACT_INDEX) {
            captured = CH_IAT_GATE_EXTRA_EXACT_VALUE;
        } else {
            captured = resolved;
        }

        if (index == CH_IAT_GATE_RELATIVE_INDEX) {
            state = ch_iat_policy_evaluate(module_base + captured, resolved,
                captured, module_base, relative);
            CHECK(state == CH_IAT_POLICY_RETAIN_UNRESOLVED);
        } else {
            const uintptr_t actual = (index & 1u) != 0 ? resolved : captured;
            state = ch_iat_policy_evaluate(actual, resolved, captured,
                module_base, relative);
            CHECK(state == (actual == resolved
                ? CH_IAT_POLICY_RESOLVED : CH_IAT_POLICY_NEEDS_REPAIR));
            CHECK(ch_iat_policy_evaluate(resolved, resolved, captured,
                module_base, relative) == CH_IAT_POLICY_RESOLVED);
        }
    }
}

static void test_required_plugins(void)
{
    static const wchar_t* files[CH_REQUIRED_PLUGIN_COUNT] = {
        L"0010_DisableGameGuard.dll", L"0020_ClientCompatibility.dll",
        L"0100_NetworkHook.dll", L"0900_PreventMinimize.dll"
    };
    static const char* names[CH_REQUIRED_PLUGIN_COUNT] = {
        "DisableGameGuard", "ClientCompatibility", "NetworkHook", "PreventMinimize"
    };
    ch_required_plugin_state state;
    uint32_t i;
    ch_required_plugin_state_init(&state);
    CHECK(!ch_required_plugins_complete(&state));
    CHECK(ch_required_plugin_identify(files[0], "wrong", CH_PLUGIN_REQUIRED, NULL) == -1);
    CHECK(ch_required_plugin_identify(files[0], names[0], 0, NULL) == -1);
    CHECK(ch_required_plugin_identify(L"1000_Optional.dll", "Optional", 0, NULL) == 0);
    for (i = 0; i < CH_REQUIRED_PLUGIN_COUNT; ++i) {
        uint32_t index = UINT32_MAX;
        CHECK(ch_required_plugin_identify(files[i], names[i], CH_PLUGIN_REQUIRED, &index) == 1);
        CHECK(index == i);
        ch_required_plugin_mark_initialized(&state, index);
    }
    CHECK(ch_required_plugins_complete(&state));
}

static int count_continuation(void* context)
{
    int* count = (int*)context;
    ++*count;
    return 1;
}

static void test_startup_order(void)
{
    uint8_t expected[32] = { 0 };
    uint8_t actual[32] = { 0 };
    int continuation_calls = 0;
    CHECK(ch_startup_after_identity(0, actual, expected, count_continuation,
        &continuation_calls) == CH_STARTUP_EXE_UNREADABLE);
    CHECK(continuation_calls == 0);
    actual[0] = 1;
    CHECK(ch_startup_after_identity(1, actual, expected, count_continuation,
        &continuation_calls) == CH_STARTUP_EXE_MISMATCH);
    CHECK(continuation_calls == 0);
    actual[0] = 0;
    CHECK(ch_startup_after_identity(1, actual, expected, count_continuation,
        &continuation_calls) == CH_STARTUP_COMPLETED);
    CHECK(continuation_calls == 1);
}

static void test_plugin_names(void)
{
    ch_plugin_candidate candidates[4];
    CHECK(ch_plugin_filename_parse(L"0010_Network.dll", &candidates[0]));
    CHECK(candidates[0].priority == 10);
    CHECK(ch_plugin_filename_parse(L"2_Beta.DLL", &candidates[1]));
    CHECK(!ch_plugin_filename_parse(L"NoPrefix.dll", &candidates[2]));
    CHECK(!ch_plugin_filename_parse(L"1_.dll", &candidates[2]));
    CHECK(!ch_plugin_filename_parse(L"42949672960_Overflow.dll", &candidates[2]));
    CHECK(ch_plugin_filename_parse(L"10_alpha.dll", &candidates[2]));
    CHECK(ch_plugin_filename_parse(L"10_Beta.dll", &candidates[3]));
    qsort(candidates, 4, sizeof(candidates[0]), ch_plugin_candidate_compare);
    CHECK(wcscmp(candidates[0].filename, L"2_Beta.DLL") == 0);
    CHECK(wcscmp(candidates[1].filename, L"0010_Network.dll") == 0);
    CHECK(wcscmp(candidates[2].filename, L"10_alpha.dll") == 0);
    CHECK(wcscmp(candidates[3].filename, L"10_Beta.dll") == 0);
}

static ch_status CH_CALL fake_initialize(const ch_host_api* host)
{
    (void)host;
    return CH_STATUS_OK;
}

static void test_abi(void)
{
    ch_plugin_descriptor descriptor;
    memset(&descriptor, 0, sizeof(descriptor));
    descriptor.struct_size = sizeof(descriptor);
    descriptor.abi_version = CLIENTHACK_ABI_VERSION;
    descriptor.name = "test";
    descriptor.initialize = fake_initialize;
    CHECK(ch_plugin_descriptor_is_valid(&descriptor));
    descriptor.abi_version++;
    CHECK(!ch_plugin_descriptor_is_valid(&descriptor));
    descriptor.abi_version = CLIENTHACK_ABI_VERSION;
    descriptor.struct_size--;
    CHECK(!ch_plugin_descriptor_is_valid(&descriptor));
    descriptor.struct_size = sizeof(descriptor);
    descriptor.name = NULL;
    CHECK(!ch_plugin_descriptor_is_valid(&descriptor));
}

static void test_patch_state(void)
{
    uint8_t target[] = { 1, 2, 3 };
    const uint8_t expected[] = { 1, 2, 3 };
    const uint8_t replacement[] = { 4, 5, 6 };
    uint8_t original[3];
    ch_patch_buffer_record record;
    memset(&record, 0, sizeof(record));

    CHECK(ch_patch_buffer_apply(target, expected, replacement, 3, &record, original) == CH_PATCH_APPLIED);
    CHECK(memcmp(target, replacement, 3) == 0);
    CHECK(record.changed);
    CHECK(ch_patch_buffer_rollback(&record));
    CHECK(memcmp(target, expected, 3) == 0);

    memcpy(target, replacement, 3);
    memset(&record, 0, sizeof(record));
    CHECK(ch_patch_buffer_apply(target, expected, replacement, 3, &record, original) == CH_PATCH_ALREADY_APPLIED);
    CHECK(!record.changed);

    target[0] = 9;
    CHECK(ch_patch_buffer_apply(target, expected, replacement, 3, &record, original) == CH_PATCH_MISMATCH);
    CHECK(target[0] == 9);
}

static void test_staged_plugin_metadata(void)
{
    static const wchar_t* filenames[] = {
        L"0010_DisableGameGuard.dll",
        L"0020_ClientCompatibility.dll",
        L"0100_NetworkHook.dll",
        L"0900_PreventMinimize.dll"
    };
    static const char* names[] = {
        "DisableGameGuard", "ClientCompatibility", "NetworkHook", "PreventMinimize"
    };
    size_t i;
    for (i = 0; i < sizeof(filenames) / sizeof(filenames[0]); ++i) {
        wchar_t path[MAX_PATH];
        HMODULE module;
        ch_plugin_query_fn query;
        const ch_plugin_descriptor* descriptor;
        CHECK(_snwprintf_s(path, MAX_PATH, _TRUNCATE, L"%s/plugins/%s",
            CLIENTHACK_STAGE_DIR, filenames[i]) > 0);
        module = LoadLibraryExW(path, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        CHECK(module != NULL);
        if (module == NULL) continue;
        query = (ch_plugin_query_fn)(void*)GetProcAddress(module, CH_PLUGIN_QUERY_EXPORT);
        CHECK(query != NULL);
        descriptor = query != NULL ? query() : NULL;
        CHECK(ch_plugin_descriptor_is_valid(descriptor));
        if (descriptor != NULL) {
            CHECK(strcmp(descriptor->name, names[i]) == 0);
            CHECK((descriptor->flags & CH_PLUGIN_REQUIRED) != 0);
        }
        FreeLibrary(module);
    }
}

int main(void)
{
    test_sha256();
    test_gate();
    test_iat_state();
    test_iat_policy_and_transaction();
    test_production_iat_gate_policy();
    test_required_plugins();
    test_startup_order();
    test_plugin_names();
    test_abi();
    test_patch_state();
    test_staged_plugin_metadata();
    if (g_failures != 0) {
        fprintf(stderr, "%d test assertion(s) failed\n", g_failures);
        return 1;
    }
    puts("all clienthack tests passed");
    return 0;
}
