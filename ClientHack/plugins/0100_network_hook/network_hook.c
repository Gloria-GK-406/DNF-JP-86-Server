#include "clienthack/plugin_api.h"
#include "clienthack/runtime_signatures.h"

#include <windows.h>
#include <string.h>

enum {
    STAGE_OFFSET = 0x15E218u,
    STAGE_SIZE = 0x0AF000u,
    STAGE_WPOS_OFFSET = STAGE_OFFSET + STAGE_SIZE,
    STAGE_CS_OFFSET = STAGE_OFFSET + STAGE_SIZE + 8u
};

static void* g_original_encrypt;
static void* g_original_decrypt;
static void* g_original_socket_write;

static int __fastcall cipher_passthrough(
    void* self, void* edx_value, int packet_type, const char* input,
    int input_size, char* output, int* output_size)
{
    (void)self;
    (void)edx_value;
    (void)packet_type;
    if (input == NULL || output == NULL || output_size == NULL || input_size < 0) return 0;
    if (input_size != 0) memcpy(output, input, (size_t)input_size);
    *output_size = input_size;
    return 1;
}

static int __fastcall socket_write(
    void* self, void* edx_value, const char* buffer, int size)
{
    uint8_t* base = (uint8_t*)self;
    CRITICAL_SECTION* critical_section;
    DWORD* write_position;
    DWORD position;
    (void)edx_value;

    if (base == NULL || base[8] == 0) return (int)0x80000054u;
    if (buffer == NULL || size <= 0) return -1;
    critical_section = (CRITICAL_SECTION*)(base + STAGE_CS_OFFSET);
    write_position = (DWORD*)(base + STAGE_WPOS_OFFSET);
    EnterCriticalSection(critical_section);
    position = *write_position;
    if (position >= STAGE_SIZE || (DWORD)size >= STAGE_SIZE - position) {
        LeaveCriticalSection(critical_section);
        return -2;
    }
    memcpy(base + STAGE_OFFSET + position, buffer, (size_t)size);
    *write_position = position + (DWORD)size;
    LeaveCriticalSection(critical_section);
    return 0;
}

static ch_status apply_network_xor_patch(const ch_host_api* host)
{
    static const uint8_t nops[5] = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    ch_patch_request request = {
        sizeof(request), 0x00D8999Bu, CH_ORIG_NETWORK_XOR_00D8999B,
        nops, sizeof(nops), "disable-fixed-key-xor"
    };
    return host->patch_apply(&request);
}

static ch_status create_hook(const ch_host_api* host, uintptr_t rva,
    const uint8_t* expected, uint32_t expected_length, void* detour,
    void** original, const char* name)
{
    ch_hook_request request = {
        sizeof(request), rva, expected, expected_length,
        detour, original, name
    };
    return host->hook_create(&request);
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    ch_status status;
    if (host == NULL || host->struct_size < sizeof(*host) ||
        host->abi_version != CLIENTHACK_ABI_VERSION ||
        host->patch_apply == NULL || host->hook_create == NULL) {
        return CH_STATUS_UNSUPPORTED_ABI;
    }
    status = apply_network_xor_patch(host);
    if (status != CH_STATUS_OK) return status;
#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable: 4055)
#endif
    status = create_hook(host, 0x01C11360u, CH_SIG_CIPHER_ENCRYPT,
        sizeof(CH_SIG_CIPHER_ENCRYPT), (void*)cipher_passthrough,
        &g_original_encrypt, "cipher-encrypt-passthrough");
    if (status == CH_STATUS_OK) {
        status = create_hook(host, 0x01C11420u, CH_SIG_CIPHER_DECRYPT,
            sizeof(CH_SIG_CIPHER_DECRYPT), (void*)cipher_passthrough,
            &g_original_decrypt, "cipher-decrypt-passthrough");
    }
    if (status == CH_STATUS_OK) {
        status = create_hook(host, 0x048FF9ABu, CH_SIG_SOCKET_WRITE,
            sizeof(CH_SIG_SOCKET_WRITE), (void*)socket_write,
            &g_original_socket_write, "socket-write-staging");
    }
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
    return status;
}
