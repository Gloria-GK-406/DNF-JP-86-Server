using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
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
    IGuildOverviewDomainService overviews, IGuildProfileDomainService profiles,
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
                (_, token) => profiles.SetRecommendedChannelAsync(actor.CharacterId, channel.ChannelId, token), ct)
                .ConfigureAwait(false);
            await outbox.RegisterCmdAsync(connection.ConnectionId,
                new GuildSetChannelReply(status == GuildMutationStatus.Success ? null : (byte)0x13), ct).ConfigureAwait(false);
            if (status != GuildMutationStatus.Success)
                return false;
        }

        var panel = await units.ReadOnlyAsync(context, new("guild-panel", [recipient]),
            token => GuildInfoPanel.ReadAsync(overviews, actor.CharacterId, token), ct).ConfigureAwait(false);
        if (panel.FindMember(actor.CharacterId) is not { } member)
            return false;

        await outbox.RegisterNotiAsync(connection.ConnectionId, GuildMemberInfoMessage.From(member), ct)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(connection.ConnectionId, panel.CreateMessage(member), ct)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(connection.ConnectionId,
            new GuildHallInfoMessage(GuildHallTown.InitialEntranceStage), ct).ConfigureAwait(false);
        return true;
    }
}

internal sealed record GuildInfoPanel(GuildOverview? Overview) : INotification<GuildInfoPanel>
{
    public static GuildInfoPanel Empty { get; } = new((GuildOverview?)null);

    public Guild? Guild => Overview?.Guild;
    public IReadOnlyList<GuildMember> Members => Overview?.Members ?? [];
    public IReadOnlyList<GuildPurchasedContent> Contents => Overview?.Contents ?? [];
    public GuildAlliancePeer? Ally => Overview?.Ally;

    public static async ValueTask<GuildInfoPanel> ReadAsync(IGuildOverviewDomainService overviews,
        CharacterId viewer, CancellationToken ct) =>
        await overviews.ReadAsync(viewer, ct).ConfigureAwait(false) is { } overview ? new(overview) : Empty;

    public GuildMember? FindMember(CharacterId character) => Members.FirstOrDefault(member => member.Character == character);

    public GuildInfoMessage CreateMessage(GuildMember member)
    {
        var overview = Overview ?? throw new InvalidOperationException("An empty guild panel has no message.");
        return new(overview.Guild, member.IsLeader, overview.Members.Count, overview.AttendanceCount, overview.Ranks,
            overview.Contents, overview.Ally, overview.Contributions.Guild,
            overview.Contributions.Members.GetValueOrDefault(member.Character));
    }
}

internal sealed class GuildInfoPanelHandler : INotificationHandler<GuildInfoPanel>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildInfoPanel panel, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            panel.FindMember(recipient.CharacterId) is { } member
                ? [panel.CreateMessage(member), new GuildHallInfoMessage(GuildHallTown.InitialEntranceStage)]
                : []);
}
