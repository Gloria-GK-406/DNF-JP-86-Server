using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class TauKingdomTests
{
    [SkippableFact]
    public async Task Real_entry_scales_actors_charges_once_and_failed_or_rolled_back_entry_keeps_ticket()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s =>
            s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await f.SeedAsync();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        containers.Register(new(71), new PersistentItemContainer(ContainerId.ForCharacter(new(71), InventoryContainerType.Main),
            new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        var tau = f.Services.GetRequiredService<ITauKingdomDomainService>();
        var context = new DungeonEntryContext(3700, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(26, default);
            Assert.Equal(DungeonEntryFailure.LevelTooLow, (await tau.EnterAsync(context, default)).Failure);
            await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(70, default);
            Assert.Equal(DungeonEntryFailure.MissingRequiredItem, (await tau.EnterAsync(context, default)).Failure);
            await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 690001556, 2, default);
            await tx.CommitAsync(default);
        }
        await using (var tx = f.Scope.Begin())
        {
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await tau.EnterAsync(context with
                { Participants = [.. context.Participants, new(new(2), new(72), 1, false)] }, default)).Failure);
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await tau.EnterAsync(context with { MirrorQuestId = 1 }, default)).Failure);
            var entry = await tau.EnterAsync(context, default);
            Assert.True(entry.Entry is not null, $"{entry.Failure}: {entry.Detail}");
            Assert.Equal((byte)70, entry.Entry.Layout.FixedMonsterLevel);
            Assert.Equal(7, entry.Entry.Layout.ExperienceWeight);
            Assert.Equal(3, entry.Entry.Layout.Cells.Count);
            Assert.False(entry.Entry.Layout.ConsumesFatigue);
            Assert.Equal(50, entry.Entry.Visits[new(71)].Actors.Count);
            // No commit: both runtime publication and material debit must roll back.
        }
        Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new Dfo.Server.Data.Characters.CharacterId(71)));
        var held = await containers.GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default);
        Assert.Equal(2, held.Where(x => x.ItemId == 690001556).Sum(x => x.Count));
        await using (var tx = f.Scope.Begin())
        {
            Assert.NotNull((await tau.EnterAsync(context, default)).Entry);
            await tx.CommitAsync(default);
        }
        held = await containers.GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default);
        Assert.Equal(1, held.Where(x => x.ItemId == 690001556).Sum(x => x.Count));
        Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
    }

    [Fact]
    public async Task Default_activity_is_open_and_an_explicit_disable_is_preserved()
    {
        await using var f = new LearningFixture();
        var events = f.Services.GetRequiredService<IEventManagementDomainService>();
        var at = DateTimeOffset.UtcNow;
        var enabled = await events.GetEventInfoListAsync(new(1), at, default);
        Assert.Contains(enabled, x => x.EventId == 2215 && x.LinkKey == "taukingdom/0/0");
        var companion = Assert.Single(enabled, x => x.EventId == 69);
        Assert.False(companion.HasDetail);
        Assert.True(Assert.Single(enabled, x => x.EventId == 2215).HasDetail);
        Assert.Equal("", companion.Title);
        Assert.Equal("", companion.StartNotice);
        Assert.Equal("", companion.EndNotice);
        Assert.DoesNotContain((await events.ListAsync(at, default)).Entries, x => x.EventId == 69);
        Assert.Empty(await events.GetEventInfoListAsync(new(0), at, default));
        await using (var tx = f.Scope.Begin())
        {
            await events.ChangeAsync(new(2215, 0, "disable"), "test", at, default);
            await tx.CommitAsync(default);
        }
        Assert.DoesNotContain(await events.GetEventInfoListAsync(new(1), at, default), x => x.EventId is 2215 or 69);
        await using (var tx = f.Scope.Begin())
        {
            await events.ChangeAsync(new(2215, 1, "enable"), "test", at, default);
            await tx.CommitAsync(default);
        }
        Assert.Contains(await events.GetEventInfoListAsync(new(1), at, default), x => x.EventId == 69);
    }

    [Theory]
    [InlineData(-2, -1)]
    [InlineData(1, 2)]
    public async Task Inactive_tau_period_excludes_primary_and_companion(int startOffset, int endOffset)
    {
        await using var f = new LearningFixture();
        var events = f.Services.GetRequiredService<IEventManagementDomainService>();
        var at = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        await using (var tx = f.Scope.Begin())
        {
            await events.ChangeAsync(new(2215, 0, "configure", "牛头怪乐园", "牛头怪乐园", "", "", "",
                at.AddDays(startOffset), at.AddDays(endOffset)), "test", at, default);
            await tx.CommitAsync(default);
        }
        Assert.DoesNotContain(await events.GetEventInfoListAsync(new(1), at, default), x => x.EventId is 2215 or 69);
    }
    [SkippableFact]
    public void Real_boss_drops_three_books_through_existing_table_and_respects_level_ceiling()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var content = ContentArchive.CreateContentProvider();
        var drops = new IndependentDropRuleService(content.GetRequiredService<IIndependentDropTableSource>(),
            () => throw new InvalidOperationException(), NullLogger<IndependentDropRuleService>.Instance);
        var request = new MonsterDropRequest(62040, new(DungeonRunId.New(), 3700, 123,
            new(2, 0), 26009, DungeonDifficulty.Normal, 101, 70, 3, new CharacterId(71), [new(new(1), new(71))]));
        Assert.Equal(3, drops.Draw(request, 70).Where(x => x.ItemId == 690001555).Sum(x => x.Count));
        Assert.Equal(3, drops.Draw(request, 85).Where(x => x.ItemId == 690001555).Sum(x => x.Count));
        Assert.DoesNotContain(drops.Draw(request, 86), x => x.ItemId == 690001555);
    }

    [SkippableFact]
    public async Task Online_refill_uses_login_ledger_and_skips_displaced_session()
    {
        var clock = new TauClock();
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true, configure: s =>
            {
                s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
                s.AddSingleton<TimeProvider>(clock);
            });
        await f.SeedAsync();
        var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
        containers.Register(new(71), new PersistentItemContainer(ContainerId.ForCharacter(new(71), InventoryContainerType.Main),
            new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
            f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        var tau = f.Services.GetRequiredService<ITauKingdomDomainService>();
        var rule = (await f.Services.GetRequiredService<IDailyRefillConfigurationDomainService>()
            .EffectiveAsync(clock.Now, default)).Single(x => x.ItemId == 690001556);
        await using (var tx = f.Scope.Begin())
        {
            // Login and online callback share the same durable per-item claim.
            await f.Services.GetRequiredService<IDailyRefillDomainService>().ApplyDailyRefillAsync(
                f.Session.AccountId, f.Session.CharacterId, [rule], clock.Now, default);
            Assert.True(await tau.RefillOnlineTicketAsync(f.Session, default));
            await tx.CommitAsync(default);
        }
        var items = await containers.GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default);
        Assert.Equal(1, items.Where(x => x.ItemId == 690001556).Sum(x => x.Count));
        f.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().RemoveExact(f.Session);
        await using (var tx = f.Scope.Begin())
            Assert.False(await tau.RefillOnlineTicketAsync(f.Session, default));
        // At 06:00 the scheduled callback sees an unpublished selection; publication then rechecks.
        clock.Now = clock.Now.AddMinutes(1);
        await using (var tx = f.Scope.Begin())
            Assert.False(await tau.RefillOnlineTicketAsync(f.Session, default));
        f.Services.GetRequiredService<ISelectedSessionRuntimeDirectory>().Publish(f.Session, false);
        for (var repeat = 0; repeat < 2; repeat++)
            Assert.True(await f.Services.GetRequiredService<ICommandBus>().SendAsync(new RefillOnlineTauTicketCommand(f.Session)));
        items = await containers.GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default);
        Assert.Equal(2, items.Where(x => x.ItemId == 690001556).Sum(x => x.Count));
        // Books created by the same arrival path carry the next game-day deadline.
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 690001555, 1, default);
            await tx.CommitAsync(default);
        }
        items = await containers.GetContainer(new(71), InventoryContainerType.Main).GetAllAsync(default);
        var book = Assert.Single(items, x => x.ItemId == 690001555);
        var boundary = f.Services.GetRequiredService<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService>()
            .PeriodStart(clock.Now).AddDays(1);
        Assert.Equal(boundary.ToUnixTimeSeconds(), book.Snapshot.ExpireTime);
    }

    private sealed class TauClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

}
