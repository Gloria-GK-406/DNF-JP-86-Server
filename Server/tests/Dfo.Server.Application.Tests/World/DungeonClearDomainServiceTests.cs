using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.World;

public sealed class DungeonClearDomainServiceTests
{
    private static readonly AccountId LeaderAccount = new(10);
    private static readonly CharacterId Leader = new(100);
    private static readonly AccountId HelperAccount = new(20);
    private static readonly CharacterId Helper = new(200);

    [Fact]
    public async Task Boss_clear_opens_only_after_room_and_settlement_conditions_are_true()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Leader, BossLayout(), difficulty: 0);
        var room = new MutableRoomCleared();
        var settlement = new MutableSettlementRules();
        var service = new DungeonClearDomainService(runs, room, settlement);

        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        room.Cleared = true;
        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        settlement.CanComplete = true;

        var opened = await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default);

        Assert.NotNull(opened);
        Assert.True(runs.Find(Leader)!.ClearOpen);
        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
        Assert.Equal(2, settlement.TimesAsked);
    }

    [Fact]
    public async Task Party_helper_rechecks_the_frozen_leaders_story_condition()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [
                new DungeonRunParticipant(LeaderAccount, Leader, 0, IsLeader: true),
                new DungeonRunParticipant(HelperAccount, Helper, 1, IsLeader: false),
            ],
            BossLayout(),
            difficulty: 0);
        var settlement = new MutableSettlementRules { CanComplete = true };
        var service = new DungeonClearDomainService(
            runs, new MutableRoomCleared { Cleared = true }, settlement);

        var opened = await service.EvaluateAsync(
            HelperAccount, Helper, run.RunId, run.RoomVersion, default);

        Assert.NotNull(opened);
        Assert.Equal(LeaderAccount, settlement.LastAccountId);
        Assert.Equal(Leader, settlement.LastCharacterId);
        Assert.Equal([Leader, Helper], opened.Recipients);
    }

    [Fact]
    public async Task Ordinary_room_and_stale_room_version_cannot_open_the_clear()
    {
        var runs = new DungeonRunDomainService();
        var ordinary = runs.Begin(Leader, OrdinaryLayout(), difficulty: 0);
        var settlement = new MutableSettlementRules { CanComplete = true };
        var service = new DungeonClearDomainService(
            runs, new MutableRoomCleared { Cleared = true }, settlement);

        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion, default));
        Assert.Null(await service.EvaluateAsync(
            LeaderAccount, Leader, ordinary.RunId, ordinary.RoomVersion + 1, default));
        Assert.Equal(0, settlement.TimesAsked);
    }

    private static DungeonLayout BossLayout() => new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(0, 0),
        StartRoomMapId: 8153,
        Seed: 7,
        Rooms: new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8153 });

    private static DungeonLayout OrdinaryLayout() => new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 2,
        Height: 1,
        StartCell: new MazeCell(0, 0),
        BossCell: new MazeCell(1, 0),
        StartRoomMapId: 8153,
        Seed: 7,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 0)] = 8153,
            [new MazeCell(1, 0)] = 8154,
        });

    private sealed class MutableRoomCleared : IMonsterKillDomainService
    {
        public bool Cleared { get; set; }

        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Cleared);
    }

    private sealed class MutableSettlementRules : IDungeonSettlementRuleService
    {
        public bool CanComplete { get; set; }
        public int TimesAsked { get; private set; }
        public AccountId? LastAccountId { get; private set; }
        public CharacterId? LastCharacterId { get; private set; }

        public ValueTask<DungeonSettlementRules> ForAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunState run,
            CancellationToken cancellationToken)
        {
            TimesAsked++;
            LastAccountId = accountId;
            LastCharacterId = characterId;
            return ValueTask.FromResult(new DungeonSettlementRules(
                CanComplete, run.Layout.Purpose.IsQuestLinked));
        }
    }
}
