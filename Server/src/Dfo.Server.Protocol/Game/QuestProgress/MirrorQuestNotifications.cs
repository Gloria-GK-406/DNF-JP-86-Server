using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.QuestProgress;

internal sealed class QuestProgressViewChangedHandler(IQuestQueryDomainService quests,
    ISelectedSessionDomainService sessions, IDungeonRunQueryDomainService runs) : INotificationHandler<QuestProgressViewChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        QuestProgressViewChanged notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.CharacterId) return [];
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId) return [];
        var expectedRunId = runs?.Find(session.CharacterId)?.RunId;
        var accepted = await quests.ReadAcceptedAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        if (!sessions.IsInWorld(session)) return [];
        var currentRun = runs?.Find(session.CharacterId);
        if (currentRun?.RunId != expectedRunId) return [];
        // A clear-map/inventory event can finish after the initial clear was projected.
        // Offer the native check again only while this run's bound task is still accepted/ready.
        var ready = CycleQuestRechallengeProjection.ReadyFrames(quests, currentRun, accepted);
        return ready.Count > 0 ? [.. ready] : [CycleQuestRechallengeProjection.Progress(accepted)];
    }
}

internal sealed class MirrorQuestViewChangedHandler(IMirrorAradQuestDomainService mirror,
    ISelectedSessionDomainService sessions) : INotificationHandler<MirrorQuestViewChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        MirrorQuestViewChanged notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.CharacterId) return [];
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId) return [];
        // Read the current run here. A delayed cleanup hint for the previous run must
        // never carry an empty snapshot over the next run's accepted task.
        var snapshot = await mirror.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        return [new AcceptedQuestProgressMessage([.. snapshot.Quests.OrderBy(x => x.Slot)
            .Select(x => new AcceptedQuestProgressState(checked((ushort)x.QuestId), unchecked((uint)x.TriggerValue)))])];
    }
}
