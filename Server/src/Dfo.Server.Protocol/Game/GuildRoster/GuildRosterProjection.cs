using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.GuildRoster;

internal static class GuildRosterProjection
{
    public static async ValueTask<GuildRosterSnapshot?> BuildAsync(
        IReadOnlyList<GuildRosterEntry> entries, ISelectedSessionDomainService sessions,
        IChannelEntryDomainService channels, CancellationToken ct)
    {
        if (entries.FirstOrDefault(entry => entry.Membership.IsLeader) is not { } leader)
            return null;
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

        return new GuildRosterSnapshot(leader.Membership.Guild, leader.Name, rows);
    }
}

internal sealed record GuildRosterChanged(GuildRosterSnapshot Roster) : INotification<GuildRosterChanged>;

internal sealed class GuildRosterChangedHandler : INotificationHandler<GuildRosterChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildRosterChanged notification, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new GuildRosterReply(ServerMessageType.GameGuildAllMemberList, notification.Roster)]);
}
