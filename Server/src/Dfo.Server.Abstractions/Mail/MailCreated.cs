using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Mail;

/// <summary>One persisted letter, independent of whether its recipient is online.</summary>
public sealed record MailCreated(long MailId, CharacterId Recipient) : IDomainEvent;
