using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Game.DungeonClear;

/// <summary>Projects one committed shared clear fact onto current frozen transport generations.</summary>
public sealed class DungeonClearProjectionDelivery(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    DungeonPartyRecipients? partyRecipients = null)
{
    public async ValueTask<int> DeliverAsync(
        SelectedSessionIdentity requester,
        DungeonClearChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(change);
        var recipients = change.Recipients.ToHashSet();
        var targets = (partyRecipients?.Find(requester, change.RunId)
                ?? deliveries.Find(change.RunId))
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => selectedSessions.FindByCharacter(frozen.CharacterId) == frozen)
            .Where(frozen => runs.Find(frozen.CharacterId) is { } run
                && run.RunId == change.RunId)
            .ToArray();
        if (targets.Length == 0)
            return 0;

        IGameServerMessage[] frames =
        [
            new EnableClearDungeonNotification(),
            new SecretShopNpcNotification(GameProtocol.SecretShopNpcId),
        ];
        var required = targets.Any(target => target == requester) ? frames : [];
        var secondary = targets
            .Where(target => target != requester)
            .Select(target => new GameExplicitMessageTarget(target.ConnectionId, frames))
            .ToArray();
        await sender.SendExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);
        return targets.Length;
    }
}
