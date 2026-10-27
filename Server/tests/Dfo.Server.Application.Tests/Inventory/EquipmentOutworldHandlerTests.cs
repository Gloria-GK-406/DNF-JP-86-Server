using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Outworld;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentOutworldHandlerTests
{
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
        Assert.Equal(9, shop.Sender.Messages.Count);
        var first = Assert.IsType<PurifyItemResponse>(shop.Sender.Messages[0]);
        Assert.True(first.Success);
        Assert.Equal(1, first.MaterialRemaining);
        Assert.Equal((byte)1, first.AmplifyType);
        Assert.Single(shop.Sender.Messages.OfType<PurifyItemResponse>());
        Assert.Single(shop.Sender.Batches, EffectRequestTransport.IsStateReplay);
        Assert.Equal(3, Assert.Single(shop.Sender.Batches, batch => batch.Messages.Any(message => message is PurifyItemResponse)).Messages.Length);
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
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 7, false, 2, 8238, 0).AsTask());
        var responses = shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>().ToArray();
        Assert.Single(responses);
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

        Assert.Single(shop.Sender.Batches);
        Assert.Single(shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>());
        Assert.All(shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>(), message =>
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

        Assert.Single(shop.Sender.Batches);
        Assert.Single(shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>());
        Assert.All(shop.Sender.Messages.OfType<InvestAmplifyOptionResponse>(), message =>
        {
            var response = Assert.IsType<InvestAmplifyOptionResponse>(message);
            Assert.False(response.Success);
            Assert.Equal(expectedCode, response.ErrorCode);
        });
        Assert.Equal(2, shop.Count(materialId));
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        Assert.Equal(original, (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore());
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
        Assert.Equal(Piece(3), (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore());
    }

    [Theory]
    [InlineData("amulet")]
    [InlineData("wrist")]
    [InlineData("ring")]
    public async Task Jewelry_without_durability_assigns_option_commits_and_refreshes(string type)
    {
        using var shop = Fixture(services => services.AddSingleton<IEquipmentDefinitionSource>(
            new FixedEquipment(new(80, 82, 2, type, null))));
        var original = Piece(0) with { Durability = 0 };
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1286, 1);
        var bytes = shop.StoredCore(9);
        await Send(shop, 7, false, 0, 1286, 3);
        var response = Assert.IsType<InvestAmplifyOptionResponse>(shop.Sender.Messages[0]);
        Assert.True(response.Success);
        Assert.Equal((byte)3, response.AmplifyType);
        Assert.Equal((ushort)5, response.AmplifyValue);
        Assert.Equal(0, shop.Count(1286));
        Assert.Equal(3, shop.Sender.Messages.Count);
        Assert.False(bytes.SequenceEqual(shop.StoredCore(9)));
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        Assert.Equal(original with { AmplifyType = 3, AmplifyValue = 5 },
            (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore());
    }

    private sealed class FixedEquipment(EquipmentDefinition definition) : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(definition);
    }

    [Fact]
    public async Task Invalid_equipment_content_logs_error_preserves_assets_and_emits_no_ack()
    {
        var logger = new CaptureLogger<GameplayExecutor>();
        using var shop = Fixture(services =>
        {
            services.AddSingleton<IEquipmentDefinitionSource>(new BrokenEquipment());
            services.AddSingleton<ILogger<GameplayExecutor>>(logger);
        });
        var original = Piece(0);
        await shop.SeedCore(9, original);
        await shop.Seed(121, 1286, 2);
        var bytes = shop.StoredCore(9);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => Send(shop, 7, false, 0, 1286, 3).AsTask());
        Assert.Contains("[durability]", failure.Message);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error
            && ReferenceEquals(entry.Exception, failure));
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(2, shop.Count(1286));
        Assert.Equal(bytes, shop.StoredCore(9));
        var bound = shop.Services.GetRequiredService<IBusinessInventorySlotDomainService>()
            .Get(shop.Session.CharacterId, new(BusinessInventorySpace.CarriedEquipment, 0));
        Assert.Equal(original, (await bound.Container.GetAsync(bound.Slot.Index, default))?.Snapshot.ToCore());
        // Internal content failures are not memoized as a normal business rejection.
        await Assert.ThrowsAsync<InvalidDataException>(
            () => Send(shop, 7, false, 0, 1286, 3).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(2, shop.Count(1286));
    }

    private sealed class BrokenEquipment : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            throw new InvalidDataException(
                $"Equipment {itemId}, PVF 'equipment/broken.equ', field '[durability]': not an integer.");
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        internal List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((level, exception));
    }

    internal static ShopFixture Fixture(Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IEquipmentOutworldContentSource>(new Content());
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Equipment());
        services.AddSingleton<IEquipmentDefinitionSource>(new Equipment());
        configure?.Invoke(services);
    });

    private static InventoryItemCoreState Piece(byte amplifyType) =>
        StackableItemCore.Create(1, 3000, 1234) with
        { Durability = 45, AmplifyType = amplifyType, AmplifyValue = amplifyType is >= 1 and <= 4 ? (ushort)5 : (ushort)0 };

    private static ValueTask Send(ShopFixture shop, ushort sequence,
        bool purify, byte wireAction, int materialId, byte selected = 0)
    {
        var metadata = new GameMessageMetadata(1,
            purify ? (ushort)0x00CC : (ushort)0x00CD, 0, sequence);
        return EffectRequestTransport.Send(shop, new OutworldRequest(metadata, purify, wireAction,
                9, 3000, 121, materialId, selected));
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

    private sealed class Equipment : IEquipmentReinforcementDefinitionSource, IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) => ValueTask.FromResult<EquipmentDefinition?>(
                itemId == 3000 ? new(75, 1, 2, "weapon", 45) : null);
        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);

        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(null);
    }
}
