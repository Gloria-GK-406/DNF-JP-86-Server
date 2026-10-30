using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;

using Dfo.Server.Protocol.Game.DungeonClear;
namespace Dfo.Server.Protocol.Game;
internal static class DungeonClearCommandProjection
{
    internal static async ValueTask<int> DeliverAsync(ISelectedSessionDomainService selectedSessions, IDungeonRunQueryDomainService runs,
        IReadOnlyList<SelectedSessionIdentity> candidates, IQuestQueryDomainService quests,
        IGameCommandOutbox outbox,
        SelectedSessionIdentity requester, DungeonClearChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(change);
        var recipients = change.Recipients.ToHashSet();
        var targets = candidates
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
            List<IGameServerMessage> frames = run?.Layout.Mode is DungeonRunMode.DarkElfRuins
                ? [.. Dfo.Server.Protocol.Game.DarkElfRuins.RuinsProjection.ClearPreamble(run)]
                : [new EnableClearDungeonNotification(), new SecretShopNpcNotification(GameProtocol.SecretShopNpcId)];
            if (CycleQuestSubmission.IsCycleClear(run))
            {
                // The map-clear subscriber is asynchronous. Resynchronize an already-ready
                // task after the kill frames, which may contain an older trigger snapshot.
                {
                    var accepted = await quests.ReadAcceptedAsync(target.AccountId, target.CharacterId,
                        cancellationToken).ConfigureAwait(false);
                    if (CycleQuestSubmission.IsReady(run, accepted))
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
        await outbox.RegisterExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);
        return projected.Count;
    }
}
