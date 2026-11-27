using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildMemberInfo;

namespace Dfo.Server.Protocol.Game.GuildRoster;

public sealed record GameGuildRosterCommand(NetworkDispatchContext Dispatch, GuildRosterRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildRosterCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildMembershipQueryDomainService guilds,
    IChannelEntryDomainService channels,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildRosterCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildRosterCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        var replyType = request.MessageType == ClientMessageType.GameGuildAllMemberList
            ? ServerMessageType.GameGuildAllMemberList
            : ServerMessageType.GameGuildMemberList;
        if (!request.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var viewer))
            return await RefuseAsync().ConfigureAwait(false);

        var recipient = new SelectedSessionRecipient(viewer, connection, request.Metadata);
        var entries = await units.ReadOnlyAsync(context, new("guild-roster", [recipient]),
            token => guilds.ReadRosterEntriesAsync(viewer.CharacterId, token), ct).ConfigureAwait(false);
        var ownEntry = entries.FirstOrDefault(entry => entry.Membership.Character == viewer.CharacterId);
        if (ownEntry is null || await GuildRosterProjection.BuildAsync(entries, sessions, channels, ct)
                .ConfigureAwait(false) is not { } snapshot)
            return await RefuseAsync().ConfigureAwait(false);

        await outbox.RegisterNotiAsync(connection.ConnectionId, GuildMemberInfoMessage.From(ownEntry.Membership), ct)
            .ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new GuildRosterReply(replyType, snapshot), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RefuseAsync()
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildRosterReply(replyType), ct).ConfigureAwait(false);
            return false;
        }
    }
}
