using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class HerasDungeonTests
{
    [Fact]
    public async Task Default_activity_has_a_visible_identity_and_a_matching_scheduled_dungeon()
    {
        await using var f = new LearningFixture();
        await f.SeedAsync();
        var events = f.Services.GetRequiredService<IEventManagementDomainService>();
        Assert.Contains(await events.GetEventInfoListAsync(new(1), DateTimeOffset.UtcNow, default), x => x.EventId == 2367);
        var snapshot = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
        Assert.True(Assert.Single(snapshot.ScheduledDungeons, x => x.DungeonId == 3900).Open);
    }

    [SkippableFact]
    public async Task Actual_entry_charges_three_times_then_refuses_without_creating_a_fourth_run()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
            new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(s, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        });
        await f.SeedAsync();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(70, default);
                await tx.CommitAsync(default);
            }
        });
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        var commands = f.Services.GetRequiredService<ICommandBus>();
        var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
        var context = new DungeonEntryContext(3900, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
        for (var n = 0; n < 3; n++)
        {
            var result = await commands.SendAsync(new EnterDungeonCommand(context));
            Assert.True(result.Entry is not null, $"{result.Failure}: {result.Detail}");
            Assert.Equal(9, result.Entry.Layout.Cells.Count);
            Assert.False(result.Entry.Layout.ConsumesFatigue);
            Assert.Equal((byte)70, result.Entry.Layout.FixedMonsterLevel);
            Assert.Equal((DungeonDifficulty)1, result.Entry.Change!.Run.Difficulty);
            Assert.All(result.Entry.Visits.Values.SelectMany(v => v.Actors), a => Assert.Equal((byte)70, a.Actor.Level));
            TestGameplay.Run(f.Services, () =>
            {
                runs.End(new(71));
            });
        }
        Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await commands.SendAsync(new EnterDungeonCommand(context))).Failure);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(runs.Find(new Dfo.Server.Data.Characters.CharacterId(71)));
        });
    }

    private static DungeonEntryContext Entry() => new(3900, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
    private static async Task<LearningFixture> Fixture(byte level = 70)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
            new Dfo.Server.Protocol.ProtocolModule().ConfigureServices(s, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        });
        await f.SeedAsync();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(level, default);
            await tx.CommitAsync(default);
        });
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        return f;
    }
    [SkippableFact]
    public async Task Refused_modes_and_low_level_do_not_charge_and_zero_fatigue_is_allowed()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture(49);
        var commands = f.Services.GetRequiredService<ICommandBus>();
        var heras = f.Services.GetRequiredService<IHerasDungeonDomainService>();
        Assert.Equal(DungeonEntryFailure.LevelTooLow, (await commands.SendAsync(new EnterDungeonCommand(Entry()))).Failure);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(50, default);
                var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
                await fatigue.SpendAsync(new(71), (await fatigue.BalanceOfAsync(new(71), default)).Remaining, default);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await commands.SendAsync(new EnterDungeonCommand(Entry() with { PracticeMode = 1 }))).Failure);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await commands.SendAsync(new EnterDungeonCommand(Entry() with { Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)] }))).Failure);
        Assert.Equal(3, (await heras.ReadQuotaAsync(new(71), default)).Remaining);
        Assert.NotNull((await commands.SendAsync(new EnterDungeonCommand(Entry()))).Entry);
        Assert.Equal(2, (await heras.ReadQuotaAsync(new(71), default)).Remaining);
        Assert.Equal(3, (await heras.ReadQuotaAsync(new(72), default)).Remaining);
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Remaining);
    }
    [SkippableFact]
    public async Task Failed_room_creation_rolls_back_the_charged_quota()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await using (var tx = f.Scope.Begin())
            {
                var result = await f.Services.GetRequiredService<IDungeonAdmissionDomainService>().ExecuteAsync(Entry() with { Facts = DungeonEntryFacts.None with { Difficulty = (DungeonDifficulty)1 } },
                    new() { RequireMinimumLevel = true, ConsumesFatigue = false, RequiredItemsOverride = [],
                        Quota = new(Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.HerasDungeon,
                            (await f.Services.GetRequiredService<IHerasDungeonDomainService>().ReadQuotaAsync(new(71), default)).Period, 3, 3900) },
                    (_, _) => ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid)), default);
                Assert.Equal(DungeonEntryFailure.ConfigurationInvalid, result.Failure);
            }
            Assert.Equal(3, (await f.Services.GetRequiredService<IHerasDungeonDomainService>().ReadQuotaAsync(new(71), default)).Remaining);
            Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new Dfo.Server.Data.Characters.CharacterId(71)));
        });
    }
    [SkippableFact]
    public async Task Disabled_activity_hides_the_schedule_and_refuses_new_runs_without_charging()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = await Fixture();
        var events = f.Services.GetRequiredService<IEventManagementDomainService>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await events.ChangeAsync(new(2367, 0, "disable", null, null, null, null, null, null, null), "test", DateTimeOffset.UtcNow, default);
                await tx.CommitAsync(default);
            }
        });
        var snapshot = await f.Services.GetRequiredService<IEventPlayerProjectionDomainService>().ReadNowAsync(f.Session, default);
        Assert.False(Assert.Single(snapshot.ScheduledDungeons, x => x.DungeonId == 3900).Open);
        Assert.DoesNotContain(snapshot.Information, x => x.EventId == 2367);
        Assert.Equal(DungeonEntryFailure.OutsideOpeningHours, (await f.Services.GetRequiredService<ICommandBus>().SendAsync(new EnterDungeonCommand(Entry()))).Failure);
        Assert.Equal(3, (await f.Services.GetRequiredService<IHerasDungeonDomainService>().ReadQuotaAsync(new(71), default)).Remaining);
    }
}
