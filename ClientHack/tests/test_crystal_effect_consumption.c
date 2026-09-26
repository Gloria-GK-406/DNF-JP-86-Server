#include "clienthack/plugin_api.h"
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host);
static uint8_t* image;
static int writes;
static int failures;
#define CHECK(x) do { if (!(x)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); ++failures; } } while (0)
static ch_status CH_CALL apply(const ch_patch_request* request)
{
    ++writes;
    memcpy(image + request->target_rva, request->replacement, request->length);
    return CH_STATUS_OK;
}
int main(void)
{
    static const uint8_t first[] = { 0xE8,0xB2,0xBC,0xFF,0xFF,0x84,0xC0,0x0F,0x85,0xBB,0,0,0 };
    static const uint8_t second[] = { 0xE8,0x00,0xAC,0xFF,0xFF,0x84,0xC0,0x0F,0x85,0xBF,0,0,0 };
    ch_host_api host = { 0 };
    image = (uint8_t*)calloc(0x00A9827Bu + sizeof(second), 1);
    if (image == NULL) return 2;
    host.struct_size = sizeof(host); host.abi_version = CLIENTHACK_ABI_VERSION;
    host.dnf_base = (uintptr_t)image; host.patch_apply = apply;
    memcpy(image + 0x00A971C9u, first, sizeof(first));
    memcpy(image + 0x00A9827Bu, second, sizeof(second));
    image[0x00A9827Bu] ^= 1;
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_INCOMPATIBLE_MEMORY);
    CHECK(writes == 0);
    CHECK(memcmp(image + 0x00A971C9u, first, sizeof(first)) == 0);
    image[0x00A9827Bu] ^= 1;
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_OK);
    CHECK(writes == 2);
    CHECK(memcmp(image + 0x00A971C9u, first, 7) == 0);
    CHECK(memcmp(image + 0x00A9827Bu, second, 7) == 0);
    for (int i = 0; i < 6; ++i) {
        CHECK(image[0x00A971D0u + i] == 0x90);
        CHECK(image[0x00A98282u + i] == 0x90);
    }
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_OK);
    CHECK(ch_plugin_initialize(NULL) == CH_STATUS_UNSUPPORTED_ABI);
    free(image);
    return failures == 0 ? 0 : 1;
}
