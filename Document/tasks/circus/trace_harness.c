/* Synthetic ABI/recording fixture. This executable is not the game client. */
#include "clienthack/plugin_api.h"
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef int (__fastcall *cipher_fn)(void*, void*, int, const char*, int, char*, int*);
typedef void (__fastcall *setter_fn)(void*, void*, int, int);
static void* encrypt_detour;
static void* decrypt_detour;
static void* setter_detour;
static int hook_count;
static int patch_count;
static int setter_first;
static int setter_second;
static void* setter_self;

static void __fastcall original_setter(void* self, void* unused, int first, int second)
{
    (void)unused;
    setter_self = self;
    setter_first = first;
    setter_second = second;
}
static void CH_CALL log_line(uint32_t level, const char* component, const char* message)
{
    (void)level;
    printf("%s: %s\n", component, message);
}
static ch_status CH_CALL patch(const ch_patch_request* request)
{
    if (request->target_rva != 0x024A06A2u || request->length != 5)
        return CH_STATUS_INVALID_ARGUMENT;
    patch_count++;
    return CH_STATUS_OK;
}
static ch_status CH_CALL hook(const ch_hook_request* request)
{
    if (request->expected == NULL || request->expected_length == 0)
        return CH_STATUS_INVALID_ARGUMENT;
    hook_count++;
    if (request->target_rva == 0x022B7D90u) encrypt_detour = request->detour;
    else if (request->target_rva == 0x022B7E50u) decrypt_detour = request->detour;
    else if (request->target_rva == 0x003DB370u) {
        static const uint8_t signature[] = { 0x55, 0x8B, 0xEC, 0x8B, 0x45, 0x08 };
        if (request->expected_length != sizeof(signature) || memcmp(request->expected, signature, sizeof(signature)))
            return CH_STATUS_INCOMPATIBLE_MEMORY;
        setter_detour = request->detour;
#pragma warning(push)
#pragma warning(disable: 4055)
        *request->original = (void*)original_setter;
#pragma warning(pop)
    } else if (request->target_rva != 0x05390FE9u) return CH_STATUS_INVALID_ARGUMENT;
    return CH_STATUS_OK;
}

int main(int argc, char** argv)
{
    HMODULE library;
    ch_plugin_query_fn query;
    const ch_plugin_descriptor* descriptor;
    ch_host_api host = { 0 };
    unsigned char frame[32] = { 0 };
    char output[32] = { 0 };
    int output_size = 0;
    int fields[2] = { 4114, 0 };
    int self = 123;
    if (argc != 2) return 1;
    library = LoadLibraryA(argv[1]);
    if (!library) return 2;
#pragma warning(push)
#pragma warning(disable: 4055 4191)
    query = (ch_plugin_query_fn)GetProcAddress(library, CH_PLUGIN_QUERY_EXPORT);
    if (!query) return 3;
    descriptor = query();
    if (!descriptor || strcmp(descriptor->name, "A21RuntimeFix") || descriptor->abi_version != CLIENTHACK_ABI_VERSION)
        return 4;
    host.struct_size = sizeof(host);
    host.abi_version = CLIENTHACK_ABI_VERSION;
    host.dnf_base = 0x00400000u;
    host.log = log_line;
    host.patch_apply = patch;
    host.hook_create = hook;
    if (descriptor->initialize(&host) != CH_STATUS_OK || hook_count != 4 || patch_count != 1)
        return 5;
    frame[14] = 1;
    frame[15] = 3;
    /* LOGIN is intentionally passed to the hook and must not be recorded. */
    if (!((cipher_fn)encrypt_detour)(NULL, NULL, 1, (const char*)frame + 14, 2, output, &output_size)) return 6;
    if (!((cipher_fn)encrypt_detour)(NULL, NULL, 0x0048, (const char*)frame + 14, 2, output, &output_size)) return 7;
    if (output_size != 2 || output[0] != 1 || output[1] != 3) return 8;
    /* Deliberately outside the whitelist: discovery must preserve unknown sends. */
    if (!((cipher_fn)encrypt_detour)(NULL, NULL, 0x0357, (const char*)frame + 14, 2, output, &output_size)) return 12;
    if (!((cipher_fn)decrypt_detour)(NULL, NULL, 0x0282, (const char*)fields, sizeof(fields), output, &output_size)) return 9;
    if (output_size != sizeof(fields) || memcmp(output, fields, sizeof(fields))) return 10;
    ((setter_fn)setter_detour)(&self, NULL, fields[0], fields[1]);
    if (setter_self != &self || setter_first != 4114 || setter_second != 0) return 11;
#pragma warning(pop)
    FreeLibrary(library);
    puts("synthetic harness passed; no game client was executed");
    return 0;
}
