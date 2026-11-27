using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildPresence;

internal sealed record GuildMemberPresenceChanged(
    CharacterId Character, byte[] Name, byte Channel, SelectedSessionIdentity? OnlineSession = null)
    : INotification<GuildMemberPresenceChanged>;

internal sealed class GuildMemberPresenceHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<GuildMemberPresenceChanged>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, GuildMemberPresenceChanged change, CancellationToken ct)
    {
        if (recipient.CharacterId == change.Character)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);

        var isCurrent = change.OnlineSession is { } online
            ? sessions.IsInWorld(online)
            : !sessions.TryGetInWorld(change.Character, out _);
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            isCurrent ? [new GuildMemberPresenceMessage(change.Channel, change.Name)] : []);
    }
}

internal sealed record GuildMemberPresenceMessage(byte Channel, byte[] Name) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildMemberPresence;
}

internal sealed class GuildMemberPresenceEncoder : GameNotiMessageEncoder<GuildMemberPresenceMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildMemberPresenceMessage message)
    {
        ChatDString.Require(message.Name, 1, ChatDString.DecodedNameLimit, "guild member name");
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(message.Channel != 0);
        writer.Write(message.Channel);
        ChatDString.Write(writer, message.Name);
        return OnePayload(message, body.ToArray());
    }
}

internal static class GuildPresenceRegistration
{
    internal static void AddGuildPresenceSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<GuildMemberPresenceMessage, GuildMemberPresenceEncoder>();
        services.AddNotificationHandler<GuildMemberPresenceChanged, GuildMemberPresenceHandler>();
    }
}
