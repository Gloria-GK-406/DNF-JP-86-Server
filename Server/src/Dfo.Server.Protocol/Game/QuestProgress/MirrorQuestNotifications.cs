using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.QuestProgress;

internal sealed class QuestProgressViewChangedHandler(IQuestQueryDomainService quests,
    ISelectedSessionDomainService sessions) : INotificationHandler<QuestProgressViewChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        QuestProgressViewChanged notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.CharacterId) return [];
        var session = sessions.FindByCharacter(recipient.CharacterId);
        if (session is null || session.ConnectionId != recipient.ConnectionId) return [];
        var accepted = await quests.ReadAcceptedAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        return [new AcceptedQuestProgressMessage([.. accepted.Select(x =>
            new AcceptedQuestProgressState(checked((ushort)x.Quest.QuestId), unchecked((uint)x.Quest.TriggerValue)))])];
    }
}

internal sealed class MirrorQuestViewChangedHandler(IMirrorAradQuestDomainService mirror,
    ISelectedSessionDomainService sessions) : INotificationHandler<MirrorQuestViewChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        MirrorQuestViewChanged notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.CharacterId) return [];
        var session = sessions.FindByCharacter(recipient.CharacterId);
        if (session is null || session.ConnectionId != recipient.ConnectionId) return [];
        // Read the current run here. A delayed cleanup hint for the previous run must
        // never carry an empty snapshot over the next run's accepted task.
        var snapshot = await mirror.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        return [new AcceptedQuestProgressMessage([.. snapshot.Quests.OrderBy(x => x.Slot)
            .Select(x => new AcceptedQuestProgressState(checked((ushort)x.QuestId), unchecked((uint)x.TriggerValue)))])];
    }
}
