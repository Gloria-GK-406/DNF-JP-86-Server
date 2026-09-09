/* A21 CMD0041's failure branch returns without releasing the cash-shop wait
 * flag. The close button checks that flag before returning to the game.
 * Evidence: Document/CeraShop-AbilityCase-20260909.md. No success ticket or
 * packet data is changed; the original four-argument callback runs first. */
#include "clienthack/plugin_api.h"
#include <string.h>

#define TICKET_HANDLER_RVA 0x00D1A0F0u
#define GAME_CONTEXT_SLOT_RVA 0x0365C9B8u
#define CASH_SHOP_GETTER_RVA 0x01F00D10u
#define CASH_SHOP_CLEAR_RVA 0x0071BF60u
#define CASH_SHOP_OFFSET 0x8FACu
#define CASH_SHOP_WAIT_OFFSET 0x20C20u

static const uint8_t ticket_signature[] = { 0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x88, 0, 0, 0 };
static const uint8_t getter_signature[] = { 0x8B, 0x81, 0xAC, 0x8F, 0, 0, 0xC3 };
static const uint8_t clear_signature[] = { 0xC6, 0x81, 0x20, 0x0C, 0x02, 0, 0, 0xC3 };
typedef void (__cdecl *ticket_handler)(uint32_t type, uint32_t success, uint32_t error, void* context);
static const ch_host_api* g_host;
static void* g_original;

static void __cdecl ticket_response(uint32_t type, uint32_t success, uint32_t error, void* context)
{
    uintptr_t game;
    uintptr_t shop;
    ((ticket_handler)g_original)(type, success, error, context);
    if ((success & 0xFFu) != 0u) return;
    game = *(const uintptr_t*)(g_host->dnf_base + GAME_CONTEXT_SLOT_RVA);
    if (game == 0u) return;
    shop = *(const uintptr_t*)(game + CASH_SHOP_OFFSET);
    if (shop == 0u) return;
    *(volatile uint8_t*)(shop + CASH_SHOP_WAIT_OFFSET) = 0;
    if (g_host->log != NULL)
        g_host->log(2, "CeraTicketFailureFix", "CMD0041 failure released cash-shop wait");
}

ch_status CH_CALL ch_plugin_initialize(const ch_host_api* host)
{
    ch_hook_request request;
    if (host == NULL || host->struct_size < sizeof(*host)
        || host->abi_version != CLIENTHACK_ABI_VERSION || host->dnf_base == 0u
        || host->hook_create == NULL) return CH_STATUS_UNSUPPORTED_ABI;
    /* These routines prove the offsets used above belong to this client. */
    if (memcmp((const void*)(host->dnf_base + CASH_SHOP_GETTER_RVA), getter_signature, sizeof(getter_signature)) != 0
        || memcmp((const void*)(host->dnf_base + CASH_SHOP_CLEAR_RVA), clear_signature, sizeof(clear_signature)) != 0)
        return CH_STATUS_INCOMPATIBLE_MEMORY;
    g_host = host;
    request.struct_size = sizeof(request);
    request.target_rva = TICKET_HANDLER_RVA;
    request.expected = ticket_signature;
    request.expected_length = sizeof(ticket_signature);
    request.detour = (void*)ticket_response;
    request.original = &g_original;
    request.name = "a21-cera-ticket-failure-unlock";
    return host->hook_create(&request);
}
