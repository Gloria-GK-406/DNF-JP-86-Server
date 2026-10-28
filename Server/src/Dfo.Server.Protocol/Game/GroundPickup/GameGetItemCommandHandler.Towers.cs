using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.GroundPickup;

// Tower channels: the caller has already dropped a stale request and located the requester in this tower.
internal sealed partial class GameGetItemCommandHandler
{
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
    private async ValueTask PickupBossAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        DungeonPlace.InBossTower place,GameMessageMetadata metadata,ushort groundUid,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session,expected))return;
        var members=expected.Members.Select(member=>member.Session).ToArray();var captured=CaptureConnections(members);
        var participants=members.Select(member=>member==session?CaptureParticipant(member)with{Metadata=metadata}:new SelectedSessionRecipient(member,null,metadata)).ToArray();
        var picked=await workUnits.WriteAsync(execution,new("pick-up-boss-tower-item",participants),
            (unit,ct)=>bossTowers.PickupAsync(BossTowerActionProjection.BossTowerContext(expected,session),groundUid,ct),token).ConfigureAwait(false);
        if(!picked.Action.Changed||picked.Action.Run is not{}run)
        {
            if(picked.NoRoom)await outbox.RegisterExplicitAsync(connectionId,new([new GetItemResponse(false)],[]),token).ConfigureAwait(false);
            return;
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
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);
    }
    private async ValueTask PickupIllusionAsync(CommandExecutionContext execution,NetworkConnectionId connectionId,
        DungeonPlace.InIllusionTower place,ushort groundUid,CancellationToken token)
    {
        var session=place.Session;var expected=place.Run;
        if(!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session,expected))return;
        var captured=CaptureConnections(expected.Members.Select(member=>member.Session));
        var picked=await workUnits.WriteAsync(execution,new("pick-up-illusion-tower-item",[]),
            (unit,ct)=>illusionTowers.PickupAsync(IllusionTowerActionProjection.IllusionTowerContext(expected,session),groundUid,ct),token).ConfigureAwait(false);
        if(!picked.Action.Changed||picked.Action.Run is not{}run||picked.Update is not{}update)
        {
            if(picked.NoRoom)await outbox.RegisterExplicitAsync(connectionId,new([new GetItemResponse(false)],[]),token).ConfigureAwait(false);
            return;
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
        await CloseFailuresAsync(failures,captured).ConfigureAwait(false);
    }
}
