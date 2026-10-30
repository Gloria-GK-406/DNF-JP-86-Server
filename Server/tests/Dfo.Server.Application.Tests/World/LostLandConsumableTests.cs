using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class LostLandConsumableTests
{
    [Theory]
    [InlineData(5, 2, 101)]
    [InlineData(5, 2, 4)]
    [InlineData(7, 2, 4)]
    public async Task Observed_capsule_origin_coordinates_are_accepted_from_bag_and_quickbar(byte x, byte y, short slot)
    {
        using var fixture = Fixture(origin: new(x, y));
        await fixture.Seed(slot, 10093346, 2);
        await Send(fixture, 101, y, originX: x, slot: slot);
        Assert.Equal(1, fixture.Count(10093346));
        Assert.True(Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(10, 2)]
    public async Task Stale_or_out_of_bounds_capsule_origins_never_consume_or_grant_a_warp(uint x, uint y)
    {
        using var fixture = Fixture(origin: new(5, 2));
        await fixture.Seed(65, 10093346, 2);
        await Send(fixture, 101, y, originX: x);
        Assert.Equal(2, fixture.Count(10093346));
        Assert.False(Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var current = runs.Find(fixture.Session.CharacterId)!;
            Assert.Equal(new MazeCell(5, 2), current.CurrentCell);
        });
    }

    [Fact]
    public async Task Capsule_moves_immediately_without_client_move_request_and_preserves_run()
    {
        using var fixture = Fixture(charged: true);
        await fixture.Seed(65, 10093346, 2);
        var before = TestGameplay.Run(fixture.Services, () => fixture.Services
            .GetRequiredService<IDungeonRunDomainService>().Find(fixture.Session.CharacterId)!);
        await Send(fixture, 101, 7);
        var moved = TestGameplay.Run(fixture.Services, () => fixture.Services
            .GetRequiredService<IDungeonRunDomainService>().Find(fixture.Session.CharacterId)!);
        Assert.Equal(new MazeCell(2, 0), moved.CurrentCell);
        Assert.Equal(61002, moved.CurrentRoomMapId);
        Assert.Equal(before.RunId, moved.RunId);
        Assert.Equal(before.StartedAt, moved.StartedAt);
        Assert.Equal(1, before.LostLandFever()!.Percent);
        Assert.Equal(before.LostLandFever(), moved.LostLandFever());
        Assert.Equal(before.RoomVersion + 1, moved.RoomVersion);
        Assert.Empty(moved.ReadyParticipants);
        Assert.Single(fixture.Sender.Messages, message => message.GetType().Name == "StartMapNotification");
        var sent = fixture.Sender.Messages.ToArray();
        Assert.IsType<QuestClearCouponResponse>(sent[0]);
        Assert.Equal("StartMapNotification", sent[^1].GetType().Name);
        await Send(fixture, 101, 7);
        Assert.Single(fixture.Sender.Messages, message => message.GetType().Name == "StartMapNotification");
        Assert.Equal(1, fixture.Count(10093346));
    }

    [Fact]
    public async Task Alternate_declared_transformation_consumes_from_quickbar_in_persistent_inventory()
    {
        using var fixture = Fixture();
        await fixture.Seed(4, 10092764, 1);
        await Send(fixture, 99, 0, slot: 4);
        Assert.True(Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        Assert.Equal(0, fixture.Count(10092764)); // Count reads SQLite through a separate connection.
        Assert.DoesNotContain(fixture.Sender.Messages, message => message.GetType().Name == "StartMapNotification");
    }

    [Fact]
    public async Task Failed_inventory_flush_rolls_back_the_room_and_cooldown_before_retry()
    {
        using var fixture = Fixture();
        await fixture.Seed(65, 10093346, 2);
        fixture.Sql("CREATE TRIGGER reject_capsule_update BEFORE UPDATE ON item_slots WHEN OLD.count=2 BEGIN SELECT RAISE(ABORT, 'forced capsule failure'); END;");
        fixture.Sql("CREATE TRIGGER reject_capsule_delete BEFORE DELETE ON item_slots WHEN OLD.count=2 BEGIN SELECT RAISE(ABORT, 'forced capsule failure'); END;");
        await Send(fixture, 101, 7);
        Assert.Equal(2, fixture.Count(10093346));
        var current = TestGameplay.Run(fixture.Services, () => fixture.Services
            .GetRequiredService<IDungeonRunDomainService>().Find(fixture.Session.CharacterId)!);
        Assert.Equal(new MazeCell(0, 7), current.CurrentCell);
        Assert.False(Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
        Assert.DoesNotContain(fixture.Sender.Messages, message => message.GetType().Name == "StartMapNotification");
        fixture.Sql("DROP TRIGGER reject_capsule_update");
        fixture.Sql("DROP TRIGGER reject_capsule_delete");
        await Send(fixture, 101, 7, sequence: 8);
        Assert.Equal(1, fixture.Count(10093346));
        Assert.Single(fixture.Sender.Messages, message => message.GetType().Name == "StartMapNotification");
    }

    [Theory]
    [InlineData(99u, 10092741)]
    [InlineData(101u, 10093346)]
    public async Task A_new_request_during_cooldown_never_consumes_another_item(uint action, int item)
    {
        using var fixture = Fixture();
        await fixture.Seed(65, item, 2);
        await Send(fixture, action, action == 101 ? 7u : 0u);
        if (action == 101) TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var moved = runs.Find(fixture.Session.CharacterId)!;
            runs.TryMarkParticipantReady(fixture.Session.CharacterId, moved.RunId, moved.RoomVersion);
        });
        await Send(fixture, action, 0, sequence: 8, originX: action == 101 ? 2u : 0u);
        Assert.Equal(1, fixture.Count(item));
        Assert.Equal(new[] { true, false }, fixture.Sender.Messages.OfType<QuestClearCouponResponse>().Select(reply => reply.Succeeded));
    }

    [Theory]
    [InlineData(false, 99u, 10092741)]
    [InlineData(false, 101u, 10093346)]
    [InlineData(true, 99u, 10093346)]
    [InlineData(true, 101u, 10092741)]
    public async Task Wrong_location_or_action_does_not_consume(bool inDungeon, uint action, int item)
    {
        using var fixture = Fixture(inDungeon);
        await fixture.Seed(65, item, 2);
        await Send(fixture, action, 0);
        Assert.Equal(2, fixture.Count(item));
        Assert.False(Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>()).Succeeded);
    }

    [Fact]
    public async Task Rollback_restores_quantity_cooldown_and_room()
    {
        using var fixture = Fixture();
        await fixture.Seed(65, 10093346, 2);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var transactions = (DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = transactions.Begin())
            {
                await TestEffectFootprints.Run([fixture.Session], cap => fixture.Services
                    .GetRequiredService<ILostLandConsumableDomainService>().UseAsync(fixture.Session,
                        new(Dfo.Server.Data.Inventory.BusinessInventorySpace.Consumable, 0),
                        LostLandConsumableKind.Teleport, new(0, 7), cap, default));
            }
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var current = runs.Find(fixture.Session.CharacterId)!;
            Assert.Equal(new MazeCell(0, 7), current.CurrentCell);
        });
        Assert.Equal(2, fixture.Count(10093346));
        await Send(fixture, 101, 7);
        Assert.Equal(1, fixture.Count(10093346));
    }

    [Theory]
    [InlineData(10092741, 99, 0)]
    [InlineData(10093346, 101, 7)]
    public async Task Native_action_consumes_one_and_replay_never_consumes_twice(int item, uint action, uint tail)
    {
        using var fixture = Fixture();
        await fixture.Seed(65, item, 2);
        await Send(fixture, action, tail);
        Assert.Equal(1, fixture.Count(item));
        var ack = Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>());
        Assert.True(ack.Succeeded);
        Assert.Equal(action, ack.Action);
        await Send(fixture, action, tail);
        Assert.Equal(1, fixture.Count(item));
        Assert.Single(fixture.Sender.Messages.OfType<QuestClearCouponResponse>());
    }

    internal static ShopFixture Fixture(bool inDungeon = true, MazeCell? origin = null, bool charged = false)
    {
        var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<IDungeonStructureRuleService, Layouts>();
            services.AddSingleton<IDungeonCellFactory, Cells>();
            services.AddSingleton<IItemUseRuleDefinitionSource, Rules>();
            services.AddSingleton<ILostLandConsumableDefinitionSource, Definitions>();
            services.AddSingleton<IDungeonRunDomainService>(provider =>
                global::Dfo.Server.Testing.TestParties.Runs(provider.GetRequiredService<IDomainTransactionAccessor>(),
                parties: provider.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>(),
                participation: provider.GetRequiredService<IDungeonParticipationDomainService>(),
                clock: provider.GetRequiredService<TimeProvider>()));
        });
        if (inDungeon) TestGameplay.Run(fixture.Services, () =>
        {
            var start = origin ?? new MazeCell(0, 7);
            var layout = new DungeonLayout(4000, 0, 10, 10, start, new(2, 0), 61000, 17,
                new Dictionary<MazeCell, DungeonCellDefinition>
                {
                    [start] = DungeonCellDefinition.Fixed(61000),
                    [new(2, 0)] = DungeonCellDefinition.Fixed(61002),
                }) { Mode = new DungeonRunMode.LostLand(new(600, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])) };
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = fixture.InTransaction(() => runs.PreparePartyEntry(
                [new(fixture.Session.AccountId, fixture.Session.CharacterId, 0, true)], layout, 0,
                new(layout.StartCell, 61000, 1,
                    [new DungeonCellActorState(1, new(3001, 1, 0) { IsHostile = true }, false, false, [], IsMonster: true)], []),
                DungeonRunId.New(), fixture.Clock.GetUtcNow()).Run);
            if (charged) runs.CommitActorDeathChange(runs.TryClaimActorDeath(fixture.Session.CharacterId, 1)!
                with { OccurredAt = fixture.Clock.GetUtcNow() });
            runs.TryMarkParticipantReady(fixture.Session.CharacterId, run.RunId, run.RoomVersion);
        });
        return fixture;
    }

    internal static ValueTask Send(ShopFixture fixture, uint action, uint tail, ushort sequence = 7,
        uint originX = 0, short slot = 65)
    {
        var body = new byte[19];
        BinaryPrimitives.WriteInt16LittleEndian(body, slot);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(7), action);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(11), originX);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(15), tail);
        return EffectRequestTransport.SendRaw(fixture, new GameMessageMetadata(1, 0x0204, 0, sequence), body);
    }

    private sealed class Layouts : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token)
        {
            Assert.Null(from); // Capsule destination does not require an ordinary entrance direction.
            return ValueTask.FromResult(DungeonMapSelection.Selected(61002));
        }
    }
    private sealed class Cells : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) =>
            ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
    }
    private sealed class Rules : IItemUseRuleDefinitionSource
    {
        public ValueTask<ItemUseRule?> GetAsync(int itemId, CancellationToken token) =>
            ValueTask.FromResult<ItemUseRule?>(new(true, 10000, 0, 1, ItemJobRestriction.Any,
                ItemPlaceRestriction.Anywhere, Action: itemId is 10092741 or 10092764 ? "[riding]" : "[move map]"));
    }
    private sealed class Definitions : ILostLandConsumableDefinitionSource
    {
        public ValueTask<LostLandConsumableDefinition?> GetAsync(int itemId, CancellationToken token) =>
            ValueTask.FromResult<LostLandConsumableDefinition?>(itemId is 10092741 or 10092764
                ? new(LostLandConsumableKind.Transformation, null, [])
                : itemId is >= 10093342 and <= 10093346 ? new(LostLandConsumableKind.Teleport, new(2, 0), []) : null);
    }
}
