using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.CharacterDeath;

internal sealed partial class GameCharacterDeathCommandHandler : ICommandHandler<GameCharacterDeathCommand,bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ILogger logger;
    private readonly ICharacterDomainService characters;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;

    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly ICommandConnectionClosures closures;
    public GameCharacterDeathCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        IDungeonRunProjectionDomainService registry, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        ICharacterDomainService characters, IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units,
        IGameCommandOutbox outbox, ICommandConnectionClosures closures, TimeProvider clock, ILogger<GameCharacterDeathCommandHandler> logger )

    {
        this.sessions = sessions; this.logger = logger; this.characters = characters; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.clock = clock; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers;this.runs=runs;this.registry=registry;this.closures=closures;}

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCharacterDeathCommand command, CancellationToken token)
    {
        var connectionId=command.Dispatch.Connection.ConnectionId;var message=command.Input;
        var session=sessions.FindByConnection(connectionId);if(session is null)return true;
        if(await DieBossAsync(execution,connectionId,message.Metadata,token).ConfigureAwait(false))return true;
        if(await DieIllusionAsync(execution,connectionId,message.Metadata,token).ConfigureAwait(false))return true;
        var expected=runs.Find(session.CharacterId);if(expected is null)return true;
        if(!sessions.IsInWorld(session)||registry.Find(expected.RunId).SingleOrDefault(x=>x.CharacterId==session.CharacterId)!=session)return true;
        // Original Direct mutation: no transaction or work unit is created for ordinary death.
        var committed=runs.TryMarkParticipantDead(session.CharacterId,expected.RunId,expected.RoomVersion);if(committed is null)return true;
        var notification=new CharacterLifeStateNotification(session.WireUserId);
        var secondary=registry.Find(committed.RunId)
            .Where(x=>x.CharacterId!=session.CharacterId)
            .Where(x=>committed.Participants.Any(p=>p.CharacterId==x.CharacterId))
            .Where(x=>sessions.IsInWorld(x))
            .Where(x=>runs.Find(x.CharacterId)?.RunId==committed.RunId)
            .Select(x=>new GameExplicitMessageTarget(x.ConnectionId,[notification])).ToArray();
        await outbox.RegisterExplicitAsync(connectionId,new([new CharacterDeathResponse(),notification],secondary),token).ConfigureAwait(false);
        logger.LogInformation("Character death user={WireUserId} character={CharacterId} run={RunId}: CMD 0028 ACK + NOTI 0020 sent; dead={DeadCount}/{MemberCount}, report=({First},{Second},{Third})",
            session.WireUserId,session.CharacterId,committed.RunId,committed.DeadParticipants.Count,committed.Participants.Count,message.First,message.Second,message.Third);
        return true;
    }
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
    private async ValueTask<bool> DieBossAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,GameMessageMetadata metadata,CancellationToken token)
    {
        if(GameRequestFreshness.IsStale(metadata))return true;
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:bossTowers.Find(session);
        if(session is null||expected is null)return false;
        if(!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session,expected))return true;
        var members=expected.Members.Select(x=>x.Session).ToArray();var captured=CaptureConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member)with{Metadata=metadata}:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var action=await workUnits.WriteAsync(execution,new("die-in-boss-tower",participants,Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>bossTowers.DieAsync(BossTowerActionProjection.BossTowerContext(expected,session),unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed||action.Run is null)return true;
        var failures=await BossTowerActionProjection.BossTowerProjectActionAsync(sessions, characters, bossTowers, clock, outbox, action,new CharacterLifeStateNotification(session.WireUserId),token,session,new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
    private async ValueTask<bool> DieIllusionAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,GameMessageMetadata metadata,CancellationToken token)
    {
        if(GameRequestFreshness.IsStale(metadata))return true;
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:illusionTowers.Find(session);
        if(session is null||expected is null)return false;
        if(!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session,expected))return true;
        var members=expected.Members.Select(x=>x.Session).ToArray();var captured=CaptureConnections(members);
        var action=await workUnits.WriteAsync(execution,new("die-in-illusion-tower",[],Footprint:CommandFootprintMode.DeclaredNonRequest,FootprintOwners:members),
            (unit,ct)=>illusionTowers.DieAsync(IllusionTowerActionProjection.IllusionTowerContext(expected,session),unit.Footprint,ct),token).ConfigureAwait(false);
        if(!action.Changed||action.Run is null)return true;
        var failures=await IllusionTowerActionProjection.IllusionTowerProjectActionAsync(sessions, characters, illusionTowers, clock, outbox, action,new CharacterLifeStateNotification(session.WireUserId),token,session,new CharacterDeathResponse()).ConfigureAwait(false);
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
}
