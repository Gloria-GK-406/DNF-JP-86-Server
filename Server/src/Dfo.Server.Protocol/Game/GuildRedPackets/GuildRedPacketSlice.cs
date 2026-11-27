using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildRedPackets;

public sealed record GuildRedPacketRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid,
    uint Packet = 0, int Selection = 0, byte Shares = 0) : IClientNetworkMessage;

internal abstract class GuildRedPacketDecoder(ClientMessageType type) : IGameMessageDecoder
{
    private const int Prefix = 14;

    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        switch (type)
        {
            case ClientMessageType.GameGuildRedPacketList or ClientMessageType.GameGuildRedPacketPoints:
                return new GuildRedPacketRequest(type, metadata, body.IsEmpty);
            case ClientMessageType.GameGuildRedPacketHistory or ClientMessageType.GameGuildRedPacketTake
                when body.Length == Prefix + sizeof(uint):
                var id = BinaryPrimitives.ReadUInt32LittleEndian(body[Prefix..]);
                return new GuildRedPacketRequest(type, metadata, id > 0, Packet: id);
            case ClientMessageType.GameGuildRedPacketSend when body.Length == Prefix + sizeof(int) + 1:
                return new GuildRedPacketRequest(type, metadata, true,
                    Selection: BinaryPrimitives.ReadInt32LittleEndian(body[Prefix..]), Shares: body[^1]);
            default:
                return new GuildRedPacketRequest(type, metadata, false);
        }
    }
}

internal sealed class GuildRedPacketListDecoder() : GuildRedPacketDecoder(ClientMessageType.GameGuildRedPacketList);
internal sealed class GuildRedPacketPointsDecoder() : GuildRedPacketDecoder(ClientMessageType.GameGuildRedPacketPoints);
internal sealed class GuildRedPacketHistoryDecoder() : GuildRedPacketDecoder(ClientMessageType.GameGuildRedPacketHistory);
internal sealed class GuildRedPacketSendDecoder() : GuildRedPacketDecoder(ClientMessageType.GameGuildRedPacketSend);
internal sealed class GuildRedPacketTakeDecoder() : GuildRedPacketDecoder(ClientMessageType.GameGuildRedPacketTake);

internal sealed record GuildRedPacketReply(ServerMessageType MessageType, byte[]? Payload = null, byte? Error = null)
    : IGameCmdMessage;

internal sealed class GuildRedPacketReplyEncoder : GameCmdMessageEncoder<GuildRedPacketReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildRedPacketReply message) =>
        OnePayload(message, message.Error is { } error
            ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept(message.Payload ?? []));
}

internal static class GuildRedPacketWire
{
    public static byte[] List(GuildRedPacketList? view, long now)
    {
        var rows = view?.Packets ?? [];
        using var stream = new MemoryStream(14 + 40 * rows.Count);
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((ushort)(view?.MemberCount ?? 0)));
        writer.Write(0u);
        writer.Write(view?.TakenToday ?? 0);
        writer.Write(rows.Count);
        foreach (var packet in rows)
        {
            writer.Write(packet.Id);
            writer.Write(packet.ItemId);
            WriteName(writer, packet.SenderName, 30);
            writer.Write((byte)packet.SharesLeft);
            writer.Write((byte)(packet.IsOpenAt(now) ? 0 : 1));
        }
        return stream.ToArray();
    }

    public static byte[] Points(IReadOnlyList<GuildContributionMember> members)
    {
        using var stream = new MemoryStream(4 + 39 * members.Count);
        using var writer = new BinaryWriter(stream);
        writer.Write(members.Count);
        foreach (var member in members)
        {
            writer.Write(member.Character.Value);
            writer.Write((byte)member.Rank);
            WriteName(writer, member.Name, 30);
            writer.Write(member.Contribution);
        }
        return stream.ToArray();
    }

    public static byte[] History(IReadOnlyList<GuildRedPacketClaim> rows)
    {
        using var stream = new MemoryStream(4 + 35 * rows.Count);
        using var writer = new BinaryWriter(stream);
        writer.Write(rows.Count);
        foreach (var row in rows)
        {
            WriteName(writer, row.Name, 32);
            writer.Write(checked((ushort)row.Amount));
            writer.Write((byte)0);
        }
        return stream.ToArray();
    }

    public static byte[] Mutation(int status, int itemId, int amount)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(payload, status);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), amount);
        return payload;
    }

    private static void WriteName(BinaryWriter writer, string name, int size)
    {
        Span<byte> field = stackalloc byte[size];
        field.Clear();
        Encoding.UTF8.GetEncoder().Convert(name.AsSpan(), field[..^1], true, out _, out _, out _);
        writer.Write(field);
    }
}

internal sealed record GuildRedPacketAvailable(int Count) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildRedPacketAvailable;
}

internal sealed class GuildRedPacketAvailableEncoder : GameNotiMessageEncoder<GuildRedPacketAvailable>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildRedPacketAvailable message)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, message.Count);
        return OnePayload(message, payload);
    }
}

internal sealed record GuildRedPacketAvailabilityChanged : INotification<GuildRedPacketAvailabilityChanged>;

internal sealed class GuildRedPacketAvailabilityHandler(IGuildRedPacketDomainService packets, ISelectedSessionDomainService sessions)
    : INotificationHandler<GuildRedPacketAvailabilityChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        GuildRedPacketAvailabilityChanged change, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var actor) || actor.CharacterId != recipient.CharacterId)
            return [];
        var count = await packets.CountAvailableAsync(actor.CharacterId, actor.AccountId, ct).ConfigureAwait(false);
        return count > 0 ? [new GuildRedPacketAvailable(count)] : [];
    }
}

internal sealed class GuildRedPacketHandler(ICommandBus bus)
    : ClientMessageHandler<GuildRedPacketRequest>, IProtocolCommandMessageHandler<GameGuildRedPacketCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildRedPacketRequest request,
        CancellationToken cancellationToken) =>
        _ = await bus.SendAsync(new GameGuildRedPacketCommand(context, request), cancellationToken).ConfigureAwait(false);
}

internal static class GuildRedPacketRegistration
{
    public static void AddGuildRedPacketSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildRedPacketListDecoder>(ClientMessageType.GameGuildRedPacketList);
        services.AddGameCmdMessageDecoder<GuildRedPacketPointsDecoder>(ClientMessageType.GameGuildRedPacketPoints);
        services.AddGameCmdMessageDecoder<GuildRedPacketHistoryDecoder>(ClientMessageType.GameGuildRedPacketHistory);
        services.AddGameCmdMessageDecoder<GuildRedPacketSendDecoder>(ClientMessageType.GameGuildRedPacketSend);
        services.AddGameCmdMessageDecoder<GuildRedPacketTakeDecoder>(ClientMessageType.GameGuildRedPacketTake);
        services.AddGameCmdMessageEncoder<GuildRedPacketReply, GuildRedPacketReplyEncoder>();
        services.AddGameNotiMessageEncoder<GuildRedPacketAvailable, GuildRedPacketAvailableEncoder>();
        services.AddNotificationHandler<GuildRedPacketAvailabilityChanged, GuildRedPacketAvailabilityHandler>();
        ClientMessageType[] types = [ClientMessageType.GameGuildRedPacketList, ClientMessageType.GameGuildRedPacketPoints,
            ClientMessageType.GameGuildRedPacketHistory, ClientMessageType.GameGuildRedPacketSend, ClientMessageType.GameGuildRedPacketTake];
        services.AddProtocolCommandHandler<GameGuildRedPacketCommand, bool, GameGuildRedPacketCommandHandler>(
            types.Select(type => new ProtocolCommandMessageBinding(type, typeof(GuildRedPacketRequest))).ToArray());
        foreach (var type in types)
            services.RegisterProtocolMessageHandler<GuildRedPacketHandler, GuildRedPacketRequest, GameGuildRedPacketCommand>(type);
    }
}
