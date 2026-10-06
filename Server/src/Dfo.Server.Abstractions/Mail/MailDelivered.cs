using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Mail;

/// <summary>New mail has committed for this recipient; delivery is best effort.</summary>
public sealed record MailDelivered(CharacterId Character) : INotification<MailDelivered>;
