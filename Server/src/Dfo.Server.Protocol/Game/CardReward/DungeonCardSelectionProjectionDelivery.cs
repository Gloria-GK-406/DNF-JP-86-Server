using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>Projects one committed shared card snapshot and the actor's private durable deltas.</summary>
public sealed class DungeonCardSelectionProjectionDelivery(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    DungeonPartyRecipients? partyRecipients = null)
{
    public async ValueTask<int> DeliverAsync(
        SelectedSessionIdentity actor,
        SelectCardResult result,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(result);
        var change = result.Change
            ?? throw new ArgumentException(
                "A successful card projection requires its committed change.", nameof(result));

        var recipients = change.Recipients.ToHashSet();
        var targets = (partyRecipients?.Find(actor, change.RunId)
                ?? deliveries.Find(change.RunId))
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => selectedSessions.FindByCharacter(frozen.CharacterId) == frozen)
            .Where(frozen => runs.Find(frozen.CharacterId) is { } run
                && run.RunId == change.RunId)
            .ToArray();
        if (targets.Length == 0)
            return 0;

        var shared = new SelectCardResponse(change.Selections);
        var actorFrames = new List<IGameServerMessage> { shared };
        if (result.Delivered is { } delivered)
            actorFrames.Add(GameInventoryDelivery.Notification(delivered));
        if (result.GoldPaid > 0 || result.GoldCharged > 0)
        {
            actorFrames.Add(GameInventoryDelivery.CarriedGoldNotification(
                result.GoldCarried, preparedAt));
        }

        IReadOnlyList<IGameServerMessage> required = targets.Any(target => target == actor)
            ? actorFrames
            : [];
        var secondary = targets
            .Where(target => target != actor)
            .Select(target => new GameExplicitMessageTarget(target.ConnectionId, [shared]))
            .ToArray();
        await sender.SendExplicitAsync(
            actor.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);
        return targets.Length;
    }
}
