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

/* Evaluates an IAT slot without reference to any captured absolute address.
 *
 * The packer leaves arbitrary intermediate values in IAT slots at the startup
 * gate (stale stubs, template pointers, partial resolutions) that are neither
 * enumerable nor stable across boots, so the slot's prior value is never used
 * as an acceptance or rejection criterion. Matches the reference S4A12
 * RepairIAT semantics:
 *  - resolved != 0, actual == resolved   -> RESOLVED (idempotent)
 *  - resolved != 0, actual != resolved   -> NEEDS_REPAIR (write resolved)
 *  - resolved == 0, is_data_export,
 *    actual == module_base + export_rva  -> RETAIN_UNRESOLVED (DNF data export)
 *  - otherwise (unresolvable, not data
 *    export)                             -> REJECTED (fail closed)
 *
 * Fail-closed is preserved at the meaningful boundary: a slot whose export
 * cannot be resolved and is not the declared DNF data export is rejected and
 * aborts repair with rollback. The repair position itself is constrained to
 * the fixed iat_table.h slot list at the caller. */
ch_iat_policy_state ch_iat_policy_evaluate(
    uintptr_t actual, uintptr_t resolved,
    uintptr_t module_base, int is_data_export, uintptr_t data_export_rva);

int ch_iat_repair_transaction(
    const uintptr_t* original, const uintptr_t* resolved,
    const uint8_t* needs_repair, size_t count,
    ch_iat_write_fn write_slot, void* context);

#endif
