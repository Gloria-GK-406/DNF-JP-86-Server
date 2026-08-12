#include "clienthack/plugin_api.h"

#define ARRAY_COUNT(values) (sizeof(values) / sizeof((values)[0]))

typedef struct compatibility_patch {
    uintptr_t rva;
    const uint8_t* expected;
    const uint8_t* replacement;
    uint32_t length;
    const char* name;
} compatibility_patch;

static const uint8_t original_codepage[] = { 0x68, 0xA8, 0x03, 0x00, 0x00 };
static const uint8_t patched_codepage[] = { 0x68, 0xE9, 0xFD, 0x00, 0x00 };
static const uint8_t original_branch[] = { 0x74 };
static const uint8_t patched_branch[] = { 0xEB };
static const uint8_t original_opcode[] = {
    0x0F, 0xB7, 0x46, 0x01, 0x57, 0x83, 0xF8, 0x57, 0x7F, 0x15, 0x74, 0x1A
};
static const uint8_t patched_opcode[] = {
    0x0F, 0xB7, 0x46, 0x07, 0x57, 0x83, 0xF8, 0x00, 0x7F, 0x1C, 0x74, 0x0A
};
static const uint8_t original_jump[] = { 0x75, 0x3F };
static const uint8_t patched_jump[] = { 0xEB, 0x3F };
static const uint8_t original_limit[] = { 0xBF, 0x06, 0x00, 0x00, 0x00 };
static const uint8_t patched_limit[] = { 0xBF, 0x03, 0x00, 0x00, 0x00 };

static const compatibility_patch patches[] = {
    { 0x00023EB0u, original_codepage, patched_codepage, sizeof(original_codepage), "utf8-codepage" },
    { 0x00B54606u, original_branch, patched_branch, sizeof(original_branch), "legacy-branch" },
    { 0x00D89628u, original_opcode, patched_opcode, sizeof(original_opcode), "noti-character" },
    { 0x00F753A8u, original_jump, patched_jump, sizeof(original_jump), "legacy-jump" },
    { 0x018BFC00u, original_limit, patched_limit, sizeof(original_limit), "channel-limit" }
};

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    size_t i;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->patch_apply == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    for (i = 0; i < ARRAY_COUNT(patches); ++i) {
        ch_patch_request request = {
            sizeof(request), patches[i].rva, patches[i].expected,
            patches[i].replacement, patches[i].length, patches[i].name
        };
        ch_status status = host->patch_apply(&request);
        if (status != CH_STATUS_OK) return status;
    }
    return CH_STATUS_OK;
}
