#include "clienthack/plugin_api.h"

static const uint8_t expected_call[] = { 0xFF, 0x15, 0x60, 0x35, 0x7D, 0x02 };
static const uint8_t replacement[] = { 0x83, 0xC4, 0x10, 0x31, 0xC0, 0x90 };

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    ch_patch_request request = {
        sizeof(request), 0x0099E698u, expected_call, replacement,
        sizeof(expected_call), "prevent-minimize-other-windows"
    };
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->patch_apply == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    return host->patch_apply(&request);
}
