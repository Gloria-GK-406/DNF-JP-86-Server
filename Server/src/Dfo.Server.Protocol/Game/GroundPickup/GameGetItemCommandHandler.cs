using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
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
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.GroundPickup;

internal sealed partial class GameGetItemCommandHandler : ICommandHandler<GameGetItemCommand,bool>
{
    private readonly ISelectedSessionDomainService sessions;
    private readonly ILogger logger;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
    private readonly ITowerRunKind bossKind;
    private readonly ITowerRunKind illusionKind;
    private readonly IDungeonParticipationDomainService participation;
    private readonly TimeProvider clock;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandWorkUnits workUnits;
    private readonly IOnlineUserQueryDomainService onlineUsers;
    private readonly IPartyService parties;

    private readonly IDungeonRunDomainService runs;
    private readonly IDungeonRunProjectionDomainService registry;
    private readonly IDungeonClearDomainService clearEvaluator;
    private readonly IQuestQueryDomainService quests;
    private readonly IGroundPickupDomainService floor;
    private readonly ICommandConnectionClosures closures;
    private readonly IInventorySlotProjectionDomainService slots;

    public GameGetItemCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        IDungeonRunProjectionDomainService registry, IPartyService parties, IDungeonClearDomainService clearEvaluator,
        IGroundPickupDomainService floor, IQuestQueryDomainService quests, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IDungeonParticipationDomainService participation, IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units, IGameCommandOutbox outbox,
        ICommandConnectionClosures closures, TimeProvider clock, IInventorySlotProjectionDomainService slots, ILogger<GameGetItemCommandHandler> logger )

    {
        this.sessions = sessions; this.logger = logger; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; bossKind = new BossTowerRunKind(bossTowers); illusionKind = new IllusionTowerRunKind(illusionTowers); this.participation = participation; this.clock = clock; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties;
        this.runs=runs;this.registry=registry;this.clearEvaluator=clearEvaluator;this.quests=quests;
        this.floor=floor;this.closures=closures;this.slots=slots;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,GameGetItemCommand command,CancellationToken token)
    {
        var connectionId=command.Dispatch.Connection.ConnectionId;var message=command.Input;
        if(GameRequestFreshness.IsStale(message.Metadata))return true;
        var session=sessions.FindByConnection(connectionId);
        if(session is null)
        {
            logger.LogWarning("Get item {ConnectionId}: no published session, ignoring drop {Number}",connectionId,message.GroundDropNumber);
            return true;
        }
        switch(DungeonLocator.Locate(session,participation,bossTowers,illusionTowers))
        {
            case DungeonPlace.InBossTower boss:
                await PickupBossAsync(execution,connectionId,boss,message.Metadata,message.GroundDropNumber,token).ConfigureAwait(false);return true;
            case DungeonPlace.InIllusionTower illusion:
                await PickupIllusionAsync(execution,connectionId,illusion,message.GroundDropNumber,token).ConfigureAwait(false);return true;
            default:
                await PickupOrdinaryAsync(execution,connectionId,session,message.GroundDropNumber,token).ConfigureAwait(false);return true;
        }
    }
    private async ValueTask PickupOrdinaryAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        SelectedSessionIdentity session,ushort groundDropNumber,CancellationToken token)
    {
        var run=runs.Find(session.CharacterId);
        if(!sessions.IsInWorld(session))return;
        if(run is not null && !FindRecipients(session,run.RunId).Contains(session))return;
        var result=await SettleAsync(execution,new(session.AccountId,session.CharacterId,groundDropNumber,
            run?.RunId??default,run?.RoomVersion),token).ConfigureAwait(false);
        // The committed quest pickup changes authoritative clear state before any currentness-dependent projection.
        var clear=await EvaluateClearAsync(clearEvaluator,session,result).ConfigureAwait(false);
        if(result.Status is GroundPickupStatus.Banked or GroundPickupStatus.PickedUp or GroundPickupStatus.EpicPieceCollected)
        {
            logger.LogInformation("Get item {ConnectionId} user={WireUserId}: drop {Number} status={Status} item={ItemId} slot={Slot} amount={Amount} total={Total}",
                connectionId,session.WireUserId,groundDropNumber,result.Status,result.ItemId,result.SlotIndex,result.Amount,result.Total);
            _=await DeliverPickupAsync(session,result,token).ConfigureAwait(false);
            if(clear is not null)
                _=await DungeonClearCommandProjection.DeliverAsync(sessions,runs,FindRecipients(session,clear.RunId),quests,outbox,session,clear,token).ConfigureAwait(false);
            return;
        }
        logger.LogInformation("Get item {ConnectionId} user={WireUserId}: drop {Number} refused as {Status}",connectionId,session.WireUserId,groundDropNumber,result.Status);
        if(result.Status is GroundPickupStatus.InventoryFull)await RefuseInventoryFullAsync(session,token).ConfigureAwait(false);
    }
    private IReadOnlyList<SelectedSessionIdentity> FindRecipients(SelectedSessionIdentity requester,DungeonRunId runId) =>
        DungeonPartyRecipients.FindCurrent(parties,sessions,runs,registry,requester,runId);
    private ValueTask<GroundPickupResult> SettleAsync(CommandExecutionContext execution,GroundPickupInput input,CancellationToken token) =>
        workUnits.WriteAsync(execution,new("pick-up-ground-drop",[]),(unit,ct)=>PickUpAsync(input,ct),token);
}
