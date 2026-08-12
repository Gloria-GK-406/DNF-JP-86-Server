#include "clienthack/gate.h"

#include <string.h>

ch_gate_result ch_gate_evaluate(
    int readable,
    const uint8_t actual_hash[32],
    const uint8_t expected_hash[32])
{
    if (!readable || actual_hash == NULL || expected_hash == NULL) {
        return CH_GATE_UNREADABLE;
    }
    return memcmp(actual_hash, expected_hash, 32) == 0
        ? CH_GATE_ACCEPTED
        : CH_GATE_HASH_MISMATCH;
}
