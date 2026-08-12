#include "clienthack/plugin_api.h"
#include "clienthack/runtime_signatures.h"

#define ARRAY_COUNT(values) (sizeof(values) / sizeof((values)[0]))

typedef struct gameguard_patch {
    uintptr_t rva;
    const uint8_t* expected;
    const uint8_t* replacement;
    uint32_t length;
    const char* name;
} gameguard_patch;

static const uint8_t return_true[] = { 0x31, 0xC0, 0x40, 0xC2, 0x04, 0x00 };
static const uint8_t jump_005F80FD[] = { 0xE9, 0x8E, 0x06, 0x00, 0x00, 0x90 };
static const uint8_t jump_short[] = { 0xEB };
static const uint8_t nop2[] = { 0x90, 0x90 };
static const uint8_t jump_018AD5FB[] = { 0xE9, 0xB9, 0x00 };
static const uint8_t prologue_00AB5C80[] = { 0x55, 0x8B, 0xEC, 0x6A, 0xFF };
static const uint8_t decrypted_az_string[] = {
    0xA1, 0x2C, 0x00, 0x00,
    0x61, 0x00, 0x7A, 0x00, 0x2E, 0x00, 0x64, 0x00,
    0x6C, 0x00, 0x6C, 0x00, 0x00, 0x00
};

static const gameguard_patch patches[] = {
    { 0x005F7CE0u, CH_SIG_N5_CHECK_1, return_true, sizeof(return_true), "n5-check-1" },
    { 0x00D8A160u, CH_SIG_N5_CHECK_2, return_true, sizeof(return_true), "n5-check-2" },
    { 0x00D8B1D0u, CH_SIG_N5_CHECK_3, return_true, sizeof(return_true), "n5-check-3" },
    { 0x005F80FDu, CH_ORIG_PROTECTION_005F80FD, jump_005F80FD,
        sizeof(jump_005F80FD), "skip-tcls-mode-init" },
    { 0x00914B81u, CH_ORIG_PROTECTION_00914B81, jump_short,
        sizeof(jump_short), "skip-encrypted-channel-key" },
    { 0x00B9EB0Au, CH_ORIG_PROTECTION_00B9EB0A, nop2,
        sizeof(nop2), "bypass-safe-storage-init" },
    { 0x02122BDFu, CH_ORIG_PROTECTION_02122BDF, nop2,
        sizeof(nop2), "bypass-startup-validation" },
    { 0x01120157u, CH_ORIG_PROTECTION_01120157, jump_short,
        sizeof(jump_short), "bypass-version-signature" },
    { 0x0189D104u, CH_ORIG_PROTECTION_0189D104, jump_short,
        sizeof(jump_short), "skip-event-6f" },
    { 0x018AD5FBu, CH_ORIG_PROTECTION_018AD5FB, jump_018AD5FB,
        sizeof(jump_018AD5FB), "skip-game-state-verification" },
    { 0x00AB5C80u, CH_ORIG_PROTECTION_00AB5C80, prologue_00AB5C80,
        sizeof(prologue_00AB5C80), "restore-prologue" },
    { 0x028D5C9Cu, CH_ORIG_AZ_STRING_028D5C9C, decrypted_az_string,
        sizeof(decrypted_az_string), "decrypt-az-module-name" }
};

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    size_t i;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION || host->patch_apply == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    for (i = 0; i < ARRAY_COUNT(patches); ++i) {
        const gameguard_patch* patch = &patches[i];
        ch_patch_request request = {
            sizeof(request), patch->rva, patch->expected,
            patch->replacement, patch->length, patch->name
        };
        ch_status status = host->patch_apply(&request);
        if (status != CH_STATUS_OK) return status;
    }
    return CH_STATUS_OK;
}
