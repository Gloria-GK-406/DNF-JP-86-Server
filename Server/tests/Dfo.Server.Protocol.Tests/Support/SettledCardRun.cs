using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Support;

internal static class SettledCardRun
{
    internal static IDungeonRunDomainService Create(SelectedSessionIdentity session)
    {
        var runs = TestParties.Runs();
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(144, 0, 1, 1, cell, cell, 8153, 7,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(8153) }, 5);
        var run = runs.Begin(session.CharacterId, layout, DungeonDifficulty.Normal);
        runs.FixClearRewardCards(session.CharacterId, run.RunId,
            new(ClearRewardCardKind.Free, [], 0), new(ClearRewardCardKind.Paid, [], 0), 0);
        Assert.NotNull(runs.TryOpenClear(session.CharacterId, run.RunId, run.RoomVersion));
        var claim = Assert.IsType<DungeonSettlementClaim>(runs.TryClaimSettlement(session.CharacterId, run.RunId));
        Assert.NotNull(runs.CommitSettlement(claim));
        Assert.False(runs.Find(session.CharacterId)!.CardLayoutOpen);
        return runs;
    }
}
