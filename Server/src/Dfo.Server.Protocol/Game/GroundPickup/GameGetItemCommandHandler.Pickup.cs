using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GroundPickup;
internal sealed partial class GameGetItemCommandHandler
{
    internal async ValueTask<GroundPickupResult> PickUpAsync(
        GroundPickupInput command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var picked = await floor.PickUpAsync(new(command.AccountId, command.CharacterId,
            command.GroundDropNumber, command.ExpectedRunId, command.ExpectedRoomVersion), cancellationToken).ConfigureAwait(false);
        // The bag's own slot becomes the wire's, and a shared-material landing is reported as the
        // account's runtime value; neither is the domain's concern.
        return picked.Granted is { } granted
            ? new(picked.Status, picked.ItemId, ItemGrantWireProjection.Index(granted, picked.SlotIndex),
                Change: picked.Change, QuestProgress: picked.QuestProgress, PendingCommit: picked.PendingCommit,
                AccountMaterial: await ItemGrantWireProjection.SharedAsync(granted, cancellationToken).ConfigureAwait(false))
            : new(picked.Status, picked.ItemId, 0, picked.Amount, picked.Total, picked.Change,
                picked.QuestProgress, picked.PendingCommit, EpicPiece: picked.EpicPiece);
    }

    internal async ValueTask<bool> DeliverPickupAsync(
        SelectedSessionIdentity requester,
        GroundPickupResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(result);
        var change = result.CommittedChange;
        if (change is null || change.Picker != requester.CharacterId)
            return false;

        var recipients = change.Recipients.ToHashSet();
        var partyMembers = FindRecipients(requester, change.RunId);
        var frozenPicker = partyMembers
            .SingleOrDefault(frozen => frozen.CharacterId == change.Picker);
        if (!recipients.Contains(change.Picker)
            || frozenPicker is null
            || frozenPicker != requester
            || !sessions.IsInWorld(frozenPicker))
        {
            return false;
        }

        IGameServerMessage notification;
        IReadOnlyList<IGameServerMessage> required;
        switch (result.Status)
        {
            case GroundPickupStatus.Banked when change.Kind is DungeonPickupKind.Gold:
                notification = new GroundGoldPickupNotification(
                    change.Drop.Number, requester.WireUserId, result.Amount);
                var goldMessages = new List<IGameServerMessage>
                {
                    new GetItemResponse(Accepted: true),
                    notification,
                    GameInventoryDelivery.CarriedGoldNotification(
                        result.Total, clock.GetUtcNow()),
                };
                if (result.QuestProgress is { } goldProgress)
                    goldMessages.Add(QuestProgressMessage(goldProgress));
                required = goldMessages;
                break;

            case GroundPickupStatus.EpicPieceCollected when change.Kind is DungeonPickupKind.EpicPiece:
                notification = new GroundPickupNotification(change.Drop.Number, requester.WireUserId, 0);
                var epicMessages = new List<IGameServerMessage> { new GetItemResponse(Accepted: true), notification };
                if (result.EpicPiece is { Added: > 0 } added)
                    epicMessages.Add(new EpicBookStateMessage(1, [new(added.PieceId, added.Total)]));
                required = epicMessages;
                break;

            case GroundPickupStatus.PickedUp when change.Kind is DungeonPickupKind.Item:
                notification = new GroundPickupNotification(
                    change.Drop.Number,
                    requester.WireUserId,
                    checked((ushort)result.SlotIndex));
                var itemMessages = new List<IGameServerMessage>
                {
                    new GetItemResponse(Accepted: true),
                    notification,
                };
                if (result.AccountMaterial is { } accountMaterial)
                {
                    itemMessages.Add(GameInventoryDelivery.VirtualResourceNotification(
                        [new InventoryRuntimeValueState(
                            accountMaterial.SlotIndex,
                            accountMaterial.ItemId,
                            accountMaterial.Value)],
                        clock.GetUtcNow()));
                }
                if (result.QuestProgress is { } questProgress)
                    itemMessages.Add(QuestProgressMessage(questProgress));
                required = itemMessages;
                break;

            default:
                return false;
        }

        static AcceptedQuestProgressMessage QuestProgressMessage(
            QuestProgressSnapshot progress) =>
            new([.. progress.Quests
                .OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId),
                    unchecked((uint)quest.TriggerValue)))]);

        var secondary = partyMembers
            .Where(frozen => frozen.CharacterId != change.Picker)
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => sessions.IsInWorld(frozen))
            .Select(frozen => new GameExplicitMessageTarget(
                frozen.ConnectionId,
                [notification]))
            .ToArray();

        await outbox.RegisterExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    internal ValueTask RefuseInventoryFullAsync(
        SelectedSessionIdentity requester,
        CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery([new GetItemResponse(Accepted: false)], []),
            cancellationToken);


    internal static ValueTask<DungeonClearChange?> EvaluateClearAsync(IDungeonClearDomainService clearEvaluator,
        SelectedSessionIdentity requester, GroundPickupResult result)
    {
        var change = result.CommittedChange;
        return result.Status is GroundPickupStatus.PickedUp && result.QuestProgress is not null
            && change is not null && change.Picker == requester.CharacterId
                ? clearEvaluator.TryOpenClearAsync(requester.AccountId, requester.CharacterId,
                    change.RunId, change.RoomVersion, CancellationToken.None)
                : ValueTask.FromResult<DungeonClearChange?>(null);
    }

}
