using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuardianAltar;

internal static class GuardianAltarProjection
{
    public static async ValueTask RestoreInventoryAsync(Dfo.Server.Data.World.SelectedSessionIdentity session,
        ISelectedSessionDomainService sessions, ISelectedCharacterCurrentViewDomainService currentViews,
        ICharacterStateProjectionDomainService currentState, IGameCommandOutbox outbox, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session)) return;
        var view = await currentViews.TryLoadAsync(session, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Cannot restore altar inventory for {session.CharacterId}.");
        var restored = await currentState.RefreshAsync(view, ct).ConfigureAwait(false);
        if (!sessions.IsInWorld(session)) return;
        // The altar replaces the local item quickbar; rebuild it from current inventory before town entry.
        await outbox.RegisterExplicitAsync(session.ConnectionId, new([
            new InventoryFullSnapshotMessage(restored.CharacterInventory.Containers.Single(x => x.ContainerType == Dfo.Server.Data.Inventory.InventoryContainerType.Main),
                restored.CharacterInventory, restored.AccountInventory, restored.World.PreparedAt)], []), ct).ConfigureAwait(false);
    }
    public static IReadOnlyList<IGameServerMessage> InventoryFrames(ItemDeliveryReceipt? receipt, IInventorySlotProjectionDomainService slots, TimeProvider clock) => (receipt?.Inventory ?? [])
        .GroupBy(x => slots.Project(x.Slot).Space).Select(group => (IGameServerMessage)new InventoryDeltaMessage(group.Key,
            group.GroupBy(x => x.Slot).Select(x => { var last = x.Last(); return new Dfo.Server.Data.Inventory.InventoryItemState(
                null, slots.Project(last.Slot).Index, last.Snapshot.ToCore()); }).ToArray(), clock.GetUtcNow())).ToArray();
    public static async ValueTask PublishAsync(GuardianAltarActionResult action, IGuardianAltarDomainService altar, ISelectedSessionDomainService sessions,
        IDungeonRunQueryDomainService runs, IGameCommandOutbox outbox, IInventorySlotProjectionDomainService slots, TimeProvider clock, CancellationToken ct)
    {
        var state = action.State; var session = state.Session;
        if (!sessions.IsInWorld(session) || runs.Find(session.CharacterId)?.RunId != state.RunId
            || altar.Find(state.RunId)?.Generation != state.Generation) return;
        var frames = new List<IGameServerMessage>();
        if (action.RoundStarted)
            frames.Add(new GuardianAltarMapMessage(runs.Find(state.RunId)!, state.Admission));
        frames.AddRange(action.Spawns.Select(x => new GuardianAltarSpawnMessage(x.Sequence, altar.DescribeSpawn(x.Spawn))));
        if (action.StageEnded)
        {
            var progress = action.Progress ?? await altar.ReadProgressAsync(session.CharacterId, ct).ConfigureAwait(false);
            if (state.Status is GuardianAltarStatus.Won or GuardianAltarStatus.BetweenRounds)
                frames.Add(new GuardianAltarResultMessage(action, altar.DescribeProgress(progress)));
            if (state.Status != GuardianAltarStatus.BetweenRounds) frames.Add(new GuardianAltarEplpMessage(state, progress.FreeTickets + progress.ExtraTickets > 0));
        }
        frames.AddRange(InventoryFrames(action.Delivery, slots, clock));
        if (frames.Count > 0) await outbox.RegisterExplicitAsync(session.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
    }
}
