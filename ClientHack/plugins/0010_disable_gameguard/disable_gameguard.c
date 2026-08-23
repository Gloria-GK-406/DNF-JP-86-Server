#include "clienthack/plugin_api.h"

#define ARRAY_COUNT(values) (sizeof(values) / sizeof((values)[0]))

typedef struct gameguard_patch {
    uintptr_t rva;
    const uint8_t* expected;
    const uint8_t* replacement;
    uint32_t length;
    const char* name;
} gameguard_patch;

/* Every address here belongs to ONE packed client, so the two tables never
 * mix: the build picks exactly one. Both say the same things about the same
 * checks -- the client differs, the intent does not. */
#if !defined(CLIENTHACK_CLIENT_A21)

#include "clienthack/runtime_signatures.h"

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

#else   /* ---------------- S4A21 ---------------- */

/* Transcribed from the upstream S4A21 client patch
 * (S4A21ClientPatch/GameGaurd/GameGaurd.cpp, StaticPatches). Upstream writes
 * these blind; the expected bytes below were read back out of an S4A21 memory
 * dump at each RVA so the host can refuse to patch a client that does not
 * actually look like this. That check is the whole reason a wrong-client build
 * fails loudly instead of corrupting code at a plausible-looking address.
 *
 * Addresses upstream states as VAs; RVA = VA - 0x00400000. */
static const uint8_t return_true[] = { 0x31, 0xC0, 0x40, 0xC2, 0x04, 0x00 };
static const uint8_t jump_tcls_mode_init[] = { 0xE9, 0x6B, 0x06, 0x00, 0x00, 0x90 };
static const uint8_t jump_short[] = { 0xEB };
static const uint8_t nop2[] = { 0x90, 0x90 };
static const uint8_t jump_wwinmain[] = { 0xEB, 0x0A };
static const uint8_t branch_exception_filter[] = { 0x75, 0x67 };
static const uint8_t jump_game_state_verify[] = { 0xE9, 0xA2, 0x00 };
static const uint8_t restored_prologue[] = {
    0xE8, 0x33, 0x55, 0x51, 0x00, 0xA1, 0xE0, 0xCE,
    0x99, 0x03, 0x31, 0xE8, 0x89, 0x45, 0xFC, 0xA1,
    0xE4, 0xCE, 0xB7, 0x03
};
static const uint8_t decrypted_az_string[] = {
    0xA1, 0x2C, 0x00, 0x00,
    0x61, 0x00, 0x7A, 0x00, 0x2E, 0x00, 0x64, 0x00,
    0x6C, 0x00, 0x6C, 0x00, 0x00, 0x00
};

static const uint8_t orig_tcls_mode_init[] = { 0x0F, 0x84, 0x6A, 0x06, 0x00, 0x00 };
static const uint8_t orig_channel_key[] = { 0x74 };
static const uint8_t orig_safe_storage[] = { 0x75, 0x3B };
static const uint8_t orig_startup_validation[] = { 0x74, 0x0A };
static const uint8_t orig_wwinmain_check[] = { 0xE8, 0xE9 };
static const uint8_t orig_exception_filter[] = { 0x75, 0x5B };
static const uint8_t orig_version_signature[] = { 0x74 };
static const uint8_t orig_game_state_verify[] = { 0x0F, 0x84, 0xA1 };
static const uint8_t orig_packed_prologue[] = {
    0x54, 0x89, 0x24, 0x24, 0x68, 0x4A, 0xE0, 0xDC,
    0x01, 0xE8, 0xBC, 0x1F, 0x8B, 0x02, 0xE8, 0xDE,
    0xAF, 0xEA, 0x05, 0x03
};
static const uint8_t orig_n5_check_1[] = { 0x55, 0x8B, 0xEC, 0x51, 0x56, 0x8B };
static const uint8_t orig_n5_check_2[] = { 0x55, 0x8B, 0xEC, 0x8A, 0x01, 0x8B };
static const uint8_t orig_n5_check_3[] = { 0x55, 0x8B, 0xEC, 0x53, 0x8B, 0x5D };
static const uint8_t orig_az_string[] = {
    0x00, 0x54, 0x58, 0x2A, 0xA0, 0x4C, 0x14, 0x9A,
    0x12, 0xF0, 0x69, 0x4F, 0x73, 0x28, 0x52, 0x70,
    0xAA, 0xDC
};

static const gameguard_patch patches[] = {
    /* n5 anti-cheat entry points -> return 1 (pass). Same shape as S4A12:
     * a function prologue replaced by xor eax,eax / inc eax / ret 4. */
    { 0x00A0CBF0u, orig_n5_check_1, return_true, sizeof(return_true), "n5-check-1" },
    { 0x0123C650u, orig_n5_check_2, return_true, sizeof(return_true), "n5-check-2" },
    { 0x0123D8B0u, orig_n5_check_3, return_true, sizeof(return_true), "n5-check-3" },
    /* parseCommandLineChina: skip TCLS mode init (JZ near -> JMP near + NOP). */
    { 0x00A0D04Cu, orig_tcls_mode_init, jump_tcls_mode_init,
        sizeof(jump_tcls_mode_init), "skip-tcls-mode-init" },
    /* NOTIFUNC_CHANNELINFO: skip the encrypted key section (JZ -> JMP). */
    { 0x00D69661u, orig_channel_key, jump_short, sizeof(jump_short),
        "skip-encrypted-channel-key" },
    /* QQSafeStorage::Init: bypass (JNZ -> NOP NOP). */
    { 0x0103E97Au, orig_safe_storage, nop2, sizeof(nop2),
        "bypass-safe-storage-init" },
    /* startup init: bypass the validation check (JZ -> NOP NOP). */
    { 0x0286C8D4u, orig_startup_validation, nop2, sizeof(nop2),
        "bypass-startup-validation" },
    /* wWinMain check. */
    { 0x02393BF2u, orig_wwinmain_check, jump_wwinmain, sizeof(jump_wwinmain),
        "fix-wwinmain-check" },
    /* TopLevelExceptionFilter check + event: retarget the branch. */
    { 0x011479E3u, orig_exception_filter, branch_exception_filter,
        sizeof(branch_exception_filter), "fix-exception-filter" },
    /* version signature check (JZ -> JMP). */
    { 0x01673BBCu, orig_version_signature, jump_short, sizeof(jump_short),
        "bypass-version-signature" },
    /* game state verification (JZ near -> JMP near). */
    { 0x01F1467Du, orig_game_state_verify, jump_game_state_verify,
        sizeof(jump_game_state_verify), "skip-game-state-verification" },
    /* a prologue the packer destroyed, restored to the official bytes. */
    { 0x0234E6D8u, orig_packed_prologue, restored_prologue,
        sizeof(restored_prologue), "restore-prologue" },
    /* The module name the client decrypts before GetModuleHandleW: overwritten
     * with the already-decrypted "az.dll" so the lookup finds our DLL. */
    { 0x031E997Cu, orig_az_string, decrypted_az_string,
        sizeof(decrypted_az_string), "decrypt-az-module-name" }
};

#endif

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
