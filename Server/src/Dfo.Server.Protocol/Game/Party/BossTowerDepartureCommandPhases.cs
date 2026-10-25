using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;

using Dfo.Server.Protocol.Game.GroundPickup;




using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;

using Dfo.Server.Protocol.Game;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected virtual async ValueTask ProjectBossTowerPartyDepartureAsync(CommandExecutionContext? execution, BossTowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var participants = captured.Members.Select(member => member.Session)
            .Append(returning).OfType<SelectedSessionIdentity>().Distinct().ToArray();
        var exact = BossTowerExactMembers(captured);
        var current = BossTowerCurrentRun(captured);
        if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await BossTowerSendToExactAsync(
                current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        current = BossTowerCurrentRun(captured);
        if (current is not null && BossTowerExactMembers(current).Count == current.Members.Length
            && current.Room.Phase == TowerRoomPhase.Ready
            && current.Members.SingleOrDefault(member => member.IsLeader) is { } readyLeader)
        {
            var start = await StartBossPartyTowerAsync(execution, current, readyLeader.Session, token).ConfigureAwait(false);
            if (start.Run is { Room.Phase: TowerRoomPhase.Fighting } fighting)
                failures.AddRange(await BossTowerSendToExactAsync(fighting, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false));
        }
        if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
            failures.AddRange(await BossTowerProjectActionAsync(action, null, token).ConfigureAwait(false));
        if (returning is not null
            && sessions.IsInWorld(returning)
            && !bossTowers.IsInTower(returning.CharacterId))
            {
            var entries = await BossTowerCaptureReturnEntriesAsync([returning], response, failures, token, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        }
        await BossTowerCloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

    protected virtual async ValueTask ProjectBossTowerPartyHostAsync(
        BossTowerSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var current = BossTowerCurrentRun(captured);
        if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await BossTowerSendToExactAsync(
                current, _ => [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        await BossTowerCloseProjectionFailuresAsync(failures, captured.RunId).ConfigureAwait(false);
    }

}
