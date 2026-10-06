using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Mailbox;

public sealed class MailDeliveredHandler(IMailboxQueryDomainService mailbox) : INotificationHandler<MailDelivered>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, MailDelivered notice, CancellationToken token)
    {
        if (recipient.CharacterId != notice.Character) return [];
        var current = await mailbox.PageAsync(notice.Character, 1, token);
        return [new MailboxAlarm((ushort)Math.Clamp(current.TotalCount, 0, ushort.MaxValue))];
    }
}
