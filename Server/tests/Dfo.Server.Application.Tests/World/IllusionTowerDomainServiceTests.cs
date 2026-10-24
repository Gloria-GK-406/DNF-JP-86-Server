using Dfo.Server.Testing;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Data.Sqlite;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.World;

public sealed class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Cleared_floor_advances_its_map_quest_asynchronously_even_after_leaving_the_tower()
    {
        var content = new Dfo.Server.Application.Tests.Quests.OrdinaryQuestEffectTests.Rules();
        content.Quest = content.Quest with { QuestId = 975, ObjectiveTypeCode = "clear map", ObjectiveArguments = [60001] };
        using var f = Fixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 1);
            f.Sql("INSERT INTO character_active_quests VALUES(1,0,975,1);");
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            Assert.Equal(1, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=975"));
            await tower.LeaveAsync(context, false, default);
        });
        await f.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();
        Assert.Equal(0, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=975"));
    }

    [Fact]
    public async Task Clear_report_keeps_the_current_floor_until_a_move_request()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(configure: services => new RecordingCloser().Use(services));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            f.Sender.Messages.Clear();

            await new Dfo.Server.Protocol.Game.IllusionTowerStage.IllusionTowerStageHandler(
                f.Services.GetRequiredService<IllusionTowerProjectionDelivery>()).HandleAsync(
                    new(f.Connection), new Dfo.Server.Protocol.Game.IllusionTowerStage.IllusionTowerStageRequest(
                        GameMessageMetadata.Empty, 2, []), default);

            f.Clock.Advance(TimeSpan.FromSeconds(30));
            await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>().SweepAsync(f.Clock.GetUtcNow(), default);
            Assert.Equal(1, tower.Find(f.Session)!.Room.Level);
            Assert.Equal(TowerRoomPhase.Cleared, tower.Find(f.Session)!.Room.Phase);
            Assert.DoesNotContain(f.Sender.Messages, message => message.MessageType == ServerMessageType.GameIllusionTowerMap);
        });
    }

    [Fact]
    public async Task Cleared_floor_keeps_drops_until_move_and_moves_immediately_without_a_minimum_stay()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Items = [6521] },
                configure: services => new RecordingCloser().Use(services));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            var killed = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            var drop = Assert.Single(killed.Drops);
            var delivery = f.Services.GetRequiredService<IllusionTowerProjectionDelivery>();
            f.Sender.Messages.Clear();

            Assert.Equal(1, tower.Find(f.Session)!.Room.Level);
            Assert.Empty(f.Sender.Messages);
            await delivery.PickupAsync(f.Session.ConnectionId, GameMessageMetadata.Empty, drop.Uid, default);
            Assert.True(Assert.Single(tower.Find(f.Session)!.Room.Items).Claimed);
            await delivery.SweepAsync(f.Clock.GetUtcNow(), default);
            Assert.Equal(1, tower.Find(f.Session)!.Room.Level);
            Assert.DoesNotContain(f.Sender.Messages, message => message.MessageType == ServerMessageType.GameIllusionTowerMap);

            await delivery.AdvanceAsync(f.Session.ConnectionId, GameMessageMetadata.Empty, default);
            Assert.Equal(2, tower.Find(f.Session)!.Room.Level);
            await delivery.AdvanceAsync(f.Session.ConnectionId, GameMessageMetadata.Empty, default);
            Assert.Single(f.Sender.Messages, message => message.MessageType == ServerMessageType.GameIllusionTowerMap);
        });
    }

    [Fact]
    public async Task Option_three_has_no_return_deadline_and_is_acknowledged_immediately()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var closer = new RecordingCloser();
            using var f = Fixture(rewards: true,
                configure: services => closer.Use(services));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var context = Context((await Enter(f)).Run!, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await tower.DieAsync(context, default);

            var handled = await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>()
                .SelectReturnAsync(f.Session.ConnectionId, GameMessageMetadata.Empty,
                    EplpCommandHandler.LeaveState, 3, default);

            Assert.True(handled);
            Assert.Null(tower.Find(f.Session)!.ReturnDueAt);
            var acknowledgement = Assert.Single(f.Sender.Messages.OfType<EplpCommandResponse>());
            Assert.Equal(EplpCommandHandler.LeaveState, acknowledgement.State);
            Assert.Equal(3, acknowledgement.Option);
            Assert.Empty(closer.Closed);
        });
    }

    [Fact]
    public async Task Failed_completion_projection_can_replay_the_committed_receipt_without_awarding_twice()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new TowerRewardTestContent();
            content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
            var closer = new RecordingCloser();
            using var f = Fixture(new Rules
                { FloorCount = 1, CardCount = 10, Probabilities = [350, 651, 0] }, rewards: true,
                configure: services =>
                {
                    TowerRewardTestContent.Register(services, content);
                    closer.Use(services);
                });
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            f.Sender.FailExplicit = true;

            Assert.True(await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>()
                .KillAsync(f.Session.ConnectionId, GameMessageMetadata.Empty,
                    run.Room.Actors[0].Uid, f.Session.WireUserId, default));

            Assert.Equal([f.Session.ConnectionId], closer.Closed);
            var committedReceipt = Assert.Single(tower.Find(f.Session)!.Completion!.Receipt!.Members);
            Assert.Contains(committedReceipt.Candidates, candidate => candidate.ItemId == 0);
            Assert.Contains(committedReceipt.Candidates, candidate => candidate.ItemId == 3000);
            var gold = f.Gold;
            var experience = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
            var items = f.Count(3000);
            Assert.True(gold > 100000);
            Assert.True(experience > 0);
            Assert.Equal(2, items);
            f.Sender.FailExplicit = false;
            f.Sender.Messages.Clear();

            var settled = tower.Find(f.Session)!;
            await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>()
                .ReprojectCompletionAsync(Context(settled, f.Session), default);

            var projectedTypes = f.Sender.Messages.Select(message => message.GetType().Name).ToArray();
            Assert.Contains("IllusionTowerRankingNotification", projectedTypes);
            Assert.Contains("IllusionTowerRewardNotification", projectedTypes);
            Assert.Contains("IllusionTowerContinueNotification", projectedTypes);
            Assert.Equal(gold, f.Gold);
            Assert.Equal(experience, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(items, f.Count(3000));
        });
    }

    [Fact]
    public async Task Settlement_return_selection_and_due_query_follow_the_tower_policy()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var f = Fixture(rewards: true, configure: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
            });
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            run = (await tower.DieAsync(context, default)).Run!;

            Assert.Equal(time.GetUtcNow().AddSeconds(60), run.ReturnDueAt);
            Assert.Empty(tower.FindReturnsDue(time.GetUtcNow().AddSeconds(59)));
            Assert.Single(tower.FindReturnsDue(time.GetUtcNow().AddSeconds(60)));

            Assert.True((await tower.SelectReturnAsync(Context(run, f.Session), 0, default)).Changed);
            Assert.Equal(time.GetUtcNow().AddSeconds(3), tower.Find(f.Session)!.ReturnDueAt);
            Assert.True((await tower.SelectReturnAsync(Context(run, f.Session), 2, default)).Changed);
            Assert.Equal(time.GetUtcNow().AddSeconds(1), tower.Find(f.Session)!.ReturnDueAt);
            Assert.True((await tower.SelectReturnAsync(Context(run, f.Session), 3, default)).Changed);
            Assert.Null(tower.Find(f.Session)!.ReturnDueAt);
            Assert.False((await tower.SelectReturnAsync(Context(run, f.Session), 4, default)).Changed);
        });
    }

    [Fact]
    public async Task Defeat_commits_real_experience_and_one_settlement_receipt()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(rewards: true);
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            var result = await tower.DieAsync(context, default);
            Assert.True(result.SettlementCreated);
            Assert.True(result.Run!.Completion!.Settled);
            Assert.NotNull(result.Run.Completion.Receipt);
            Assert.True(f.Scalar("SELECT exp FROM characters WHERE character_id=1") > f.ExperienceForLevel(50));
            var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
            var duplicate = await tower.DieAsync(context, default);
            Assert.False(duplicate.Changed);
            Assert.False(duplicate.SettlementCreated);
            Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        });
    }

    [Fact]
    public async Task Entry_consumes_ticket_and_publishes_the_first_room()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = new ShopFixture(s => s.AddSingleton<IIllusionTowerRuleService>(new Rules()));
            f.Sql($"UPDATE characters SET level=50,exp={f.ExperienceForLevel(50)};");
            await f.Seed(65, 4183, 2);
            var tower = f.Services.GetRequiredService<IIllusionTowerDomainService>();
            var result = await tower.EnterAsync([new(f.Session, 2)], f.Session, 42, default);
            Assert.Null(result.Rejection);
            Assert.Equal(1, f.Count(4183));
            Assert.Equal(1, tower.Find(f.Session)!.Room.Level);
            Assert.Equal(TowerRoomPhase.Loading, result.Run!.Room.Phase);
        });
    }

    [Theory]
    [InlineData(39, 2, "LevelTooLow")]
    [InlineData(50, 0, "InsufficientMaterial")]
    public async Task Failed_eligibility_preserves_ticket_and_membership(int level, int tickets, string reason)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            f.Sql($"UPDATE characters SET level={level},exp={f.ExperienceForLevel(checked((byte)level))};");
            if (tickets > 0) await f.Seed(65, 4183, tickets);
            var tower = Tower(f);
            Assert.Equal(reason, (await Enter(f)).Rejection);
            Assert.Null(tower.Find(f.Session));
            Assert.Equal(tickets, f.Count(4183));
        });
    }

    [Fact]
    public async Task Duplicate_and_stale_sessions_do_not_charge_again()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 3);
            var tower = Tower(f);
            var stale = f.Session with { ConnectionGeneration = f.Session.ConnectionGeneration + 1 };
            Assert.Equal("NoSession", (await tower.EnterAsync([new(stale, 0)], stale, 42, default)).Rejection);
            Assert.Equal("InvalidParty", (await tower.EnterAsync([new(f.Session, 0), new(f.Session, 1)], f.Session, 42, default)).Rejection);
            var entered = await Enter(f);
            Assert.Null(entered.Rejection);
            Assert.Equal("AlreadyInDungeon", (await Enter(f)).Rejection);
            Assert.Equal(2, f.Count(4183));
            Assert.False((await tower.LoadAsync(Context(entered.Run!, stale), default)).Changed);
        });
    }

    [Fact]
    public async Task Fifteen_floors_require_load_start_and_distinct_actor_deaths()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var original = Context(run, f.Session);
            var uids = new HashSet<ushort> { f.Session.WireUserId };
            for (var level = 1; level <= 15; level++)
            {
                var context = Context(run, f.Session);
                var actor = Assert.Single(run.Room.Actors);
                Assert.True(uids.Add(actor.Uid));
                Assert.False((await tower.StartAsync(context, default)).Changed);
                Assert.False((await tower.KillAsync(context, actor.Uid, default)).Changed);
                Assert.True((await tower.LoadAsync(context, default)).Changed);
                Assert.False((await tower.LoadAsync(context, default)).Changed);
                Assert.True((await tower.StartAsync(context, default)).Changed);
                Assert.False((await tower.StartAsync(context, default)).Changed);
                Assert.False((await tower.NextLevelAsync(context, default)).Changed);
                Assert.False((await tower.KillAsync(context with { RunId = Guid.NewGuid() }, actor.Uid, default)).Changed);
                if (level > 1)
                {
                    Assert.False((await tower.KillAsync(original, actor.Uid, default)).Changed);
                    Assert.False((await tower.KillAsync(context, (ushort)(actor.Uid - 1), default)).Changed);
                }
                var killed = await tower.KillAsync(context, actor.Uid, default);
                Assert.True(killed.Changed);
                Assert.NotNull(killed.KilledActor);
                Assert.Equal(TowerRoomPhase.Cleared, killed.Run!.Room.Phase);
                Assert.NotNull(killed.Run.Room.StartedAt);
                Assert.NotNull(killed.Run.Room.ClearedAt);
                Assert.Equal(level, killed.Run.CompletedLevels.Length);
                Assert.False((await tower.KillAsync(context, actor.Uid, default)).Changed);
                var next = await tower.NextLevelAsync(context, default);
                if (level < 15)
                {
                    Assert.True(next.Changed);
                    Assert.False((await tower.NextLevelAsync(context, default)).Changed);
                    run = next.Run!;
                    Assert.Equal(level + 1, run.Room.Level);
                }
                else
                {
                    Assert.False(next.Changed);
                    Assert.Equal(TowerEndReason.Completed, next.Run!.Completion!.Reason);
                    Assert.Equal(15, next.Run.Completion.RewardLevel);
                }
            }
        });
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(5, 4, 4)]
    public async Task Wipe_counts_completed_floors_separately(int floor, int completed, int reward)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Probabilities = [0, 0, 100] }, rewards: true);
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            for (var n = 1; n <= floor; n++)
            {
                var context = Context(run, f.Session);
                await tower.LoadAsync(context, default);
                await tower.StartAsync(context, default);
                if (n == floor)
                {
                    var result = await tower.DieAsync(context, default);
                    Assert.Equal(TowerEndReason.Defeated, result.Run!.Completion!.Reason);
                    Assert.Equal(completed, result.Run.Completion.CompletedLevels);
                    Assert.Equal(reward, result.Run.Completion.RewardLevel);
                    Assert.Equal((uint)(reward * 1066), Assert.Single(result.Run.Completion.Receipt!.Members).Experience);
                    Assert.False((await tower.DieAsync(context, default)).Changed);
                    break;
                }
                await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
                run = (await tower.NextLevelAsync(context, default)).Run!;
            }
        });
    }

    [Fact]
    public async Task Entry_and_kill_are_visible_to_same_transaction_but_discarded_on_abort()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var tower = Tower(f);
            await using (var tx = Scope(f).Begin())
            {
                var entry = await Enter(f);
                Assert.Equal(entry.Run!.RunId, tower.Find(f.Session)!.RunId);
            }
            Assert.Null(tower.Find(f.Session));
            Assert.Equal(2, f.Count(4183));
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await using (var tx = Scope(f).Begin())
            {
                await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
                Assert.Equal(TowerRoomPhase.Cleared, tower.Find(f.Session)!.Room.Phase);
            }
            Assert.Equal(TowerRoomPhase.Fighting, tower.Find(f.Session)!.Room.Phase);
            Assert.False(tower.Find(f.Session)!.Room.Actors[0].Dead);
        });
    }

    [Fact]
    public async Task Settlement_receipt_is_once_only_and_rolls_back_with_outer_use_case()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var context = Context((await Enter(f)).Run!, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            var receipt = new TowerSettlementReceipt([new(f.Session.CharacterId, 123, 456, [new(4183, 1)])]);
            await using (var tx = Scope(f).Begin())
            {
                await tower.DieAsync(context, default);
                Assert.True((await tower.MarkSettledAsync(context, receipt, default)).Changed);
                Assert.Equal(receipt, tower.Find(f.Session)!.Completion!.Receipt);
            }
            Assert.Null(tower.Find(f.Session)!.Completion);
            await using (var tx = Scope(f).Begin())
            {
                await tower.DieAsync(context, default);
                Assert.True((await tower.MarkSettledAsync(context, receipt, default)).Changed);
                Assert.False((await tower.MarkSettledAsync(context, new([]), default)).Changed);
                await tx.CommitAsync(default);
            }
            Assert.Equal(receipt, tower.Find(f.Session)!.Completion!.Receipt);
        });
    }

    [Fact]
    public async Task Room_preparation_failure_preserves_clear_room_and_uid_allocation()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var rules = new Rules();
            using var f = Fixture(rules);
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            rules.FailLevel = 2;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await tower.NextLevelAsync(context, default));
            Assert.Equal(1, tower.Find(f.Session)!.Room.Version);
            Assert.Equal(TowerRoomPhase.Cleared, tower.Find(f.Session)!.Room.Phase);
            rules.FailLevel = 0;
            Assert.Equal(run.Room.Actors[0].Uid + 1, (await tower.NextLevelAsync(context, default)).Run!.Room.Actors[0].Uid);
        });
    }

    [Fact]
    public async Task Multi_member_missing_ticket_or_sql_failure_rolls_back_all_members()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var other = await AddMember(f, 0);
            var tower = Tower(f);
            TowerEntryMember[] members = [new(f.Session, 0), new(other, 3)];
            Assert.Equal("InsufficientMaterial", (await tower.EnterAsync(members, f.Session, 1, default)).Rejection);
            Assert.Equal(2, f.Count(4183));
            await SeedOther(f, other, 2);
            f.Sql("CREATE TRIGGER fail_tower_ticket BEFORE UPDATE ON item_slots WHEN OLD.owner_id=3 BEGIN SELECT RAISE(ABORT,'ticket failure'); END;");
            await Assert.ThrowsAsync<SqliteException>(async () => await tower.EnterAsync(members, f.Session, 1, default));
            Assert.Null(tower.Find(f.Session));
            Assert.Null(tower.Find(other));
            Assert.Equal(2, f.Count(4183));
            Assert.Equal(2, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=3"));
        });
    }

    [Fact]
    public async Task Loading_party_leader_transfer_and_last_survivor_departure_are_explicit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var other = await AddMember(f, 2);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(other, 3)], f.Session, 1, default)).Run!;
            var first = Context(run, f.Session);
            var second = Context(run, other);
            Assert.Equal((byte)3, run.Members[1].Slot);
            Assert.DoesNotContain(run.Room.Actors, x => x.Uid == other.WireUserId);
            await tower.LoadAsync(first, default);
            Assert.False((await tower.StartAsync(first, default)).Changed);
            await tower.LoadAsync(second, default);
            Assert.False((await tower.StartAsync(second, default)).Changed);
            await tower.StartAsync(first, default);
            await tower.DieAsync(second, default);
            var left = await tower.LeaveAsync(first, true, default);
            Assert.Equal(TowerEndReason.Defeated, left.Run!.Completion!.Reason);
            Assert.True(Assert.Single(left.Run.Members).IsLeader);
            Assert.Null(tower.Find(f.Session));
            await tower.LeaveAsync(second, true, default);
            Assert.Null(tower.Find(other));
            Assert.Null((await Enter(f)).Rejection);
            Assert.False((await tower.LeaveAsync(first, true, default)).Changed);
            Assert.NotNull(tower.Find(f.Session));
        });
    }

    [Fact]
    public async Task Reconnected_session_cannot_be_removed_by_old_cleanup()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 3);
            var tower = Tower(f);
            var old = f.Session;
            var context = Context((await Enter(f)).Run!, old);
            var replacement = old with { ConnectionGeneration = old.ConnectionGeneration + 1 };
            var sessions = f.Services.GetRequiredService<ISelectedSessionDomainService>();
            sessions.Select(replacement);
            // The replacement is selected over the same connection and enters the world through it.
            sessions.EnterWorld(replacement);
            Assert.False((await tower.LoadAsync(context, default)).Changed);
            Assert.True((await tower.LeaveAsync(context, true, default)).Changed);
            var next = await tower.EnterAsync([new(replacement, 0)], replacement, 2, default);
            Assert.Null(next.Rejection);
            Assert.False((await tower.LeaveAsync(context, true, default)).Changed);
            Assert.NotNull(tower.Find(replacement));
        });
    }

    [Fact]
    public async Task Two_disconnected_members_can_both_be_cleaned_up()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(other, 3)], f.Session, 1, default)).Run!;
            var sessions = f.Services.GetRequiredService<ISelectedSessionDomainService>();
            sessions.ReleaseExact(f.Session);
            sessions.ReleaseExact(other);
            Assert.True((await tower.LeaveAsync(Context(run, f.Session), true, default)).Changed);
            Assert.True((await tower.LeaveAsync(Context(run, other), true, default)).Changed);
            Assert.False(tower.IsInTower(f.Session.CharacterId));
            Assert.False(tower.IsInTower(other.CharacterId));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ordinary_and_tower_entry_are_exclusive_even_inside_one_transaction(bool ordinaryFirst)
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var visits = f.Services.GetRequiredService<IDungeonVisitDomainService>();
            var tower = Tower(f);
            await using (var tx = Scope(f).Begin())
            {
                if (ordinaryFirst)
                {
                    Assert.NotNull(await visits.EnterAsync(f.Session.CharacterId, 10000, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default));
                    Assert.Equal("AlreadyInDungeon", (await Enter(f)).Rejection);
                }
                else
                {
                    Assert.Null((await Enter(f)).Rejection);
                    var result = await visits.TryEnterPartyAsync([new(f.Session.AccountId, f.Session.CharacterId, 0, true)],
                        10000, new(DungeonDifficulty.Normal, [], []), default);
                    Assert.Equal(DungeonEntryFailure.AlreadyInDungeon, result.Failure);
                }
                await tx.CommitAsync(default);
            }
            Assert.Equal(ordinaryFirst ? 2 : 1, f.Count(4183));
            Assert.Equal(!ordinaryFirst, tower.IsInTower(f.Session.CharacterId));
            if (ordinaryFirst) Assert.Equal("AlreadyInDungeon", (await Enter(f)).Rejection);
            else Assert.Null(await visits.EnterAsync(f.Session.CharacterId, 10000, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default));
        });
    }

    [Theory]
    [InlineData("map")]
    [InlineData("difficulty")]
    [InlineData("fatigue")]
    public async Task Refused_ordinary_entry_does_not_reserve_mode(string reason)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var layouts = new OrdinaryLayouts { Missing = reason == "map" };
            using var f = Fixture(layouts: layouts);
            await f.Seed(65, 4183, 2);
            var visits = f.Services.GetRequiredService<IDungeonVisitDomainService>();
            var difficulty = reason == "difficulty" ? DungeonDifficulty.Adventure : DungeonDifficulty.Normal;
            await using (var tx = Scope(f).Begin())
            {
                if (reason == "fatigue")
                    await f.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(f.Session.CharacterId, 188, default);
                Assert.Null(await visits.EnterAsync(f.Session.CharacterId, 10000, difficulty, new(difficulty, [], []), default));
                Assert.False(Tower(f).IsInTower(f.Session.CharacterId));
                await tx.CommitAsync(default);
            }
            // A failed paid-entry attempt settles its transaction before a new command can enter a tower.
            await using (var tx = Scope(f).Begin())
            {
                Assert.Null((await Enter(f)).Rejection);
                await tx.CommitAsync(default);
            }
            Assert.NotNull(Tower(f).Find(f.Session));
        });
    }

    [Fact]
    public async Task Rolled_back_ordinary_reservation_does_not_block_later_tower_entry()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            await using (var tx = Scope(f).Begin())
            {
                Assert.NotNull(await f.Services.GetRequiredService<IDungeonVisitDomainService>().EnterAsync(f.Session.CharacterId,
                    10000, DungeonDifficulty.Normal, new(DungeonDifficulty.Normal, [], []), default));
                Assert.Equal("AlreadyInDungeon", (await Enter(f)).Rejection);
            }
            Assert.Null((await Enter(f)).Rejection);
        });
    }

    [Fact]
    public async Task Prepare_failure_publishes_neither_ticket_debit_nor_challenge()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            await using (var tx = Scope(f).Begin())
            {
                Assert.Null((await Enter(f)).Rejection);
                Scope(f).Current!.Enlist(new object(), () => new PrepareFailure());
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await tx.CommitAsync(default));
            }
            Assert.Equal(2, f.Count(4183));
            Assert.Null(Tower(f).Find(f.Session));
        });
    }

    [Fact]
    public async Task Concurrent_entry_charges_only_the_winner()
    {
        using var f = Fixture();
        await f.Seed(65, 4183, 2);
        var results = await Task.WhenAll(TestGameplay.RunAsync(f.Services, () => Enter(f).AsTask()), TestGameplay.RunAsync(f.Services, () => Enter(f).AsTask()));
        Assert.Single(results, x => x.Rejection is null);
        Assert.Single(results, x => x.Rejection == "AlreadyInDungeon");
        Assert.Equal(1, f.Count(4183));
    }

    [Fact]
    public async Task Apc_kill_pays_each_current_level_once_and_advances_actual_quest_rows()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { ActorType = 5, FloorCount = 2, Probabilities = [0, 0, 100] }, rewards: true,
                configure: s => { s.AddSingleton<IQuestDefinitionDomainService, TowerQuestDefinitions>(); s.AddSingleton<IQuestContentRuleService, TowerQuestDefinitions>(); s.AddSingleton<IQuestEnemyDropDomainService, NoQuestDrops>(); s.AddSingleton<IExperienceBonusDomainService, ForbiddenBonuses>(); });
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            f.Sql($"UPDATE characters SET level=40,exp={f.ExperienceForLevel(40)} WHERE character_id=3; INSERT INTO character_active_quests VALUES(1,0,64,2),(1,1,65,2),(1,2,999,1);");
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(other, 3)], f.Session, 1, default)).Run!;
            for (var floor = 1; floor <= 2; floor++)
            {
                var context = Context(run, f.Session);
                await tower.LoadAsync(context, default);
                await tower.LoadAsync(Context(run, other), default);
                await tower.StartAsync(context, default);
                var killed = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
                Assert.Equal(new uint[] { 2076, 1431 }, killed.KillRewards.Select(x => x.Award.Gained));
                Assert.Equal(new byte[] { 0, 3 }, killed.KillRewards.Select(x => x.Slot));
                Assert.All(killed.KillRewards, x => Assert.Equal(x.Award.Base, x.Award.Gained));
                Assert.Equal(2 - floor, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=64"));
                Assert.Equal(2 - floor, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=65"));
                Assert.Equal(floor == 2 ? 0 : 1, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=999"));
                var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
                Assert.False((await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Changed);
                Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
                if (floor == 1) run = (await tower.NextLevelAsync(context, default)).Run!;
                else
                {
                    var receipt = killed.Run!.Completion!.Receipt!;
                    Assert.Equal(new byte[] { 0, 3 }, receipt.Members.Select(x => x.Slot));
                    Assert.Equal(new uint[] { 2132, 1470 }, receipt.Members.Select(x => x.Experience));
                }
            }
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Apc_kill_grants_real_quest_950_material_once_inside_the_tower_transaction()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { ActorType = 5, FloorCount = 2, Probabilities = [0, 0, 100] }, rewards: true,
                configure: services => services.AddSingleton<IItemPlacementDefinitionSource, TowerQuestPlacementContent>());
            await f.Seed(65, 4183, 1);
            f.Sql("INSERT INTO character_active_quests VALUES(1,0,950,10);");
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);

            var killed = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);

            Assert.InRange(f.Count(10089420), 1, 3);
            var reward = Assert.Single(killed.KillRewards);
            Assert.All(reward.InventoryUpdates, update => Assert.Equal(BusinessInventorySpace.Quest, update.Slot.Space));
            Assert.Equal(10 - f.Count(10089420), f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=950"));
            var count = f.Count(10089420);
            Assert.False((await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Changed);
            Assert.Equal(count, f.Count(10089420));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Quest_950_drop_respects_active_task_cap_and_a_full_quest_page()
    {
        await TestGameplay.RunAsync(async () =>
        {
            async Task<int> KillWith(int held, bool active, bool fillPage)
            {
                using var f = Fixture(new Rules { ActorType = 5, FloorCount = 2, Probabilities = [0, 0, 100] }, rewards: true,
                    configure: services => services.AddSingleton<IItemPlacementDefinitionSource, TowerQuestPlacementContent>());
                await f.Seed(65, 4183, 1);
                if (held > 0) await f.Seed(177, 10089420, held, 4);
                if (fillPage)
                {
                    for (var slot = 177; slot <= 232; slot++)
                        await f.Seed(slot, 90000000 + slot, 1, 4);
                    for (var slot = 3; slot <= 8; slot++)
                        await f.Seed(slot, 90000000 + slot, 1, 4);
                }
                if (active) f.Sql($"INSERT INTO character_active_quests VALUES(1,0,950,{10 - held});");
                var tower = Tower(f);
                var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default)).Run!;
                var context = Context(run, f.Session);
                await tower.LoadAsync(context, default);
                await tower.StartAsync(context, default);
                await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
                return f.Count(10089420);
            }

            Assert.Equal(0, await KillWith(0, active: false, fillPage: false));
            Assert.Equal(10, await KillWith(9, active: true, fillPage: false));
            Assert.Equal(10, await KillWith(10, active: true, fillPage: false));
            Assert.Equal(0, await KillWith(0, active: true, fillPage: true));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Quest_material_kill_sql_failure_rolls_back_actor_experience_material_and_progress_then_retries()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { ActorType = 5, FloorCount = 2, Probabilities = [0, 0, 100] }, rewards: true,
                configure: services => services.AddSingleton<IItemPlacementDefinitionSource, TowerQuestPlacementContent>());
            await f.Seed(65, 4183, 1);
            f.Sql("INSERT INTO character_active_quests VALUES(1,0,950,10);");
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            f.Sql("CREATE TRIGGER fail_tower_kill_exp BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'xp failure'); END;");

            await Assert.ThrowsAsync<SqliteException>(async () => await tower.KillAsync(context, run.Room.Actors[0].Uid, default));

            Assert.False(tower.Find(f.Session)!.Room.Actors[0].Dead);
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(0, f.Count(10089420));
            Assert.Equal(10, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=950"));
            f.Sql("DROP TRIGGER fail_tower_kill_exp;");
            var retried = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            Assert.True(retried.Run!.Room.Actors[0].Dead);
            Assert.InRange(f.Count(10089420), 1, 3);
            Assert.True(f.Scalar("SELECT exp FROM characters WHERE character_id=1") > f.ExperienceForLevel(50));
            Assert.Equal(10 - f.Count(10089420), f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=950"));
        });
    }

    [Fact]
    public async Task Quest_drop_preserves_shared_material_ownership_and_grant_limit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { ActorType = 5, FloorCount = 2, Probabilities = [0, 0, 100] }, rewards: true,
                configure: services => { services.AddSingleton<IQuestEnemyDropDomainService>(new SharedQuestDrop()); services.AddSingleton<IQuestDefinitionDomainService, TowerQuestDefinitions>(); services.AddSingleton<IQuestContentRuleService, TowerQuestDefinitions>(); });
            await f.Seed(65, 4183, 1);
            await f.SeedShared(3033, 9);
            f.Sql("INSERT INTO character_active_quests VALUES(1,0,950,1);");
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);

            var reward = Assert.Single((await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).KillRewards);

            Assert.Equal(10, f.SharedCount(3033));
            var update = Assert.Single(reward.InventoryUpdates);
            Assert.Equal(BusinessInventorySpace.SharedMaterial, update.Slot.Space);
            Assert.Equal(10, update.Item.Count);
        });
    }

    [Fact]
    public async Task Equipment_full_bag_preserves_all_candidates_between_inventory_and_mail()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new TowerRewardTestContent();
            content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
            using var f = Fixture(new Rules { Probabilities = [0, 1001, 100] }, rewards: true,
                configure: s => TowerRewardTestContent.Register(s, content));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var context = Context((await Enter(f)).Run!, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            var result = await tower.DieAsync(context, default);
            var receipt = Assert.Single(result.Run!.Completion!.Receipt!.Members);
            Assert.Equal(4, receipt.Candidates.Length);
            Assert.All(receipt.Candidates, x => Assert.Equal(3000, x.ItemId));
            Assert.Equal(4, receipt.Items.Length);
            Assert.Equal(4, receipt.Candidates.Count(x => x.Delivered));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));
            var update = receipt.InventoryUpdates[0];
            Assert.Equal(35, update.Item.Durability);
            Assert.Equal(update.Item.InstanceValue, receipt.Candidates[0].DisplayValue);
            Assert.All(receipt.Candidates, candidate => Assert.NotEqual(0, candidate.DisplayValue));
            var mailedSeeds = receipt.Candidates.Skip(2).Select(x => x.DisplayValue).ToArray();
            foreach (var seed in mailedSeeds)
                Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM mailbox_attachments WHERE instance_value={seed} AND claimed_flag=0"));
            Assert.Equal(2, f.Count(3000));
            Assert.Equal((ushort)35, await f.Durability(InventoryContainerType.Main, 9));
            Assert.False((await tower.DieAsync(context, default)).Changed);
            Assert.Equal(2, f.Count(3000));
        });
    }

    [Fact]
    public async Task Settlement_sql_failure_rolls_back_gold_experience_and_challenge_then_retry_awards_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Probabilities = [1001, 0, 100] }, rewards: true);
            await f.Seed(65, 4183, 2);
            var tower = Tower(f);
            var context = Context((await Enter(f)).Run!, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            f.Sql("CREATE TRIGGER fail_tower_exp BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'xp failure'); END;");
            await Assert.ThrowsAsync<SqliteException>(async () => await tower.DieAsync(context, default));
            Assert.Null(tower.Find(f.Session)!.Completion);
            Assert.False(tower.Find(f.Session)!.Members[0].Dead);
            Assert.Equal(100000, f.Gold);
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            f.Sql("DROP TRIGGER fail_tower_exp;");
            var receipt = Assert.Single((await tower.DieAsync(context, default)).Run!.Completion!.Receipt!.Members);
            Assert.Equal(16896, receipt.Gold);
            Assert.Equal(116896, receipt.GoldBalance);
            Assert.True(await tower.CanContinueAsync(context, default));
            Assert.Equal(1, f.Count(4183));
            Assert.False((await tower.DieAsync(context, default)).Changed);
            Assert.Equal(116896, f.Gold);
        });
    }

    [Fact]
    public async Task Outer_abort_restores_kill_experience_equipment_and_final_completion()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new TowerRewardTestContent();
            content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
            using var f = Fixture(new Rules { FloorCount = 1, Probabilities = [0, 1001, 100] }, rewards: true,
                configure: s => TowerRewardTestContent.Register(s, content));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await Enter(f)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            await using (var tx = Scope(f).Begin())
            {
                var result = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
                Assert.Single(result.KillRewards);
                Assert.True(result.Run!.Completion!.Settled);
                Assert.Equal(4, result.Run.Completion.Receipt!.Members[0].Items.Length);
            }
            Assert.Equal(0, f.Count(3000));
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Null(tower.Find(f.Session)!.Completion);
            Assert.False(tower.Find(f.Session)!.Room.Actors[0].Dead);
            var retried = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            Assert.True(retried.Run!.Completion!.Settled);
            Assert.Equal(2, f.Count(3000));
            Assert.False(await tower.CanContinueAsync(context, default));
        });
    }

    [Fact]
    public async Task Leaving_settles_only_valid_dead_members_and_never_awards_last_departure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Probabilities = [0, 0, 100] }, rewards: true);
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(other, 3)], f.Session, 1, default)).Run!;
            var first = Context(run, f.Session);
            var second = Context(run, other);
            await tower.LoadAsync(first, default); await tower.LoadAsync(second, default); await tower.StartAsync(first, default);
            await tower.DieAsync(second, default);
            f.Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(other);
            var left = await tower.LeaveAsync(first, true, default);
            Assert.Empty(left.Run!.Completion!.Receipt!.Members);
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=3"));
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.True((await tower.LeaveAsync(second, true, default)).Changed);
        });
    }

    [Fact]
    public async Task Synchronized_leader_alone_can_start_and_advance_the_current_run()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync(
                [new(f.Session, 0), new(other, 3)], f.Session, 1, default)).Run!;
            var first = Context(run, f.Session);
            var second = Context(run, other);
            await tower.LoadAsync(first, default);
            await tower.LoadAsync(second, default);

            var synchronized = await tower.SynchronizeLeaderAsync(first, other, default);

            Assert.True(synchronized.Changed);
            Assert.False((await tower.StartAsync(first, default)).Changed);
            Assert.True((await tower.StartAsync(second, default)).Changed);
            var cleared = await tower.KillAsync(second, run.Room.Actors[0].Uid, default);
            Assert.Equal(TowerRoomPhase.Cleared, cleared.Run!.Room.Phase);
            Assert.False((await tower.NextLevelAsync(first, default)).Changed);
            Assert.True((await tower.NextLevelAsync(second, default)).Changed);
        });
    }

    [Fact]
    public async Task Party_departure_rolls_back_before_the_in_memory_party_write_and_retries_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Probabilities = [0, 0, 100] }, rewards: true,
                configure: services => new RecordingCloser().Use(services));
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            var parties = f.Services.GetRequiredService<IPartyService>();
            var firstParty = PartyCharacter(f.Session);
            var otherParty = PartyCharacter(other);
            Assert.True(parties.RequestInvite(firstParty, otherParty,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(otherParty, firstParty).Succeeded);

            var tower = Tower(f);
            var run = (await tower.EnterAsync(
                [new(f.Session, 0), new(other, 1)], f.Session, 1, default)).Run!;
            var first = Context(run, f.Session);
            var second = Context(run, other);
            await tower.LoadAsync(first, default);
            await tower.LoadAsync(second, default);
            await tower.StartAsync(first, default);
            await tower.DieAsync(second, default);
            f.Sql("CREATE TRIGGER fail_tower_settlement BEFORE UPDATE OF exp ON characters " +
                "WHEN OLD.character_id=3 BEGIN SELECT RAISE(ABORT,'settlement failure'); END;");
            var coordinator = f.Services.GetRequiredService<IllusionTowerPartyCoordinator>();

            await Assert.ThrowsAnyAsync<Exception>(() => coordinator.TryLeaveAsync(
                f.Session, allowVirtual: false, departureResponse: null, default).AsTask());

            var unchangedParty = parties.FindParty(firstParty);
            Assert.False(unchangedParty.IsVirtual);
            Assert.Equal([f.Session.CharacterId, other.CharacterId],
                unchangedParty.Members.OrderBy(member => member.Slot)
                    .Select(member => member.CharacterId));
            Assert.Equal(f.Session.WireUserId, unchangedParty.LeaderWireUserId);
            Assert.Equal(2, tower.Find(f.Session)!.Members.Length);
            Assert.Equal(run.RunId, tower.Find(other)!.RunId);
            Assert.Null(tower.Find(f.Session)!.Completion);
            Assert.Equal((int)f.ExperienceForLevel(50), f.Scalar("SELECT exp FROM characters WHERE character_id=3"));

            f.Sql("DROP TRIGGER fail_tower_settlement;");
            var retried = await coordinator.TryLeaveAsync(
                f.Session, allowVirtual: false, departureResponse: null, default);

            Assert.True(retried.Handled);
            Assert.True(retried.Result.Succeeded);
            Assert.True(parties.FindParty(firstParty).IsVirtual);
            var survivorParty = parties.FindParty(otherParty);
            Assert.False(survivorParty.IsVirtual);
            Assert.Equal(other.WireUserId, survivorParty.LeaderWireUserId);
            Assert.Equal(other.CharacterId, Assert.Single(survivorParty.Members).CharacterId);
            Assert.Null(tower.Find(f.Session));
            var survivor = Assert.IsType<IllusionTowerSnapshot>(tower.Find(other));
            Assert.Equal(TowerEndReason.Defeated, survivor.Completion!.Reason);
            Assert.True(survivor.Completion.Settled);
            Assert.Single(survivor.Completion.Receipt!.Members);
            Assert.True(f.Scalar("SELECT exp FROM characters WHERE character_id=3") > f.ExperienceForLevel(50));
        });
    }

    [Fact]
    public async Task Successful_party_tower_entry_consumes_intent_and_kick_returns_the_member()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var intents = new TrackingIntents();
            using var f = Fixture(configure: services =>
            {
                services.RemoveAll<ICharacterSkillPointDomainService>();
                services.AddSingleton<ICharacterSkillPointDomainService,
                    CharacterSkillPointDomainService>();
                services.AddSingleton<IDungeonEntryIntentDomainService>(intents);
                RegisterEntryContent(services);
                new RecordingCloser().Use(services);
            });
            await f.Seed(65, 4183, 1);
            var other = await AddMember(f, 1);
            f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0),(2,0),(3,0); " +
                "INSERT INTO character_pvp(character_id) VALUES(1),(2),(3); " +
                "INSERT INTO character_settings(character_id) VALUES(1),(2),(3); " +
                "INSERT INTO character_expert_job(character_id) VALUES(1),(2),(3);");
            var parties = f.Services.GetRequiredService<IPartyService>();
            var firstParty = PartyCharacter(f.Session);
            var otherParty = PartyCharacter(other);
            Assert.True(parties.RequestInvite(firstParty, otherParty,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(otherParty, firstParty).Succeeded);
            var sessions = f.Services.GetRequiredService<ISelectedSessionDomainService>();
            sessions.EnsureInWorld(f.Session);
            sessions.EnsureInWorld(other);
            var positions = f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            positions.Begin(f.Session, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            positions.Begin(other, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            intents.Record(f.Session, DungeonEntryIntent.Interactive);
            intents.Record(other, DungeonEntryIntent.Interactive);
            Assert.NotNull(intents.Consume(f.Session));
            var restored = false;

            var handled = await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>()
                .EnterAsync(f.Session.ConnectionId, f.Session, parties.FindParty(firstParty),
                    DungeonEntryIntent.Interactive, () => restored = true, default);

            Assert.True(handled);
            Assert.False(restored);
            Assert.Null(intents.Consume(other));
            Assert.Equal(Tower(f).Find(f.Session)!.RunId, Tower(f).Find(other)!.RunId);
            IllusionTowerPublicFlowTests.AssertEntryDebitPrecedesTowerMode(f.Sender.Messages, receivers: 2);

            var removed = await f.Services.GetRequiredService<IllusionTowerPartyCoordinator>()
                .TryRemoveAsync(f.Session, targetSlot: 1, default);

            Assert.True(removed.Result.Succeeded);
            Assert.Null(Tower(f).Find(other));
            Assert.DoesNotContain(parties.FindParty(firstParty).Members,
                member => member.CharacterId == other.CharacterId);
            Assert.NotNull(f.Services.GetRequiredService<ITownAreaDomainService>()
                .Find(other.CharacterId));
        });
    }

    [Fact]
    public async Task Death_tower_keeps_main_on_entry_and_refreshes_it_after_native_restore()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(configure: services =>
            {
                services.RemoveAll<ICharacterSkillPointDomainService>();
                services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
                RegisterEntryContent(services);
                new RecordingCloser().Use(services);
            });
            await f.Seed(65, 4183, 2);
            await f.Seed(66, 2000, 7);
            await f.SeedShared(3033, 90);
            f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0),(2,0); " +
                "INSERT INTO character_pvp(character_id) VALUES(1),(2); " +
                "INSERT INTO character_settings(character_id) VALUES(1),(2); " +
                "INSERT INTO character_expert_job(character_id) VALUES(1),(2);");
            f.Services.GetRequiredService<ISelectedSessionDomainService>().EnsureInWorld(f.Session);
            f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
                .Begin(f.Session, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            var party = f.Services.GetRequiredService<IPartyService>().FindParty(PartyCharacter(f.Session));
            var delivery = f.Services.GetRequiredService<IllusionTowerProjectionDelivery>();

            Assert.True(await delivery.EnterAsync(f.Session.ConnectionId, f.Session, party,
                DungeonEntryIntent.Interactive, () => Assert.Fail("Entry was refused."), default, dungeonId: 11000));

            Assert.DoesNotContain(f.Sender.Messages, message => message.GetType().Name == "InventoryFullSnapshotMessage");
            f.Sender.Messages.Clear();

            Assert.True(await delivery.LeaveAndReturnAsync(f.Session.ConnectionId,
                GameMessageMetadata.Empty, response: null, default));

            Assert.Null(Tower(f).Find(f.Session));
            var returned = Snapshot();
            Assert.Contains(returned.Items, item => item.Id == 4183 && item.Count == 1);
            Assert.Contains(returned.Items, item => item.Id == 2000 && item.Count == 7);
            Assert.Contains(returned.Items, item => item.Id == 3033 && item.Count == 90);

            (IServerNetworkMessage Message, (int Slot, int Id, int Count)[] Items) Snapshot()
            {
                var registrations = f.Services.GetServices<ServerMessageEncoderRegistration>().ToDictionary(x => x.MessageType);
                var encoded = f.Sender.Messages.SelectMany(message =>
                    ((IServerMessageEncoder)f.Services.GetRequiredService(registrations[message.GetType()].EncoderType))
                        .Encode(message).Select(frame => (Message: message, Frame: frame.ToArray())));
                var snapshot = Assert.Single(encoded, value => value.Frame[0] == 0 && BitConverter.ToUInt16(value.Frame, 1) == 0x000D);
                var body = snapshot.Frame.AsSpan(15).ToArray();
                Assert.Equal((byte)InventoryContainerType.Main, body[0]);
                var count = BitConverter.ToUInt16(body, 3);
                Assert.Equal(5 + count * 101, body.Length);
                return (snapshot.Message, Enumerable.Range(0, count).Select(index =>
                {
                    var offset = 5 + index * 101;
                    return ((int)BitConverter.ToUInt16(body, offset), BitConverter.ToInt32(body, offset + 2), BitConverter.ToInt32(body, offset + 6));
                }).ToArray());
            }
        });
    }

    [Fact]
    public async Task Final_experience_cap_keeps_display_breakdown_equal_to_actual_award()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { ExperienceWeight = 100000, Probabilities = [0, 0, 100] }, rewards: true,
                configure: s => s.AddSingleton<IExperienceBonusDomainService, ForbiddenBonuses>());
            f.SetLevel(86);
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var context = Context((await Enter(f)).Run!, f.Session);
            await tower.LoadAsync(context, default);
            await tower.StartAsync(context, default);
            var receipt = Assert.Single((await tower.DieAsync(context, default)).Run!.Completion!.Receipt!.Members);
            Assert.Equal(1100000u, receipt.Experience);
            Assert.Equal(receipt.Experience, receipt.Award!.Gained);
            Assert.Equal(receipt.Experience, receipt.Award.Base);
            Assert.Equal((int)f.ExperienceForLevel(86), f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(f.ExperienceForLevel(86) + receipt.Experience, receipt.Award.NotificationTotal);
        });
    }

    private sealed class ForbiddenBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion, CancellationToken token)
            => throw new InvalidOperationException("Ordinary bonuses must not be used for tower awards.");
    }
    private sealed class TowerQuestDefinitions : IQuestDefinitionDomainService, IQuestContentRuleService
    {
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken token) => ValueTask.FromResult<QuestDefinition?>(
            id is 64 or 65 ? new(id, "", null, null, null, null, null, null, [], "hunt enemy", "", null, null, null, null, null, null, null,
                [], [], [], [], KillObjectives: QuestKillObjectiveParser.Parse("hunt enemy", [11001, -1, 608, HuntEnemyTargetTypes.AiCharacter, 2]))
            : new(id, "", null, null, null, null, null, null, [], "condition under clear", "", null, null, null, null, null, null, null,
                [], [], [], [], DungeonClearRequirement: new(11001, -1)));
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => throw new NotSupportedException();
        public QuestParameters RewardParameters() => throw new NotSupportedException();
        public QuestRewardResolution ResolveRewards(QuestDefinition definition, QuestRewardCandidate candidate, ushort selection, ushort count)
            => throw new NotSupportedException();
    }

    private sealed class TowerQuestPlacementContent : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken token) =>
            ValueTask.FromResult<ItemPlacement?>(itemId switch
            {
                10089420 => new(4, 1000, new(BusinessInventorySpace.Quest, 0, 55)),
                >= 90000177 and <= 90000232 => new(4, 1, new(BusinessInventorySpace.Quest, 0, 55)),
                3000 => new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 1))
                    { WorldClass = ItemWorldClass.Equipment },
                _ => new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 56)),
            });
    }

    private static void RegisterEntryContent(IServiceCollection services)
    {
        var content = new Support.TestCharacterContent();
        services.AddSingleton<ICharacterStatDefinitionSource>(content);
        services.AddSingleton<ISkillRuleDefinitionSource>(content);
        services.AddSingleton<IWorldContentEntryRuleService>(new Support.StubWorldContentEntryRuleService());
        services.AddSingleton<ICollectionBoxDefinitionSource, NoCollectionBoxes>();
        services.AddSingleton<IEntryPlacementRuleService>(new Support.StubEntryPlacementRuleService());
    }
    private sealed class NoQuestDrops : IQuestEnemyDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(QuestEnemyDropRequest request,
            MonsterDropParticipant participant, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }

    // Synthetic current-state reads must not acquire the production archive through collection UI.
    private sealed class NoCollectionBoxes : ICollectionBoxDefinitionSource
    {
        public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<CollectionBoxDefinition>>([]);
        public ValueTask<CollectionBoxDefinition?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<CollectionBoxDefinition?>(null);
    }

    private sealed class SharedQuestDrop : IQuestEnemyDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(QuestEnemyDropRequest request,
            MonsterDropParticipant participant, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RolledDrop>>([
                new(3033, 3, DropRarity.Common, DropItemKind.Stackable,
                    OwnerCharacterId: participant.CharacterId,
                    Delivery: RolledDropDelivery.DirectQuestItem,
                    QuestId: 950,
                    GrantLimit: 10),
            ]);
    }

    private sealed class PrepareFailure : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("fixture prepare failure");
        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class OrdinaryLayouts : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<DungeonDefinition?>(null);
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(DungeonMapSelection.Selected(layout.Cells[target].MapCandidates[0]));
        public bool Missing { get; init; }
        public ValueTask<DungeonLayout?> ResolveAsync(int dungeonId, DungeonEntryFacts facts, int seed, CancellationToken cancellationToken) =>
            ValueTask.FromResult<DungeonLayout?>(Missing ? null : new DungeonLayout(10000, 0, 1, 1, new(0, 0), new(0, 0),
                60000, seed, new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(60000)}));
    }
    private sealed class OrdinaryCells : IDungeonCellFactory
    {
        public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
    }

    /// <summary>
    /// The closes the fixture's real closer registered, read from the outbox (TASK-25); every call goes to that closer.
    /// </summary>
    private sealed class RecordingCloser : INetworkConnectionCloser
    {
        private RecordedOutbound? outbound;

        public List<NetworkConnectionId> Closed => [.. Outbound.Closes];

        private RecordedOutbound Outbound => outbound ?? throw new InvalidOperationException("The fixture has not been built.");

        public void Use(IServiceCollection services) =>
            services.AddSingleton<INetworkConnectionCloser>(provider =>
            {
                outbound = provider.GetRequiredService<RecordedOutbound>();
                return this;
            });

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected,
            CancellationToken cancellationToken) => Outbound.Closer.RequestCloseExactAsync(expected, cancellationToken);

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId,
            CancellationToken cancellationToken) => Outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
    }

    private sealed class TrackingIntents : IDungeonEntryIntentDomainService
    {
        private readonly Dictionary<SelectedSessionIdentity, DungeonEntryIntent> values = [];

        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) =>
            values[session] = intent;

        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) =>
            values.Remove(session, out var intent) ? intent : null;

        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) =>
            values.TryAdd(session, intent);

        public bool ClearExact(SelectedSessionIdentity session) => values.Remove(session);
    }

    private static ShopFixture Fixture(Rules? rules = null, OrdinaryLayouts? layouts = null, bool rewards = false, Action<IServiceCollection>? configure = null)
    {
        var f = new ShopFixture(s =>
        {
            if (!rewards) s.RemoveAll<IIllusionTowerRewardDomainService>();
            else TowerRewardTestContent.Register(s);
            s.AddSingleton<IIllusionTowerRuleService>(rules ?? new Rules());
            s.AddSingleton<IDungeonStructureRuleService>(layouts ?? new OrdinaryLayouts());
            s.AddSingleton<IDungeonAdmissionRuleService>(p => new OrdinaryAdmission(p.GetRequiredService<IDungeonStructureRuleService>()));
            s.AddSingleton<IDungeonDifficultyRuleService>(new Dfo.Server.Application.Tests.Support.TestDungeonDifficultyRules());
            // Returning from the tower reads quota projections; the synthetic fixture must not load a real PVF.
            s.AddSingleton<IAltarDungeonContentSource, NoAltarContent>();
            s.AddSingleton<ITournamentDungeonContentSource, NoTournamentContent>();
            s.AddSingleton<IGoldSilverHornContentSource, SyntheticGoldSilverHornContent>();
            s.AddSingleton<IDungeonCellFactory, OrdinaryCells>();
            s.AddSingleton<IDungeonRunDomainService>(p => new DungeonRunDomainService(p.GetRequiredService<IDomainTransactionAccessor>()));
            configure?.Invoke(s);
        });
        f.Sql($"UPDATE characters SET level=50,exp={f.ExperienceForLevel(50)};");
        return f;
    }
    private static IIllusionTowerDomainService Tower(ShopFixture f) => f.Services.GetRequiredService<IIllusionTowerDomainService>();
    private sealed class OrdinaryAdmission(IDungeonStructureRuleService layouts) : IDungeonAdmissionRuleService
    {
        public async ValueTask<DungeonAdmissionDefinition?> GetAsync(int id, CancellationToken ct) =>
            await layouts.GetDefinitionAsync(id, ct).ConfigureAwait(false) is { } d ? DungeonAdmissionDefinition.From(d) : null;
    }
    private sealed class NoAltarContent : IAltarDungeonContentSource
    {
        public ValueTask<AltarDungeonDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AltarDungeonDefinition(dungeonId, AltarDungeonKind.Endless, 1, 1, 1, 0, [],
                new(0, 0, 0, 0, [], [], [], [], ""), 3));
    }
    private sealed class NoTournamentContent : ITournamentDungeonContentSource
    {
        public ValueTask<TournamentDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new TournamentDefinition(dungeonId, 1, 1, 1, 0, 0, [], [], [], [], [], 0));
    }
    private sealed class SyntheticGoldSilverHornContent : IGoldSilverHornContentSource
    {
        public GoldSilverHornContent Load() => new(4123, 3);
    }
    private static DomainTransactionScope Scope(ShopFixture f) => (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static ValueTask<TowerEntryResult> Enter(ShopFixture f) => Tower(f).EnterAsync([new(f.Session, 2)], f.Session, 42, default);
    private static TowerActionContext Context(IllusionTowerSnapshot run, SelectedSessionIdentity session) => new(session, run.RunId, run.Room.Version);
    private static PartyCharacterSnapshot PartyCharacter(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId,
            new PartyDirectoryScope(1, 0, 0));
    private static async Task<SelectedSessionIdentity> AddMember(ShopFixture f, int tickets)
    {
        f.Sql($"INSERT INTO accounts(account_id,m_id) VALUES(2,'tower2'); INSERT INTO characters(character_id,account_id,name,job,level,exp,slot_index) VALUES(3,2,'tower3',12,50,{f.ExperienceForLevel(50)},0); INSERT INTO character_stats(character_id,carried_gold) VALUES(3,100);");
        var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new("tower-other"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(user, new(2));
        var session = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId, registry.GetRequiredUser(user).Generation, 10011);
        registry.Select(session);
        registry.EnterWorld(session);
        f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        if (tickets > 0) await SeedOther(f, session, tickets);
        return session;
    }
    private static async Task SeedOther(ShopFixture f, SelectedSessionIdentity member, int count)
    {
        await using var tx = Scope(f).Begin();
        var store = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(Scope(f).Current!);
        await store.PutAsync(ContainerId.ForCharacter(member.CharacterId, InventoryContainerType.Main), 65,
            await store.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2, 4183, count), true), default), default);
        await tx.CommitAsync(default);
    }

    internal sealed class Rules : IIllusionTowerRuleService
    {
        public IReadOnlyList<int> Items { get; set; } = [];
        public int FailLevel { get; set; }
        public byte ActorType { get; set; } = 4;
        public int FloorCount { get; set; } = 15;
        public double ExperienceWeight { get; set; } = 1;
        public int[] Probabilities { get; set; } = [50, 49, 100];
        public int CardCount { get; set; } = 4;
        public ValueTask<IllusionTowerDefinition> GetDefinitionAsync(CancellationToken cancellationToken, int dungeonId = 11001) => ValueTask.FromResult(
            new IllusionTowerDefinition(dungeonId, 40, 40, [new(4183, 1, true)],
                Enumerable.Range(1, FloorCount).Select(n => new TowerRoomDefinition(n, 60000 + n, [], 0, [], [])).ToArray(), [],
                new(Enumerable.Repeat(ExperienceWeight, 15).ToArray(), Enumerable.Repeat(CardCount, 15).ToArray(), Probabilities, 11, 3, 1), new(4, 0, 0, 0, [], [], [])));
        public ValueTask<TowerRoomSeed> CreateRoomAsync(int level, uint seed, CancellationToken cancellationToken, int dungeonId = 11001) => level == FailLevel ? throw new InvalidDataException("fixture room failure") : ValueTask.FromResult(
            new TowerRoomSeed(level, 60000 + level, seed, [new(0, 608, 40, ActorType, true, new(1, 2, 0)) { Items = Items }], []));
    }

    [Fact]
    public async Task Death_entry_replaces_memory_default_and_preserves_bag_and_shared_materials()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Items = [6515, 6521] });
            await f.Seed(65, 4183, 2);
            await f.SeedShared(3033, 90);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var main = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main, isDefault: true);
            var crystal = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.SharedMaterial, isDefault: true);
            var run = (await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            Assert.Equal(11000, run.DungeonId);
            Assert.Same(main, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main));
            Assert.Same(crystal, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.SharedMaterial));
            Assert.Same(main, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main, isDefault: true));
            Assert.Same(crystal, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.SharedMaterial, isDefault: true));
            Assert.Equal(1, (await main.GetAsync(65, default))!.Count);
            Assert.Empty(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default));
            var context = Context(run, f.Session);
            await Tower(f).LoadAsync(context, default);
            await Tower(f).StartAsync(context, default);
            var killed = await Tower(f).KillAsync(context, run.Room.Actors[0].Uid, default);
            Assert.Equal(2, killed.Drops.Length);
            var crystalDrop = killed.Drops.Single(x => x.ItemId == 6515);
            var first = await Tower(f).PickupAsync(context, crystalDrop.Uid, default);
            Assert.True(first.Action.Changed);
            Assert.Equal(BusinessInventorySpace.TowerQuickSlot, first.Update!.Slot.Space);
            Assert.False((await Tower(f).PickupAsync(context, crystalDrop.Uid, default)).Action.Changed);
            Assert.Equal(90, f.SharedCount(3033));
            var bagDrop = killed.Drops.Single(x => x.ItemId == 6521);
            Assert.True((await Tower(f).PickupAsync(context, bagDrop.Uid, default)).Action.Changed);
            Assert.DoesNotContain(await main.GetAllAsync(default), item => item.ItemId == 6521);
            var held = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            Assert.True(held.IsDefault);
            Assert.False(held.IsPersistent);
            Assert.Equal(2, (await held.GetAllAsync(default)).Count);
            await Tower(f).LeaveAsync(context, false, default);
            Assert.NotSame(held, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
            Assert.Empty(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default));
            Assert.Same(main, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main));
            Assert.Same(crystal, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.SharedMaterial));
            Assert.Single(containers.GetContainers(f.Session.CharacterId, InventoryContainerType.Main));
            Assert.Equal(90, f.SharedCount(3033));
            Assert.DoesNotContain(await main.GetAllAsync(default), item => item.ItemId == 6521);
        });
    }

    [Fact]
    public async Task Death_quickslot_potion_use_sends_remaining_then_empty_without_consuming_default_bag()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(configure: services => services.AddSingleton<IItemUseRuleDefinitionSource>(
                new FakeItemUseRules((6521, ItemUseRules.Potion(cooldownMilliseconds: 0)))));
            await f.Seed(65, 4183, 2);
            await f.Seed(4, 2000, 7);
            var run = (await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var current = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            var permanent = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main, isDefault: true);
            await using (var tx = Scope(f).Begin())
            {
                var lifecycle = f.Services.GetRequiredService<IItemLifecycleDomainService>();
                await current.PutAsync(4, await lifecycle.CreateAsync(
                    ItemSnapshot.FromCore(StackableItemCore.Create(2, 6521, 2), true), default), default);
                await tx.CommitAsync(default);
            }
            var handler = f.Services.GetRequiredService<Dfo.Server.Protocol.Game.UseStackable.UseStackableHandler>();
            var registrations = f.Services.GetServices<ServerMessageEncoderRegistration>().ToDictionary(x => x.MessageType);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                f.Sender.Messages.Clear();
                var request = new Dfo.Server.Protocol.Game.UseStackable.UseStackableRequest(
                    GameMessageMetadata.Empty, new(InventoryContainerType.QuickSlot, 4), 25333, 6521);
                using var source = f.Services.GetRequiredService<Dfo.Server.Application.Commands.NetworkCommandAuthorization>()
                    .Enter(() => true, request.Metadata, Dfo.Server.Application.Commands.NetworkCommandAuthorization.MessageCategory.Effect);
                // Explicit test request state per attempt; this driver does not use the network replay ledger.
                using var operation = f.Services.GetRequiredService<Dfo.Server.Application.Effects.EffectOperationContext>().Enter(
                    new(f.Session, attempt + 1, 1, 0x2C, 0, "explicit-test-attempt-" + attempt, 0),
                    f.Services.GetRequiredService<Dfo.Server.Application.Commands.SelectedSessionSafety>().Capture(f.Session, request.Metadata),
                    request.MinimumReplayNeeds);
                await handler.HandleAsync(new(f.Connection), request, default);
                var frames = f.Sender.Messages.SelectMany(message =>
                    ((IServerMessageEncoder)f.Services.GetRequiredService(registrations[message.GetType()].EncoderType))
                        .Encode(message).Select(frame => frame.ToArray())).ToArray();
                Assert.Equal(2, frames.Length);
                Assert.Equal(1, frames[0][0]);
                Assert.Equal(0x002C, BitConverter.ToUInt16(frames[0], 1));
                Assert.Equal(attempt < 2 ? 1 : 0, frames[0][15]);
                Assert.Equal(0, frames[1][0]);
                Assert.Equal(0x000E, BitConverter.ToUInt16(frames[1], 1));
                var body = frames[1].AsSpan(15).ToArray();
                Assert.Equal(104, body.Length);
                Assert.Equal((byte)InventoryContainerType.QuickSlot, body[0]);
                Assert.Equal(1, BitConverter.ToUInt16(body, 1));
                Assert.Equal(4, BitConverter.ToUInt16(body, 3));
                Assert.Equal(attempt == 0 ? 6521 : -1, BitConverter.ToInt32(body, 5));
                Assert.Equal(attempt == 0 ? 1 : 0, BitConverter.ToInt32(body, 9));
                Assert.Equal(7, (await permanent.GetAsync(4, default))!.Count);
            }
            Assert.Null(await current.GetAsync(4, default));
            await Tower(f).LeaveAsync(Context(run, f.Session), false, default);
            Assert.Same(permanent, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main));
            Assert.Equal(7, (await permanent.GetAsync(4, default))!.Count);
        });
    }

    [Fact]
    public async Task Death_entry_prepare_failure_rolls_back_ticket_and_both_bindings()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var before = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            await using (var tx = Scope(f).Begin())
            {
                Assert.NotNull((await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run);
                Assert.NotSame(before, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
                Scope(f).Current!.Enlist(new object(), () => new PrepareFailure());
                await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
            }
            Assert.Null(Tower(f).Find(f.Session));
            Assert.Same(before, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
            Assert.True(containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).IsDefault);
            Assert.True(containers.GetContainer(f.Session.CharacterId, InventoryContainerType.SharedMaterial).IsDefault);
            Assert.Equal(2, (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAsync(65, default))!.Count);
        });
    }

    [Fact]
    public async Task Death_pickup_rollback_keeps_drop_claimable_and_leave_rollback_keeps_temporary_bindings()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Items = [6521] });
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            var killed = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
            var item = Assert.Single(killed.Drops);
            await using (var tx = Scope(f).Begin())
            {
                Assert.True((await tower.PickupAsync(context, item.Uid, default)).Action.Changed);
                Scope(f).Current!.Enlist(new object(), () => new PrepareFailure());
                await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
            }
            Assert.True((await tower.PickupAsync(context, item.Uid, default)).Action.Changed);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var temporary = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            await using (var tx = Scope(f).Begin())
            {
                await tower.LeaveAsync(context, false, default);
                Assert.NotSame(temporary, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
                Scope(f).Current!.Enlist(new object(), () => new PrepareFailure());
                await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(default).AsTask());
            }
            Assert.Same(temporary, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
            Assert.NotNull(tower.Find(f.Session));
            Assert.Single(await temporary.GetAllAsync(default));
            await tower.LeaveAsync(context, true, default);
            Assert.NotSame(temporary, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
        });
    }

    [Fact]
    public async Task Ordinary_and_default_rewards_keep_reaching_permanent_bags_inside_death_tower()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 2); await f.SeedShared(3033, 90);
            var run = (await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var grants = f.Services.GetRequiredService<IItemGrantDomainService>();
            await using (var tx = Scope(f).Begin())
            {
                var ordinary = await grants.GrantAsync(f.Session.AccountId, f.Session.CharacterId, containers, 3033, 2, default);
                Assert.True(ordinary.Container.IsPersistent);
                var permanent = await grants.GrantAsync(f.Session.AccountId, f.Session.CharacterId, containers, 3033, 3, default, isDefault: true);
                Assert.True(permanent.Container.IsDefault);
                await tx.CommitAsync(default);
            }
            Assert.Equal(95, f.SharedCount(3033));
            var temp = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            Assert.Empty(await temp.GetAllAsync(default));
            await Tower(f).LeaveAsync(Context(run, f.Session), false, default);
            Assert.Equal(95, f.SharedCount(3033));
        });
    }

    [Fact]
    public async Task Death_settlement_before_exit_awards_permanent_equipment_and_projects_it_to_the_visible_main_bag()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var content = new TowerRewardTestContent();
            content.Table = content.Table with { ItemTypeRates = [0, 10000, 0, 0] };
            using var f = Fixture(new Rules { FloorCount = 1, Probabilities = [0, 1001, 100] }, rewards: true,
                configure: s => { TowerRewardTestContent.Register(s, content); new RecordingCloser().Use(s); });
            await f.Seed(65, 4183, 2);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            await f.Services.GetRequiredService<IllusionTowerProjectionDelivery>().KillAsync(f.Session.ConnectionId,
                GameMessageMetadata.Empty, run.Room.Actors[0].Uid, f.Session.WireUserId, default);
            var ended = tower.Find(f.Session)!;
            Assert.True(ended.Completion!.Settled);
            Assert.Equal(2, f.Count(3000));
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            Assert.Equal(2, (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default)).Count(x => x.ItemId == 3000));
            Assert.Contains(f.Sender.Messages, message => message.GetType().Name == "InventoryDeltaMessage");
            Assert.True(await tower.CanContinueAsync(context, default));
            await tower.LeaveAsync(context, false, default);
            Assert.Equal(2, (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default)).Count(x => x.ItemId == 3000));
            Assert.Equal(2, f.Count(3000));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Death_current_pvf_all_45_floors_progress_and_every_drop_stays_in_run_inventory()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(configure: s =>
            {
                s.AddSingleton<IIllusionTowerRuleService, IllusionTowerRuleService>();
                s.AddSingleton<IItemPlacementDefinitionSource, Dfo.Server.Infrastructure.Content.PvfItemPlacementDefinitionSource>();
            });
            await f.Seed(65, 4183, 2); await f.SeedShared(3033, 90);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var pickupCount = 0;
            for (var floor = 1; floor <= 45; floor++)
            {
                Assert.Equal(floor, run.Room.Level);
                Assert.Equal(30000 + floor, run.Room.MapId);
                var context = Context(run, f.Session);
                await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
                foreach (var actor in run.Room.Actors)
                {
                    var killed = await tower.KillAsync(context, actor.Uid, default);
                    Assert.True(killed.Changed);
                    foreach (var drop in killed.Drops)
                    {
                        var picked = await tower.PickupAsync(context, drop.Uid, default);
                        if (picked.Action.Changed)
                        {
                            pickupCount++;
                            Assert.Equal(BusinessInventorySpace.TowerQuickSlot, picked.Update!.Slot.Space);
                        }
                        else
                        {
                            Assert.True(picked.NoRoom);
                            Assert.False(tower.Find(f.Session)!.Room.Items.Single(item => item.Uid == drop.Uid).Claimed);
                        }
                    }
                }
                run = tower.Find(f.Session)!;
                if (floor < 45)
                {
                    run = (await tower.NextLevelAsync(Context(run, f.Session), default)).Run!;
                }
            }
            Assert.True(pickupCount > 20);
            Assert.Equal(TowerEndReason.Completed, run.Completion!.Reason);
            Assert.Equal(45, run.Completion.CompletedLevels);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            Assert.Equal(6, (await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default)).Count);
            Assert.All(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main, isDefault: true).GetAllAsync(default), item => Assert.Equal(4183, item.ItemId));
            Assert.Equal(90, f.SharedCount(3033));
            await tower.LeaveAsync(Context(run, f.Session), false, default);
            Assert.True(containers.GetContainer(f.Session.CharacterId, InventoryContainerType.Main).IsDefault);
            Assert.Equal(1, f.Count(4183));
            Assert.Equal(90, f.SharedCount(3033));
        });
    }

    [Fact]
    public async Task Death_skill_materials_consume_tower_cube_without_touching_account_crystals()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture();
            await f.Seed(65, 4183, 1); await f.SeedShared(3033, 90);
            await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var costs = f.Services.GetRequiredService<IMaterialConsumptionDomainService>();
            const int index = 0;
            await using (var tx = Scope(f).Begin())
            {
                var denied = await costs.ConsumeAsync(f.Session, [new(new(BusinessInventorySpace.TowerQuickSlot, index), 6515, 1)], default);
                Assert.False(denied.Success);
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAsync(f.Session.AccountId,
                    f.Session.CharacterId, containers, 6515, 2, default);
                Assert.True((await costs.ConsumeAsync(f.Session, [new(new(BusinessInventorySpace.TowerQuickSlot, index), 6515, 2)], default)).Success);
                await tx.CommitAsync(default);
            }
            Assert.Equal(90, f.SharedCount(3033));
            Assert.Empty(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default));
        });
    }

    [Fact]
    public async Task Death_command_pickup_publishes_drop_removal_and_item_update_without_leaking_to_permanent_storage()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var f = Fixture(new Rules { Items = [6521] }, configure: s => new RecordingCloser().Use(s));
            await f.Seed(65, 4183, 1);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            var delivery = f.Services.GetRequiredService<IllusionTowerProjectionDelivery>();
            await delivery.KillAsync(f.Session.ConnectionId, GameMessageMetadata.Empty, run.Room.Actors[0].Uid, f.Session.WireUserId, default);
            var drop = Assert.Single(tower.Find(f.Session)!.Room.Items);
            f.Sender.Messages.Clear();
            await f.Services.GetRequiredService<Dfo.Server.Protocol.Game.GroundPickup.GetItemHandler>().HandleAsync(new(f.Connection),
                new Dfo.Server.Protocol.Game.GroundPickup.GetItemRequest(GameMessageMetadata.Empty, drop.Uid), default);
            Assert.True(Assert.Single(f.Sender.Messages.OfType<Dfo.Server.Protocol.Game.GroundPickup.GetItemResponse>()).Accepted);
            Assert.Equal(drop.Uid, Assert.Single(f.Sender.Messages.OfType<Dfo.Server.Protocol.Game.GroundPickup.GroundPickupNotification>()).GroundDropNumber);
            var delivered = f.Sender.Messages.Select(message => message.GetType().Name).ToArray();
            Assert.True(Array.IndexOf(delivered, "GetItemResponse") < Array.IndexOf(delivered, "GroundPickupNotification"));
            Assert.True(Array.IndexOf(delivered, "GroundPickupNotification") < Array.IndexOf(delivered, "InventoryDeltaMessage"));
            Assert.Equal(0, f.Count(2000));
            Assert.True(tower.Find(f.Session)!.Room.Items.Single().Claimed);
        });
    }

    [Fact]
    public async Task Death_tower_quickslots_cannot_accept_persistent_worn_equipment()
    {
        using var f = Fixture();
        await f.Seed(65, 4183, 1);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000);
        });
        var bus = f.Services.GetRequiredService<Dfo.Server.Application.Commands.ICommandBus>();
        var result = await bus.SendAsync(new Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommand(f.Session.CharacterId,
            new(BusinessInventorySpace.WornEquipment, 0), 1, new(BusinessInventorySpace.TowerQuickSlot, 0)) { Session = f.Session }, default);
        Assert.Equal(Dfo.Server.Protocol.Game.MoveItemSpace.ItemMoveStatus.ItemCannotOccupySlot, result.Status);
    }

    [Fact]
    public async Task Seven_tower_templates_share_six_slots_stack_when_full_and_reset_only_between_runs()
    {
        await TestGameplay.RunAsync(async () =>
        {
            int[] ids = [6515, 6518, 6519, 6520, 6521, 6523, 6524];
            using var f = Fixture(new Rules { Items = ids });
            await f.Seed(65, 4183, 2);
            var tower = Tower(f);
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            var memory = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            var itemSequence = f.Scalar("SELECT COALESCE(MAX(seq),0) FROM sqlite_sequence WHERE name='items'");
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            var drops = (await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Drops;
            Assert.Equal(7, drops.Length);
            foreach (var drop in drops.Take(6)) Assert.True((await tower.PickupAsync(context, drop.Uid, default)).Action.Changed);
            var refused = await tower.PickupAsync(context, drops[6].Uid, default);
            Assert.True(refused.NoRoom);
            Assert.False(tower.Find(f.Session)!.Room.Items.Single(x => x.Uid == drops[6].Uid).Claimed);
            Assert.Equal(6, (await memory.GetAllAsync(default)).Count);
            Assert.Equal(itemSequence, f.Scalar("SELECT COALESCE(MAX(seq),0) FROM sqlite_sequence WHERE name='items'"));
            Assert.All(ids, id => Assert.Equal(0, f.Count(id)));

            run = (await tower.NextLevelAsync(context, default)).Run!;
            Assert.Same(memory, containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot));
            context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            drops = (await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Drops;
            Assert.True((await tower.PickupAsync(context, drops[0].Uid, default)).Action.Changed);
            Assert.Equal(2, (await memory.GetAllAsync(default)).Single(x => x.ItemId == ids[0]).Count);
            await using (var tx = Scope(f).Begin())
            {
                var result = await f.Services.GetRequiredService<IMaterialConsumptionDomainService>()
                    .ConsumeAsync(f.Session, [new(new(BusinessInventorySpace.TowerQuickSlot, 0), ids[0], 2)], default);
                Assert.True(result.Success);
                await tx.CommitAsync(default);
            }
            Assert.True((await tower.PickupAsync(context, drops[6].Uid, default)).Action.Changed);
            Assert.Equal(ids[6], (await memory.GetAsync(3, default))!.ItemId);
            await tower.LeaveAsync(context, true, default);
            Assert.Empty(await containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot).GetAllAsync(default));
            var restarted = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 99, default, 11000)).Run!;
            var fresh = containers.GetContainer(f.Session.CharacterId, InventoryContainerType.QuickSlot);
            Assert.NotEqual(memory.Id, fresh.Id);
            Assert.Empty(await fresh.GetAllAsync(default));
            await tower.LeaveAsync(Context(restarted, f.Session), false, default);
        });
    }

    [Fact]
    public async Task Tower_slots_swap_in_memory_and_replay_reads_the_active_run_only()
    {
        using var f = Fixture(new Rules { Items = [6515, 6521] });
        await f.Seed(65, 4183, 1);
        await f.Seed(3, 2000, 7);
        var tower = Tower(f);
        var (run, context) = await TestGameplay.RunAsync(f.Services, async () =>
        {
            var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            foreach (var drop in (await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Drops)
                Assert.True((await tower.PickupAsync(context, drop.Uid, default)).Action.Changed);
            return (run, context);
        });
        var bus = f.Services.GetRequiredService<Dfo.Server.Application.Commands.ICommandBus>();
        var moved = await bus.SendAsync(new Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommand(
            f.Session.CharacterId, new(BusinessInventorySpace.TowerQuickSlot, 0), 1,
            new(BusinessInventorySpace.TowerQuickSlot, 1)) { Session = f.Session }, default);
        Assert.Equal(Dfo.Server.Protocol.Game.MoveItemSpace.ItemMoveStatus.Moved, moved.Status);
        var escaped = await bus.SendAsync(new Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommand(
            f.Session.CharacterId, new(BusinessInventorySpace.TowerQuickSlot, 0), 1,
            new(BusinessInventorySpace.Consumable, 1)) { Session = f.Session }, default);
        Assert.Equal(Dfo.Server.Protocol.Game.MoveItemSpace.ItemMoveStatus.ItemCannotOccupySlot, escaped.Status);
        var request = new Dfo.Server.Abstractions.Effects.ReplayProjectionRequest(
            Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.Inventory, [new(BusinessInventorySpace.TowerQuickSlot, 0)]);
        var active = await bus.SendAsync(new Dfo.Server.Application.Effects.ReadReplayStateCommand(f.Session, request), default);
        var memory = active.Inventory!.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.QuickSlot);
        Assert.Equal(6521, memory.Items.Single(x => x.SlotIndex == 3).Core.ItemId);
        Assert.Equal(6515, memory.Items.Single(x => x.SlotIndex == 4).Core.ItemId);
        Assert.Equal(7, f.Count(2000));
        Assert.Equal(0, f.Count(6515)); Assert.Equal(0, f.Count(6521));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await tower.LeaveAsync(context, false, default);
        });
        var returned = await bus.SendAsync(new Dfo.Server.Application.Effects.ReadReplayStateCommand(f.Session, request), default);
        Assert.DoesNotContain(returned.Inventory!.Character().Containers, x => x.ContainerType == InventoryContainerType.QuickSlot);
        Assert.Equal(2000, returned.Inventory.Character().Containers.Single(x => x.ContainerType == InventoryContainerType.Main)
            .Items.Single(x => x.SlotIndex == 3).Core.ItemId);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
