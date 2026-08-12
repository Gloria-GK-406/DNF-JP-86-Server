#include "clienthack/patch_state.h"

#include <string.h>

ch_patch_state_result ch_patch_buffer_apply(
    uint8_t* target,
    const uint8_t* expected,
    const uint8_t* replacement,
    size_t length,
    ch_patch_buffer_record* record,
    uint8_t* original_storage)
{
    if (target == NULL || expected == NULL || replacement == NULL ||
        length == 0 || record == NULL || original_storage == NULL) {
        return CH_PATCH_INVALID;
    }

    memset(record, 0, sizeof(*record));
    if (memcmp(target, replacement, length) == 0) {
        return CH_PATCH_ALREADY_APPLIED;
    }
    if (memcmp(target, expected, length) != 0) {
        return CH_PATCH_MISMATCH;
    }

    memcpy(original_storage, target, length);
    memcpy(target, replacement, length);
    record->target = target;
    record->original_storage = original_storage;
    record->length = length;
    record->changed = 1;
    return CH_PATCH_APPLIED;
}

int ch_patch_buffer_rollback(ch_patch_buffer_record* record)
{
    if (record == NULL || !record->changed || record->target == NULL ||
        record->original_storage == NULL || record->length == 0) {
        return 0;
    }
    memcpy(record->target, record->original_storage, record->length);
    record->changed = 0;
    return 1;
}
