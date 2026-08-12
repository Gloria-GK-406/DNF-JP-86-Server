#include "clienthack/startup.h"

#include "clienthack/gate.h"

ch_startup_result ch_startup_after_identity(
    int readable,
    const uint8_t actual_hash[32],
    const uint8_t expected_hash[32],
    ch_startup_continuation continuation,
    void* context)
{
    const ch_gate_result gate = ch_gate_evaluate(readable, actual_hash, expected_hash);
    if (gate == CH_GATE_UNREADABLE) return CH_STARTUP_EXE_UNREADABLE;
    if (gate == CH_GATE_HASH_MISMATCH) return CH_STARTUP_EXE_MISMATCH;
    if (continuation == NULL || !continuation(context)) return CH_STARTUP_CONTINUATION_FAILED;
    return CH_STARTUP_COMPLETED;
}
