#ifndef CLIENTHACK_PATCH_STATE_H
#define CLIENTHACK_PATCH_STATE_H

#include <stddef.h>
#include <stdint.h>

typedef enum ch_patch_state_result {
    CH_PATCH_APPLIED = 0,
    CH_PATCH_ALREADY_APPLIED = 1,
    CH_PATCH_MISMATCH = 2,
    CH_PATCH_INVALID = 3
} ch_patch_state_result;

typedef struct ch_patch_buffer_record {
    uint8_t* target;
    uint8_t* original_storage;
    size_t length;
    int changed;
} ch_patch_buffer_record;

ch_patch_state_result ch_patch_buffer_apply(
    uint8_t* target,
    const uint8_t* expected,
    const uint8_t* replacement,
    size_t length,
    ch_patch_buffer_record* record,
    uint8_t* original_storage);

int ch_patch_buffer_rollback(ch_patch_buffer_record* record);

#endif
