#include <windows.h>
#include <stdio.h>
#include "../plugins/0150_cera_ticket_failure_fix/cera_ticket_failure_fix.c"

#define CHECK(x) do { if (!(x)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); return 1; } } while (0)
static ticket_handler installed;
static uint32_t original_calls, last_type, last_success, last_error;
static void* last_context;
static void __cdecl original(uint32_t type, uint32_t success, uint32_t error, void* context)
{
    ++original_calls;
    last_type = type; last_success = success; last_error = error; last_context = context;
}
static ch_status CH_CALL install(const ch_hook_request* request)
{
    if (request->target_rva != TICKET_HANDLER_RVA || request->expected_length != sizeof(ticket_signature)
        || memcmp((void*)(g_host->dnf_base + request->target_rva), request->expected, request->expected_length))
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    installed = (ticket_handler)request->detour;
    *request->original = (void*)original;
    return CH_STATUS_OK;
}
int main(void)
{
    uint8_t* image = VirtualAlloc(NULL, GAME_CONTEXT_SLOT_RVA + 4096u, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    uint8_t* game = VirtualAlloc(NULL, CASH_SHOP_OFFSET + 4096u, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    uint8_t* shop = VirtualAlloc(NULL, CASH_SHOP_WAIT_OFFSET + 4096u, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    ch_host_api host = { 0 };
    CHECK(sizeof(uintptr_t) == 4u && image != NULL && game != NULL && shop != NULL);
    host.struct_size = sizeof(host); host.abi_version = CLIENTHACK_ABI_VERSION;
    host.dnf_base = (uintptr_t)image; host.hook_create = install;
    CHECK(ch_plugin_initialize(NULL) == CH_STATUS_UNSUPPORTED_ABI);
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_INCOMPATIBLE_MEMORY && installed == NULL);
    memcpy(image + CASH_SHOP_GETTER_RVA, getter_signature, sizeof(getter_signature));
    memcpy(image + CASH_SHOP_CLEAR_RVA, clear_signature, sizeof(clear_signature));
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_INCOMPATIBLE_MEMORY && installed == NULL);
    memcpy(image + TICKET_HANDLER_RVA, ticket_signature, sizeof(ticket_signature));
    CHECK(ch_plugin_initialize(&host) == CH_STATUS_OK && installed != NULL);
    *(uintptr_t*)(image + GAME_CONTEXT_SLOT_RVA) = (uintptr_t)game;
    *(uintptr_t*)(game + CASH_SHOP_OFFSET) = (uintptr_t)shop;
    shop[CASH_SHOP_WAIT_OFFSET] = 1;
    installed(0x41, 1, 77, game);
    CHECK(original_calls == 1 && shop[CASH_SHOP_WAIT_OFFSET] == 1);
    CHECK(last_type == 0x41 && last_success == 1 && last_error == 77 && last_context == game);
    installed(0x41, 0, 1, shop);
    CHECK(original_calls == 2 && shop[CASH_SHOP_WAIT_OFFSET] == 0);
    CHECK(last_type == 0x41 && last_success == 0 && last_error == 1 && last_context == shop);
    *(uintptr_t*)(game + CASH_SHOP_OFFSET) = 0;
    installed(0x41, 0, 1, NULL);
    *(uintptr_t*)(image + GAME_CONTEXT_SLOT_RVA) = 0;
    installed(0x41, 0, 1, NULL);
    CHECK(original_calls == 4);
    VirtualFree(shop, 0, MEM_RELEASE); VirtualFree(game, 0, MEM_RELEASE); VirtualFree(image, 0, MEM_RELEASE);
    puts("CERA ticket failure: callback ABI, signatures, failure unlock, success and null-context checks passed");
    return 0;
}
