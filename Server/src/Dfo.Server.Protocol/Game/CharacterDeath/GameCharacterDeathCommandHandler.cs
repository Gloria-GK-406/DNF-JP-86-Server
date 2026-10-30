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
using Dfo.Server.Protocol.Game.Dungeons;
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
    private readonly ITowerRunKind bossKind;
    private readonly ITowerRunKind illusionKind;
    private readonly IDungeonParticipationDomainService participation;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;

    private readonly IDungeonRunParticipantDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly ICommandConnectionClosures closures;
    private readonly IGuardianAltarDomainService guardian;
    public GameCharacterDeathCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunParticipantDomainService runs,
        IDungeonRunProjectionDomainService registry, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDungeonParticipationDomainService participation, ICharacterDomainService characters, IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units,
        IGameCommandOutbox outbox, ICommandConnectionClosures closures, TimeProvider clock, ILogger<GameCharacterDeathCommandHandler> logger,
        IGuardianAltarDomainService guardian)

    {
        this.sessions = sessions; this.logger = logger; this.characters = characters; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; bossKind = new BossTowerRunKind(bossTowers); illusionKind = new IllusionTowerRunKind(illusionTowers); this.participation = participation; this.clock = clock; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers;this.runs=runs;this.registry=registry;this.closures=closures;
        this.guardian = guardian;
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameCharacterDeathCommand command, CancellationToken token)
    {
        var connectionId=command.Dispatch.Connection.ConnectionId;var message=command.Input;
        if(GameRequestFreshness.IsStale(message.Metadata))return true;
        var session=sessions.FindByConnection(connectionId);if(session is null)return true;
        switch(DungeonLocator.Locate(session,participation,bossTowers,illusionTowers))
        {
            case DungeonPlace.InBossTower boss:await DieBossAsync(execution,boss,message.Metadata,token).ConfigureAwait(false);return true;
            case DungeonPlace.InIllusionTower illusion:await DieIllusionAsync(execution,illusion,token).ConfigureAwait(false);return true;
            default:await DieOrdinaryAsync(execution,connectionId,session,message,token).ConfigureAwait(false);return true;
        }
    }
    private async ValueTask DieOrdinaryAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,SelectedSessionIdentity session,CharacterDeathRequest message,CancellationToken token)
    {
        var expected=runs.Find(session.CharacterId);if(expected is null)return;
        if(!sessions.IsInWorld(session)||registry.Find(expected.RunId).SingleOrDefault(x=>x.CharacterId==session.CharacterId)!=session)return;
        // Original Direct mutation: no transaction or work unit is created for ordinary death.
        var occurredAt = clock.GetUtcNow();
        var committed=runs.TryMarkParticipantDead(session.CharacterId,expected.RunId,expected.RoomVersion);if(committed is null)return;
        if (committed.Layout.Mode is DungeonRunMode.Guardian)
        {
            var scheduled = await workUnits.WriteAsync(execution, new("guardian-altar.character-death", [CaptureParticipant(session) with { Metadata = message.Metadata }]),
                (_, ct) => guardian.ReportCharacterDeathAsync(session, committed.RunId, committed.RoomVersion, occurredAt, ct), token).ConfigureAwait(false);
            if (scheduled && guardian.Find(committed.RunId) is { } ending)
                logger.LogInformation("Guardian altar death scheduled character={CharacterId} run={RunId} reason={Reason} occurred={OccurredAt} eplpDue={Deadline}",
                    session.CharacterId, committed.RunId, ending.EndReason, occurredAt, ending.Deadline);
        }
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
    }
}
