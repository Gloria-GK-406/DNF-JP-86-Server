using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.QuestProgress;

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
