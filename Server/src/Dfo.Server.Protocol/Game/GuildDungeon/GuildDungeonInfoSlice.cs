using System.Buffers.Binary;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildDungeon;

internal sealed record GuildDungeonInfoMessage(GuildDungeonView? View) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildDungeonInfo;
}

internal sealed class GuildDungeonInfoEncoder : GameNotiMessageEncoder<GuildDungeonInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDungeonInfoMessage message)
    {
        var view = message.View;
        var progress = view?.Progress ?? [];
        var body = new byte[17 + progress.Count * 8];
        body[0] = view?.MedalShopId is null ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), view?.MedalShopId ?? -1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), checked((uint)(view?.Admission.Period.EndUnix ?? 0)));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(13), progress.Count);
        for (var index = 0; index < progress.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(17 + index * 8), progress[index].DungeonId);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(21 + index * 8), progress[index].ClearCount);
        }
        return OnePayload(message, body);
    }
}

internal sealed class GuildDungeonProgressHandler(IGuildOverviewDomainService overviews)
    : INotificationHandler<GuildDungeonProgressNotification>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildDungeonProgressNotification notification, CancellationToken cancellationToken)
    {
        var update = new GuildDungeonInfoMessage(notification.View);
        if (!notification.RefreshGuildInfo)
            return [update];

        var panel = await GuildInfoPanel.ReadAsync(overviews, recipient.CharacterId, cancellationToken).ConfigureAwait(false);
        return panel.Guild?.Id == notification.View.Admission.Guild && panel.FindMember(recipient.CharacterId) is { } member
            ? [update, panel.CreateMessage(member)]
            : [];
    }
}

internal sealed record GuildDungeonRepeatBonusInfoMessage(int Progress) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildDungeonRepeatBonusInfo;
}

internal sealed class GuildDungeonRepeatBonusInfoEncoder : GameNotiMessageEncoder<GuildDungeonRepeatBonusInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDungeonRepeatBonusInfoMessage message)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)message.Progress));
        return OnePayload(message, body);
    }
}

internal sealed class GuildDungeonRepeatBonusHandler : INotificationHandler<GuildDungeonRepeatBonusNotification>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildDungeonRepeatBonusNotification notification, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new GuildDungeonRepeatBonusInfoMessage(notification.Progress)]);
}

internal static class GuildDungeonRegistration
{
    internal static void AddGuildDungeonSlice(IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<GuildDungeonInfoMessage, GuildDungeonInfoEncoder>();
        services.AddNotificationHandler<GuildDungeonProgressNotification, GuildDungeonProgressHandler>();
        services.AddGameNotiMessageEncoder<GuildDungeonRepeatBonusInfoMessage, GuildDungeonRepeatBonusInfoEncoder>();
        services.AddNotificationHandler<GuildDungeonRepeatBonusNotification, GuildDungeonRepeatBonusHandler>();
    }
}
