using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Raids;
using Dfo.Server.Data.Characters;
using Xunit;

namespace Dfo.Server.Application.Tests.Raids;

[Trait("Category", "Unit")]
public sealed class AntonRaidRewardSettlementTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset End = Start.AddMinutes(1);

    [Fact]
    public void ManualAndAutomaticSelectionShareSlotsAndOnlyGrantSupportedRewardTypes()
    {
        var service = Create();
        var seed = Seed(true);
        Assert.NotNull(service.Begin(seed, End));
        Assert.Null(service.Begin(seed, End));
        var p = seed.Eligible;
        Assert.False(service.Select(seed.Instance, 0, p[0].Character, 1, 0, End).Accepted);
        Assert.False(service.FinishMovie(seed.Instance, 0, p[0].Character, 0, End)!.OpenedNow);
        Assert.Equal(AntonRaidRewardStage.Result, service.Find(seed.Instance, 0)!.Stage);
        Assert.True(service.FinishMovie(seed.Instance, 0, p[0].Character, 1, End)!.OpenedNow);
        Assert.False(service.FinishMovie(seed.Instance, 0, p[1].Character, 1, End)!.OpenedNow);

        var first = service.Select(seed.Instance, 0, p[0].Character, 1, 1, End);
        Assert.Equal(1, first.Reveal!.Slot);
        var second = service.Select(seed.Instance, 0, p[1].Character, 1, 1, End);
        Assert.Equal(0, second.Reveal!.Slot);
        var sameSlot = service.Select(seed.Instance, 0, p[0].Character, 0, 2, End);
        Assert.Equal(1, sameSlot.Reveal!.Slot);
        Assert.Equal(160000, sameSlot.Grant!.Item!.Quantity);
        var retry = service.Select(seed.Instance, 0, p[0].Character, 0, 0, End);
        Assert.Null(retry.Reveal);
        Assert.Equal(sameSlot.Grant, retry.Grant);
        var pcroom = service.Select(seed.Instance, 0, p[0].Character, 2, 0, End);
        Assert.True(pcroom.Accepted);
        Assert.Null(pcroom.Grant);

        var auto = Advance(service, AntonRaidRewardAdvanceKind.CardsAutoSelected);
        Assert.Equal(3, auto.Revealed.Count);
        Assert.Equal(0, auto.Revealed.Single(card => card.Character == p[1].Character).Slot);
        Assert.All(auto.Revealed.Where(card => card.Character == p[2].Character), card => Assert.Equal(0, card.Slot));
        var squad = Advance(service, AntonRaidRewardAdvanceKind.SquadItemsShown);
        Assert.Equal(3, squad.Revealed.Count);
        var delivery = Advance(service, AntonRaidRewardAdvanceKind.Deliver);
        Assert.Equal(12, delivery.Grants.Count);
        var repeated = Advance(service, AntonRaidRewardAdvanceKind.Deliver);
        Assert.Equal(delivery.Grants, repeated.Grants);
        Assert.True(service.Complete(seed.Instance, 0));
        Assert.Empty(service.Deadlines());
    }

    [Fact]
    public void MovieFallbackStartsCardsAndFailureOnlyGrantsRetryCertificates()
    {
        var service = Create();
        var success = Seed(true);
        Assert.NotNull(service.Begin(success, End));
        Assert.Equal(End.AddSeconds(2), Assert.Single(service.Deadlines()).At);
        Advance(service, AntonRaidRewardAdvanceKind.MoviePrompt);
        Assert.Equal(End.AddSeconds(15), Assert.Single(service.Deadlines()).At);
        Advance(service, AntonRaidRewardAdvanceKind.CardsOpened);
        Assert.Equal(End.AddSeconds(25), Assert.Single(service.Deadlines()).At);

        var failure = Seed(false);
        Assert.NotNull(service.Begin(failure, End));
        var due = service.Deadlines().Single(deadline => deadline.Instance == failure.Instance);
        var advance = Assert.IsType<AntonRaidRewardAdvance>(service.Advance(due, End));
        Assert.Equal(AntonRaidRewardAdvanceKind.Deliver, advance.Kind);
        Assert.Equal(failure.Eligible.Count, advance.Grants.Count);
        Assert.All(advance.Grants, grant =>
        {
            Assert.Equal(AntonRaidRewardKind.RetryCertificate, grant.Kind);
            Assert.Equal(new AntonRaidRewardItem(10099897, 1), grant.Item);
        });
    }

    private static AntonRaidRewardAdvance Advance(IAntonRaidRewardSettlementDomainService service, AntonRaidRewardAdvanceKind kind)
    {
        var due = Assert.Single(service.Deadlines());
        var advance = Assert.IsType<AntonRaidRewardAdvance>(service.Advance(due, due.At));
        Assert.Equal(kind, advance.Kind);
        return advance;
    }

    private static AntonRaidRewardSettlementSeed Seed(bool succeeded)
    {
        AntonRaidRewardParticipant[] players = [new(new(1), new(11), 1, 0),
            new(new(2), new(12), 1, 2), new(new(3), new(13), 0, 2)];
        return new(Guid.NewGuid(), 65, 0, succeeded, 0, Start, End,
            players.Select(player => player.Character).ToArray(), players);
    }

    private static AntonRaidRewardSettlementDomainService Create() => new(new AntonRaidRewardRuleService(new TestContent()), new WakeProbe());

    private sealed class WakeProbe : IDungeonDeadlineWake
    {
        public void Wake(DungeonDeadlineKind kind) { }
    }

    private sealed class TestContent : IAntonRaidRewardContentSource
    {
        public AntonRaidRewardDefinition Read()
        {
            var phase = new AntonRaidPhaseRewardDefinition([new(0, 1, 0), new(2, 5, 1), new(6, 9, 2), new(10, 14, 3)],
                [new(AntonRaidRewardKind.Gold, 3, 1, 0, [new(1, new(0, 160000))]),
                 new(AntonRaidRewardKind.PartyCard, -1, 1, 0, [new(1, new(10093974, 3))]),
                 new(AntonRaidRewardKind.SquadItem, -1, 1, 1, [new(1, new(10095702, 1))])]);
            return new([phase, phase], new(10099897, 1));
        }
    }
}
