using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonRevivalTests
{
    private static async Task<int> Coins(ShopFixture shop) =>
        (await shop.Services.GetRequiredService<ICharacterContainerDomainService>()
            .FindContainer(shop.Session.CharacterId, InventoryContainerType.Main)!
            .GetAsync(1, default))?.Snapshot.ToCore().Value ?? 0;

    private static DungeonRunId EnterDead(ShopFixture shop, bool tournament = false)
    {
        return TestGameplay.Run(shop.Services, () =>
        {
            var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
            var layout = new DungeonLayout(144, 0, 1, 1, new(0, 0), new(0, 0), 61000, 1,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000)});
            if (tournament) layout = layout with
            {
                Tournament = new(new TournamentDefinition(120, 17100, 69, 72, 2, 2, [], [], [], [], [], 16)
                    { CoinLimit = 1 }, [], [], 1),
            };
            var run = shop.InTransaction(() => runs.PreparePartyEntry([new(shop.Session.AccountId, shop.Session.CharacterId, 0, true)],
                layout, DungeonDifficulty.Normal)).Run;
            runs.TryMarkParticipantReady(shop.Session.CharacterId, run.RunId, run.RoomVersion);
            runs.TryMarkParticipantDead(shop.Session.CharacterId, run.RunId, run.RoomVersion);
            runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
            return run.RunId;
        });
    }

    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public async Task Revival_commits_one_coin_and_cancels_wipe_duplicate_is_refused(int coins)
    {
        using var shop = new ShopFixture();
        await shop.Seed(1, 1, coins, 0);
        var run = EnterDead(shop);
        var command = new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        Assert.True((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(coins - 1, (await Coins(shop)));
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Empty(shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        });
        Assert.False((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(coins - 1, (await Coins(shop)));
    }

    [Fact]
    public async Task Teammate_revival_charges_payer_and_second_death_gets_fresh_deadline()
    {
        using var shop = new ShopFixture(services => services.AddSingleton<IDungeonRunDomainService>(provider =>
            new Dfo.Server.Application.World.DungeonRunDomainService(
                provider.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>())));
        await shop.Seed(1, 1, 10, 0);
        var runs = shop.Services.GetRequiredService<IDungeonRunDomainService>();
        var layout = new DungeonLayout(144, 0, 1, 1, new(0, 0), new(0, 0), 61000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000)});
        var target = new Dfo.Server.Data.Characters.CharacterId(2);
        var run = TestGameplay.Run(shop.Services, () =>
        {
            var run = shop.InTransaction(() => runs.PreparePartyEntry([
                new(shop.Session.AccountId, shop.Session.CharacterId, 0, true),
                new(new(2), target, 1, false)], layout, DungeonDifficulty.Normal)).Run;
            foreach (var id in new[] { shop.Session.CharacterId, target })
            {
                runs.TryMarkParticipantReady(id, run.RunId, run.RoomVersion);
                runs.TryMarkParticipantDead(id, run.RunId, run.RoomVersion);
            }
            runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
            return run;
        });
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, target, run.RunId));
        Assert.True(result.Succeeded);
        Assert.Equal(9, await Coins(shop));
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
            runs.TryMarkParticipantDead(target, run.RunId, run.RoomVersion);
            Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(12)));
            Assert.Empty(runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(21)));
            Assert.Equal([run.RunId], runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(22)));
        });
    }

    [Fact]
    public async Task Durable_failure_preserves_coins_and_pending_wipe()
    {
        using var shop = new ShopFixture();
        await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop);
        shop.Sql("CREATE TRIGGER refuse_coin BEFORE UPDATE OF count ON item_slots BEGIN SELECT RAISE(ABORT,'coin failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await shop.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run)));
        Assert.Equal(10, (await Coins(shop)));
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_coin_or_foreign_target_does_not_cancel_wipe(bool foreignTarget)
    {
        using var shop = new ShopFixture();
        if (foreignTarget) await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(new UseRevivalCoinCommand(
            shop.OnlineUser, shop.Session, foreignTarget ? new(999) : shop.Session.CharacterId, run));
        Assert.False(result.Succeeded);
        Assert.Equal(foreignTarget ? 10 : 0, (await Coins(shop)));
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        });
    }

    [Fact]
    public async Task Tournament_revival_limit_refusal_keeps_coin_life_state_and_wipe_deadline()
    {
        var tournament = new TournamentRevival { Allowed = false };
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(tournament));
        await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop, tournament: true);
        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run));
        Assert.False(result.Succeeded);
        Assert.Equal(10, await Coins(shop));
        Assert.Equal(0, tournament.Revivals);
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        });
    }

    [Fact]
    public async Task Tournament_revival_counter_commits_with_coin_and_rolls_back_on_durable_failure()
    {
        var tournament = new TournamentRevival();
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(provider =>
        {
            tournament.Transactions = provider.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
            return tournament;
        }));
        await shop.Seed(1, 1, 10, 0);
        var run = EnterDead(shop, tournament: true);
        var bus = shop.Services.GetRequiredService<ICommandBus>();
        var command = new UseRevivalCoinCommand(shop.OnlineUser, shop.Session, shop.Session.CharacterId, run);
        shop.Sql("CREATE TRIGGER refuse_coin BEFORE UPDATE OF count ON item_slots BEGIN SELECT RAISE(ABORT,'coin failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(async () => await bus.SendAsync(command));
        Assert.Equal(10, await Coins(shop));
        Assert.Equal(0, tournament.Revivals);
        TestGameplay.Run(shop.Services, () =>
        {
            Assert.Equal([run], shop.Services.GetRequiredService<IDungeonRunDomainService>().FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        });
        shop.Sql("DROP TRIGGER refuse_coin;");
        Assert.True((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(9, await Coins(shop));
        Assert.Equal(1, tournament.Revivals);
        Assert.False((await bus.SendAsync(command)).Succeeded);
        Assert.Equal(9, await Coins(shop));
    }

    private sealed class TournamentRevival : ITournamentDungeonDomainService
    {
        public ValueTask<IReadOnlyList<TournamentQuotaSnapshot>> ReadQuotaAsync(Dfo.Server.Data.Characters.CharacterId characterId, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TournamentQuotaSnapshot>>([]);
        public bool Allowed { get; init; } = true;
        public int Revivals { get; private set; }
        public Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor? Transactions { get; set; }
        public bool CanRevive(Dfo.Server.Data.Characters.CharacterId characterId, DungeonRunId runId) => Allowed && Revivals == 0;
        public bool StageRevival(Dfo.Server.Data.Characters.CharacterId characterId, DungeonRunId runId)
        {
            if (!CanRevive(characterId, runId)) return false;
            Transactions!.Current!.Enlist(this, () => new Revival(this));
            return true;
        }
        private sealed class Revival(TournamentRevival owner) : Dfo.Server.Abstractions.Transactions.ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask CommitAsync(CancellationToken ct) { owner.Revivals++; return ValueTask.CompletedTask; }
            public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
        }
        public IReadOnlyList<TournamentDungeonState> PendingRewards(DateTimeOffset now) => [];
        public bool BeginRewardSelection(SelectedSessionIdentity session, DungeonRunId runId) => false;
        public bool Supports(int dungeonId) => dungeonId == 120;
        public TournamentDungeonState? Find(Dfo.Server.Data.Characters.CharacterId characterId) => null;
        public bool CanKill(DungeonRunState run, ushort actor) => throw new NotSupportedException();
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TournamentActionResult?> AdvanceAsync(SelectedSessionIdentity session, DungeonRunId runId, ushort actor, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TournamentActionResult?> EliminateAsync(SelectedSessionIdentity session, DungeonRunId runId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TournamentActionResult?> SelectRewardAsync(SelectedSessionIdentity session, DungeonRunId runId, byte row, byte index, CancellationToken ct) => throw new NotSupportedException();
    }
}

