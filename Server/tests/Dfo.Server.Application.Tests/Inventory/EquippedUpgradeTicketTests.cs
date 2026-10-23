using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquippedUpgradeTicketTests
{
    [PvfTheory]
    [InlineData(0, 6, InventoryContainerType.Equipment)]
    [InlineData(15284, 6, InventoryContainerType.Equipment)]
    [InlineData(15285, 7, InventoryContainerType.Equipment)]
    [InlineData(99999, 13, InventoryContainerType.Equipment)]
    [InlineData(0, 6, InventoryContainerType.Main)]
    public async Task Blessing_ticket_overwrites_the_level_once_without_gold_or_equipment_replacement(
        int draw, byte level, InventoryContainerType container)
    {
        using var archive = new PvfArchiveProvider(new());
        using var shop = Fixture(archive, draw);
        await Seed(shop, 0, 2682157, 12);
        var core = StackableItemCore.Create(1, 3000, 1234) with { Attribute = 0xB4, Durability = 45, EnchantCardId = 42 };
        await shop.SeedCore(12, core, container);
        var before = shop.StoredCore(12, container);
        var uid = shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_id=1 AND list_type={(int)container} AND slot_index=12");
        await Send(shop, 0, 2682157, 12, container);
        var ack = Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(ack.Success);
        Assert.Equal(level, ack.NewLevel);
        before[9] = (byte)(0xA0 | level);
        Assert.Equal(before, shop.StoredCore(12, container));
        Assert.Equal(uid, shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_id=1 AND list_type={(int)container} AND slot_index=12"));
        Assert.Equal(1, shop.Count(2682157));
        Assert.Equal(100000, shop.Gold);
        await Send(shop, 0, 2682157, 12, container);
        Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Equal(1, shop.Count(2682157));
    }

    [PvfTheory]
    [InlineData(2682155)]
    [InlineData(2682156)]
    public async Task Blessing_ticket_for_another_equipment_family_does_not_consume_assets(int ticket)
    {
        using var archive = new PvfArchiveProvider(new());
        using var shop = Fixture(archive);
        await Seed(shop, 0, ticket, 12);
        var before = shop.StoredCore(12, InventoryContainerType.Equipment);
        await Send(shop, 0, ticket, 12);
        Assert.False(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(before, shop.StoredCore(12, InventoryContainerType.Equipment));
        Assert.Equal(2, shop.Count(ticket));
        Assert.Equal(100000, shop.Gold);
    }

    [PvfTheory]
    [InlineData(0, 7275, 12, 12)]
    [InlineData(0, 7275, 12, 20)]
    [InlineData(1, 10008100, 11, 12)]
    public async Task Ticket_updates_only_equipped_target_and_replays_without_another_debit(
        byte method, int ticket, byte level, short slot)
    {
        using var archive = new PvfArchiveProvider(new());
        using var shop = Fixture(archive);
        await Seed(shop, method, ticket, slot);
        var before = shop.StoredCore(slot, InventoryContainerType.Equipment);
        var bagBefore = shop.StoredCore(slot);
        var uid = shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_id=1 AND list_type=3 AND slot_index={slot}");

        await Send(shop, method, ticket, slot);

        var ack = Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(ack.Success);
        Assert.Equal(method, ack.Method);
        Assert.Equal(level, ack.NewLevel);
        var wire = Encode(shop, ack);
        Assert.Equal((byte)3, wire[15 + 14]);
        Assert.Equal(slot, BitConverter.ToInt16(wire, 15 + 15));
        var equippedDelta = Assert.Single(shop.Sender.Messages.Select(message => Encode(shop, message)),
            frame => frame[0] == 0 && BitConverter.ToUInt16(frame, 1) == 0x000E && frame[15] == 3);
        Assert.Equal(slot, BitConverter.ToInt16(equippedDelta, 18));
        Assert.Equal((byte)(0xA0 | level), equippedDelta[18 + 10]);
        before[9] = (byte)(0xA0 | level);
        Assert.Equal(before, shop.StoredCore(slot, InventoryContainerType.Equipment));
        Assert.Equal(bagBefore, shop.StoredCore(slot));
        Assert.Equal(1, shop.Count(ticket));
        Assert.Equal(100000, shop.Gold);

        await Send(shop, method, ticket, slot);
        Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Single(shop.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(4, Assert.Single(shop.Sender.Batches, batch => batch.Messages.Any(message => message is ReinforceItemResponse)).Messages.Length);
        Assert.Equal(1, shop.Count(ticket));
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        Assert.Equal(before, shop.StoredCore(slot, InventoryContainerType.Equipment));
        Assert.Equal(uid, shop.Scalar($"SELECT core_uid FROM item_slots WHERE owner_id=1 AND list_type=3 AND slot_index={slot}"));
    }

    [PvfTheory]
    [InlineData(2)]
    [InlineData(38)]
    [InlineData(255)]
    public async Task Unsupported_container_does_not_fall_back_to_same_numbered_bag_slot(byte list)
    {
        using var archive = new PvfArchiveProvider(new());
        using var shop = Fixture(archive);
        await Seed(shop, 0, 7275, 12);
        var before = shop.StoredCore(12);
        var equipped = shop.StoredCore(12, InventoryContainerType.Equipment);
        await Send(shop, 0, 7275, 12, (InventoryContainerType)list);
        Assert.False(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(before, shop.StoredCore(12));
        Assert.Equal(equipped, shop.StoredCore(12, InventoryContainerType.Equipment));
        Assert.Equal(2, shop.Count(7275));
        Assert.Equal(100000, shop.Gold);
    }

    [PvfFact]
    public async Task Equipped_ticket_commit_failure_rolls_back_and_retry_can_settle()
    {
        using var archive = new PvfArchiveProvider(new());
        using var shop = Fixture(archive);
        await Seed(shop, 0, 7275, 12);
        var before = shop.StoredCore(12, InventoryContainerType.Equipment);
        shop.Sql("CREATE TRIGGER reject_equipped_ticket BEFORE UPDATE OF item_core ON items "
            + "WHEN substr(hex(OLD.item_core),3,8)='B80B0000' BEGIN SELECT RAISE(ABORT,'equipped ticket'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 0, 7275, 12).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(before, shop.StoredCore(12, InventoryContainerType.Equipment));
        Assert.Equal(2, shop.Count(7275));
        shop.Sql("DROP TRIGGER reject_equipped_ticket;");
        await Send(shop, 0, 7275, 12);
        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(1, shop.Count(7275));
    }

    private static async Task Seed(ShopFixture shop, byte method, int ticket, short slot)
    {
        var core = StackableItemCore.Create(1, 3000, 1234) with
        {
            Attribute = 0xA3, Durability = 45, EnchantCardId = 42,
            AmplifyType = method == 1 ? (byte)2 : (byte)0,
            AmplifyValue = method == 1 ? (ushort)5 : (ushort)0,
        };
        await shop.SeedCore(slot, core, InventoryContainerType.Equipment);
        await shop.SeedCore(slot, core);
        await shop.Seed(65, ticket, 2);
    }

    private static ValueTask Send(ShopFixture shop, byte method, int ticket, short slot,
        InventoryContainerType list = InventoryContainerType.Equipment) => EffectRequestTransport.Send(shop,
            new ReinforceItemRequest(new(1, 0x0050, 0, 7), method, slot, 3000, 65, -1, [], list));

    private static byte[] Encode(ShopFixture shop, IServerNetworkMessage message)
    {
        var registration = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .Single(value => value.MessageType == message.GetType());
        return Assert.Single(((IServerMessageEncoder)shop.Services.GetRequiredService(registration.EncoderType)).Encode(message));
    }

    private static ShopFixture Fixture(PvfArchiveProvider archive, int draw = 0) => new(services =>
    {
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new PvfEquipmentReinforcementDefinitionSource(archive));
        services.AddSingleton<IEquipmentDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentReinforcementRollSource>(new Roll(draw));
    });

    private sealed class Roll(int draw) : IEquipmentReinforcementRollSource
    {
        public int Next(int exclusiveUpperBound) => draw;
    }

    private sealed class Equipment : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId == 3000 ? new(70, 82, 2, "weapon", 45) : null);
    }
}
