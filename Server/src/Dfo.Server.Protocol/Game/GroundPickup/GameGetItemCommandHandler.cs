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
    private readonly ICharacterDomainService characters;
    private readonly IBossTowerDomainService bossTowers;
    private readonly IIllusionTowerDomainService illusionTowers;
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
    private readonly ICharacterContainerDomainService containers;
    private readonly IQuestProgressDomainService questProgress;
    private readonly IItemGrantDomainService itemGrants;
    private readonly IEpicBookDomainService epicBook;
    private readonly ICommandConnectionClosures closures;
    private readonly IInventorySlotProjectionDomainService slots;

    public GameGetItemCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunDomainService runs,
        IDungeonRunProjectionDomainService registry, IPartyService parties, IDungeonClearDomainService clearEvaluator,
        IGroundPickupDomainService floor, ICharacterContainerDomainService containers, ICharacterDomainService characters,
        IQuestProgressDomainService progress, IItemGrantDomainService grants, IEpicBookDomainService epicBook,
        IQuestQueryDomainService quests, IBossTowerDomainService bossTowers, IIllusionTowerDomainService illusionTowers,
        IOnlineUserQueryDomainService onlineUsers, ICommandWorkUnits units, IGameCommandOutbox outbox,
        ICommandConnectionClosures closures, TimeProvider clock, IInventorySlotProjectionDomainService slots, ILogger<GameGetItemCommandHandler> logger )

    {
        this.sessions = sessions; this.logger = logger; this.characters = characters; this.bossTowers = bossTowers; this.illusionTowers = illusionTowers; this.clock = clock; this.outbox = outbox; this.workUnits = units; this.onlineUsers = onlineUsers; this.parties = parties;
        this.runs=runs;this.registry=registry;this.clearEvaluator=clearEvaluator;this.quests=quests;
        this.floor=floor;this.containers=containers;this.questProgress=progress;this.itemGrants=grants;this.epicBook=epicBook;this.closures=closures;this.slots=slots;
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,GameGetItemCommand command,CancellationToken token)
    {
        var connectionId=command.Dispatch.Connection.ConnectionId;var message=command.Input;
        if(await PickupBossAsync(execution,connectionId,message.Metadata,message.GroundDropNumber,token).ConfigureAwait(false))return true;
        if(await PickupIllusionAsync(execution,connectionId,message.Metadata,message.GroundDropNumber,token).ConfigureAwait(false))return true;
        var session=sessions.FindByConnection(connectionId);
        if(session is null)
        {
            logger.LogWarning("Get item {ConnectionId}: no published session, ignoring drop {Number}",connectionId,message.GroundDropNumber);
            return true;
        }
        var run=runs.Find(session.CharacterId);
        if(!sessions.IsInWorld(session))return true;
        if(run is not null && !FindRecipients(session,run.RunId).Contains(session))return true;
        var result=await SettleAsync(execution,new(session.AccountId,session.CharacterId,message.GroundDropNumber,
            run?.RunId??default,run?.RoomVersion),token).ConfigureAwait(false);
        // The committed quest pickup changes authoritative clear state before any currentness-dependent projection.
        var clear=await EvaluateClearAsync(clearEvaluator,session,result).ConfigureAwait(false);
        if(result.Status is GroundPickupStatus.Banked or GroundPickupStatus.PickedUp or GroundPickupStatus.EpicPieceCollected)
        {
            logger.LogInformation("Get item {ConnectionId} user={WireUserId}: drop {Number} status={Status} item={ItemId} slot={Slot} amount={Amount} total={Total}",
                connectionId,session.WireUserId,message.GroundDropNumber,result.Status,result.ItemId,result.SlotIndex,result.Amount,result.Total);
            _=await DeliverPickupAsync(session,result,token).ConfigureAwait(false);
            if(clear is not null)
                _=await DungeonClearCommandProjection.DeliverAsync(sessions,runs,FindRecipients(session,clear.RunId),quests,outbox,session,clear,token).ConfigureAwait(false);
            return true;
        }
        logger.LogInformation("Get item {ConnectionId} user={WireUserId}: drop {Number} refused as {Status}",connectionId,session.WireUserId,message.GroundDropNumber,result.Status);
        if(result.Status is GroundPickupStatus.InventoryFull)await RefuseInventoryFullAsync(session,token).ConfigureAwait(false);
        return true;
    }
    private IReadOnlyList<SelectedSessionIdentity> FindRecipients(SelectedSessionIdentity requester,DungeonRunId runId) =>
        DungeonPartyRecipients.FindCurrent(parties,sessions,runs,registry,requester,runId);
    private ValueTask<GroundPickupResult> SettleAsync(CommandExecutionContext execution,GroundPickupInput input,CancellationToken token) =>
        workUnits.WriteAsync(execution,new("pick-up-ground-drop",[]),(unit,ct)=>PickUpAsync(input,ct),token);

    private Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> CaptureConnections(IEnumerable<SelectedSessionIdentity> members) =>
        members.ToDictionary(member=>member,member=>onlineUsers.TryGetByConnection(member.ConnectionId,out var user)==true && user is not null
            && user.AccountId==member.AccountId && user.CurrentCharacterId==member.CharacterId?user.Connection:null);
    private async ValueTask CloseFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session,Exception Error)> failures,
        Dictionary<SelectedSessionIdentity,NetworkConnectionContext?> captured)
    {
        foreach(var(session,error)in failures.DistinctBy(value=>value.Session))
        {
            logger.LogWarning(error,"Tower pickup projection failed for exact session {ConnectionId}",session.ConnectionId);
            if(sessions.IsInWorld(session)&&captured.GetValueOrDefault(session)is{}physical)
                await closures.RequestCloseAsync(physical,CancellationToken.None).ConfigureAwait(false);
        }
    }
    private async ValueTask<bool> PickupBossAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        GameMessageMetadata metadata,ushort groundUid,CancellationToken token)
    {
        // Boss lookup precedes stale; Illusion's stale gate below precedes lookup, even outside a tower.
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:bossTowers.Find(session);
        if(session is null||expected is null)return false;
        if(GameRequestFreshness.IsStale(metadata))return true;
        if(!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session,expected))return true;
        var members=expected.Members.Select(member=>member.Session).ToArray();var captured=CaptureConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member)with{Metadata=metadata}:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var picked=await workUnits.WriteAsync(execution,new("pick-up-boss-tower-item",participants),
            (unit,ct)=>bossTowers.PickupAsync(BossTowerActionProjection.BossTowerContext(expected,session),groundUid,ct),token).ConfigureAwait(false);
        if(!picked.Action.Changed||picked.Action.Run is not{}run)
        {
            if(picked.NoRoom)await outbox.RegisterExplicitAsync(connectionId,new([new GetItemResponse(false)],[]),token).ConfigureAwait(false);
            return true;
        }
        var item=run.GroundItems.Single(value=>value.Uid==groundUid);var failures=new List<(SelectedSessionIdentity,Exception)>();
        foreach(var member in BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, run))
        {
            try
            {
                var frames=new List<IGameServerMessage>();if(member==session)frames.Add(new GetItemResponse(true));
                if(item.Rolled.IsGold)frames.Add(new GroundGoldPickupNotification(groundUid,session.WireUserId,item.Rolled.Count));
                else
                {
                    var update=picked.Action.InventoryUpdates.FirstOrDefault();
                    frames.Add(new GroundPickupNotification(groundUid,session.WireUserId,update is null?(ushort)0:checked((ushort)slots.Project(update.Slot).Index)));
                }
                if(member==session)
                {
                    BossTowerActionProjection.BossTowerAppendInventory(frames,picked.Action.InventoryUpdates,clock.GetUtcNow());
                    BossTowerActionProjection.BossTowerAppendQuest(frames,picked.Action.QuestProgress);
                    if(picked.Action.GoldBalance is{}balance)frames.Add(GameInventoryDelivery.CarriedGoldNotification(balance,clock.GetUtcNow()));
                }
                await BossTowerActionProjection.BossTowerSendCommittedBatchAsync(sessions, bossTowers, outbox, run,member,new(frames,[]),token).ConfigureAwait(false);
            }
            catch(Exception error)when(error is not OperationCanceledException||!token.IsCancellationRequested){failures.Add((member,error));}
        }
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
    private async ValueTask<bool> PickupIllusionAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        GameMessageMetadata metadata,ushort groundUid,CancellationToken token)
    {
        if(GameRequestFreshness.IsStale(metadata))return true;
        var session=sessions.FindByConnection(connectionId);var expected=session is null?null:illusionTowers.Find(session);
        if(session is null||expected is null)return false;
        if(!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session,expected))return true;
        var captured=CaptureConnections(expected.Members.Select(member=>member.Session));
        var picked=await workUnits.WriteAsync(execution,new("pick-up-illusion-tower-item",[]),
            (unit,ct)=>illusionTowers.PickupAsync(IllusionTowerActionProjection.IllusionTowerContext(expected,session),groundUid,ct),token).ConfigureAwait(false);
        if(!picked.Action.Changed||picked.Action.Run is not{}run||picked.Update is not{}update)
        {
            if(picked.NoRoom)await outbox.RegisterExplicitAsync(connectionId,new([new GetItemResponse(false)],[]),token).ConfigureAwait(false);
            return true;
        }
        var failures=new List<(SelectedSessionIdentity,Exception)>();
        foreach(var member in IllusionTowerActionProjection.IllusionTowerExactMembers(sessions, illusionTowers, run))
        {
            try
            {
                var frames=new List<IGameServerMessage>();if(member==session)frames.Add(new GetItemResponse(true));
                frames.Add(new GroundPickupNotification(groundUid,session.WireUserId,checked((ushort)slots.Project(update.Slot).Index)));
                if(member==session)IllusionTowerActionProjection.IllusionTowerAppendInventory(frames,[update],clock.GetUtcNow());
                await IllusionTowerActionProjection.IllusionTowerSendCommittedBatchAsync(sessions, illusionTowers, outbox, run,member,new(frames,[]),token).ConfigureAwait(false);
            }
            catch(Exception error)when(error is not OperationCanceledException||!token.IsCancellationRequested){failures.Add((member,error));}
        }
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);return true;
    }
}
