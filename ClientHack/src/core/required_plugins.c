#include "clienthack/required_plugins.h"

#include "clienthack/plugin_api.h"

#include <string.h>

typedef struct ch_required_identity {
    const wchar_t* filename;
    const char* descriptor_name;
} ch_required_identity;

static const ch_required_identity required_plugins[CH_REQUIRED_PLUGIN_COUNT] = {
    { L"0010_DisableGameGuard.dll", "DisableGameGuard" },
    { L"0020_ClientCompatibility.dll", "ClientCompatibility" },
    { L"0100_NetworkHook.dll", "NetworkHook" },
    { L"0110_ServerRedirect.dll", "ServerRedirect" },
    { L"0900_PreventMinimize.dll", "PreventMinimize" }
};

void ch_required_plugin_state_init(ch_required_plugin_state* state)
{
    if (state != NULL) state->initialized_mask = 0;
}

int ch_required_plugin_identify(
    const wchar_t* filename,
    const char* descriptor_name,
    uint32_t descriptor_flags,
    uint32_t* index)
{
    uint32_t i;
    if (filename == NULL) return 0;
    for (i = 0; i < CH_REQUIRED_PLUGIN_COUNT; ++i) {
        if (_wcsicmp(filename, required_plugins[i].filename) == 0) {
            if (descriptor_name == NULL ||
                strcmp(descriptor_name, required_plugins[i].descriptor_name) != 0 ||
                (descriptor_flags & CH_PLUGIN_REQUIRED) == 0) return -1;
            if (index != NULL) *index = i;
            return 1;
        }
    }
    return 0;
}

void ch_required_plugin_mark_initialized(ch_required_plugin_state* state, uint32_t index)
{
    if (state != NULL && index < CH_REQUIRED_PLUGIN_COUNT) {
        state->initialized_mask |= (uint32_t)1u << index;
    }
}

int ch_required_plugins_complete(const ch_required_plugin_state* state)
{
    const uint32_t complete = ((uint32_t)1u << CH_REQUIRED_PLUGIN_COUNT) - 1u;
    return state != NULL && state->initialized_mask == complete;
}
