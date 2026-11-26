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
    IGuildQueryDomainService query,
    IGuildProgressDomainService progress,
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

        var panel = await units.ReadOnlyAsync(context, new("guild-panel", [recipient]),
            token => GuildInfoPanel.ReadAsync(query, progress, actor.CharacterId, token), ct).ConfigureAwait(false);
        if (panel.FindMember(actor.CharacterId) is not { } member)
            return false;

        await outbox.RegisterNotiAsync(connection.ConnectionId, GuildMemberInfoMessage.From(member), ct)
            .ConfigureAwait(false);
        await outbox.RegisterNotiAsync(connection.ConnectionId, panel.CreateMessage(member), ct)
            .ConfigureAwait(false);
        return true;
    }
}

internal sealed record GuildInfoPanel(
    Guild? Guild, IReadOnlyList<GuildMember> Members, int AttendanceCount, IReadOnlyList<GuildRankSetting> Ranks)
    : INotification<GuildInfoPanel>
{
    public static GuildInfoPanel Empty { get; } = new(null, [], 0, GuildRankSettings.Defaults);

    public GuildMember? FindMember(CharacterId character) => Members.FirstOrDefault(member => member.Character == character);

    public GuildInfoMessage CreateMessage(GuildMember member) =>
        new(Guild ?? throw new InvalidOperationException("An empty guild panel has no message."),
            member.IsLeader, Members.Count, member.DonationPoints, AttendanceCount, Ranks);

    public static async ValueTask<GuildInfoPanel> ReadAsync(
        IGuildQueryDomainService query, IGuildProgressDomainService progress, CharacterId viewer, CancellationToken ct)
    {
        if (await query.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is not { } membership)
            return Empty;

        var members = await query.ReadRosterAsync(viewer, ct).ConfigureAwait(false);
        var attended = await progress.ReadAttendanceTodayAsync(viewer, ct).ConfigureAwait(false);
        var ranks = await query.ReadRankSettingsAsync(membership.Guild.Id, ct).ConfigureAwait(false);
        return new(membership.Guild, members, attended.Count, ranks);
    }
}

internal sealed class GuildInfoPanelHandler : INotificationHandler<GuildInfoPanel>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildInfoPanel panel, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            panel.FindMember(recipient.CharacterId) is { } member ? [panel.CreateMessage(member)] : []);
}
