using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
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
    DungeonPartyRecipients? partyRecipients = null,
    IQuestQueryDomainService? quests = null)
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
            .Where(frozen => selectedSessions.IsInWorld(frozen))
            .Where(frozen => runs.Find(frozen.CharacterId) is { } run
                && run.RunId == change.RunId)
            .ToArray();
        if (targets.Length == 0)
            return 0;

        var projected = new List<GameExplicitMessageTarget>();
        foreach (var target in targets)
        {
            var run = runs.Find(target.CharacterId);
            List<IGameServerMessage> frames =
            [new EnableClearDungeonNotification(), new SecretShopNpcNotification(GameProtocol.SecretShopNpcId)];
            if (CycleQuestRechallengeProjection.IsCycleClear(run))
            {
                // The map-clear subscriber is asynchronous. Resynchronize an already-ready
                // task after the kill frames, which may contain an older trigger snapshot.
                if (quests is not null)
                {
                    var accepted = await quests.ReadAcceptedAsync(target.AccountId, target.CharacterId,
                        cancellationToken).ConfigureAwait(false);
                    if (CycleQuestRechallengeProjection.IsReady(run, accepted))
                        frames.Add(CycleQuestRechallengeProjection.Progress(accepted));
                }
                frames.Add(CycleQuestRechallengeProjection.Check());
            }
            // A read above may yield across logout, connection replacement, or a new run.
            if (selectedSessions.IsInWorld(target)
                && runs.Find(target.CharacterId)?.RunId == change.RunId)
                projected.Add(new(target.ConnectionId, frames));
        }
        var required = projected.FirstOrDefault(target => target.ConnectionId == requester.ConnectionId)
            ?.OrderedMessages ?? [];
        var secondary = projected.Where(target => target.ConnectionId != requester.ConnectionId).ToArray();
        await sender.SendExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);
        return projected.Count;
    }
}
