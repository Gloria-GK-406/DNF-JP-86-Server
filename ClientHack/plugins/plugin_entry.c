#include "clienthack/plugin_api.h"
#include "plugin_logic.h"

#ifndef CLIENTHACK_PLUGIN_NAME
#error CLIENTHACK_PLUGIN_NAME must be defined by the plugin target
#endif

#ifndef CLIENTHACK_PLUGIN_FLAGS
#define CLIENTHACK_PLUGIN_FLAGS 0u
#endif

#if defined(CLIENTHACK_PLUGIN_HAS_SHUTDOWN)
#define CLIENTHACK_PLUGIN_SHUTDOWN ch_plugin_shutdown
#else
#define CLIENTHACK_PLUGIN_SHUTDOWN NULL
#endif

static const ch_plugin_descriptor descriptor = {
    sizeof(descriptor), CLIENTHACK_ABI_VERSION, CLIENTHACK_PLUGIN_FLAGS,
    CLIENTHACK_PLUGIN_NAME, ch_plugin_initialize, CLIENTHACK_PLUGIN_SHUTDOWN
};

CH_EXPORT const ch_plugin_descriptor* CH_CALL ClientHackPlugin_Query(void)
{
    return &descriptor;
}
