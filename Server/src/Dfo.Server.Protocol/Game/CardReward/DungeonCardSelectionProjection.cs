using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>One shared projection of a committed card snapshot and the actor's private deltas.</summary>
internal static class DungeonCardSelectionProjection
{
    internal static GameExplicitMessageDelivery? CreateDelivery(ISelectedSessionDomainService selectedSessions,
        IDungeonRunQueryDomainService runs, IReadOnlyList<SelectedSessionIdentity> candidates, SelectedSessionIdentity actor, SelectCardResult result, DateTimeOffset preparedAt, out int recipientCount)
    {
        recipientCount = 0;
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(result);
        var change = result.Change
            ?? throw new ArgumentException(
                "A successful card projection requires its committed change.", nameof(result));

        var recipients = change.Recipients.ToHashSet();
        var targets = candidates
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => selectedSessions.IsInWorld(frozen))
            .Where(frozen => runs.Find(frozen.CharacterId) is { } run
                && run.RunId == change.RunId)
            .ToArray();
        if (targets.Length == 0)
            return null;

        // A private confirmation has no shared board yet. Only the last free-card confirmation
        // carries the complete, party-ordered ownership snapshot.
        SelectCardResponse? shared = change.Selections.Count > 0
            ? new SelectCardResponse(change.Selections)
            : null;
        var actorFrames = new List<IGameServerMessage>();
        if (shared is not null)
            actorFrames.Add(shared);
        if (result.Delivered is { } delivered)
            actorFrames.Add(GameInventoryDelivery.Notification(delivered));
        if (result.AccountMaterial is { } accountMaterial)
        {
            actorFrames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [new InventoryRuntimeValueState(
                    accountMaterial.SlotIndex,
                    accountMaterial.ItemId,
                    accountMaterial.Value)],
                preparedAt));
        }
        if (result.BlackDiamondDelivered is { } bonusDelivery)
            actorFrames.Add(GameInventoryDelivery.Notification(bonusDelivery));
        if (result.BlackDiamondAccountMaterial is { } bonusMaterial)
            actorFrames.Add(GameInventoryDelivery.VirtualResourceNotification(
                [new InventoryRuntimeValueState(bonusMaterial.SlotIndex, bonusMaterial.ItemId,
                    bonusMaterial.Value)], preparedAt));
        if (result.GoldPaid > 0 || result.GoldCharged > 0)
        {
            actorFrames.Add(GameInventoryDelivery.CarriedGoldNotification(
                result.GoldCarried, preparedAt));
        }

        actorFrames.Add(new PremiumServiceStateMessage(result.Entitlements, result.Usage, preparedAt));

        IReadOnlyList<IGameServerMessage> required = targets.Any(target => target == actor)
            ? actorFrames
            : [];
        var secondary = targets
            .Where(target => target != actor)
            .Where(_ => shared is not null)
            .Select(target => new GameExplicitMessageTarget(target.ConnectionId, [shared!]))
            .ToArray();
        if (required.Count == 0 && secondary.Length == 0)
            return null;
        recipientCount = targets.Length;
        return new GameExplicitMessageDelivery(required, secondary);
    }
}
