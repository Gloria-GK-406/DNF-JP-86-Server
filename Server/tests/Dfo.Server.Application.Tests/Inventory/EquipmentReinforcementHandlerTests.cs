using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Dfo.Server.Abstractions.Networking;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class EquipmentReinforcementHandlerTests
{
    [Dfo.Server.Testing.PvfTheory]
    [InlineData(false, 0)]
    [InlineData(true, 89999)]
    [InlineData(true, 90000)]
    public async Task Ticket_handler_projects_committed_slots_and_replay_never_spends_or_draws_twice(bool amplification, int draw)
    {
        using var provider = new Dfo.Server.Infrastructure.Content.PvfArchiveProvider(new());
        var rolls = new TicketRoll(draw);
        using var shop = new ShopFixture(services =>
        {
            services.AddSingleton<IEquipmentReinforcementDefinitionSource>(
                new Dfo.Server.Infrastructure.Content.PvfEquipmentReinforcementDefinitionSource(provider));
            services.AddSingleton<IEquipmentDefinitionSource>(new TicketEquipment());
            services.AddSingleton<IEquipmentReinforcementRollSource>(rolls);
        });
        var original = Piece() with { Attribute = 0xA3, Durability = 0,
            AmplifyType = amplification ? (byte)4 : (byte)0, AmplifyValue = amplification ? (ushort)5 : (ushort)0 };
        await shop.SeedCore(9, original);
        var itemId = amplification ? 10008107 : 7275;
        await shop.Seed(101, itemId, 2, 2);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(1, shop.Count(itemId));
            Assert.Equal(100000, shop.Gold);
            Assert.Equal(draw < 90000 ? (byte)0xAC : (byte)0xA3, shop.StoredCore(9)[9]);
        };
        await Send(shop, 70, method: amplification ? (byte)1 : (byte)0, materialSlot: 101);
        var response = Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.True(response.Success); // Request accepted; outcome independently conveys the probability result.
        Assert.Equal(draw < 90000 ? EquipmentReinforcementOutcome.Succeeded : EquipmentReinforcementOutcome.Retained, response.Outcome);
        Assert.Equal(draw < 90000 ? (byte)12 : (byte)3, response.NewLevel);
        var coupon = Assert.Single(DeltaEntries(shop), entry => BitConverter.ToInt16(entry, 0) == 101);
        Assert.Equal(1, BitConverter.ToInt32(coupon, 6));
        if (draw < 90000)
            Assert.Equal((byte)0xAC, Assert.Single(DeltaEntries(shop), entry => BitConverter.ToInt16(entry, 0) == 9)[0x0A]);
        await Send(shop, 70, method: amplification ? (byte)1 : (byte)0, materialSlot: 101);
        Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Equal(1, rolls.Calls);
        Assert.Equal(1, shop.Count(itemId));
        shop.Sender.BeforeSend = null;
        await Send(shop, 71, method: amplification ? (byte)1 : (byte)0, materialSlot: 101);
        Assert.Equal(2, rolls.Calls);
        Assert.Equal(0, shop.Count(itemId));
        Assert.Equal(100000, shop.Gold);
    }

    private sealed class TicketEquipment : IEquipmentDefinitionSource
    {
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(new(70, 1, 2, "amulet", null));
    }
    private sealed class TicketRoll(int draw) : IEquipmentReinforcementRollSource
    {
        public int Calls { get; private set; }
        public int Next(int exclusiveUpperBound) { Calls++; return draw; }
    }

    [Fact]
    public async Task Reinforcement_is_committed_and_projected_in_attribute_bits_without_changing_enchant_upgrade()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece() with { Attribute = 0xA0, EnchantUpgradeCount = 1 });
        await shop.Seed(121, 1000, 20, 3);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");

        await Send(shop, 7);

        var stored = shop.StoredCore(9);
        Assert.Equal((byte)0xA1, stored[9]); // attr low five bits; high three bits retain reseal count.
        Assert.Equal((byte)1, stored[17]); // Independent enchant upgrade count.
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));
        Assert.Equal((byte)1, Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).NewLevel);
        var immediate = Assert.Single(shop.Sender.Messages.Select(message => Encode(shop, message)),
            frame => frame[0] == 0 && BitConverter.ToUInt16(frame, 1) == 0x000E
                && BitConverter.ToInt16(frame, 15 + 3) == 9);
        Assert.Equal((byte)0xA1, immediate[15 + 3 + 0x0A]);
        Assert.Equal((byte)1, immediate[15 + 3 + 0x12]);

        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        var reloaded = await shop.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
            .LoadAsync(shop.Session.AccountId, shop.Session.CharacterId, default);
        var account = await shop.Services.GetRequiredService<IAccountInventoryEntryDomainService>()
            .LoadAsync(shop.Session.AccountId, shop.Session.CharacterId, default);
        var main = Assert.Single(reloaded.Containers, container => container.ContainerType == InventoryContainerType.Main);
        var snapshotType = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .Single(registration => registration.MessageType.Name == "InventoryFullSnapshotMessage").MessageType;
        var snapshotMessage = (IServerNetworkMessage)Activator.CreateInstance(snapshotType,
            main, reloaded, account, shop.Clock.GetUtcNow())!;
        var snapshot = Encode(shop, snapshotMessage);
        Assert.Equal((byte)0, snapshot[0]);
        Assert.Equal((ushort)0x000D, BitConverter.ToUInt16(snapshot, 1));
        var entryOffset = 15 + 5 + 3 * 101; // Frame and main headers, then three currency entries.
        Assert.Equal((short)9, BitConverter.ToInt16(snapshot, entryOffset));
        Assert.Equal((byte)0xA1, snapshot[entryOffset + 0x0A]);
        Assert.Equal((byte)1, snapshot[entryOffset + 0x12]);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Ack_is_emitted_only_after_equipment_material_and_gold_commit()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(10, shop.Count(1000));
            Assert.Equal(99_900, shop.Gold);
        };

        await Send(shop, 7);

        var ack = Assert.IsType<ReinforceItemResponse>(shop.Sender.Messages[0]);
        Assert.True(ack.Success);
        Assert.Equal((byte)1, ack.NewLevel);
        Assert.True(shop.Sender.Messages.Count > 2);
    }

    [Fact]
    public async Task Same_connection_sequence_replay_refreshes_current_state_without_spending_again()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        var countAfterFirst = shop.Sender.Messages.Count;
        await Send(shop, 7);

        Assert.Equal(countAfterFirst + 6, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches);
        Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Same_sequence_with_changed_request_is_rejected_instead_of_replaying_success()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 7, expectedItemId: 3001).AsTask());

        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Empty(shop.Sender.Batches);
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Switching_character_does_not_replay_the_previous_characters_ack()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        shop.Select(2);
        await Assert.ThrowsAsync<InvalidDataException>(() => Send(shop, 7).AsTask());
        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Empty(shop.Sender.Batches);
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Wrong_virtual_material_slot_is_rejected_without_falling_back_to_another_asset()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        await shop.SeedShared(3033, 20, 1);

        await Send(shop, 7, materialSlot: 354);

        var reply = Assert.IsType<ReinforceItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.False(reply.Success);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(20, shop.SharedCount(3033, 1));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Different_sequence_can_intentionally_reinforce_the_next_level()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7);
        await Send(shop, 8);

        Assert.Equal([1, 2], shop.Sender.Messages.OfType<ReinforceItemResponse>()
            .Select(message => (int)message.NewLevel));
        Assert.Equal(0, shop.Count(1000));
        Assert.Equal(99_800, shop.Gold);
    }

    [Fact]
    public async Task Failed_commit_is_not_cached_as_a_success_for_replay()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);
        shop.Sql("CREATE TRIGGER refuse_upgrade BEFORE UPDATE OF carried_gold ON character_stats "
            + "WHEN NEW.carried_gold <> OLD.carried_gold BEGIN SELECT RAISE(ABORT,'forced'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(20, shop.Count(1000));
        shop.Sql("DROP TRIGGER refuse_upgrade;");

        await Send(shop, 7);

        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(10, shop.Count(1000));
    }

    [Theory]
    [InlineData(7804, 0, 1)]
    [InlineData(7804, 0, 2)]
    [InlineData(10007840, 10, 1)]
    [InlineData(10007840, 10, 2)]
    public async Task Automatic_protection_commits_then_encodes_ticket_remaining_or_clear_and_matching_reload(
        int ticketId, byte newLevel, int ticketCount)
    {
        using var shop = Fixture(protectedFailure: true);
        await shop.SeedCore(9, Piece() with { Attribute = 0xAB, EnchantUpgradeCount = 7 });
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, ticketId, ticketCount);
        var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9");
        var before = shop.StoredCore(9);
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(ticketCount - 1, shop.Count(ticketId));
            Assert.Equal((byte)(0xA0 | newLevel), shop.StoredCore(9)[9]);
            Assert.Equal(10, shop.Count(1000));
            Assert.Equal(99_900, shop.Gold);
        };

        await Send(shop, 7);

        var ack = Assert.IsType<ReinforceItemResponse>(shop.Sender.Messages[0]);
        Assert.True(ack.Success);
        Assert.Equal(EquipmentReinforcementOutcome.Downgraded, ack.Outcome);
        Assert.Equal((byte)11, ack.OldLevel);
        Assert.Equal(newLevel, ack.NewLevel);
        var entries = DeltaEntries(shop).ToArray();
        var equipment = Assert.Single(entries, entry => BitConverter.ToInt16(entry, 0) == 9);
        Assert.Equal((byte)(0xA0 | newLevel), equipment[0x0A]);
        Assert.Equal((byte)7, equipment[0x12]);
        var ticket = Assert.Single(entries, entry => BitConverter.ToInt16(entry, 0) == 3);
        Assert.Equal(ticketCount == 1 ? -1 : ticketId, BitConverter.ToInt32(ticket, 2));
        Assert.Equal(ticketCount - 1, BitConverter.ToInt32(ticket, 6));
        before[9] = (byte)(0xA0 | newLevel);
        Assert.Equal(before, shop.StoredCore(9));
        Assert.Equal(uid, shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_scope='character' "
            + "AND owner_id=1 AND list_type=0 AND slot_index=9"));

        var snapshot = await ReloadSnapshot(shop);
        var snapshotEntries = Entries(snapshot, 15 + 5, BitConverter.ToUInt16(snapshot, 15 + 3)).ToArray();
        Assert.Equal(equipment, Assert.Single(snapshotEntries, entry => BitConverter.ToInt16(entry, 0) == 9));
        if (ticketCount == 1)
            Assert.DoesNotContain(snapshotEntries, entry => BitConverter.ToInt16(entry, 0) == 3);
        else
            Assert.Equal(ticket, Assert.Single(snapshotEntries, entry => BitConverter.ToInt16(entry, 0) == 3));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(7804, 0)]
    [InlineData(10007840, 10)]
    public async Task Protected_failure_replay_returns_result_without_another_ticket_or_asset_debit(
        int ticketId, byte newLevel)
    {
        using var shop = Fixture(protectedFailure: true);
        await shop.SeedCore(9, Piece() with { Attribute = 11 });
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, ticketId, 2);

        await Send(shop, 7);
        var countAfterFirst = shop.Sender.Messages.Count;
        await Send(shop, 7);

        Assert.Equal(countAfterFirst + 6, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Batches);
        Assert.Equal(newLevel, Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).NewLevel);
        Assert.Equal(1, shop.Count(ticketId));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(7804, 0)]
    [InlineData(10007840, 10)]
    public async Task Protected_failure_with_virtual_clear_cube_encodes_the_committed_account_balance(
        int ticketId, byte expectedLevel)
    {
        using var shop = Fixture(protectedFailure: true, materialId: 3037);
        await shop.SeedCore(9, Piece() with { Attribute = 11 });
        await shop.Seed(3, ticketId, 2);
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var seed = scope.Begin())
            {
                await shop.PutShared(3037, 20);
                await seed.CommitAsync(default);
            }
        });

        await Send(shop, 7, materialSlot: 358);

        var ack = Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Equal(expectedLevel, ack.NewLevel);
        Assert.Equal(10, ack.MaterialRemaining);
        var cube = Assert.Single(DeltaEntries(shop), entry => BitConverter.ToInt16(entry, 0) == 358);
        Assert.Equal(3037, BitConverter.ToInt32(cube, 2));
        Assert.Equal(10, BitConverter.ToInt32(cube, 6));
        Assert.Equal(10, shop.SharedCount(3037, 1));
        Assert.Equal(1, shop.Count(ticketId));
        Assert.Equal(99_900, shop.Gold);
    }

    [Theory]
    [InlineData(7804)]
    [InlineData(10007840)]
    public async Task Protected_failure_commit_error_rolls_back_equipment_ticket_material_and_gold_and_allows_retry(
        int ticketId)
    {
        using var shop = Fixture(protectedFailure: true);
        await shop.SeedCore(9, Piece() with { Attribute = 0xAB, EnchantUpgradeCount = 7 });
        await shop.Seed(121, 1000, 20, 3);
        await shop.Seed(3, ticketId, 1);
        var before = shop.StoredCore(9);
        // Equipment mutation is staged after ticket/material/gold in the domain. Failing its
        // SQLite flush proves those earlier writes share the same commit boundary.
        shop.Sql("CREATE TRIGGER refuse_protected_upgrade BEFORE UPDATE OF item_core ON items "
            + "WHEN substr(hex(OLD.item_core),3,8)='B80B0000' "
            + "BEGIN SELECT RAISE(ABORT,'forced protected equipment failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());

        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(before, shop.StoredCore(9));
        Assert.Equal(1, shop.Count(ticketId));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
        shop.Sql("DROP TRIGGER refuse_protected_upgrade;");
        await Send(shop, 7);
        Assert.True(Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Success);
        Assert.Equal(0, shop.Count(ticketId));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    [Fact]
    public async Task Amplification_mode_does_not_run_ordinary_reinforcement()
    {
        using var shop = Fixture();
        await shop.SeedCore(9, Piece());
        await shop.Seed(121, 1000, 20, 3);

        await Send(shop, 7, method: 1);

        var ack = Assert.IsType<ReinforceItemResponse>(Assert.Single(shop.Sender.Messages));
        Assert.False(ack.Success);
        Assert.Equal((byte)0x0D, ack.ErrorCode);
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
    }

    [Fact]
    public async Task Destruction_ack_and_one_virtual_delta_describe_committed_reward_and_replay_does_not_repeat_it()
    {
        using var shop = Fixture(protectedFailure: true, materialId: 3037);
        await shop.SeedCore(9, Piece() with { Attribute = 11 });
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var seed = scope.Begin())
            {
                await shop.PutShared(3037, 20);
                await seed.CommitAsync(default);
            }
        });
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(43, shop.SharedCount(3037, 1));
            Assert.Equal(99_900, shop.Gold);
            Assert.Equal(0, shop.Count(3000));
        };
        await Send(shop, 7, materialSlot: 358);
        var ack = Assert.IsType<ReinforceItemResponse>(shop.Sender.Messages[0]);
        Assert.Equal(EquipmentReinforcementOutcome.Destroyed, ack.Outcome);
        Assert.Equal(43, ack.MaterialRemaining);
        Assert.Equal(new ReinforceItemReward(new GameSlot(InventoryContainerType.Main, 358), 3037, 33),
            Assert.Single(ack.DestroyRewards!));
        var encodedAck = Encode(shop, ack);
        Assert.Equal((byte)1, encodedAck[15 + 19]);
        Assert.Equal((short)358, BitConverter.ToInt16(encodedAck, 15 + 20));
        Assert.Equal(3037, BitConverter.ToInt32(encodedAck, 15 + 22));
        Assert.Equal(33, BitConverter.ToInt32(encodedAck, 15 + 26));
        var cube = Assert.Single(DeltaEntries(shop), entry => BitConverter.ToInt16(entry, 0) == 358);
        Assert.Equal(43, BitConverter.ToInt32(cube, 6));
        var afterFirst = shop.Sender.Messages.Count;
        await Send(shop, 7, materialSlot: 358);
        Assert.Equal(afterFirst + 7, shop.Sender.Messages.Count);
        Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>());
        Assert.Equal(43, EffectRequestTransport.MainValue(shop, 358));
        Assert.Equal(43, shop.SharedCount(3037, 1));
        Assert.Equal(99_900, shop.Gold);
        var snapshot = await ReloadSnapshot(shop);
        var entries = Entries(snapshot, 15 + 5, BitConverter.ToUInt16(snapshot, 15 + 3)).ToArray();
        Assert.DoesNotContain(entries, entry => BitConverter.ToInt16(entry, 0) == 9);
        Assert.Equal(cube, Assert.Single(entries, entry => BitConverter.ToInt16(entry, 0) == 358));
    }

    [Fact]
    public async Task Destruction_commit_error_rolls_back_all_rewards_and_is_not_cached_or_notified()
    {
        using var shop = Fixture(protectedFailure: true);
        await shop.SeedCore(9, Piece() with { Attribute = 11 });
        await shop.Seed(121, 1000, 20, 3);
        var before = shop.StoredCore(9);
        shop.Sql("CREATE TRIGGER refuse_destroy_gold BEFORE UPDATE OF carried_gold ON character_stats "
            + "WHEN NEW.carried_gold <> OLD.carried_gold BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Send(shop, 7).AsTask());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(before, shop.StoredCore(9));
        Assert.Equal(0, shop.SharedCount(3037, 1));
        Assert.Equal(20, shop.Count(1000));
        Assert.Equal(100_000, shop.Gold);
        shop.Sql("DROP TRIGGER refuse_destroy_gold;");
        await Send(shop, 7);
        Assert.Equal(EquipmentReinforcementOutcome.Destroyed,
            Assert.Single(shop.Sender.Messages.OfType<ReinforceItemResponse>()).Outcome);
        Assert.Equal(33, shop.SharedCount(3037, 1));
        Assert.Equal(10, shop.Count(1000));
        Assert.Equal(99_900, shop.Gold);
    }

    private static byte[] Encode(ShopFixture shop, IServerNetworkMessage message)
    {
        var registration = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .Single(value => value.MessageType == message.GetType());
        var encoder = (IServerMessageEncoder)shop.Services.GetRequiredService(registration.EncoderType);
        return Assert.Single(encoder.Encode(message));
    }

    private static IEnumerable<byte[]> DeltaEntries(ShopFixture shop) => shop.Sender.Messages
        .Select(message => Encode(shop, message))
        .Where(frame => frame[0] == 0 && BitConverter.ToUInt16(frame, 1) == 0x000E)
        .SelectMany(frame => Entries(frame, 15 + 3, BitConverter.ToUInt16(frame, 15 + 1)));

    private static IEnumerable<byte[]> Entries(byte[] frame, int offset, int count)
    {
        for (var index = 0; index < count; index++)
            yield return frame.AsSpan(offset + index * 101, 101).ToArray();
    }

    private static async Task<byte[]> ReloadSnapshot(ShopFixture shop)
    {
        shop.Services.GetRequiredService<ICharacterContainerDomainService>().Clear(shop.Session.CharacterId);
        shop.Select(1);
        var character = await shop.Services.GetRequiredService<ICharacterInventoryEntryDomainService>()
            .LoadAsync(shop.Session.AccountId, shop.Session.CharacterId, default);
        var account = await shop.Services.GetRequiredService<IAccountInventoryEntryDomainService>()
            .LoadAsync(shop.Session.AccountId, shop.Session.CharacterId, default);
        var main = Assert.Single(character.Containers, container => container.ContainerType == InventoryContainerType.Main);
        var messageType = shop.Services.GetServices<ServerMessageEncoderRegistration>()
            .Single(registration => registration.MessageType.Name == "InventoryFullSnapshotMessage").MessageType;
        return Encode(shop, (IServerNetworkMessage)Activator.CreateInstance(messageType,
            main, character, account, shop.Clock.GetUtcNow())!);
    }

    private static ShopFixture Fixture(bool protectedFailure = false, int materialId = 1000) => new(services =>
    {
        services.AddSingleton<IEquipmentReinforcementDefinitionSource>(new Content(protectedFailure, materialId));
        services.AddSingleton<IEquipmentDefinitionSource>(new Content(protectedFailure, materialId));
        services.AddSingleton<IEquipmentDisjointDefinitionSource>(new ReinforcementDestructionTestContent(0, 10, 20520));
        services.AddSingleton<IEquipmentReinforcementRollSource>(new FixedRoll());
    });

    private static InventoryItemCoreState Piece() =>
        StackableItemCore.Create(1, 3000, 1234) with { Durability = 45 };

    private static ValueTask Send(ShopFixture shop, ushort sequence, byte method = 0,
        int expectedItemId = 3000, short materialSlot = 121) =>
        EffectRequestTransport.Send(shop,
            new ReinforceItemRequest(new(1, 0x0050, 0, sequence), method,
                9, expectedItemId, materialSlot, -1, [0x41]));

    private sealed class FixedRoll : IEquipmentReinforcementRollSource
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    private sealed class Content(bool protectedFailure, int materialId) : IEquipmentReinforcementDefinitionSource, IEquipmentDefinitionSource
    {
        public ValueTask<EquipmentReinforcementTable?> GetAmplificationTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(null);

        public ValueTask<EquipmentReinforcementTable?> GetNormalTableAsync(CancellationToken ct) =>
            ValueTask.FromResult<EquipmentReinforcementTable?>(new(
                [new(1, new double[19], 0, 1, 0, materialId, 10),
                    new(2, new double[19], 0, 1, 0, materialId, 10),
                    new(12, new double[19], protectedFailure ? 100_000 : 0, 3, 0, materialId, 10)],
                [0, 100], [1], [1], new Dictionary<int, double> { [0] = 1, [1] = 1 },
                new Dictionary<int, int>(), new Dictionary<int, int>(),
                new(3243, 40, 10, new Dictionary<int, double>(),
                    new Dictionary<int, int>()), 12));

        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(0);

        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(
            int itemId, CancellationToken ct) =>
            ValueTask.FromResult<EquipmentDefinition?>(
                itemId == 3000 ? new(1, 10, 0, "weapon", 45) : null);

        public ValueTask<EquipmentReinforcementProtectionDefinition?> GetProtectionAsync(
            int itemId, CancellationToken ct, EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement) =>
            ValueTask.FromResult<EquipmentReinforcementProtectionDefinition?>(itemId switch
            {
                7804 => new(EquipmentReinforcementProtectionKind.ResetToZero, 0),
                10007840 => new(EquipmentReinforcementProtectionKind.RetainLevel, 10),
                _ => null,
            });
    }
}
