#ifndef CLIENTHACK_PLUGIN_LOGIC_H
#define CLIENTHACK_PLUGIN_LOGIC_H

#include "clienthack/plugin_api.h"

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host);

#if defined(CLIENTHACK_PLUGIN_HAS_SHUTDOWN)
void CH_CALL ch_plugin_shutdown(void);
#endif

#endif
