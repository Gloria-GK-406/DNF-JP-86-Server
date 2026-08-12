#include "host_internal.h"

#include "clienthack/iat_state.h"
#include "iat_gate_policy.h"
#include "iat_table.h"

#include <stdio.h>
#include <string.h>

typedef struct ch_module_cache_entry {
    const char* name;
    HMODULE module;
} ch_module_cache_entry;

typedef struct ch_iat_write_context {
    uintptr_t dnf_base;
} ch_iat_write_context;

static int ch_write_iat_slot(void* context, size_t index, uintptr_t value)
{
    const ch_iat_write_context* write_context =
        (const ch_iat_write_context*)context;
    const uintptr_t rva = g_ch_iat_table[index].preferred_va - 0x00400000u;
    uintptr_t* slot = (uintptr_t*)(write_context->dnf_base + rva);
    DWORD old_protection;
    DWORD ignored;
    if (!VirtualProtect(slot, sizeof(*slot), PAGE_EXECUTE_READWRITE,
        &old_protection)) return 0;
    *slot = value;
    if (!VirtualProtect(slot, sizeof(*slot), old_protection, &ignored)) return 0;
    return 1;
}

int ch_repair_client_iat(void)
{
    ch_module_cache_entry modules[8];
    uintptr_t resolved[CH_IAT_ENTRY_COUNT];
    uintptr_t original[CH_IAT_ENTRY_COUNT];
    uint8_t needs_repair[CH_IAT_ENTRY_COUNT];
    ch_iat_write_context write_context;
    size_t module_count = 0;
    size_t repair_count = 0;
    size_t resolved_count = 0;
    size_t retained_count = 0;
    size_t index;

    memset(modules, 0, sizeof(modules));
    memset(resolved, 0, sizeof(resolved));
    memset(original, 0, sizeof(original));
    memset(needs_repair, 0, sizeof(needs_repair));

    for (index = 0; index < CH_IAT_ENTRY_COUNT; ++index) {
        const ch_iat_entry* entry = &g_ch_iat_table[index];
        HMODULE module = NULL;
        uintptr_t captured;
        uintptr_t rva;
        const uintptr_t* slot;
        MEMORY_BASIC_INFORMATION memory;
        ch_iat_policy_state state;
        size_t module_index;
        int captured_is_module_rva = 0;

        if (entry->preferred_va < 0x00400000u) return 0;
        rva = entry->preferred_va - 0x00400000u;
        if (rva > 0x06693000u - sizeof(uintptr_t)) return 0;
        slot = (const uintptr_t*)(g_ch_dnf_base + rva);
        if (VirtualQuery(slot, &memory, sizeof(memory)) != sizeof(memory) ||
            memory.State != MEM_COMMIT ||
            (memory.Protect & (PAGE_NOACCESS | PAGE_GUARD)) != 0) {
            ch_log(0, "iat", "IAT slot is not readable");
            return 0;
        }

        for (module_index = 0; module_index < module_count; ++module_index) {
            if (_stricmp(modules[module_index].name, entry->module_name) == 0) {
                module = modules[module_index].module;
                break;
            }
        }
        if (module == NULL) {
            module = GetModuleHandleA(entry->module_name);
            if (module == NULL) {
                module = LoadLibraryExA(entry->module_name, NULL,
                    LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            }
            if (module == NULL ||
                module_count == sizeof(modules) / sizeof(modules[0])) return 0;
            modules[module_count].name = entry->module_name;
            modules[module_count].module = module;
            ++module_count;
        }

        resolved[index] = (uintptr_t)GetProcAddress(module, entry->function_name);
        original[index] = *slot;
        if (index < CH_IAT_GATE_EXACT_COUNT) {
            captured = g_ch_iat_gate_exact[index];
        } else if (index == CH_IAT_GATE_RELATIVE_INDEX) {
            captured = CH_IAT_GATE_RELATIVE_RVA;
            captured_is_module_rva = 1;
        } else if (index == CH_IAT_GATE_EXTRA_EXACT_INDEX) {
            captured = CH_IAT_GATE_EXTRA_EXACT_VALUE;
        } else {
            captured = resolved[index];
        }

        state = ch_iat_policy_evaluate(original[index], resolved[index], captured,
            (uintptr_t)module, captured_is_module_rva);
        if (state == CH_IAT_POLICY_REJECTED) {
            char detail[128];
            _snprintf_s(detail, sizeof(detail), _TRUNCATE,
                "IAT slot %lu does not match captured or resolved state",
                (unsigned long)index);
            ch_log(0, "iat", detail);
            return 0;
        }
        if (state == CH_IAT_POLICY_NEEDS_REPAIR) {
            needs_repair[index] = 1;
            ++repair_count;
        } else if (state == CH_IAT_POLICY_RESOLVED) {
            ++resolved_count;
        } else {
            ++retained_count;
        }
    }

    write_context.dnf_base = g_ch_dnf_base;
    if (!ch_iat_repair_transaction(original, resolved, needs_repair,
        CH_IAT_ENTRY_COUNT, ch_write_iat_slot, &write_context)) {
        ch_log(0, "iat", "IAT repair failed and rollback was attempted");
        return 0;
    }
    FlushInstructionCache(GetCurrentProcess(), NULL, 0);
    {
        char detail[160];
        _snprintf_s(detail, sizeof(detail), _TRUNCATE,
            "IAT policy accepted: %lu repaired, %lu already resolved, %lu retained",
            (unsigned long)repair_count, (unsigned long)resolved_count,
            (unsigned long)retained_count);
        ch_log(3, "iat", detail);
    }
    return 1;
}
