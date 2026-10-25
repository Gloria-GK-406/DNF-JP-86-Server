using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Mail;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Mailbox;

public sealed record GameMailboxCommand(NetworkDispatchContext Dispatch, IClientNetworkMessage Message) : IProtocolCommand<GameMailboxOutcome>
{
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Message switch
    {
        MailboxSendRequest value => value.Metadata,
        MailboxClaimRequest value => value.Metadata,
        MailboxActionRequest value => value.Metadata,
        MailboxOpenRequest value => value.Metadata,
        MailboxQueryRequest value => value.Metadata,
        _ => throw new InvalidOperationException("Unknown mailbox message shape.")
    });
}

public sealed record GameMailboxOutcome(MailboxResult Result, IGameCmdMessage? Reply = null,
    MailFailure? Refusal = null, Exception? Failure = null);

public sealed class GameMailboxCommandHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    IMailboxDomainService mailbox, IMailboxQueryDomainService query, IMailboxAssetDomainService assets,
    IMailboxSendDomainService sending, ICharacterDomainService characters, ICommandWorkUnits units,
    ICommandProjections notifications, IGameCommandOutbox outbox, ILogger<GameMailboxCommandHandler> logger)
    : ICommandHandler<GameMailboxCommand, GameMailboxOutcome>
{
    public async ValueTask<GameMailboxOutcome> HandleAsync(CommandExecutionContext context, GameMailboxCommand command, CancellationToken token)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null || !sessions.IsInWorld(session)
            || !users.TryGetByConnection(connection, out var user) || user is null) return new(new());
        var operation = command.Message switch
        {
            MailboxSendRequest => MailboxOperation.Send,
            MailboxClaimRequest => MailboxOperation.Claim,
            MailboxActionRequest => MailboxOperation.Read,
            MailboxQueryRequest => MailboxOperation.Query,
            MailboxOpenRequest => MailboxOperation.Open,
            _ => throw new InvalidOperationException("Unknown mailbox operation.")
        };
        var wireId = command.Message switch { MailboxClaimRequest m => m.AttachmentId, MailboxActionRequest m => m.MailId, _ => 0u };
        var action = command.Message is MailboxActionRequest state ? state.Action : (ushort)0;
        var send = command.Message as MailboxSendRequest;
        MailboxResult result = new();
        Exception? failure = null;
        byte error = 0;
        if (operation == MailboxOperation.Read && action is not (0 or 2))
            failure = new MailException(MailFailure.InvalidMail, "Unsupported mail state action.");
        else
        {
            var settled = await units.WriteSelfSettledResultAsync(context, new("Mailbox", []), async (unit, ct) =>
            {
                // Current-session guards are outside the business-refusal catch.
                DemandCurrent();
                try
                {
                    switch (operation)
                    {
                        case MailboxOperation.Send:
                            var sent = await unit.InvokeSelfSettlingAsync(t => sending.SendAsync(session.CharacterId, send!.Mail, t), ct);
                            var projection = await assets.PrepareAsync(session.CharacterId, sent.Mail.Changes, ct);
                            return new MailboxResult(Assets: projection with { Gold = await characters.Get(session.CharacterId).GoldAsync(ct) });
                        case MailboxOperation.Claim:
                            var claim = await unit.InvokeSelfSettlingAsync(t => mailbox.ClaimAsync(session.CharacterId, MailboxIds.DecodeAttachment(wireId), t), ct);
                            return new MailboxResult(Assets: await assets.PrepareAsync(session.CharacterId, claim.Changes, ct));
                        case MailboxOperation.Read:
                            if (action == 0)
                                await unit.InvokeSelfSettlingAsync(async t => { await mailbox.DeleteAsync(session.CharacterId, wireId, t); return true; }, ct);
                            else
                                await unit.InvokeSelfSettlingAsync(t => mailbox.ReadAsync(session.CharacterId, wireId, t), ct);
                            return new MailboxResult();
                        case MailboxOperation.Query:
                            var recipient = await query.FindRecipientAsync(((MailboxQueryRequest)command.Message).Name, ct);
                            if (recipient?.CharacterId == session.CharacterId) throw new MailException(MailFailure.InvalidMail, "Cannot send to self.");
                            return new MailboxResult(Recipient: recipient);
                        default:
                            return new MailboxResult(Inbox: MailboxProjection.Map(await query.ReadStorageAsync(session.CharacterId, ct)));
                    }
                }
                catch (Exception cause) when (cause is not (CommandSelfSettlementPropagationException or OperationCanceledException)
                    && !TransactionOutcomeUncertainException.Contains(cause))
                {
                    // This guard itself is deliberately outside its own catch.
                    unit.RequireRollback(cause);
                    failure = cause;
                    return new MailboxResult();
                }
            }, token);
            switch (settled)
            {
                case CommandSelfSettledWriteResult<MailboxResult>.Returned returned: result = returned.Value; break;
                case CommandSelfSettledWriteResult<MailboxResult>.ActionRolledBack rollback: failure = rollback.Cause; break;
                case CommandSelfSettledWriteResult<MailboxResult>.CommitRolledBack rollback: failure = rollback.Cause; break;
                default: throw new InvalidOperationException("Unknown mailbox settlement result.");
            }
            if (failure is not null) DemandCurrent();
        }
        if (failure is MailException rejected)
        {
            error = (byte)(operation == MailboxOperation.Send ? rejected.Reason switch
            { MailFailure.CharacterNotFound => 8, MailFailure.InsufficientAssets => 10, MailFailure.InvalidMail => 7, _ => 3 }
                : rejected.Reason switch { MailFailure.NoCapacity => 4, MailFailure.GoldLimit => 10, MailFailure.InvalidMail when operation == MailboxOperation.Query => 7, _ => 21 });
            logger.LogInformation("Mailbox rejected character={Character} operation={Operation} id={Id} reason={Reason}", session.CharacterId, operation, wireId, rejected.Reason);
        }
        else if (failure is InvalidDataException) error = operation == MailboxOperation.Send ? (byte)3 : (byte)21;
        else if (failure is not null)
        {
            error = operation == MailboxOperation.Send ? (byte)3 : (byte)21;
            logger.LogError(failure, "Mailbox failed character={Character} operation={Operation}", session.CharacterId, operation);
        }
        if (operation == MailboxOperation.Query && result.Recipient is null && failure is null) error = 21;
        IGameCmdMessage reply = operation switch
        {
            MailboxOperation.Send => new MailboxSendResponse(send!.Multi, error),
            MailboxOperation.Claim => new MailboxClaimResponse(wireId, error),
            MailboxOperation.Read => new MailboxActionResponse(wireId, action, error),
            MailboxOperation.Query => new MailboxQueryResponse(result.Recipient, error),
            _ => new MailboxOpenResponse(checked((ushort)Math.Min(result.Inbox?.NotLoadedCount ?? 0, short.MaxValue)), error)
        };
        logger.LogInformation("Mailbox character={Character} operation={Operation} id={Id} action={Action} error={Error}", session.CharacterId, operation, wireId, action, error);
        if (error != 0) await outbox.RegisterCmdAsync(connection, reply, token);
        else await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new MailboxCompleted(session, reply, result), token);
        return new(result, reply, (failure as MailException)?.Reason, failure);

        void DemandCurrent()
        {
            if (!ReferenceEquals(((CommandRequestContext.ProtocolNonEffect)context.Request!).Dispatch.Connection, command.Dispatch.Connection)
                || sessions.FindByConnection(connection) != session || !sessions.IsInWorld(session)
                || !users.TryGetByConnection(connection, out var current) || current?.OnlineUserId != user.OnlineUserId
                || !ReferenceEquals(current.Connection, command.Dispatch.Connection))
                throw new MailException(MailFailure.NotOwned, "Stale mail session.");
        }
    }
}
