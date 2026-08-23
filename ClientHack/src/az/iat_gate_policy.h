#ifndef CLIENTHACK_IAT_GATE_POLICY_H
#define CLIENTHACK_IAT_GATE_POLICY_H

#include <stdint.h>

#include "clienthack/client_profile.h"

/* The production startup-gate policy accepts every IAT slot by identity, never
 * by a captured absolute address:
 *   - resolved export address (GetProcAddress of the slot's module/function),
 *   - the unresolved initial value 0,
 *   - a forwarder stub inside a currently loaded module, or
 *   - module base + RVA for a DNF-owned data export.
 *
 * On S4A12, index 341 is the msvcp60/msvcr71 data export normalized to module
 * RVA 0x60F0. Every other slot is handled by the general identity rules in
 * ch_iat_policy_evaluate; no absolute captured values are stored. Index 375
 * (msvcr71 swprintf compatibility entry) is accepted like any other slot via
 * its resolved export identity.
 *
 * That special case is S4A12-only: the S4A21 table imports nothing but
 * kernel32, msvcr71 and ws2_32, so there is no msvcp60 data slot to normalize.
 * That profile defines no special index at all, so code reading it must guard
 * on #if defined(CH_IAT_GATE_DATA_EXPORT_INDEX). */
#if defined(CLIENTHACK_CLIENT_A21)
#define CH_IAT_GATE_ENTRY_COUNT 420u
#else
#define CH_IAT_GATE_ENTRY_COUNT 406u
#define CH_IAT_GATE_DATA_EXPORT_INDEX 341u
#define CH_IAT_GATE_DATA_EXPORT_RVA 0x000060F0u
#endif

#endif
