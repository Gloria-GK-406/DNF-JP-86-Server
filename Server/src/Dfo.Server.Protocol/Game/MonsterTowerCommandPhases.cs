using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
internal partial class GameMonsterDeathCommandHandler
{
    private Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> CaptureKillConnections(IEnumerable<SelectedSessionIdentity> members) =>
        members.ToDictionary(member => member, member => onlineUsers?.TryGetByConnection(member.ConnectionId,out var user)==true && user is not null
            && user.AccountId==member.AccountId && user.CurrentCharacterId==member.CharacterId ? user.Connection : null);
    private async ValueTask CloseKillFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session,Exception Error)> failures,
        Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> captured)
    {
        foreach(var (session,error) in failures.DistinctBy(value=>value.Session))
        {
            logger.LogWarning(error,"Tower kill projection failed for exact session {ConnectionId}",session.ConnectionId);
            if(sessions.IsInWorld(session) && captured.GetValueOrDefault(session) is {} physical)
                await closures.RequestCloseAsync(physical,CancellationToken.None).ConfigureAwait(false);
        }
    }
    protected virtual async ValueTask<bool> KillBossAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        GameMessageMetadata metadata,ushort actorUid,ushort killerUid,CancellationToken token)
    {
        if(TowerProtocol.IsStale(metadata)) return true;
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:bossTowers.Find(session);
        if(session is null || expected is null) return false;
        if(!BossTowerExact(session,expected)) return true;
        var members=expected.Members.Select(member=>member.Session).ToArray();
        var captured=CaptureKillConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member) with { Metadata=metadata }:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var action=await workUnits!.WriteAsync(execution,new("kill-boss-tower-actor",participants,
            Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>bossTowers.KillAsync(BossTowerContext(expected,session),actorUid,unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed || action.Run is null) return true;
        var failures=await BossTowerProjectActionAsync(action,new MonsterDeathNotification(actorUid,
            action.Drops.Select(item=>new MonsterDrop(item.Uid,item.Rolled)).ToArray(),killerUid),token).ConfigureAwait(false);
        await CloseKillFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
    protected virtual async ValueTask<bool> KillIllusionAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        GameMessageMetadata metadata,ushort actorUid,ushort killerUid,CancellationToken token)
    {
        // The stale gate precedes session/run lookup in the original helper, even for ordinary runs.
        if(TowerProtocol.IsStale(metadata)) return true;
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:illusionTowers.Find(session);
        if(session is null || expected is null) return false;
        if(!IllusionTowerExact(session,expected)) return true;
        var members=expected.Members.Select(member=>member.Session).ToArray();var captured=CaptureKillConnections(members);
        var action=await workUnits!.WriteAsync(execution,new("kill-illusion-tower-actor",[],
            Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>illusionTowers.KillAsync(IllusionTowerContext(expected,session),actorUid,unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed || action.Run is null) return true;
        var failures=await IllusionTowerProjectActionAsync(action,new MonsterDeathNotification(actorUid,
            action.Drops.Select(item=>new MonsterDrop(item.Uid,new RolledDrop(item.ItemId,1,DropRarity.Common,DropItemKind.Stackable))).ToArray(),killerUid),token).ConfigureAwait(false);
        await CloseKillFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
}
