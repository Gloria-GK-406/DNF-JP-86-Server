using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.AuraSkinSlot;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>CMD 0x0357: the cash-shop ticket opens the aura transmog avatar slot (#327).</summary>
public sealed class AuraSkinSlotTests
{
    private const int TicketProduct = 103818;
    private const int TicketItem = 490700411;

    [SkippableFact]
    public async Task Bought_ticket_opens_the_slot_once_and_is_spent()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var bought = await shop.Buy(CeraShopTestContent.Entry(TicketProduct));
        Assert.True(bought.Receipt is not null, bought.Detail);
        var slot = TicketSlot(bought.Receipt!);
        Assert.Equal(0, shop.Host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));

        await EffectRequestTransport.SendRaw(shop.Host, new(1, 0x357, 0, 7), BitConverter.GetBytes(slot));

        var response = Assert.Single(shop.Host.Sender.Messages.OfType<AuraSkinSlotResponse>());
        Assert.Null(response.ErrorCode);
        Assert.Single(shop.Host.Sender.Messages.OfType<InventoryDeltaMessage>());
        Assert.Equal(1, shop.Host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));
        Assert.Equal(0, shop.Host.Count(TicketItem));
        // This fixture never selects the character, so there is no current view to rebuild USERINFO
        // from; where subtype 1 puts the flag is covered by the wire test.

        // A repeat that still arrives spends nothing and says the slot is already open.
        shop.Host.Sender.Messages.Clear();
        var second = await shop.Buy(CeraShopTestContent.Entry(TicketProduct));
        await EffectRequestTransport.SendRaw(shop.Host, new(1, 0x357, 0, 8), BitConverter.GetBytes(TicketSlot(second.Receipt!)));
        Assert.Equal(AuraSkinSlotResponse.AlreadyOpen, Assert.Single(shop.Host.Sender.Messages.OfType<AuraSkinSlotResponse>()).ErrorCode);
        Assert.Equal(1, shop.Host.Count(TicketItem));
    }

    [SkippableFact]
    public async Task A_slot_without_the_ticket_is_refused_without_opening()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var bought = await shop.Buy(CeraShopTestContent.Entry());
        Assert.True(bought.Receipt is not null, bought.Detail);
        var other = bought.Receipt!.Updates.SelectMany(u => u.Items).Single().SlotIndex;

        await EffectRequestTransport.SendRaw(shop.Host, new(1, 0x357, 0, 7), BitConverter.GetBytes(other));

        Assert.Equal(AuraSkinSlotResponse.MissingTicket, Assert.Single(shop.Host.Sender.Messages.OfType<AuraSkinSlotResponse>()).ErrorCode);
        Assert.Equal(0, shop.Host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));
        Assert.Equal(1, shop.Host.Count(3));
    }

    private static int TicketSlot(CeraShopPurchaseReceipt receipt) =>
        receipt.Updates.Where(u => u.ContainerType == InventoryContainerType.Main)
            .SelectMany(u => u.Items).Single(i => i.Core.ItemId == TicketItem).SlotIndex;
}
