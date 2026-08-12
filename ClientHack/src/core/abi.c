#include "clienthack/abi.h"

int ch_plugin_descriptor_is_valid(const ch_plugin_descriptor* descriptor)
{
    if (descriptor == NULL ||
        descriptor->struct_size < sizeof(*descriptor) ||
        descriptor->abi_version != CLIENTHACK_ABI_VERSION ||
        descriptor->name == NULL || descriptor->name[0] == '\0' ||
        descriptor->initialize == NULL) {
        return 0;
    }
    return (descriptor->flags & ~CH_PLUGIN_REQUIRED) == 0;
}
