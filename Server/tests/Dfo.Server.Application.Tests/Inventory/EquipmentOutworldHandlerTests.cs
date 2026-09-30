using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Outworld;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentOutworldHandlerTests
{
    private static readonly ConditionalWeakTable<ShopFixture, FrameClock> Clocks = new();

    private sealed class FrameClock
    {
        internal long Ordinal;
        internal ushort? LastFresh;
        internal (long Ordinal, bool Fresh) Observe(ushort sequence)
        {
            var fresh = LastFresh is null || sequence == unchecked((ushort)(LastFresh.Value + 1));
            if (fresh) LastFresh = sequence;
            return (++Ordinal, fresh);
        }
    }

    [Fact]
    public async Task Purify_commits_before_ack_and_replay_does_not_consume_again()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(0x80));
        await shop.Seed(121, 1183, 2);
        shop.Sender.BeforeSend = () => Assert.Equal(1, shop.Count(1183));

        await Send(shop, 7, true, 0, 1183);
        await Send(shop, 7, true, 0, 1183);

        Assert.Equal(1, shop.Count(1183));
        Assert.Equal(6, shop.Sender.Messages.Count);
        var first = Assert.IsType<PurifyItemResponse>(shop.Sender.Messages[0]);
        Assert.True(first.Success);
        Assert.Equal(1, first.MaterialRemaining);
        Assert.Equal((byte)1, first.AmplifyType);
        Assert.True(Assert.IsType<PurifyItemResponse>(shop.Sender.Messages[3]).Success);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[1].GetType().Name);
        Assert.Equal("InventoryDeltaMessage", shop.Sender.Messages[2].GetType().Name);
    }

    [Fact]
    public async Task Invest_and_twist_route_their_materials_and_ack_actions()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(0));
        await shop.Seed(121, 1184, 1);
        await Send(shop, 7, false, 0, 1184, 3);
        Assert.Equal(0, shop.Count(1184));
        var invest = Assert.IsType<InvestAmplifyOptionResponse>(shop.Sender.Messages[0]);
        Assert.True(invest.Success);
        Assert.Equal((byte)0, invest.WireAction);
        Assert.Equal((byte)3, invest.AmplifyType);

        await shop.Seed(121, 1191, 1);
        await Send(shop, 8, false, 1, 1191, 2);
        var twist = Assert.IsType<InvestAmplifyOptionResponse>(shop.Sender.Messages[3]);
        Assert.True(twist.Success);
        Assert.Equal((byte)1, twist.WireAction);
        Assert.Equal((byte)2, twist.AmplifyType);
        Assert.Equal(0, shop.Count(1191));
    }

    [Fact]
    public async Task Dynamic_action_two_is_rejected_without_asset_debit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(3));
        await shop.Seed(121, 8238, 1);
        await Send(shop, 7, false, 2, 8238, 0);
        await Send(shop, 7, false, 2, 8238, 0);
        var responses = shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>().ToArray();
        Assert.Equal(2, responses.Length);
        Assert.All(responses, response =>
        {
            Assert.False(response.Success);
            Assert.Equal((byte)0x08, response.ErrorCode);
        });
        Assert.Equal(1, shop.Count(8238));
    }

    [Fact]
    public async Task Same_option_failure_replay_keeps_the_original_error_code()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(3));
        await shop.Seed(121, 1188, 2);

        await Send(shop, 7, false, 1, 1188, 3);
        await Send(shop, 7, false, 1, 1188, 3);

        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.All(shop.Sender.Messages, message =>
        {
            var response = Assert.IsType<InvestAmplifyOptionResponse>(message);
            Assert.False(response.Success);
            Assert.Equal((byte)0x17, response.ErrorCode);
        });
        Assert.Equal(2, shop.Count(1188));
    }

    [Fact]
    public async Task Failed_commit_emits_no_success_then_same_sequence_can_retry()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(0x80));
        await shop.Seed(121, 1183, 2);
        shop.Sql("CREATE TRIGGER refuse_outworld BEFORE UPDATE ON items "
            + "WHEN NEW.item_core <> OLD.item_core AND NEW.item_kind=1 "
            + "BEGIN SELECT RAISE(ABORT,'forced outworld failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7, true, 0, 1183).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(2, shop.Count(1183));
        shop.Sql("DROP TRIGGER refuse_outworld;");
        await Send(shop, 7, true, 0, 1183);
        Assert.True(Assert.IsType<PurifyItemResponse>(shop.Sender.Messages[0]).Success);
        Assert.Equal(1, shop.Count(1183));
    }

    [Theory]
    [InlineData(0, 10, 0, 0x12)]
    [InlineData(0, 0, 0x80, 0x14)]
    [InlineData(0, 0, 3, 0x14)]
    [InlineData(1, 0, 0, 0x15)]
    [InlineData(1, 0, 0x80, 0x15)]
    public async Task State_rejections_have_distinct_wire_codes_and_preserve_assets_on_replay(
        byte action, byte upgradeLevel, byte amplifyType, byte expectedCode)
    {
        using var shop = Fixture();
        var original = Piece(amplifyType) with { Attribute = upgradeLevel };
        var materialId = action == 0 ? 1286 : 1188;
        await shop.SeedCore(9, original);
        await shop.Seed(121, materialId, 2);

        await Send(shop, 7, false, action, materialId, 3);
        await Send(shop, 7, false, action, materialId, 3);

        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.All(shop.Sender.Messages, message =>
        {
            var response = Assert.IsType<InvestAmplifyOptionResponse>(message);
            Assert.False(response.Success);
            Assert.Equal(expectedCode, response.ErrorCode);
        });
        Assert.Equal(2, shop.Count(materialId));
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        Assert.Equal(original, (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core);
    }

    [Fact]
    public async Task Pure_amplification_book_assigns_selected_strength_and_refreshes_equipment()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece(0));
        await shop.Seed(121, 1286, 1);
        await Send(shop, 7, false, 0, 1286, 3);
        var response = Assert.IsType<InvestAmplifyOptionResponse>(shop.Sender.Messages[0]);
        Assert.True(response.Success);
        Assert.Equal((byte)3, response.AmplifyType);
        Assert.Equal((ushort)5, response.AmplifyValue);
        Assert.Equal(0, shop.Count(1286));
        Assert.Equal(3, shop.Sender.Messages.Count);
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        Assert.Equal(Piece(3), (await bound.Container.ReadSlotAsync(bound.Slot.Index, default))?.Core);
    }

    private static ShopFixture Fixture() => new(services =>
    {
        services.AddSingleton<IEquipmentOutworldContentSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Equipment());
    });

    private static InventoryItemCoreState Piece(byte amplifyType) =>
        StackableItemCore.Create(1, 3000, 1234) with
        { Durability = 45, AmplifyType = amplifyType, AmplifyValue = amplifyType is >= 1 and <= 4 ? (ushort)5 : (ushort)0 };

    private static ValueTask Send(ShopFixture shop, ushort sequence,
        bool purify, byte wireAction, int materialId, byte selected = 0)
    {
        var (ordinal, fresh) = Clocks.GetOrCreateValue(shop).Observe(sequence);
        var metadata = new GameMessageMetadata(1,
            purify ? (ushort)0x00CC : (ushort)0x00CD, 0, sequence)
        {
            InboundOrdinal = ordinal,
            SequenceIsFresh = fresh,
        };
        return shop.Services.GetRequiredService<OutworldHandler>().HandleAsync(
            new(shop.Connection), new OutworldRequest(metadata, purify, wireAction,
                9, 3000, 121, materialId, selected), default);
    }

    private sealed class Content : IEquipmentOutworldContentSource
    {
        public ValueTask<EquipmentOutworldTable?> GetAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentOutworldTable?>(new(55,
                new Dictionary<int, int> { [1183] = 1 },
                new Dictionary<int, int> { [10000408] = 1 },
                new Dictionary<int, EquipmentOutworldMaterialDefinition>
                    { [1184] = new(3, 1), [1286] = new(0, 1) },
                new Dictionary<int, EquipmentOutworldMaterialDefinition>
                    { [1191] = new(2, 1), [1188] = new(3, 1) },
                new Dictionary<int, IReadOnlyDictionary<byte, ushort>>
                    { [2] = new Dictionary<byte, ushort> { [1] = 5, [2] = 5, [3] = 5, [4] = 5 } },
                [new(1, 1_000_000), new(2, 1), new(3, 1), new(4, 1)]));
    }

    private sealed class Equipment : IEquipmentReinforcementDefinitionSource
    {
        public ValueTask<EquipmentReinforcementEquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) => ValueTask.FromResult<EquipmentReinforcementEquipmentDefinition?>(
                itemId == 3000 ? new(75, 1, 2, 0, 45, true) : null);
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }
}
