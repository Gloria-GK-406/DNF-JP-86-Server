#ifndef CLIENTHACK_REQUIRED_PLUGINS_H
#define CLIENTHACK_REQUIRED_PLUGINS_H

#include <stdint.h>
#include <wchar.h>

#define CH_REQUIRED_PLUGIN_COUNT 5u

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
