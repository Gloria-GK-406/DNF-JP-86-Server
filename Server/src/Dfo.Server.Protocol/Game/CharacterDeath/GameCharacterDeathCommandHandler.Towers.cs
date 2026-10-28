using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.CharacterDeath;

// Tower channels: the caller has already dropped a stale request and located the requester in this tower.
internal sealed partial class GameCharacterDeathCommandHandler
{
    private Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> CaptureConnections(IEnumerable<SelectedSessionIdentity> members)=>
        members.ToDictionary(member=>member,member=>onlineUsers.TryGetByConnection(member.ConnectionId,out var user)==true&&user is not null
            &&user.AccountId==member.AccountId&&user.CurrentCharacterId==member.CharacterId?user.Connection:null);
    private async ValueTask CloseFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session,Exception Error)> failures,Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> captured)
    {
        foreach(var(session,error)in failures.DistinctBy(x=>x.Session))
        {
            logger.LogWarning(error,"Tower death projection failed for exact session {ConnectionId}",session.ConnectionId);
            if(sessions.IsInWorld(session)&&captured.GetValueOrDefault(session)is{}physical)
                await closures.RequestCloseAsync(physical,CancellationToken.None).ConfigureAwait(false);
        }
    }
    private async ValueTask DieBossAsync(CommandExecutionContext execution,DungeonPlace.InBossTower place,GameMessageMetadata metadata,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session,expected))return;
        var members=expected.Members.Select(x=>x.Session).ToArray();var captured=CaptureConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member)with{Metadata=metadata}:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var action=await workUnits.WriteAsync(execution,new("die-in-boss-tower",participants,Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>bossTowers.DieAsync(BossTowerActionProjection.BossTowerContext(expected,session),unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed||action.Run is null)return;
        var failures=await BossTowerActionProjection.BossTowerProjectActionAsync(sessions, characters, bossTowers, clock, outbox, action,new CharacterLifeStateNotification(session.WireUserId),token,session,new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);
    }
    private async ValueTask DieIllusionAsync(CommandExecutionContext execution,DungeonPlace.InIllusionTower place,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session,expected))return;
        var members=expected.Members.Select(x=>x.Session).ToArray();var captured=CaptureConnections(members);
        var action=await workUnits.WriteAsync(execution,new("die-in-illusion-tower",[],Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>illusionTowers.DieAsync(IllusionTowerActionProjection.IllusionTowerContext(expected,session),unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed||action.Run is null)return;
        var failures=await IllusionTowerActionProjection.IllusionTowerProjectActionAsync(sessions, characters, illusionTowers, clock, outbox, action,new CharacterLifeStateNotification(session.WireUserId),token,session,new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);
    }
}
