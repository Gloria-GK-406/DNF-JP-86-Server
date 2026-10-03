using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class CeraCargoCapacityRefreshTests
{
    [PvfTheory]
    [InlineData(100534, 12, 16, 0x0132)]
    [InlineData(100063, 2, 24, 0x0198)]
    public async Task Cash_upgrade_refreshes_capacity_before_purchase_success_and_never_repeats_on_refusal(
        int product, byte list, ushort capacity, ushort upgradeType)
    {
        using var shop = new CeraShopFixture(useArchive: true);
        shop.Host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        var input = CeraShopTestContent.Input(CeraShopTestContent.Entry(product));
        await shop.Handle(input);
        var frames = shop.Host.Sender.Messages.Select(shop.Encode).ToArray();
        Assert.True(frames.Length >= 3);
        Assert.Equal((byte)1, frames[0][0]);
        Assert.Equal(upgradeType, BinaryPrimitives.ReadUInt16LittleEndian(frames[0].AsSpan(1)));
        Assert.Equal(new byte[] { 1 }, frames[0][15..]);
        Assert.Equal((byte)0, frames[1][0]);
        Assert.Equal((ushort)0x000d, BinaryPrimitives.ReadUInt16LittleEndian(frames[1].AsSpan(1)));
        Assert.Equal(list, frames[1][15]);
        Assert.Equal(capacity, BinaryPrimitives.ReadUInt16LittleEndian(frames[1].AsSpan(16)));
        Assert.Equal(product, Assert.IsType<CeraShopPurchaseResponse>(shop.Host.Sender.Messages[2]).ProductId);
        Assert.Equal(capacity, shop.Host.Scalar(list == 12
            ? "SELECT selection_key FROM account_cargo_state WHERE account_id=1"
            : "SELECT list_param16 FROM character_container_state WHERE character_id=1 AND list_type=2"));
        Assert.Single(frames, frame => frame[0] == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == upgradeType);

        var paidBalance = shop.Cera;
        shop.Host.Sender.Messages.Clear();
        await shop.Handle(input);
        var refusal = Assert.IsType<CeraShopPurchaseResponse>(Assert.Single(shop.Host.Sender.Messages));
        Assert.Null(refusal.ProductId);
        Assert.Equal(paidBalance, shop.Cera);
    }
}
