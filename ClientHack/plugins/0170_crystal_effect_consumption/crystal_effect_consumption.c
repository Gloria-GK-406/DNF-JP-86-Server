/* A21: retain native material checks/serialization, remove only the crystal
 * contract exception from the two effect-material list builders. The TCP
 * sender remains CNRDItemManager::requestDeleteItems(operation=3).
 * VA = RVA + 0x00400000. See Document/outputs/crystal-contract-20260926/
 * effect-consumption-patch.md for image identity and evidence boundaries. */
#include "clienthack/plugin_api.h"
#include <string.h>

typedef struct effect_patch {
    uintptr_t rva;
    uint8_t expected[13];
    uint8_t replacement[13];
    const char* name;
} effect_patch;

static const effect_patch patches[] = {
    { 0x00A971C9u,
      { 0xE8,0xB2,0xBC,0xFF,0xFF,0x84,0xC0,0x0F,0x85,0xBB,0,0,0 },
      { 0xE8,0xB2,0xBC,0xFF,0xFF,0x84,0xC0,0x90,0x90,0x90,0x90,0x90,0x90 },
      "a21-crystal-effect-material-list" },
    { 0x00A9827Bu,
      { 0xE8,0x00,0xAC,0xFF,0xFF,0x84,0xC0,0x0F,0x85,0xBF,0,0,0 },
      { 0xE8,0x00,0xAC,0xFF,0xFF,0x84,0xC0,0x90,0x90,0x90,0x90,0x90,0x90 },
      "a21-crystal-effect-material-list-secondary" }
};

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    size_t i;
    if (host == NULL || host->struct_size < sizeof(*host)
        || host->abi_version != CLIENTHACK_ABI_VERSION || host->dnf_base == 0u
        || host->patch_apply == NULL) return CH_STATUS_UNSUPPORTED_ABI;
    /* Validate both instruction sequences before the first write. The host
     * owns rollback if a later write fails. Already-applied bytes are valid. */
    for (i = 0; i < sizeof(patches) / sizeof(patches[0]); ++i) {
        const effect_patch* patch = &patches[i];
        const void* actual = (const void*)(host->dnf_base + patch->rva);
        if (memcmp(actual, patch->expected, sizeof(patch->expected)) != 0
            && memcmp(actual, patch->replacement, sizeof(patch->replacement)) != 0)
            return CH_STATUS_INCOMPATIBLE_MEMORY;
    }
    for (i = 0; i < sizeof(patches) / sizeof(patches[0]); ++i) {
        const effect_patch* patch = &patches[i];
        ch_patch_request request = { sizeof(request), patch->rva,
            patch->expected, patch->replacement, sizeof(patch->expected), patch->name };
        ch_status status = host->patch_apply(&request);
        if (status != CH_STATUS_OK) return status;
    }
    if (host->log != NULL)
        host->log(2, "CrystalEffectConsumption", "Enabled native effect consumption (CMD0012 operation=3)");
    return CH_STATUS_OK;
}
