using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestMapClearTests
{
    [Dfo.Server.Testing.PvfFact]
    public async Task Actual_awakening_quest_975_targets_the_map_of_death_tower_floor_30()
    {
        await using var f = new LearningFixture(useArchive: true);
        await f.SeedAsync();
        var content = f.Services.GetRequiredService<IQuestContentRuleService>();
        var quest = await content.FindAsync(975, default);
        Assert.NotNull(quest);
        var room = await f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerRuleService>()
            .CreateRoomAsync(30, 42, default, 11000);
        Assert.Equal(30030, room.MapId);
        Assert.Equal(room.MapId, new QuestMapClearRuleService().TargetMapOf(quest));
        Assert.Equal(1u, quest.InitialTrigger);
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,975,1)");
        await Send(f, Clear(DateTimeOffset.UtcNow));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
    }

    private static OrdinaryQuestEffectTests.Rules Content()
    {
        var content = new OrdinaryQuestEffectTests.Rules();
        content.Quest = content.Quest with { QuestId = 975, ObjectiveTypeCode = "clear map", ObjectiveArguments = [30030], InitialTrigger = 1 };
        return content;
    }
    private static DungeonMapCleared Clear(DateTimeOffset at, int map = 30030) =>
        new(DungeonRunId.New(), 11000, 0, new(0, 29), map, 30, at, [new(new(1), new(71), 0, true)]);
    private static async Task Send(LearningFixture f, DungeonMapCleared clear, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        await f.Services.GetRequiredService<IEventService>().EnqueueAsync(clear);
        if (commit) await tx.CommitAsync(default);
    }
    private static async Task Drain(LearningFixture f) =>
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("quest.map-clear");
    private static int Trigger(LearningFixture f) => f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=975");

    [Fact]
    public async Task Exact_map_is_required_and_repeated_delivery_does_not_repeat_progress_notifications()
    {
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(Content()));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,975,1)");
        var fact = Clear(DateTimeOffset.UtcNow);
        await Send(f, fact with { MapId = 11000 }); // Dungeon identity cannot stand in for map identity.
        await Drain(f);
        Assert.Equal(1, Trigger(f));
        await Send(f, fact);
        Assert.Equal(1, Trigger(f));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        await Send(f, fact);
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
        Assert.Equal(3, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='quest.map-clear' AND state=1"));
    }

    [Fact]
    public async Task Rolled_back_fact_never_advances_and_committed_fact_survives_service_restart()
    {
        var content = Content();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,975,1)");
        await Send(f, Clear(DateTimeOffset.UtcNow), commit: false);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='dungeon.map-cleared'"));
        await Drain(f);
        Assert.Equal(1, Trigger(f));
        await Send(f, Clear(DateTimeOffset.UtcNow));
        await using var restarted = new LearningFixture(existing: f.Path, configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await Drain(restarted);
        Assert.Equal(0, Trigger(f));
    }

    [Fact]
    public async Task Failed_consumption_rolls_back_trigger_and_notification_then_retries_once()
    {
        var clock = new AdvanceableTimeProvider(DateTimeOffset.UtcNow);
        await using var f = new LearningFixture(configure: s =>
        {
            s.AddSingleton<IQuestContentRuleService>(Content());
            s.AddSingleton<TimeProvider>(clock);
        });
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,975,1)");
        await Send(f, Clear(clock.GetUtcNow()));
        f.Sql("CREATE TRIGGER fail_map_quest BEFORE UPDATE ON character_active_quests BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Drain(f);
        Assert.Equal(1, Trigger(f));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='quest.map-clear'"));
        f.Sql("DROP TRIGGER fail_map_quest;");
        clock.Advance(TimeSpan.FromMinutes(1));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_event_deliveries WHERE subscriber_id='quest.map-clear' AND state=1"));
    }

    [Fact]
    public async Task Accepting_or_reaccepting_after_clear_does_not_inherit_the_queued_progress()
    {
        var clock = new AdvanceableTimeProvider(DateTimeOffset.UtcNow);
        await using var f = new LearningFixture(configure: s =>
        {
            s.AddSingleton<IQuestContentRuleService>(Content());
            s.AddSingleton<TimeProvider>(clock);
        });
        await f.SeedAsync();
        var log = f.Services.GetRequiredService<ICharacterQuestLogDomainService>().LogOf(new(1), new(71));
        await Send(f, Clear(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(1));
        await using (var tx = f.Scope.Begin())
        {
            Assert.True((await log.AcceptAsync(Content().Quest, default)).Accepted);
            await tx.CommitAsync(default);
        }
        await Drain(f);
        Assert.Equal(1, Trigger(f));
        clock.Advance(TimeSpan.FromSeconds(1));
        await Send(f, Clear(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(1));
        await using (var tx = f.Scope.Begin())
        {
            Assert.True(await log.GiveUpAsync(975, default));
            Assert.True((await log.AcceptAsync(Content().Quest, default)).Accepted);
            await tx.CommitAsync(default);
        }
        await Drain(f);
        Assert.Equal(1, Trigger(f));
    }

    [Theory]
    [InlineData(30030, true)]
    [InlineData(30029, false)]
    public async Task Progress_change_refreshes_the_online_clients_authoritative_quest_list(int map, bool completed)
    {
        using var f = new ShopFixture(s => s.AddSingleton<IQuestContentRuleService>(Content()));
        f.Sql("INSERT INTO character_active_quests VALUES(1,0,975,1)");
        f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(f.Session);
        await using (var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin())
        {
            await f.Services.GetRequiredService<IEventService>().EnqueueAsync(Clear(f.Clock.GetUtcNow(), map) with
            { Participants = [new(f.Session.AccountId, f.Session.CharacterId, 0, true)] });
            await tx.CommitAsync(default);
        }
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await dispatcher.DrainOnceAsync("quest.map-clear");
        await dispatcher.DrainOnceAsync("quest.progress-notification");
        var registrations = f.Services.GetServices<Dfo.Server.Abstractions.Networking.ServerMessageEncoderRegistration>();
        var frames = f.Sender.Messages.SelectMany(message =>
        {
            var registration = registrations.Single(x => x.MessageType == message.GetType());
            return ((Dfo.Server.Abstractions.Networking.IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(message);
        }).Where(frame => BitConverter.ToUInt16(frame, 1) == 0x023F).ToArray();
        if (completed)
        {
            var frame = Assert.Single(frames);
            Assert.Equal(0, frame[0]); // NOTI
            Assert.Equal(1u, BitConverter.ToUInt32(frame, 15));
            Assert.Equal(975, BitConverter.ToUInt16(frame, 19));
            Assert.Equal(0u, BitConverter.ToUInt32(frame, 21));
        }
        else
            Assert.Empty(frames);
    }

    [Theory]
    [InlineData("clear map", 30030, true)]
    [InlineData("[CLEAR MAP]", 30030, true)]
    [InlineData("condition under clear", 30030, false)]
    [InlineData("clear map", -1, false)]
    public void Map_rule_recognizes_declared_objective_and_positive_target(string type, int map, bool valid)
    {
        var rule = new QuestMapClearRuleService();
        var quest = Content().Quest with { ObjectiveTypeCode = type, ObjectiveArguments = [map] };
        Assert.Equal(valid ? map : (int?)null, rule.TargetMapOf(quest));
        Assert.Equal(valid ? map : (int?)null, rule.TargetMapOf(quest with { ObjectiveArguments = [map, 808] }));
        Assert.Null(rule.TargetMapOf(quest with { ObjectiveArguments = [] }));
        Assert.Null(rule.TargetMapOf(quest with { ObjectiveArguments = [map, 0, 0, 1] }));
    }
}
