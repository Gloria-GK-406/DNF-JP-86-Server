#ifndef CLIENTHACK_REQUIRED_PLUGINS_H
#define CLIENTHACK_REQUIRED_PLUGINS_H

#include <stdint.h>
#include <wchar.h>

#include "clienthack/client_profile.h"

/* Plugins whose absence or failure must abort startup, because the client would
 * otherwise run in a state the branch does not support. The roster is per
 * client: a plugin patches byte signatures at hardcoded addresses, so an S4A12
 * plugin loaded against S4A21 fails its own checks, and demanding it would only
 * turn a missing port into an unexplained refusal to boot.
 *
 * S4A21 starts empty and grows one entry at a time as each plugin is ported and
 * verified against that client. An empty roster is complete by definition, so
 * the host starts with whatever plugins happen to load -- which is exactly what
 * a dump-only bring-up run needs. */
#if defined(CLIENTHACK_CLIENT_A21)
#define CH_REQUIRED_PLUGIN_COUNT 0u
#else
#define CH_REQUIRED_PLUGIN_COUNT 6u
#endif

typedef struct ch_required_plugin_state {
    uint32_t initialized_mask;
} ch_required_plugin_state;

void ch_required_plugin_state_init(ch_required_plugin_state* state);
int ch_required_plugin_identify(
    const wchar_t* filename,
    const char* descriptor_name,
    uint32_t descriptor_flags,
    uint32_t* index);
void ch_required_plugin_mark_initialized(ch_required_plugin_state* state, uint32_t index);
int ch_required_plugins_complete(const ch_required_plugin_state* state);

#endif
