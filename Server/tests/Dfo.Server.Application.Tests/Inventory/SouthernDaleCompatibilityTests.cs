using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Events;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SouthernDaleCompatibilityTests
{
    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_daily_exchange_is_available_in_the_quest_catalog()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        var quest = await fixture.Host.Services.GetRequiredService<IQuestContentRuleService>().FindAsync(20549, default);
        Assert.NotNull(quest);
        Assert.Equal(QuestGrade.SpecialDaily, quest.Grade);
        Assert.Equal(new SeekingItemRequirement(2680739, 2), Assert.Single(quest.SeekingItems!));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_dungeon_exempts_failure_weakness()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        var layout = await fixture.Host.Services.GetRequiredService<IDungeonStructureRuleService>()
            .ResolveAsync(100, DungeonEntryFacts.None, 17, default);
        Assert.NotNull(layout);
        Assert.False(layout.AppliesFailureWeakness);
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_coin_pot_pays_wallet_gold_and_consumes_one_pot()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        await fixture.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        await fixture.Host.Seed(65, 7615, 2);
        await fixture.Host.Seed(177, 2680739, 2);
        var before = fixture.Host.Gold;
        var result = await fixture.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new LotteryOpeningCommand(fixture.Host.OnlineUser, fixture.Host.Session, 65, 0));
        var reward = Assert.Single(result.Receipt.Rewards);
        Assert.Equal(0, reward.ItemId);
        Assert.InRange(reward.Count, 1, 100000);
        Assert.Equal(before + reward.Count, fixture.Host.Gold);
        Assert.Equal(fixture.Host.Gold, result.GoldAfter);
        Assert.Equal(1, fixture.Host.Count(7615));
        Assert.Equal(0, fixture.Host.Count(2680739));
        Assert.NotEmpty(fixture.Encode(new LotteryOpeningResponse(result)));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_daily_completion_is_once_per_game_day_and_closed_activity_blocks_new_acceptance()
    {
        var clock = new MutableClock();
        using var fixture = new CeraShopFixture(useArchive: true, configure: services => services.AddSingleton<TimeProvider>(clock));
        var commands = fixture.Host.Services.GetRequiredService<ICommandBus>();
        var session = fixture.Host.Session;
        await fixture.Host.Seed(177, 2680739, 6);
        Assert.True((await commands.SendAsync(new AcceptQuestCommand(session.AccountId, session.CharacterId, 20549))).Accepted);
        Assert.True((await commands.SendAsync(new FinishQuestCommand(session.AccountId, session.CharacterId, 20549, ushort.MaxValue, 1,
            checked((uint)clock.Now.ToUnixTimeSeconds())))).Finished);
        Assert.False((await commands.SendAsync(new AcceptQuestCommand(session.AccountId, session.CharacterId, 20549))).Accepted);
        clock.Now = clock.Now.AddMinutes(1);
        var availability = fixture.Host.Services.GetRequiredService<ISouthernDaleQuestDomainService>();
        Assert.True(await availability.CanAcceptAsync(session.CharacterId, default));
        var activities = fixture.Host.Services.GetRequiredService<IEventManagementDomainService>();
        var current = Assert.Single((await activities.ListAsync(clock.Now, default)).Entries, entry => entry.EventId == 2330);
        await TestGameplay.RunAsync(fixture.Host.Services, async () =>
        {
            await using (var tx = fixture.Host.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin())
            {
                await activities.ChangeAsync(new EventChange(2330, current.Revision, "disable"), "test", clock.Now, default);
                await tx.CommitAsync(default);
            }
        });
        Assert.False(await availability.CanAcceptAsync(session.CharacterId, default));
        Assert.False((await commands.SendAsync(new AcceptQuestCommand(session.AccountId, session.CharacterId, 20549))).Accepted);
        Assert.Equal(4, fixture.Host.Count(2680739));
        Assert.Equal(1, fixture.Host.Count(7615));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_coin_pot_full_wallet_preserves_the_pot()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        await fixture.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        await fixture.Host.Seed(65, 7615, 1);
        await fixture.Host.Seed(177, 2680739, 2);
        fixture.Host.Sql("UPDATE character_stats SET carried_gold=2147483647;");
        var refused = await Assert.ThrowsAsync<Dfo.Server.Data.Inventory.OpeningRejectedException>(async () =>
            await fixture.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
                new LotteryOpeningCommand(fixture.Host.OnlineUser, fixture.Host.Session, 65, 0)));
        Assert.Equal(Dfo.Server.Data.Inventory.OpeningRejection.NoRoom, refused.Reason);
        Assert.Equal(int.MaxValue, fixture.Host.Gold);
        Assert.Equal(1, fixture.Host.Count(7615));
        Assert.Equal(2, fixture.Host.Count(2680739));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_coin_pot_without_two_keys_preserves_all_inputs()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        await fixture.Host.Services.GetRequiredService<ILotteryDefinitionCache>().InitializeAsync();
        await fixture.Host.Seed(65, 7615, 1);
        await fixture.Host.Seed(177, 2680739, 1);
        var refused = await Assert.ThrowsAsync<Dfo.Server.Data.Inventory.OpeningRejectedException>(async () =>
            await fixture.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
                new LotteryOpeningCommand(fixture.Host.OnlineUser, fixture.Host.Session, 65, 0)));
        Assert.Equal(Dfo.Server.Data.Inventory.OpeningRejection.MissingMaterial, refused.Reason);
        Assert.Equal(1, fixture.Host.Count(7615));
        Assert.Equal(1, fixture.Host.Count(2680739));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_magic_pot_uses_its_declared_holy_water_recipe()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        await fixture.Host.Seed(65, 2681819, 1);
        await fixture.Host.Seed(66, 2681820, 1);
        var result = await OpeningDomainTestDriver.Box(fixture.Host, fixture.Host.Session, new(0, 65, 2681819, 66, 2681820, 1));
        Assert.NotNull(result.Receipt);
        Assert.NotEmpty(result.Primary);
        Assert.Equal(0, fixture.Host.Count(2681819));
        Assert.Equal(0, fixture.Host.Count(2681820));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_target_refill_counts_overflow_mail_across_game_days()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new CeraShopFixture(useArchive: true);
            var at = new DateTimeOffset(2026, 10, 19, 6, 0, 0, TimeSpan.FromHours(8));
            var rule = Assert.Single(await fixture.Host.Services.GetRequiredService<IDailyRefillConfigurationDomainService>()
                .EffectiveAsync(at, default), x => x.ItemId == 2680738);
            Assert.Equal(DailyRefillMode.RefillToTarget, rule.Mode);
            Assert.Equal(2, rule.Quantity);
            // Constrain the available band to one occupied quest cell to exercise real overflow delivery.
            rule = rule with { Destination = new(Dfo.Server.Data.Inventory.BusinessInventorySpace.Quest, 0, 0) };
            await fixture.Host.Seed(177, 2680739, 1);
            for (var day = 0; day < 3; day++)
            {
                await using var tx = fixture.Host.Services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin();
                await fixture.Host.Services.GetRequiredService<IDailyRefillDomainService>()
                    .ApplyIndependentRefillAsync(new(1), new(1), [rule], at.AddDays(day), default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, fixture.Host.Count(2680738));
            Assert.Equal(2, await fixture.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.IItemDeliveryDomainService>()
                .PendingQuantityAsync(new(1), 2680738, default));
            Assert.Single(await fixture.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Mail.IMailboxDomainService>().InboxAsync(new(1)));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_offer_stays_consistent_in_current_view_and_replay_after_activity_changes()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        var services = fixture.Host.Services;
        TestGameplay.Run(services, () =>
        {
            services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
                .Begin(fixture.Host.Session, new(1, 0, 0, 0, 0), 100);
        });
        fixture.Host.Sql("""
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0),(2,0);
            INSERT INTO character_pvp(character_id) VALUES(1),(2);
            INSERT INTO character_settings(character_id) VALUES(1),(2);
            INSERT INTO character_expert_job(character_id) VALUES(1),(2);
            """);
        var scope = services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>();
        await TestGameplay.RunAsync(services, async () =>
        {
            await using (var tx = scope.Begin())
            {
                await services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterDomainService>()
                    .Get(new(1)).SetLevelAsync(27, default);
                await tx.CommitAsync(default);
            }
        });
        foreach (var open in new[] { true, false })
        {
            await TestGameplay.RunAsync(services, async () =>
            {
                if (!open)
                {
                    await using var tx = scope.Begin();
                    await services.GetRequiredService<IEventManagementDomainService>()
                        .ChangeAsync(new(2330, 0, "disable"), "test", fixture.Host.Clock.GetUtcNow(), default);
                    await tx.CommitAsync(default);
                }
            });
            await TestGameplay.RunAsync(services, async () =>
            {
                var current = await services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterCurrentStateQueryDomainService>()
                    .ReadAsync(fixture.Host.Session, default);
                Assert.Equal(open, current.World.Subject!.SouthernDaleQuestAvailable);
            });
            var replay = await services.GetRequiredService<ICommandBus>().SendAsync(new Dfo.Server.Application.Effects.ReadReplayStateCommand(
                fixture.Host.Session, new(Dfo.Server.Abstractions.Effects.ReplayProjectionNeeds.Quests, []), null));
            Assert.Equal(open, replay.Quests!.Offers.Any(x => x.QuestId == 20549));
        }
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_activity_changes_push_the_current_offer_list_to_the_online_client()
    {
        using var fixture = new CeraShopFixture(useArchive: true);
        var services = fixture.Host.Services;
        TestGameplay.Run(services, () =>
        {
            services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(fixture.Host.Session);
        });
        var events = services.GetRequiredService<IEventManagementDomainService>();
        var revision = 0;
        foreach (var open in new[] { false, true, false })
        {
            fixture.Host.Sender.Messages.Clear();
            await TestGameplay.RunAsync(services, async () =>
            {
                await using (var tx = services.GetRequiredService<Dfo.Server.Application.Transactions.DomainTransactionScope>().Begin())
                {
                    await events.ChangeAsync(new(2330, revision, open ? "enable" : "disable"), "test", fixture.Host.Clock.GetUtcNow(), default);
                    await services.GetRequiredService<Dfo.Server.Abstractions.DomainEvents.IEventService>()
                        .EnqueueAsync(new ManagedEventConfigurationChanged(2330, ++revision));
                    await tx.CommitAsync(default);
                }
            });
            await services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync("activity.refresh");
            Assert.Equal(open, OffersSouthern(fixture));
            var state = Assert.Single(fixture.Host.Sender.Messages.Select(fixture.Encode), x =>
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x033F
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(x.AsSpan(15)) == 1);
            Assert.Equal(open ? 1u : 0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(19)));
        }
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Southern_online_day_reset_pushes_the_next_daily_offer_even_with_two_tickets_held()
    {
        var clock = new MutableClock();
        using var fixture = new CeraShopFixture(useArchive: true, configure: s => s.AddSingleton<TimeProvider>(clock));
        var services = fixture.Host.Services;
        var session = fixture.Host.Session;
        services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(session);
        await fixture.Host.Seed(177, 2680739, 2);
        var commands = services.GetRequiredService<ICommandBus>();
        Assert.True((await commands.SendAsync(new AcceptQuestCommand(session.AccountId, session.CharacterId, 20549))).Accepted);
        Assert.True((await commands.SendAsync(new FinishQuestCommand(session.AccountId, session.CharacterId, 20549, ushort.MaxValue, 1,
            checked((uint)clock.Now.ToUnixTimeSeconds())))).Finished);
        foreach (var nextDay in new[] { false, true })
        {
            if (nextDay) clock.Now = clock.Now.AddMinutes(1);
            fixture.Host.Sender.Messages.Clear();
            Assert.True(await commands.SendAsync(new Dfo.Server.Application.World.RefillOnlineSouthernTicketCommand(session)));
            await services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync();
            Assert.Equal(nextDay, OffersSouthern(fixture));
            Assert.Equal(2, fixture.Host.Count(2680738));
        }
    }

    private static bool OffersSouthern(CeraShopFixture fixture)
    {
        var frame = Assert.Single(fixture.Host.Sender.Messages.Select(fixture.Encode), x =>
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1)) == 0x0015);
        using var reader = new BinaryReader(new MemoryStream(frame[15..]));
        _ = reader.ReadByte();
        var count = reader.ReadUInt16();
        var ids = Enumerable.Range(0, count).Select(_ => reader.ReadUInt16()).ToArray();
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return ids.Contains((ushort)20549);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
