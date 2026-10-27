using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Protocol.Game.Mailbox;

public enum MailboxOperation { Open, Claim, Read, Delete, Query, Send }
public sealed record MailboxResult(CharacterMailboxEntryState? Inbox = null, MailboxAssetProjection? Assets = null, MailRecipient? Recipient = null);
