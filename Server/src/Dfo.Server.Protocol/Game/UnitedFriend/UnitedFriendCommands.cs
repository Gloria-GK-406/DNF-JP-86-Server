using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UnitedFriend;

public sealed record GameAddFriendCommand(NetworkDispatchContext Dispatch, AddFriendRequest Input) : IProtocolCommand<AddFriendResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Adds the named character as a friend; the friend, online or not, is told nothing.</summary>
/// <remarks>
/// Every request is answered. On success the requester then gets the whole list and its blacklist,
/// read after the write committed: the client's reply handler changes nothing, so the list it shows
/// is only ever the last full list it was sent. Which refusals there are is the relation service's
/// rule; this resolves the name and turns outcomes into the client's reasons.
/// </remarks>
internal sealed class GameAddFriendCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterIdentityQueryDomainService identities,
    ICharacterRelationDomainService relations,
    ICharacterRelationQueryDomainService relationQuery,
    IChannelEntryDomainService channels,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    TimeProvider clock,
    ILogger<GameAddFriendCommandHandler> logger) : ICommandHandler<GameAddFriendCommand, AddFriendResult>
{
    public async ValueTask<AddFriendResult> HandleAsync(CommandExecutionContext execution, GameAddFriendCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var (result, recipient) = await DecideAsync(execution, command, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, new AddFriendResponse(result), ct).ConfigureAwait(false);
        if (result == AddFriendResult.Added)
            await UnitedFriendRefresh.SendAsync(execution, recipient!, relationQuery, identities, sessions, channels, units, outbox,
                clock.LocalTimeZone, logger, ct).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<(AddFriendResult, SelectedSessionRecipient?)> DecideAsync(
        CommandExecutionContext execution, GameAddFriendCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
            return (Refused(connection.ConnectionId, null, "NotInWorld"), null);
        if (command.Input.Target is not { } named)
            return (Refused(connection.ConnectionId, requester, "Malformed"), null);
        if (named.Server != FriendServerGroup.Own)
        {
            logger.LogInformation("Friend add refused character={Character}: server group {Server}", requester.CharacterId.Value, named.Server);
            return (AddFriendResult.OtherServerGroup, null);
        }

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var lookup = await units.ReadOnlyAsync(execution, new("FriendTarget", [recipient]),
            async token => new FriendLookup(await identities.FindByNameAsync(named.Name, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (lookup.Identity is not { } target)
            return (Answer(requester, null, AddFriendResult.NoSuchCharacter), null);

        var outcome = await units.WriteAsync(execution, new("AddFriend", [recipient]),
            (_, token) => relations.AddAsync(requester.CharacterId, target.Character, CharacterRelationKind.Friend, token), ct).ConfigureAwait(false);
        return (Answer(requester, target, UnitedFriendPayloadCodec.ResultOf(outcome)), recipient);
    }

    private AddFriendResult Answer(SelectedSessionIdentity requester, CharacterPublicIdentity? target, AddFriendResult result)
    {
        logger.LogInformation("Friend add character={Character} target={Target}: {Result}",
            requester.CharacterId.Value, target?.Character.Value, result);
        return result;
    }

    private AddFriendResult Refused(NetworkConnectionId connection, SelectedSessionIdentity? requester, string reason)
    {
        logger.LogInformation("Friend add refused connection={Connection} character={Character}: {Reason}",
            connection, requester?.CharacterId.Value, reason);
        return AddFriendResult.Refused;
    }
}

public sealed record GameDeleteFriendCommand(NetworkDispatchContext Dispatch, DeleteFriendRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Removes the named friend; only the requester's side of the friendship goes.</summary>
/// <remarks>
/// Every request is answered, and every refusal is 0x15: it is the only reason the client shows,
/// and its text -- the list is refreshed on reconnect -- is true of each of them (Spec AM-1). On
/// success the requester gets the removal, then the whole list and its blacklist.
/// </remarks>
internal sealed class GameDeleteFriendCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterIdentityQueryDomainService identities,
    ICharacterRelationDomainService relations,
    ICharacterRelationQueryDomainService relationQuery,
    IChannelEntryDomainService channels,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    TimeProvider clock,
    ILogger<GameDeleteFriendCommandHandler> logger) : ICommandHandler<GameDeleteFriendCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameDeleteFriendCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var removal = await DecideAsync(execution, command, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, new DeleteFriendResponse(removal is not null), ct).ConfigureAwait(false);
        if (removal is not { } removed) return false;

        await outbox.RegisterNotiAsync(connection.ConnectionId, new FriendRemovedMessage([removed.Name]), ct).ConfigureAwait(false);
        await UnitedFriendRefresh.SendAsync(execution, removed.Recipient, relationQuery, identities, sessions, channels, units, outbox,
            clock.LocalTimeZone, logger, ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<FriendRemoval?> DecideAsync(CommandExecutionContext execution, GameDeleteFriendCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
            return NotRemoved(connection.ConnectionId, null, "NotInWorld");
        if (command.Input.Target is not { } named)
            return NotRemoved(connection.ConnectionId, requester, "Malformed");
        if (named.Server != FriendServerGroup.Own)
            return NotRemoved(connection.ConnectionId, requester, $"ServerGroup{named.Server}");

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var lookup = await units.ReadOnlyAsync(execution, new("FriendTarget", [recipient]),
            async token => new FriendLookup(await identities.FindByNameAsync(named.Name, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (lookup.Identity is not { } target)
            return NotRemoved(connection.ConnectionId, requester, "NoSuchCharacter");

        var outcome = await units.WriteAsync(execution, new("DeleteFriend", [recipient]),
            (_, token) => relations.RemoveAsync(requester.CharacterId, target.Character, CharacterRelationKind.Friend, token), ct).ConfigureAwait(false);
        if (outcome != CharacterRelationRemoveOutcome.Removed)
            return NotRemoved(connection.ConnectionId, requester, "NotListed");

        logger.LogInformation("Friend removed character={Character} target={Target}", requester.CharacterId.Value, target.Character.Value);
        // The name the client holds is the one it asked with, which is the target's: found by exact name.
        return new(target.Name, recipient);
    }

    private FriendRemoval? NotRemoved(NetworkConnectionId connection, SelectedSessionIdentity? requester, string reason)
    {
        logger.LogInformation("Friend delete refused connection={Connection} character={Character}: {Reason}",
            connection, requester?.CharacterId.Value, reason);
        return null;
    }

    private sealed record FriendRemoval(byte[] Name, SelectedSessionRecipient Recipient);
}

/// <summary>The character a request named, or none; a work unit's result cannot itself be null.</summary>
internal sealed record FriendLookup(CharacterPublicIdentity? Identity);

/// <summary>What both commands send after a change: the requester's full list, then its blacklist.</summary>
internal static class UnitedFriendRefresh
{
    /// <remarks>
    /// Read in its own read-only unit after the change committed, so the list shows the committed
    /// relation and, for each friend, where that friend is at this moment.
    /// </remarks>
    public static async ValueTask SendAsync(CommandExecutionContext execution, SelectedSessionRecipient recipient,
        ICharacterRelationQueryDomainService relations, ICharacterIdentityQueryDomainService identities,
        ISelectedSessionDomainService sessions, IChannelEntryDomainService channels, ICommandWorkUnits units,
        IGameCommandOutbox outbox, TimeZoneInfo zone, ILogger logger, CancellationToken ct)
    {
        var owner = recipient.Session.CharacterId;
        var refresh = await units.ReadOnlyAsync(execution, new("FriendList", [recipient]),
            token => FriendListRefresh.BuildAsync(owner, relations, identities, sessions, channels, zone, token), ct).ConfigureAwait(false);
        foreach (var skipped in refresh.SkippedFriends)
            logger.LogWarning("Friend entry left out character={Character} target={Target}: NOTI 0x0111 cannot carry it ({NameBytes}-byte name, level {Level}, grow {Grow}, or a repeated name)",
                owner.Value, skipped.Character.Value, skipped.Name.Length, skipped.Level, skipped.GrowType);
        foreach (var skipped in refresh.SkippedBlacklist)
            logger.LogWarning("Blacklist entry left out character={Character} target={Target}: its {NameBytes}-byte name does not fit CMD 0x0078",
                owner.Value, skipped.Character.Value, skipped.Name.Length);
        await refresh.RegisterAsync(outbox, recipient.Session.ConnectionId, ct).ConfigureAwait(false);
    }
}

public sealed class AddFriendHandler(ICommandBus commands) : ClientMessageHandler<AddFriendRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameAddFriendCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AddFriendRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAddFriendCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class DeleteFriendHandler(ICommandBus commands) : ClientMessageHandler<DeleteFriendRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameDeleteFriendCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DeleteFriendRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDeleteFriendCommand(context, message), ct).ConfigureAwait(false);
}
