using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonClear;
namespace Dfo.Server.Protocol.Game.Party;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected async ValueTask<bool> ReturnBossTowerRunAsync(CommandExecutionContext? execution, ReturnBossTowerRunCommand command, CancellationToken token)
    {
        var expected = bossTowers.Find(command.Context.Session);
        if (expected is null || expected.RunId != command.Context.RunId || expected.Room.Version != command.Context.RoomVersion
            || !(expected.Completion is { Settled: true } && expected.ReturnDueAt is { } due && due <= command.CapturedAt
                || expected.Completion is null && expected.Room.Phase == TowerRoomPhase.Loading && expected.LoadDueAt <= command.CapturedAt)) return false;
        var frozen = BossTowerExactMembers(expected);
        if (frozen.Count == 0) return false;
        var owners = BossTowerMembers(expected);
        foreach (var member in frozen.OrderBy(member => expected.Members.Single(value => value.Session == member).Dead ? 0 : 1))
            await LeaveBossPartyTowerAsync(execution, BossTowerContext(expected, member), owners, token).ConfigureAwait(false);
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var entries = await BossTowerCaptureReturnEntriesAsync(frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
        await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        await BossTowerCloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }
    protected async ValueTask<bool> ReturnIllusionTowerRunAsync(CommandExecutionContext? execution, ReturnIllusionTowerRunCommand command, CancellationToken token)
    {
        var expected = illusionTowers.Find(command.Context.Session);
        if (expected is null || expected.RunId != command.Context.RunId || expected.Room.Version != command.Context.RoomVersion
            || expected.ReturnDueAt is not { } due || due > command.CapturedAt) return false;
        var frozen = IllusionTowerExactMembers(expected);
        if (frozen.Count == 0) return false;
        var owners = expected.Members.Select(member => member.Session).ToArray();
        foreach (var member in frozen)
            await LeaveIllusionPartyTowerAsync(execution, IllusionTowerContext(expected, member), owners, token).ConfigureAwait(false);
        IGameServerMessage? response = expected.ReturnOption is { } option
            ? new EplpCommandResponse(EplpCommandHandler.LeaveState, option) : null;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var entries = await IllusionTowerCaptureReturnEntriesAsync(frozen, response, failures, token, expected.DungeonId == 11000).ConfigureAwait(false);
        await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        await IllusionTowerCloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }
}
