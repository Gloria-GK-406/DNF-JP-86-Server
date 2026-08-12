#ifndef CLIENTHACK_STARTUP_H
#define CLIENTHACK_STARTUP_H

#include <stdint.h>

typedef int (*ch_startup_continuation)(void* context);

typedef enum ch_startup_result {
    CH_STARTUP_COMPLETED = 0,
    CH_STARTUP_EXE_UNREADABLE = 1,
    CH_STARTUP_EXE_MISMATCH = 2,
    CH_STARTUP_CONTINUATION_FAILED = 3
} ch_startup_result;

ch_startup_result ch_startup_after_identity(
    int readable,
    const uint8_t actual_hash[32],
    const uint8_t expected_hash[32],
    ch_startup_continuation continuation,
    void* context);

#endif
