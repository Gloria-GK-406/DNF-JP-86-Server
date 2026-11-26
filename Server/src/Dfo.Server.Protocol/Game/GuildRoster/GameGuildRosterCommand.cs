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
    IGuildQueryDomainService guilds,
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
        var leader = entries.FirstOrDefault(entry => entry.Membership.IsLeader);
        if (ownEntry is null || leader is null)
            return await RefuseAsync().ConfigureAwait(false);

        var channelByListener = new Dictionary<int, byte>();
        var rows = new List<GuildRosterRow>(entries.Count);
        foreach (var entry in entries)
        {
            var channel = byte.MaxValue;
            if (sessions.TryGetInWorld(entry.Membership.Character, out var online))
            {
                if (!channelByListener.TryGetValue(online.ListenerPort, out channel))
                {
                    var resolved = await channels.ResolveAsync(online.ListenerPort, ct).ConfigureAwait(false);
                    channel = resolved.ChannelId is > 0 and < byte.MaxValue ? (byte)resolved.ChannelId : byte.MaxValue;
                    channelByListener.Add(online.ListenerPort, channel);
                }
                if (!sessions.IsInWorld(online))
                    channel = byte.MaxValue;
            }
            rows.Add(new GuildRosterRow(entry, channel));
        }

        await outbox.RegisterNotiAsync(connection.ConnectionId, GuildMemberInfoMessage.From(ownEntry.Membership), ct)
            .ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new GuildRosterReply(replyType, new(ownEntry.Membership.Guild, leader.Name, rows)), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> RefuseAsync()
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildRosterReply(replyType), ct).ConfigureAwait(false);
            return false;
        }
    }
}
