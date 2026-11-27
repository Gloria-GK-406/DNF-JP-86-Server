using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildMemberInfo;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuildCreation;

public sealed record GameGuildCreationCommand(NetworkDispatchContext Dispatch, GuildCreationRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildCreationCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildNameOperationSessionDomainService drafts,
    IGuildMembershipDomainService guilds,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections,
    ICharacterVisibleSessionDomainService visibility,
    TimeProvider clock) : ICommandHandler<GameGuildCreationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildCreationCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        var replyType = (ServerMessageType)(int)request.MessageType;

        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return request.MessageType == ClientMessageType.GameGuildCancelCreation
                ? false : await ReplyAsync(0, ct).ConfigureAwait(false);

        switch (request.MessageType)
        {
            case ClientMessageType.GameGuildCheckName:
                if (GameRequestFreshness.IsStale(request.Metadata))
                    return false;
                var status = await drafts.StartAsync(actor, request.Text, ct).ConfigureAwait(false);
                return await ReplyAsync(NameError(status), ct).ConfigureAwait(false);

            case ClientMessageType.GameGuildCheckPromotion:
                return await ReplyAsync(drafts.SetPromotion(actor, request.Text) ? null : (byte)0x6C, ct)
                    .ConfigureAwait(false);

            case ClientMessageType.GameGuildRequestCreationPermit:
                if (request.Malformed || !drafts.Authorize(actor))
                    return await ReplyAsync(3, ct).ConfigureAwait(false);
                await ReplyAsync(null, ct).ConfigureAwait(false);
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GuildCreationPermit(), ct).ConfigureAwait(false);
                return true;

            case ClientMessageType.GameGuildCancelCreation:
                if (!request.Malformed)
                    drafts.Cancel(actor);
                return !request.Malformed;

            case ClientMessageType.GameGuildFinalizeCreation:
                if (request.Malformed || drafts.AuthorizedCreation(actor) is not { } authorization)
                    return await ReplyAsync(0, ct).ConfigureAwait(false);
                var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
                var result = await units.WriteAsync(context, new("create-guild", [recipient]),
                    (_, token) => guilds.CreateAsync(actor.CharacterId, authorization.Name, authorization.Promotion, token),
                    ct).ConfigureAwait(false);
                if (result is not { Status: GuildMutationStatus.Success, Guild: { } id, RemainingGold: { } gold })
                    return await ReplyAsync(0, ct).ConfigureAwait(false);
                drafts.Cancel(actor);
                await outbox.RegisterBatchAsync(connection, [GameInventoryDelivery.CarriedGoldNotification(gold, clock.GetUtcNow())], ct)
                    .ConfigureAwait(false);
                await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(actor)),
                    new CharacterUserStateRefresh(actor), ct).ConfigureAwait(false);
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GuildCreated(id, authorization.Name), ct)
                    .ConfigureAwait(false);
                await outbox.RegisterNotiAsync(connection.ConnectionId, new GuildMemberInfoMessage(GuildRank.Leader), ct)
                    .ConfigureAwait(false);
                return await ReplyAsync(null, ct).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unsupported guild creation command {request.MessageType}.");
        }

        async ValueTask<bool> ReplyAsync(byte? error, CancellationToken token)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildCreationReply(replyType, error), token)
                .ConfigureAwait(false);
            return !error.HasValue;
        }
    }

    private static byte? NameError(GuildMutationStatus status) => status switch
    {
        GuildMutationStatus.Success => null,
        GuildMutationStatus.AlreadyMember => 0x6A,
        GuildMutationStatus.NameTaken => 0x6B,
        _ => 0x6C,
    };
}
