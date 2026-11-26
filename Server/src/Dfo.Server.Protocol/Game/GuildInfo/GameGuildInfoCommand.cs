using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildMemberInfo;

namespace Dfo.Server.Protocol.Game.GuildInfo;

public sealed record GameGuildInfoCommand(NetworkDispatchContext Dispatch, GuildInfoRequest Request) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildInfoCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildQueryDomainService query,
    IGuildDomainService guilds,
    IChannelEntryDomainService channels,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGuildInfoCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        var setting = request.MessageType == ClientMessageType.GameGuildSetRecommendedChannel;
        if (!request.IsEmpty || !sessions.TryGetInWorld(connection.ConnectionId, out var actor))
        {
            if (setting)
                await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildSetChannelReply(0x13), ct).ConfigureAwait(false);
            return false;
        }

        var recipient = new SelectedSessionRecipient(actor, connection, request.Metadata);
        if (setting)
        {
            var channel = await channels.ResolveAsync(actor.ListenerPort, ct).ConfigureAwait(false);
            if (channel.Kind != ChannelEntryKind.General || channel.ChannelId <= 0)
            {
                await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildSetChannelReply(0x07), ct).ConfigureAwait(false);
                return false;
            }

            var status = await units.WriteAsync(context, new("set-guild-channel", [recipient]),
                (_, token) => guilds.SetRecommendedChannelAsync(actor.CharacterId, channel.ChannelId, token), ct)
                .ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildSetChannelReply(status == GuildMutationStatus.Success ? null : (byte)0x13), ct).ConfigureAwait(false);
            if (status != GuildMutationStatus.Success)
                return false;
        }

        var panel = await units.ReadOnlyAsync<(GuildMembership? Membership, int Count)>(context, new("guild-panel", [recipient]), async token =>
        {
            if (await query.FindMembershipAsync(actor.CharacterId, token).ConfigureAwait(false) is not { } membership)
                return (null, 0);
            var members = await query.ReadRosterAsync(actor.CharacterId, token).ConfigureAwait(false);
            return (membership, members.Count);
        }, ct).ConfigureAwait(false);
        if (panel.Membership is not { } current)
            return false;

        await outbox.RegisterNotiAsync(connection.ConnectionId, GuildMemberInfoMessage.From(current.Member), ct)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(connection.ConnectionId,
            new GuildInfoMessage(current.Guild, current.Member.IsLeader, panel.Count), ct)
            .ConfigureAwait(false);
        return true;
    }
}
