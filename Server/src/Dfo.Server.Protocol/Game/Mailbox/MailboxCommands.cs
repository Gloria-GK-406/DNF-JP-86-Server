using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Mail;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.Mailbox;

public enum MailboxOperation { Open, Claim, Read, Delete, Query, Send }
public sealed record MailboxCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, MailboxOperation Operation,
    long Id = 0, byte[]? Name = null, PlayerMailRequest? Send = null) : ICommand<MailboxResult>, IOnlineUserCommand, IAccountCommand
{ public AccountId AccountId => Session.AccountId; }
public sealed record MailboxResult(CharacterMailboxEntryState? Inbox = null, MailboxAssetProjection? Assets = null, MailRecipient? Recipient = null);
public sealed class MailboxCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IMailboxDomainService mailbox, IMailboxQueryDomainService query, IMailboxAssetDomainService assets, IMailboxSendDomainService sending, ICharacterDomainService characters) : ICommandHandler<MailboxCommand, MailboxResult>
{
    public async ValueTask<MailboxResult> HandleAsync(CommandExecutionContext context, MailboxCommand command, CancellationToken token)
    {
        var s = command.Session;
        if (!sessions.IsInWorld(s)
            || !users.TryGetByConnection(s.ConnectionId, out var user) || user?.OnlineUserId != command.OnlineUserId)
            throw new MailException(MailFailure.NotOwned, "Stale mail session.");
        switch (command.Operation)
        {
            case MailboxOperation.Send:
                var sent = await sending.SendAsync(s.CharacterId, command.Send ?? throw new InvalidDataException("Missing mail request."), token);
                var projection = await assets.PrepareAsync(s.CharacterId, sent.Mail.Changes, token);
                // Always correct local send-state estimates, even when no gold is attached.
                projection = projection with { Gold = await characters.Get(s.CharacterId).GoldAsync(token) };
                return new(Assets: projection);
            case MailboxOperation.Query:
                var recipient = await query.FindRecipientAsync(command.Name ?? [], token);
                if (recipient?.CharacterId == s.CharacterId) throw new MailException(MailFailure.InvalidMail, "Cannot send to self.");
                return new(Recipient: recipient);
            case MailboxOperation.Claim:
                var claim = await mailbox.ClaimAsync(s.CharacterId, command.Id, token);
                return new(Assets: await assets.PrepareAsync(s.CharacterId, claim.Changes, token));
            case MailboxOperation.Read:
                await mailbox.ReadAsync(s.CharacterId, command.Id, token); return new();
            case MailboxOperation.Delete:
                await mailbox.DeleteAsync(s.CharacterId, command.Id, token); return new();
            default:
                return new(Inbox: MailboxProjection.Map(await query.ReadStorageAsync(s.CharacterId, token)));
        }
    }
}
