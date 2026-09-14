using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonVisitDomainServiceTests
{
    private static readonly DungeonLayout Layout = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(0, 0),
        StartRoomMapId: 61000,
        Seed: 123,
        Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 61000 });

    [Fact]
    public async Task Party_entry_resolves_one_layout_and_describes_one_shared_start_room()
    {
        DungeonRunParticipant[] party =
        [
            new(new AccountId(11), new CharacterId(1), 1, 0, IsLeader: true),
            new(new AccountId(22), new CharacterId(2), 2, 1, IsLeader: false),
        ];
        var layouts = new RecordingLayouts();
        var visits = new DungeonVisitDomainService(
            layouts,
            new DungeonRunDomainService(),
            new EmptyRooms(),
            new NoPlacement(),
            new ImmediateSelection());

        var entered = await visits.EnterPartyAsync(
            party, Layout.DungeonId, difficulty: 2, DungeonEntryFacts.None, CancellationToken.None);

        Assert.NotNull(entered);
        Assert.Equal(1, layouts.CallCount);
        Assert.Equal(party.Select(participant => participant.CharacterId), entered.Visits.Keys);
        Assert.Single(entered.Visits.Values.Select(visit => visit.Run.RunId).Distinct());
        Assert.Single(entered.Visits.Values.Select(visit => visit.Run.CurrentCell).Distinct());
        Assert.All(entered.Visits.Values, visit => Assert.True(visit.FirstEntry));
    }

    [Fact]
    public async Task A_failed_member_projection_removes_every_participant_published_by_the_entry()
    {
        DungeonRunParticipant[] party =
        [
            new(new AccountId(11), new CharacterId(1), 1, 0, IsLeader: true),
            new(new AccountId(22), new CharacterId(2), 2, 1, IsLeader: false),
        ];
        var runs = new DungeonRunDomainService();
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            new FailingSecondRoomProjection(),
            new NoPlacement(),
            new ImmediateSelection());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await visits.EnterPartyAsync(
                party,
                Layout.DungeonId,
                difficulty: 2,
                DungeonEntryFacts.None,
                CancellationToken.None));

        Assert.All(party, participant => Assert.Null(runs.Find(participant.CharacterId)));
    }

    [Fact]
    public async Task A_cancelled_projection_rolls_back_the_old_run_without_ending_a_replacement()
    {
        DungeonRunParticipant[] party =
        [
            new(new AccountId(11), new CharacterId(1), 1, 0, IsLeader: true),
            new(new AccountId(22), new CharacterId(2), 2, 1, IsLeader: false),
        ];
        var runs = new DungeonRunDomainService();
        var rooms = new ReplacingCancelledSecondRoomProjection(runs, party[1].CharacterId);
        var visits = new DungeonVisitDomainService(
            new RecordingLayouts(),
            runs,
            rooms,
            new NoPlacement(),
            new ImmediateSelection());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await visits.EnterPartyAsync(
                party,
                Layout.DungeonId,
                difficulty: 2,
                DungeonEntryFacts.None,
                CancellationToken.None));

        Assert.Null(runs.Find(party[0].CharacterId));
        var replacement = runs.Find(party[1].CharacterId);
        Assert.NotNull(replacement);
        Assert.Equal(rooms.ReplacementRunId, replacement.RunId);
        Assert.Equal(1, replacement.PartyMemberCount);
    }

    private sealed class RecordingLayouts : IDungeonLayoutRuleService
    {
        public int CallCount { get; private set; }

        public ValueTask<DungeonLayout?> ResolveAsync(
            int dungeonId,
            DungeonEntryFacts facts,
            int seed,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            return ValueTask.FromResult<DungeonLayout?>(Layout);
        }
    }

    private class EmptyRooms : IDungeonRoomRuleService
    {
        public virtual ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonRunState run, bool firstEntry, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonRoomContents([], [], 1));

        public ValueTask<DeclaredActorLookup> FindDeclaredActorAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DeclaredActorLookup(null, 0));

        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask<IReadOnlyList<GroundDrop>> RemainingGroundDropsAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<GroundDrop>>([]);

        public ValueTask<IReadOnlyList<DungeonRoomDrop>> DeclaredDropsAsync(
            int dungeonId, int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomDrop>>([]);

        public IReadOnlyList<RoomActorDescription> Describe(
            IReadOnlyList<DungeonRoomActor> declared) => [];

        public ushort FirstActorSequenceFor(DungeonRunState run) => 1;
    }

    private sealed class FailingSecondRoomProjection : EmptyRooms
    {
        private int calls;

        public override ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonRunState run, bool firstEntry, CancellationToken cancellationToken) =>
            ++calls == 2
                ? throw new InvalidOperationException("second participant projection failed")
                : base.DescribeAsync(run, firstEntry, cancellationToken);
    }

    private sealed class ReplacingCancelledSecondRoomProjection(
        IDungeonRunDomainService runs,
        CharacterId replacementCharacterId) : EmptyRooms
    {
        private int calls;

        public DungeonRunId ReplacementRunId { get; private set; }

        public override ValueTask<DungeonRoomContents> DescribeAsync(
            DungeonRunState run, bool firstEntry, CancellationToken cancellationToken)
        {
            if (++calls != 2)
                return base.DescribeAsync(run, firstEntry, cancellationToken);

            ReplacementRunId = runs.Begin(replacementCharacterId, Layout, difficulty: 1).RunId;
            throw new OperationCanceledException("projection was cancelled after replacement");
        }
    }

    private sealed class NoPlacement : IItemPlacementRuleService
    {
        public ValueTask<ItemPlacement?> PlacementForAsync(
            int itemId, CancellationToken cancellationToken) => ValueTask.FromResult<ItemPlacement?>(null);

        public ValueTask<byte?> WornSlotForAsync(
            int itemId, CancellationToken cancellationToken) => ValueTask.FromResult<byte?>(null);

        public ValueTask<ItemWorldClass> ClassifyAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ItemWorldClass.Interchangeable);

        public ValueTask<bool> IsEquipmentAsync(
            int itemId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class ImmediateSelection : IDungeonSelectionRuleService
    {
        public bool DefersSelectionState(byte characterLevel) => false;
    }
}
