#include "clienthack/iat_state.h"

ch_iat_slot_state ch_iat_slot_evaluate(uintptr_t actual, uintptr_t resolved)
{
    if (actual == 0) return CH_IAT_SLOT_CAPTURED_ZERO;
    return actual == resolved ? CH_IAT_SLOT_RESOLVED : CH_IAT_SLOT_UNVERIFIED;
}

ch_iat_policy_state ch_iat_policy_evaluate(
    uintptr_t actual, uintptr_t resolved, uintptr_t captured,
    uintptr_t module_base, int captured_is_module_rva)
{
    if (resolved != 0) {
        if (actual == resolved) return CH_IAT_POLICY_RESOLVED;
        return actual == captured
            ? CH_IAT_POLICY_NEEDS_REPAIR
            : CH_IAT_POLICY_REJECTED;
    }
    if (captured_is_module_rva && module_base != 0 && actual >= module_base &&
        actual - module_base == captured) {
        return CH_IAT_POLICY_RETAIN_UNRESOLVED;
    }
    return CH_IAT_POLICY_REJECTED;
}

int ch_iat_repair_transaction(
    const uintptr_t* original, const uintptr_t* resolved,
    const uint8_t* needs_repair, size_t count,
    ch_iat_write_fn write_slot, void* context)
{
    size_t index;
    if (original == NULL || resolved == NULL || needs_repair == NULL ||
        write_slot == NULL) return 0;
    for (index = 0; index < count; ++index) {
        if (needs_repair[index] != 0 &&
            !write_slot(context, index, resolved[index])) {
            size_t rollback = index + 1;
            while (rollback != 0) {
                --rollback;
                if (needs_repair[rollback] != 0) {
                    (void)write_slot(context, rollback, original[rollback]);
                }
            }
            return 0;
        }
    }
    return 1;
}
