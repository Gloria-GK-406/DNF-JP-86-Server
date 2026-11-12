using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Chat;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.OneToOneChat;

public sealed record GameOpenConversationCommand(NetworkDispatchContext Dispatch, OpenConversationRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Opens a 1:1 conversation and tells both members, each of whom then opens its window.</summary>
/// <remarks>
/// Whatever does not open one -- no such character, one not in the world, oneself, a conversation
/// already going on -- is answered with nothing. The client shows nothing for a refusal other than
/// the blacklist one either, and answering a conversation already open would put a second window
/// up on both sides.
/// <para>
/// The blacklist one is the exception: when either side has blacklisted the other, only the
/// requester is answered, with result 77, and no conversation is opened. It is checked before the
/// open conversation is, so a blacklist added while a window is up still answers a new request.
/// </para>
/// </remarks>
internal sealed class GameOpenConversationCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterDomainService characters,
    IMailboxQueryDomainService directory,
    IChatConversationDomainService conversations,
    ICharacterRelationQueryDomainService relations,
    ICommandWorkUnits units,
    ICommandProjections projections,
    IGameCommandOutbox outbox,
    ILogger<GameOpenConversationCommandHandler> logger) : ICommandHandler<GameOpenConversationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameOpenConversationCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
            return false;
        if (!ChatPreSendChecks.Passes(requester))
            return Unopened(requester, "PreSendCheck");

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var members = await units.ReadOnlyAsync(execution, new("ConversationMembers", [recipient]),
            token => ReadMembersAsync(requester, command.Input.TargetName, token), ct).ConfigureAwait(false);
        if (members.Blacklisted)
        {
            await outbox.RegisterNotiAsync(requester.ConnectionId, new ConversationRefusedMessage(), ct).ConfigureAwait(false);
            return Unopened(requester, "Blacklisted");
        }
        if (members is not { Own: { } own, Peer: { } peer })
            return Unopened(requester, "NoSuchTarget");
        if (conversations.Open(own, peer) is not { } conversation)
            return Unopened(requester, "AlreadyOpen");

        await projections.ProjectAsync(new NotificationAudience.TheseSessions([own.Session, peer.Session]),
            new ChatConversationOpened(conversation), ct).ConfigureAwait(false);
        logger.LogInformation("Conversation opened id={Conversation} between {First} and {Second}",
            conversation.Id.Value, own.CharacterId, peer.CharacterId);
        return true;
    }

    private async ValueTask<OpeningMembers> ReadMembersAsync(
        SelectedSessionIdentity requester, byte[] targetName, CancellationToken ct)
    {
        if (await directory.FindRecipientAsync(targetName, ct).ConfigureAwait(false) is not { } target
            || target.CharacterId == requester.CharacterId
            || !sessions.TryGetInWorld(target.CharacterId, out var targetSession))
            return OpeningMembers.None;
        // Either direction refuses: the owner's side would drop every line of the other, which is no conversation.
        if (await relations.ExistsAsync(requester.CharacterId, target.CharacterId, CharacterRelationKind.Blacklist, ct).ConfigureAwait(false)
            || await relations.ExistsAsync(target.CharacterId, requester.CharacterId, CharacterRelationKind.Blacklist, ct).ConfigureAwait(false))
            return OpeningMembers.Refused;
        var ownName = await NameOfAsync(requester, ct).ConfigureAwait(false);
        var peerName = await NameOfAsync(targetSession, ct).ConfigureAwait(false);
        return ownName is null || peerName is null ? OpeningMembers.None : new(new(requester, ownName), new(targetSession, peerName));
    }

    // The names go out in every frame of the conversation; one the client cannot read is no conversation.
    private async ValueTask<byte[]?> NameOfAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        (await characters.Get(session.CharacterId).IdentityAsync(ct).ConfigureAwait(false))?.Name is { Length: > 0 and <= ConversationOpenedMessage.NameLimit } name
            ? name
            : null;

    private sealed record OpeningMembers(ChatConversationMember? Own, ChatConversationMember? Peer, bool Blacklisted = false)
    {
        public static OpeningMembers None { get; } = new(null, null);
        public static OpeningMembers Refused { get; } = new(null, null, true);
    }

    private bool Unopened(SelectedSessionIdentity requester, string reason)
    {
        logger.LogInformation("Conversation not opened character={Character} reason={Reason}", requester.CharacterId, reason);
        return false;
    }
}

public sealed record GameLeaveConversationCommand(NetworkDispatchContext Dispatch, LeaveConversationRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>A member closed its window: the conversation ends, and the other member is told.</summary>
internal sealed class GameLeaveConversationCommandHandler(
    ISelectedSessionDomainService sessions,
    IChatConversationDomainService conversations,
    ICommandProjections projections,
    ILogger<GameLeaveConversationCommandHandler> logger) : ICommandHandler<GameLeaveConversationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameLeaveConversationCommand command, CancellationToken ct)
    {
        if (sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } leaver || !sessions.IsInWorld(leaver))
            return false;
        if (conversations.Leave(command.Input.Conversation, leaver) is not { } departure)
            return false;
        logger.LogInformation("Conversation left id={Conversation} by {Character}", departure.Conversation.Value, leaver.CharacterId);
        // The window the other member opened is only on the session it opened it from.
        if (sessions.TryGetInWorld(departure.Remaining.CharacterId, out var remaining) && remaining == departure.Remaining.Session)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([remaining]),
                new ChatConversationLeft(departure), ct).ConfigureAwait(false);
        return true;
    }
}

public sealed record GameConversationUserStateCommand(NetworkDispatchContext Dispatch, ConversationUserStateReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>
/// The window reports its member state; there is no member-state display to update without the
/// group chat it belongs to, and the client waits for no answer.
/// </summary>
internal sealed class GameConversationUserStateCommandHandler(
    ILogger<GameConversationUserStateCommandHandler> logger) : ICommandHandler<GameConversationUserStateCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameConversationUserStateCommand command, CancellationToken ct)
    {
        logger.LogDebug("Conversation user state ignored connection={Connection}", command.Dispatch.Connection.ConnectionId);
        return ValueTask.FromResult(true);
    }
}

public sealed class OpenConversationHandler(ICommandBus commands) : ClientMessageHandler<OpenConversationRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameOpenConversationCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, OpenConversationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameOpenConversationCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class LeaveConversationHandler(ICommandBus commands) : ClientMessageHandler<LeaveConversationRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameLeaveConversationCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, LeaveConversationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameLeaveConversationCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class ConversationUserStateHandler(ICommandBus commands) : ClientMessageHandler<ConversationUserStateReport>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameConversationUserStateCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ConversationUserStateReport message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameConversationUserStateCommand(context, message), ct).ConfigureAwait(false);
}
