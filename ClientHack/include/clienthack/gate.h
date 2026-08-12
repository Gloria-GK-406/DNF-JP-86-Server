#ifndef CLIENTHACK_GATE_H
#define CLIENTHACK_GATE_H

#include <stdint.h>

typedef enum ch_gate_result {
    CH_GATE_ACCEPTED = 0,
    CH_GATE_UNREADABLE = 1,
    CH_GATE_HASH_MISMATCH = 2
} ch_gate_result;

ch_gate_result ch_gate_evaluate(
    int readable,
    const uint8_t actual_hash[32],
    const uint8_t expected_hash[32]);

#endif
