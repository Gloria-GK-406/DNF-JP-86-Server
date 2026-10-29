using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
// Tower channels: the caller has already dropped a stale request and located the requester in this tower.
internal sealed partial class GameMonsterDeathCommandHandler
{
    private Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> CaptureKillConnections(IEnumerable<SelectedSessionIdentity> members) =>
        members.ToDictionary(member => member, member => onlineUsers.TryGetByConnection(member.ConnectionId,out var user)==true && user is not null
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
    private async ValueTask KillBossAsync(CommandExecutionContext execution,DungeonPlace.InBossTower place,
        GameMessageMetadata metadata,ushort actorUid,ushort killerUid,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!TowerActionProjection.Exact(sessions, bossKind, session,expected)) return;
        var members=expected.Members.Select(member=>member.Session).ToArray();
        var captured=CaptureKillConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member) with { Metadata=metadata }:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var action=await workUnits.WriteAsync(execution,new("kill-boss-tower-actor",participants,
            Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>bossTowers.KillAsync(TowerActionProjection.Context(expected,session),actorUid,unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed || action.Run is null) return;
        var failures=await TowerActionProjection.ProjectActionAsync(sessions, characters, bossKind, clock, outbox, action,new MonsterDeathNotification(actorUid,
            action.Drops.Select(item=>new MonsterDrop(item.Uid,item.Rolled)).ToArray(),killerUid),token).ConfigureAwait(false);
        await CloseKillFailuresAsync(failures,captured).ConfigureAwait(false);
    }
    private async ValueTask KillIllusionAsync(CommandExecutionContext execution,DungeonPlace.InIllusionTower place,
        ushort actorUid,ushort killerUid,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!TowerActionProjection.Exact(sessions, illusionKind, session,expected)) return;
        var members=expected.Members.Select(member=>member.Session).ToArray();var captured=CaptureKillConnections(members);
        var action=await workUnits.WriteAsync(execution,new("kill-illusion-tower-actor",[],
            Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>illusionTowers.KillAsync(TowerActionProjection.Context(expected,session),actorUid,unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed || action.Run is null) return;
        var failures=await TowerActionProjection.ProjectActionAsync(sessions, characters, illusionKind, clock, outbox, action,new MonsterDeathNotification(actorUid,
            action.Drops.Select(item=>new MonsterDrop(item.Uid,new RolledDrop(item.ItemId,1,DropRarity.Common,DropItemKind.Stackable))).ToArray(),killerUid),token).ConfigureAwait(false);
        await CloseKillFailuresAsync(failures,captured).ConfigureAwait(false);
    }
}
