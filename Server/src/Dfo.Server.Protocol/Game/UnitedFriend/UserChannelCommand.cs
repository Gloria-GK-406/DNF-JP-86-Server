using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UnitedFriend;

public sealed record GameRequestUserChannelCommand(NetworkDispatchContext Dispatch, UserChannelRequest Input) : IProtocolCommand<UserChannelOutcome>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Tells the requester which channel the named character is on; reads only, tells the target nothing.</summary>
/// <remarks>
/// Every request is answered: the client has no timeout. Anyone in the world can be asked about,
/// friend or not, because the client asks for every name whose menu it opens, and a friend list
/// already shows the channel of anyone one adds. The move itself is the client's: it finds the
/// channel in its own catalogue and connects there as from channel selection.
/// </remarks>
internal sealed class GameRequestUserChannelCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterIdentityQueryDomainService identities,
    IChannelEntryDomainService channels,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameRequestUserChannelCommandHandler> logger) : ICommandHandler<GameRequestUserChannelCommand, UserChannelOutcome>
{
    public async ValueTask<UserChannelOutcome> HandleAsync(CommandExecutionContext execution, GameRequestUserChannelCommand command, CancellationToken ct)
    {
        var response = await DecideAsync(execution, command, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, response, ct).ConfigureAwait(false);
        return response.Outcome;
    }

    private async ValueTask<UserChannelResponse> DecideAsync(
        CommandExecutionContext execution, GameRequestUserChannelCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester)
            return Refused(null, "NoSession");
        if (!sessions.IsInWorld(requester))
            return Refused(requester, "NotInWorld");
        if (command.Input.Target is not { } named)
            return Refused(requester, "Malformed");
        if (named.Server != FriendServerGroup.Own)
            return Refused(requester, $"ServerGroup{named.Server}");

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var lookup = await units.ReadOnlyAsync(execution, new("UserChannelTarget", [recipient]),
            async token => new FriendLookup(await identities.FindByNameAsync(named.Name, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (lookup.Identity is not { } target || !sessions.TryGetInWorld(target.Character, out var session))
        {
            logger.LogDebug("Channel query character={Character} target={Target}: not there",
                requester.CharacterId.Value, lookup.Identity?.Character.Value);
            return UserChannelResponse.NotThere;
        }

        var channel = await channels.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false);
        if (channel.ChannelId is < 0 or > byte.MaxValue)
            return Refused(requester, $"Channel{channel.ChannelId}");
        logger.LogDebug("Channel query character={Character} target={Target}: channel {Channel}",
            requester.CharacterId.Value, target.Character.Value, channel.ChannelId);
        return UserChannelResponse.Found(target.Name, (byte)channel.ChannelId);
    }

    private UserChannelResponse Refused(SelectedSessionIdentity? requester, string reason)
    {
        logger.LogInformation("Channel query refused character={Character}: {Reason}", requester?.CharacterId.Value, reason);
        return UserChannelResponse.Refused;
    }
}

public sealed class UserChannelHandler(ICommandBus commands) : ClientMessageHandler<UserChannelRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameRequestUserChannelCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, UserChannelRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRequestUserChannelCommand(context, message), ct).ConfigureAwait(false);
}
