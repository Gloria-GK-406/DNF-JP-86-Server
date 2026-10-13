using System.Reflection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Tournament;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class TournamentRewardSweepTests
{
    [Fact]
    public async Task Due_sweep_dispatches_each_unselected_row_through_the_registered_command_once()
    {
        var tournament = new TournamentRewards();
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(tournament));
        tournament.State = Finished(shop, Enter(shop), shop.Clock.GetUtcNow());
        var candidate = tournament.State;

        await Sweep(shop, candidate);
        await Sweep(shop, candidate);

        Assert.Equal(new[] { (Row: (byte)0, Index: (byte)0), (Row: (byte)1, Index: (byte)0) }, tournament.Selections);
        Assert.Equal(new byte?[] { 0, 0 }, tournament.State.Selections);
        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.All(shop.Sender.Messages, message => Assert.Equal(ServerMessageType.GameTournamentSelectReward, message.MessageType));
    }

    [Fact]
    public async Task Due_sweep_preserves_the_manually_selected_card_and_only_fills_the_other_row()
    {
        var tournament = new TournamentRewards();
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(tournament));
        tournament.State = Finished(shop, Enter(shop), shop.Clock.GetUtcNow()) with { Selections = [1, null] };

        await Sweep(shop, tournament.State);

        Assert.Equal(new[] { (Row: (byte)1, Index: (byte)0) }, tournament.Selections);
        Assert.Equal(new byte?[] { 1, 0 }, tournament.State.Selections);
        Assert.Equal(ServerMessageType.GameTournamentSelectReward, Assert.Single(shop.Sender.Messages).MessageType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Old_run_candidate_cannot_deliver_into_a_new_run_or_a_new_tournament_state(bool replaceCanonicalRun)
    {
        var tournament = new TournamentRewards();
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(tournament));
        var run = Enter(shop);
        tournament.State = Finished(shop, run, shop.Clock.GetUtcNow());
        var candidate = tournament.State;
        if (replaceCanonicalRun)
        {
            Assert.True(shop.Services.GetRequiredService<IDungeonRunDomainService>().End(shop.Session.CharacterId, run));
            Assert.NotEqual(run, Enter(shop));
        }
        else tournament.State = tournament.State with { RunId = DungeonRunId.New() };

        await Sweep(shop, candidate);

        Assert.Empty(tournament.Selections);
        Assert.Empty(shop.Sender.Messages);
        Assert.All(tournament.State.Selections, value => Assert.Null(value));
    }

    [Fact]
    public async Task Sweep_rechecks_the_deadline_and_does_not_deliver_early()
    {
        var tournament = new TournamentRewards();
        using var shop = new ShopFixture(services => services.AddSingleton<ITournamentDungeonDomainService>(tournament));
        tournament.State = Finished(shop, Enter(shop), shop.Clock.GetUtcNow().AddSeconds(1));
        var candidate = tournament.State;

        await Sweep(shop, candidate);
        shop.Clock.Advance(TimeSpan.FromMilliseconds(999));
        await Sweep(shop, candidate);
        Assert.Empty(tournament.Selections);
        Assert.Empty(shop.Sender.Messages);

        shop.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Sweep(shop, candidate);
        Assert.Equal(2, tournament.Selections.Count);
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    private static readonly TournamentPlan Plan = new(
        new(120, 17100, 69, 70, 2, 2, [], [], [0, 30000, 50000, 100000, 200000], [], [], 1),
        [], [], 1);

    private static TournamentDungeonState Finished(ShopFixture shop, DungeonRunId run, DateTimeOffset deadline) =>
        new(run, shop.Session.CharacterId, Plan, 4, TournamentStatus.Champion)
        {
            RewardSelectionEndsAt = deadline,
            Rewards = [[new(3329, 1), new(3329, 2)], [new(0, 100), new(0, 200)]],
        };

    private static DungeonRunId Enter(ShopFixture shop)
    {
        var layout = new DungeonLayout(120, 0, 1, 1, new(0, 0), new(0, 0), 17100, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(17100) })
        { Tournament = Plan };
        return shop.Services.GetRequiredService<IDungeonRunDomainService>().PreparePartyEntry(
            [new(shop.Session.AccountId, shop.Session.CharacterId, 0, true)], layout, (DungeonDifficulty)2).Run.RunId;
    }

    private static async ValueTask Sweep(ShopFixture shop, TournamentDungeonState candidate)
    {
        // Protocol's internals are visible only to Protocol.Tests. Invoke the actual worker
        // method instead of widening its production API or copying its dispatch algorithm.
        var method = typeof(TournamentRewardSweepService).GetMethod(
            "SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var worker = shop.Services.GetRequiredService<TournamentRewardSweepService>();
        await Assert.IsType<ValueTask>(method.Invoke(worker, [candidate, CancellationToken.None]));
    }

    private sealed class TournamentRewards : ITournamentDungeonDomainService
    {
        public TournamentDungeonState State { get; set; } = null!;
        public List<(byte Row, byte Index)> Selections { get; } = [];
        public TournamentDungeonState? Find(CharacterId characterId) => State.CharacterId == characterId ? State : null;
        public IReadOnlyList<TournamentDungeonState> PendingRewards(DateTimeOffset now) =>
            State.RewardSelectionEndsAt is { } deadline && deadline <= now ? [State] : [];
        public bool Supports(int dungeonId) => dungeonId == 120;
        public bool BeginRewardSelection(SelectedSessionIdentity session, DungeonRunId runId) => false;
        public ValueTask<TournamentActionResult?> SelectRewardAsync(SelectedSessionIdentity session, DungeonRunId runId,
            byte row, byte index, CancellationToken ct)
        {
            if (State.RunId != runId || State.CharacterId != session.CharacterId || State.Selections[row].HasValue)
                return ValueTask.FromResult<TournamentActionResult?>(null);
            Selections.Add((row, index));
            var selected = State.Selections.ToArray();
            selected[row] = index;
            State = State with { Selections = selected };
            return ValueTask.FromResult<TournamentActionResult?>(new(State));
        }
        public bool CanKill(DungeonRunState run, ushort actor) => throw new NotSupportedException();
        public bool CanRevive(CharacterId characterId, DungeonRunId runId) => false;
        public bool StageRevival(CharacterId characterId, DungeonRunId runId) => false;
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TournamentActionResult?> AdvanceAsync(SelectedSessionIdentity session, DungeonRunId runId, ushort actor,
            CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TournamentActionResult?> EliminateAsync(SelectedSessionIdentity session, DungeonRunId runId,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
