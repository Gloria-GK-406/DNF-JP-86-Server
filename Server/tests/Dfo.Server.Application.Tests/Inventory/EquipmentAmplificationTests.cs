using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentAmplificationTests
{
    [SkippableFact]
    public async Task Real_archive_amplification_quote_and_transaction_use_amplifyupgrade_costs()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IEquipmentDefinitionSource>(new Content("weapon", 45));
            s.AddSingleton<IEquipmentReinforcementRollSource>(new Roll(99999));
        });
        await Seed(f, Piece()); f.Sql("UPDATE character_stats SET carried_gold=1000000 WHERE character_id=1");
        await Send(f, 7);
        var ack = Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(ack.Success); Assert.Equal((byte)1, ack.Method); Assert.Equal((byte)1, ack.NewLevel);
        Assert.Equal(19, f.Count(3242));
        Assert.Equal(1000000 - 56400 * 1.5 * 2, f.Gold); // level 80 + rarity correction 2; no level cost factor.
    }

    [Theory]
    [InlineData("weapon", (ushort)45)]
    [InlineData("amulet", null)]
    [InlineData("wrist", null)]
    [InlineData("ring", null)]
    public async Task Identified_equipment_amplifies_and_preserves_identity_attributes_and_relog_state(string type, ushort? durability)
    {
        using var f = Fixture(type: type, durability: durability);
        await Seed(f, Piece() with { Durability = durability ?? 123, Attribute = 0xA0, EnchantCardId = 777, GenuineUpgrade = 2 });
        var before = f.StoredCore(9);
        var uid = f.Scalar("SELECT core_uid FROM item_slots WHERE owner_id=1 AND owner_scope='character' AND slot_index=9");
        f.Sender.BeforeSend = () => { Assert.Equal(19, f.Count(3242)); Assert.Equal(type == "weapon" ? 99700 : 99895, f.Gold); };
        await Send(f, 7);
        var ack = Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(ack.Success); Assert.Equal((byte)1, ack.Method); Assert.Equal((byte)1, ack.NewLevel);
        Assert.Equal(19, ack.MaterialRemaining);
        before[9] = 0xA1; Assert.Equal(before, f.StoredCore(9));
        Assert.Equal(uid, f.Scalar("SELECT core_uid FROM item_slots WHERE owner_id=1 AND owner_scope='character' AND slot_index=9"));
        var equipment = Assert.Single(DeltaEntries(f), x => BitConverter.ToInt16(x, 0) == 9);
        Assert.Equal((byte)0xA1, equipment[0x0A]);
        Assert.Equal((byte)4, equipment[0x13]); Assert.Equal((ushort)5, BitConverter.ToUInt16(equipment, 0x14));
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(f.Session.CharacterId); f.Select(1);
        await using var transaction = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        var reloaded = await f.Services.GetRequiredService<IEquipmentDomainService>().Of(f.Session.CharacterId)
            .FindAtAsync(new(BusinessInventorySpace.CarriedEquipment, 0), default);
        Assert.NotNull(reloaded);
        Assert.Equal((byte)1, (await reloaded.ReadAsync(default)).Core.ReinforcementLevel);
        Assert.Equal((ushort)5, (await reloaded.ReadAsync(default)).Core.AmplifyValue);
    }

    [Theory]
    [InlineData(0)] [InlineData(128)]
    public async Task Missing_or_unidentified_red_attribute_rejects_without_spending(byte amplifyType)
    {
        using var f = Fixture(); await Seed(f, Piece() with { AmplifyType = amplifyType });
        var before = f.StoredCore(9); await Send(f, 7);
        Assert.False(Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(before, f.StoredCore(9)); Assert.Equal(20, f.Count(3242)); Assert.Equal(100000, f.Gold);
    }

    [Theory]
    [InlineData(0, 4, 3, EquipmentReinforcementOutcome.Downgraded)]
    [InlineData(0, 8, 0, EquipmentReinforcementOutcome.Downgraded)]
    [InlineData(10007841, 8, 7, EquipmentReinforcementOutcome.Downgraded)]
    [InlineData(10004511, 10, 0, EquipmentReinforcementOutcome.Downgraded)]
    [InlineData(10007841, 10, 7, EquipmentReinforcementOutcome.Downgraded)]
    [InlineData(7804, 10, 0, EquipmentReinforcementOutcome.Destroyed)]
    [InlineData(0, 11, 0, EquipmentReinforcementOutcome.Destroyed)]
    public async Task Failure_uses_amplification_penalty_and_only_matching_protection(int ticketId, byte oldLevel, byte newLevel,
        EquipmentReinforcementOutcome outcome)
    {
        using var f = Fixture(draw: 0); await Seed(f, Piece() with { Attribute = oldLevel });
        if (ticketId != 0) await f.Seed(3, ticketId, 1, 3);
        await Send(f, 7);
        var ack = Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(ack.Success); Assert.Equal((byte)1, ack.Method); Assert.Equal(outcome, ack.Outcome); Assert.Equal(newLevel, ack.NewLevel);
        Assert.Equal(20 - oldLevel - 1, f.Count(3242)); Assert.Equal(99700, f.Gold);
        if (ticketId is 10004511 or 10007841) Assert.Equal(0, f.Count(ticketId));
        if (ticketId == 7804) Assert.Equal(1, f.Count(ticketId));
        if (outcome == EquipmentReinforcementOutcome.Destroyed)
        {
            Assert.Equal(0, f.Count(3000)); Assert.NotEmpty(ack.DestroyRewards!);
            Assert.True(f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1") > 0);
            if (oldLevel == 11) Assert.Contains(ack.DestroyRewards!, reward => reward.ItemId == 3244 && reward.Count > 0);
        }
        else Assert.Equal(newLevel, f.StoredCore(9)[9]);
    }

    [Fact]
    public async Task Super_amplification_protection_has_priority_and_success_does_not_consume_it()
    {
        using var f = Fixture(draw: 0); await Seed(f, Piece() with { Attribute = 10 });
        await f.Seed(3, 10004511, 1, 3); await f.Seed(4, 10007841, 1, 3);
        await Send(f, 7);
        Assert.Equal((byte)7, Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>()).NewLevel);
        Assert.Equal(1, f.Count(10004511)); Assert.Equal(0, f.Count(10007841));
        using var success = Fixture(); await Seed(success, Piece() with { Attribute = 10 });
        await success.Seed(3, 10007841, 1, 3); await Send(success, 7);
        Assert.Equal((byte)11, Assert.Single(success.Sender.Messages.OfType<ReinforceItemResponse>()).NewLevel);
        Assert.Equal(1, success.Count(10007841));
    }

    [Fact]
    public async Task Amplification_replay_spends_once_and_changed_mode_is_a_conflict()
    {
        using var f = Fixture(); await Seed(f, Piece()); await Send(f, 7); await Send(f, 7); await Send(f, 7, method: 0);
        var replies = f.Sender.Messages.OfType<ReinforceItemResponse>().ToArray();
        Assert.Equal(replies[0], replies[1]); Assert.False(replies[2].Success);
        Assert.Equal(19, f.Count(3242)); Assert.Equal(99700, f.Gold); Assert.Equal((byte)1, f.StoredCore(9)[9]);
    }

    [Fact]
    public async Task Amplification_storage_failure_rolls_back_and_does_not_send_or_cache_success()
    {
        using var f = Fixture(); await Seed(f, Piece()); var before = f.StoredCore(9);
        f.Sql("CREATE TRIGGER reject_amplification BEFORE UPDATE OF item_core ON items BEGIN SELECT RAISE(ABORT,'amplification-test'); END");
        await Assert.ThrowsAsync<SqliteException>(() => Send(f, 7).AsTask());
        Assert.Empty(f.Sender.Messages); Assert.Equal(before, f.StoredCore(9)); Assert.Equal(20, f.Count(3242)); Assert.Equal(100000, f.Gold);
        f.Sql("DROP TRIGGER reject_amplification"); await Send(f, 7);
        Assert.True(Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Wrong_or_insufficient_amplification_material_never_spends_gold(bool wrong)
    {
        using var f = Fixture(); await f.SeedCore(9, Piece());
        await f.Seed(121, wrong ? 1000 : 3242, wrong ? 20 : 0, 3);
        await Send(f, 7);
        Assert.False(Assert.Single(f.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(100000, f.Gold); Assert.Equal((byte)0, f.StoredCore(9)[9]);
    }

    private static async Task Seed(ShopFixture f, InventoryItemCoreState core)
    { await f.SeedCore(9, core); await f.Seed(121, 3242, 20, 3); }
    private static InventoryItemCoreState Piece() => StackableItemCore.Create(1, 3000, 1234)
        with { Durability = 45, AmplifyType = 4, AmplifyValue = 5 };
    private static ValueTask Send(ShopFixture f, ushort sequence, ushort method = 1) =>
        f.Services.GetRequiredService<ReinforceItemHandler>().HandleAsync(new(f.Connection),
            new ReinforceItemRequest(new(1, 0x50, 0, sequence), method, 9, 3000, 121, -1, []), default);
    private static IEnumerable<byte[]> DeltaEntries(ShopFixture f)
    {
        foreach (var message in f.Sender.Messages)
        {
            var registration = f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
            var encoder = (IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType);
            foreach (var frame in encoder.Encode(message))
                if (frame[0] == 0 && BitConverter.ToUInt16(frame, 1) == 0x0E)
                    for (var index = 0; index < BitConverter.ToUInt16(frame, 16); index++)
                        yield return frame.AsSpan(18 + index * 101, 101).ToArray();
        }
    }
    private static ShopFixture Fixture(int draw = 99999, string type = "weapon", ushort? durability = 45) => new(s =>
    {
        var content = new Content(type, durability);
        s.AddSingleton<IEquipmentDefinitionSource>(content); s.AddSingleton<IEquipmentReinforcementDefinitionSource>(content);
        s.AddSingleton<IEquipmentOutworldContentSource>(content);
        s.AddSingleton<IEquipmentDisjointDefinitionSource>(new ReinforcementDestructionTestContent(2, 82, 20520));
        s.AddSingleton<IEquipmentReinforcementRollSource>(new Roll(draw));
    });
    private sealed class Roll(int draw) : IEquipmentReinforcementRollSource
    { public int Next(int exclusiveUpperBound) => exclusiveUpperBound == 100000 ? draw : 0; }
    private sealed class Content(string type, ushort? durability) : IEquipmentDefinitionSource,
        IEquipmentReinforcementDefinitionSource, IEquipmentOutworldContentSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(itemId == 3000 ? new(80, 82, 2, type, durability) : null);
        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(Table(false));
        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(Table(true));
        private static EquipmentReinforcementTable Table(bool amplify) => new(
            Enumerable.Range(1, 31).Select(level => new EquipmentReinforcementRow(level, new double[19],
                level < 5 ? 0 : 50000, level < 5 ? 1 : level <= 10 ? 2 : 3,
                level < 5 || level > 10 ? 0 : level < 8 ? 1 : level - 1, amplify ? 3242 : 3037, amplify ? level : 10)).ToArray(),
            Enumerable.Repeat(100, 100).ToArray(), [0.25, 0.75, 1.5, 2.5, 4, 2, 3],
            [2, 0, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7, 0.7],
            new Dictionary<int, double>(), new Dictionary<int, int> { [0] = 8, [1] = 8 },
            new Dictionary<int, int> { [0] = 50, [1] = 50 },
            new(amplify ? 3244 : 3243, 40, 10, new Dictionary<int, double> { [2] = 1 }, new Dictionary<int, int> { [2] = 2 }), 12);
        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(int itemId, CancellationToken ct,
            EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) => ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(
                (kind, itemId) switch
                {
                    (EquipmentUpgradeKind.Amplification, 10004511) => new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
                    (EquipmentUpgradeKind.Amplification, 10007841) => new(EquipmentReinforcementProtectionKind.RetainLevel, 7),
                    (EquipmentUpgradeKind.Reinforcement, 7804) => new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
                    _ => null,
                });
        public ValueTask<EquipmentOutworldTable?> GetAsync(CancellationToken ct) => ValueTask.FromResult<EquipmentOutworldTable?>(new(
            55, new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, EquipmentOutworldMaterialDefinition>(),
            new Dictionary<int, EquipmentOutworldMaterialDefinition>(), new Dictionary<int, IReadOnlyDictionary<byte, ushort>>(), []));
    }
}
