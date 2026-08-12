#ifndef CLIENTHACK_IAT_STATE_H
#define CLIENTHACK_IAT_STATE_H

#include <stddef.h>
#include <stdint.h>

typedef enum ch_iat_slot_state {
    CH_IAT_SLOT_CAPTURED_ZERO = 0,
    CH_IAT_SLOT_RESOLVED = 1,
    CH_IAT_SLOT_UNVERIFIED = 2
} ch_iat_slot_state;

ch_iat_slot_state ch_iat_slot_evaluate(uintptr_t actual, uintptr_t resolved);

typedef enum ch_iat_policy_state {
    CH_IAT_POLICY_REJECTED = 0,
    CH_IAT_POLICY_NEEDS_REPAIR = 1,
    CH_IAT_POLICY_RESOLVED = 2,
    CH_IAT_POLICY_RETAIN_UNRESOLVED = 3
} ch_iat_policy_state;

typedef int (*ch_iat_write_fn)(void* context, size_t index, uintptr_t value);

ch_iat_policy_state ch_iat_policy_evaluate(
    uintptr_t actual, uintptr_t resolved, uintptr_t captured,
    uintptr_t module_base, int captured_is_module_rva);

int ch_iat_repair_transaction(
    const uintptr_t* original, const uintptr_t* resolved,
    const uint8_t* needs_repair, size_t count,
    ch_iat_write_fn write_slot, void* context);

#endif
