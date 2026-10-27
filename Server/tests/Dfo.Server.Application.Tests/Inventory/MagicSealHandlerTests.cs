using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MagicSeal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MagicSealHandlerTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task Change_requires_the_A21_reserved_zero_without_consuming_assets(short second)
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { RandomOption0 = new(15, 2, 2) });
        await Send(shop, 7, true, second);
        Assert.Equal(100000, shop.Gold);
        Assert.False(Assert.Single(shop.Sender.Messages.OfType<ChangeRandomOptionResponse>()).Success);
    }
    [Fact]
    public async Task Unseal_sends_committed_equipment_and_gold_without_unhandled_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sender.BeforeSend = () => Assert.Equal(99500, shop.Gold);

        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[0].GetType().Name);
        Assert.Empty(shop.Sender.Messages.OfType<ChangeRandomOptionResponse>());
    }

    [Fact]
    public async Task Change_updates_equipment_and_gold_before_success_refresh_and_survives_reload()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { RandomOption0 = new(15, 2, 2) });

        await Send(shop, 7, true);

        Assert.Equal(99800, shop.Gold);
        Assert.Equal(3, shop.Sender.Messages.Count);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[0].GetType().Name);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[1].GetType().Name);
        Assert.True(Assert.IsType<ChangeRandomOptionResponse>(shop.Sender.Messages[2]).Success);
        var registrations = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .ToDictionary(value => value.MessageType);
        var encoded = shop.Sender.Messages.Select(message =>
            ((IServerMessageEncoder)shop.Services.GetRequiredService(registrations[message.GetType()].EncoderType))
                .Encode(message).Single().ToArray()).ToArray();
        // The A21 success callback rebuilds the conversion panel from its current item.
        // Its inventory must already contain the new option when it processes the ACK.
        Assert.Equal((ushort)0x000E, BinaryPrimitives.ReadUInt16LittleEndian(encoded[0].AsSpan(1)));
        var entry = encoded[0].AsSpan(18, 101).ToArray();
        Assert.Equal((short)9, BinaryPrimitives.ReadInt16LittleEndian(entry));
        Assert.Equal((byte)16, entry[59]);
        Assert.Equal((byte)4, entry[62]);
        Assert.Equal((byte)4, entry[65]);
        Assert.Equal(99800, BinaryPrimitives.ReadInt32LittleEndian(encoded[1].AsSpan(24)));
        var stored = shop.StoredCore(9);
        Assert.Equal(new byte[] { 16, 4, 4 }, stored.AsSpan(56, 3).ToArray());

        // Drop session containers and rebuild from SQLite, then use the production snapshot
        // projection. The reloaded representation must match the delta.
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
            shop.Select(1);
            await Dfo.Server.Application.Tests.Support.ReplayStateFixture.RefreshAsync(shop.Services,
                shop.Connection, shop.Session, new(ReplayProjectionNeeds.Inventory, []), null, default);
        });
        var snapshot = EffectRequestTransport.MainSnapshot(shop);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(snapshot.AsSpan(18));
        var reloaded = Enumerable.Range(0, count)
            .Select(index => snapshot.AsSpan(20 + index * 101, 101).ToArray())
            .Single(value => BinaryPrimitives.ReadInt16LittleEndian(value) == 9);
        Assert.Equal(entry, reloaded);
        Assert.Equal(stored, shop.StoredCore(9));
        Assert.Equal(99800, shop.Gold);
    }

    [Fact]
    public async Task Change_rejects_unsealed_target_without_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());

        await Send(shop, 7, true);

        Assert.False(Assert.Single(shop.Sender.Messages.OfType<ChangeRandomOptionResponse>()).Success);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Replayed_unseal_projects_current_state_without_second_gold_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await Send(shop, 7, false);
        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(8, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches, EffectRequestTransport.IsStateReplay);
    }

    [Fact]
    public async Task Replay_after_another_purchase_reports_current_gold_not_old_receipt()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await Send(shop, 7, false);
        shop.Sql("UPDATE character_stats SET carried_gold=99400 WHERE character_id=1");

        await Send(shop, 7, false);

        Assert.Equal(99400, EffectRequestTransport.MainValue(shop, 0));
        Assert.Equal(99400, shop.Gold);
    }

    [Fact]
    public async Task Non_sentinel_second_field_is_rejected_without_spending_assets()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 4000, 2, 3);

        await Send(shop, 7, false, materialSlot: 121);

        Assert.Equal(100000, shop.Gold);
        Assert.Equal(2, shop.Count(4000));
        Assert.Empty(shop.Sender.Messages);
    }

    [Fact]
    public async Task Failed_commit_sends_nothing_then_same_sequence_can_retry()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        shop.Sql("CREATE TRIGGER refuse_seal BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core BEGIN SELECT RAISE(ABORT,'forced seal failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7, false).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(100000, shop.Gold);
        shop.Sql("DROP TRIGGER refuse_seal;");
        await Send(shop, 7, false);

        Assert.Equal(99500, shop.Gold);
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    internal static ShopFixture Fixture(Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IEquipmentMagicSealContentSource>(new Content());
        configure?.Invoke(services);
    });

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234);

    private static ValueTask Send(ShopFixture shop, ushort sequence, bool change,
        short? materialSlot = null)
    {
        var metadata = new GameMessageMetadata(1, change ? (ushort)0x01B6 : (ushort)0x0191,
            0, sequence);
        return EffectRequestTransport.Send(shop, new MagicSealRequest(metadata, change, 9,
            materialSlot ?? (change ? (short)0 : (short)-1)));
    }

    private sealed class Content : IEquipmentMagicSealContentSource
    {
        public ValueTask<EquipmentMagicSealDefinition?> GetEquipmentAsync(int itemId,
            CancellationToken ct) => ValueTask.FromResult<EquipmentMagicSealDefinition?>(
            itemId == 3000 ? new(3000, 2, 75, 500, 200, [new(1, 1)],
                [[new(15, 2, 3, 1)]], [[new(16, 4, 5, 1)]], [new(2, 2, 1)], [new(4, 4, 1)]) : null);

        public ValueTask<MagicSealScrollDefinition?> GetScrollAsync(int itemId,
            CancellationToken ct) => ValueTask.FromResult<MagicSealScrollDefinition?>(null);
    }
}
