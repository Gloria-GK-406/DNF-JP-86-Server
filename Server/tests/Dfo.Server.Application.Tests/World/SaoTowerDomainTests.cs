using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class SaoTowerDomainTests
{
    private static readonly SelectedSessionIdentity[] Players =
    [
        SelectedSessionIdentity.Create(new(1), new(71), new("sao-one"), 1, 10011),
        SelectedSessionIdentity.Create(new(2), new(72), new("sao-two"), 1, 10011),
    ];

    private sealed class Source : ISaoTowerContentSource
    {
        public SaoTowerCatalog Load() => new(Enumerable.Range(1, 100)
            .Select(x => new SaoTowerFloor(x, 3599 + x, 39, [])).ToArray(), 10004331);
    }
    private sealed class Delivery : IItemDeliveryDomainService
    {
        public List<CharacterId> Recipients { get; } = [];
        public CharacterId? Fail { get; set; }
        public ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) => ValueTask.FromResult(0L);
        public ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character,
            IReadOnlyList<ItemDeliveryGrant> grants, string source, CancellationToken ct, ItemDeliveryMode mode, ItemDeliveryOptions? options = null)
        {
            Assert.Equal(ItemDeliveryMode.InventoryThenMail, mode);
            Assert.Equal(10004331, Assert.Single(grants).ItemId);
            Assert.Equal(1, grants[0].Count);
            if (character == Fail) throw new InvalidOperationException("injected award failure");
            Recipients.Add(character);
            return ValueTask.FromResult(new ItemDeliveryReceipt([], [], 0));
        }
    }
    private sealed class Activities(bool enabled = true) : IEventManagementDomainService
    {
        public ValueTask<ManagedEventList> ListAsync(DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult(new ManagedEventList(
            [new(214, "sao", 1, "season", enabled, enabled ? "active" : "disabled", "SAO", "SAO", "", "", "", now.AddDays(-1), now.AddDays(1), true, null, null, null)]));
        public ValueTask<bool> IsOpenAsync(int eventId, DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult(eventId == 214 && enabled);
        public ValueTask<EventInfo[]> GetEventInfoListAsync(AccountId account, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ManagedEventList> ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
    private static LearningFixture Fixture(DungeonRunDomainService runs, Delivery delivery, bool enabled = true) => new(configure: s =>
    {
        s.AddSingleton<ISaoTowerContentSource>(new Source());
        s.AddSingleton<IDungeonRunDomainService>(runs);
        s.AddSingleton<IItemDeliveryDomainService>(delivery);
        s.AddSingleton<IEventManagementDomainService>(new Activities(enabled));
    });
    private static DungeonActorDeathClaim Claim(DungeonRunDomainService runs, SaoTowerProgress? progress = null)
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(3600, 0, 1, 1, cell, cell, 27000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = new(DungeonPathDirection.None, [27000], [27000]) })
            { Mode = new DungeonRunMode.Sao(new(progress ?? new("season"))), ConsumesFatigue = false };
        var actor = new DungeonCellActorState(1, new(64811, 86, 0), false, false, []);
        var roster = Players.Select((x, i) => new DungeonRunParticipant(x.AccountId, x.CharacterId, (byte)i, i == 0)).ToArray();
        TestParties.EnsureEntryParty(runs, roster);
        runs.PreparePartyEntry(roster,
            layout, DungeonDifficulty.Normal, new(cell, 27000, 1, [actor], []), DungeonRunId.New(), DateTimeOffset.UtcNow);
        return runs.TryClaimActorDeath(new(71), 1)!;
    }

    [Fact]
    public async Task First_party_awards_every_member_once_and_an_old_floor_gets_no_extra_reward()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs(); var delivery = new Delivery();
            await using var f = Fixture(runs, delivery);
            var sao = f.Services.GetRequiredService<ISaoTowerDomainService>();
            var claim = Claim(runs);
            await using (var tx = f.Scope.Begin())
            {
                await sao.CompleteClearAsync(claim, Players, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(Players.Select(x => x.CharacterId), delivery.Recipients);
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
            Assert.Equal(2, (await sao.ReadStatusAsync(default))!.NextFloor);
            await using (var tx = f.Scope.Begin())
            {
                await sao.CompleteClearAsync(claim, Players, default);
                await tx.CommitAsync(default);
            }
            runs.ReleaseActorDeath(claim);
            TestParties.Leave(runs, Players[0].CharacterId);
            foreach (var player in Players) runs.End(player.CharacterId, claim.RunId);
            // The last member's party dissolves too, so the late entry forms its own party.
            TestParties.Leave(runs, Players[1].CharacterId);
            var late = Claim(runs); // another party's earlier floor snapshot
            await using (var tx = f.Scope.Begin())
            {
                await sao.CompleteClearAsync(late, Players, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, delivery.Recipients.Count);
            Assert.Equal(2, (await sao.ReadStatusAsync(default))!.NextFloor);
        });
    }

    [Fact]
    public async Task Second_member_failure_rolls_back_the_shared_clear_and_all_receipts()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs(); var delivery = new Delivery { Fail = new(72) };
            await using var f = Fixture(runs, delivery);
            var sao = f.Services.GetRequiredService<ISaoTowerDomainService>();
            var claim = Claim(runs);
            await using (var tx = f.Scope.Begin())
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await sao.CompleteClearAsync(claim, Players, default));
            Assert.Equal(1, (await sao.ReadStatusAsync(default))!.NextFloor);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM sao_first_clears"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
            delivery.Fail = null;
            await using (var tx = f.Scope.Begin())
            {
                await sao.CompleteClearAsync(claim, Players, default);
                await tx.CommitAsync(default);
            }
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
        });
    }

    [Fact]
    public async Task Released_claim_or_changed_participants_cannot_complete_and_disabled_activity_cannot_enter()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs(); var delivery = new Delivery();
            await using var f = Fixture(runs, delivery, enabled: false);
            var sao = f.Services.GetRequiredService<ISaoTowerDomainService>();
            Assert.Null(await sao.ReadStatusAsync(default));
            await using var tx = f.Scope.Begin();
            var entry = await sao.EnterAsync(new(3699, DungeonEntryFacts.None, 0, 0,
                [new(new(1), new(71), 0, true)]), default);
            Assert.Null(entry.Entry);
            var claim = Claim(runs);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await sao.CompleteClearAsync(claim, [Players[0]], default));
            runs.ReleaseActorDeath(claim);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await sao.CompleteClearAsync(claim, Players, default));
            Assert.Empty(delivery.Recipients);
        });
    }

    [SkippableFact]
    public async Task Real_entry_resolves_shared_floor_charges_one_ticket_and_rollback_preserves_inventory()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = new LearningFixture(useArchive: true, configure: s =>
            {
                s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path });
                s.AddSingleton<IEventManagementDomainService>(new Activities());
            });
            await f.SeedAsync();
            var containers = f.Services.GetRequiredService<ICharacterContainerDomainService>();
            containers.Register(new(71), new Dfo.Server.Application.Inventory.PersistentItemContainer(
                ContainerId.ForCharacter(new(71), Dfo.Server.Data.Inventory.InventoryContainerType.Main),
                new(Dfo.Server.Data.Inventory.InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
            f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
            f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
            f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
            f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
            var store = f.Services.GetRequiredService<ISaoTowerDomainStore>();
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 10004322, 4, default);
                Assert.True(await store.TryClearAsync(new("season"), Guid.NewGuid(), 100, default));
                await tx.CommitAsync(default);
            }
            var sao = f.Services.GetRequiredService<ISaoTowerDomainService>();
            var context = new DungeonEntryContext(3699, DungeonEntryFacts.None, 0, 0, [new(new(1), new(71), 0, true)]);
            await using (var tx = f.Scope.Begin())
            {
                var entered = await sao.EnterAsync(context, default);
                Assert.True(entered.Entry is not null, $"Refused {entered.Failure}: {entered.Detail}");
                Assert.Equal(3601, entered.Entry.Layout.DungeonId);
                Assert.Equal(2, Assert.IsType<DungeonRunMode.Sao>(entered.Entry.Layout.Mode).Plan.Progress.NextFloor);
                // Deliberate transaction rollback: no run publication and no ticket charge.
            }
            Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(new CharacterId(71)));
            var held = await containers.GetContainer(new(71), Dfo.Server.Data.Inventory.InventoryContainerType.Main).GetAllAsync(default);
            Assert.Equal(4, held.Where(x => x.ItemId == 10004322).Sum(x => x.Count));
            await using (var tx = f.Scope.Begin())
            {
                var entered = await sao.EnterAsync(context, default);
                Assert.NotNull(entered.Entry);
                await tx.CommitAsync(default);
            }
            held = await containers.GetContainer(new(71), Dfo.Server.Data.Inventory.InventoryContainerType.Main).GetAllAsync(default);
            Assert.Equal(3, held.Where(x => x.ItemId == 10004322).Sum(x => x.Count));
            Assert.Equal(0, (await f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService>()
                .BalanceOfAsync(new(71), default)).Used);
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = runs.Find(new CharacterId(71))!;
            DungeonActorDeathClaim? last = null;
            foreach (var actor in run.CurrentCellState!.Actors.Where(x => x.BlocksClear))
            {
                var death = runs.TryClaimActorDeath(new(71), actor.Sequence);
                if (death is null) continue;
                if (runs.WillClearMap(death)) { last = death; break; }
                Assert.NotNull(runs.CommitActorDeathChange(death));
            }
            Assert.NotNull(last);
            await using (var tx = f.Scope.Begin())
            {
                await sao.CompleteClearAsync(last, [f.Session], default);
                await tx.CommitAsync(default);
            }
            held = await containers.GetContainer(new(71), Dfo.Server.Data.Inventory.InventoryContainerType.Main).GetAllAsync(default);
            Assert.Equal(1, held.Where(x => x.ItemId == 10004331).Sum(x => x.Count));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM sao_first_clear_rewards"));
            Assert.Equal(3, (await sao.ReadStatusAsync(default))!.NextFloor);
        });
    }
}
