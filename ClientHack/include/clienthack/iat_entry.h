#ifndef CLIENTHACK_IAT_ENTRY_H
#define CLIENTHACK_IAT_ENTRY_H

#include <stdint.h>

/* One client IAT slot: where it lives in the preferred image, and what it must
 * point at. Shared by the per-client tables (src/az/iat_table.h for S4A12,
 * src/az/iat_table_a21.h for S4A21) so the two differ only in data. */
typedef struct ch_iat_entry {
    uintptr_t preferred_va;
    const char* module_name;
    const char* function_name;
} ch_iat_entry;

#endif
