using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Mail;

public sealed record MailboxChanged(CharacterId Character) : INotification<MailboxChanged>;
