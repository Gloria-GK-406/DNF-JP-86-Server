using System.Buffers.Binary;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildDonationHistory;

public sealed record GuildDonationHistoryRequest(GameMessageMetadata Metadata, bool IsEmpty) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGuildDonationHistory;
}

internal sealed class GuildDonationHistoryDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildDonationHistoryRequest(metadata, body.IsEmpty);
}

internal sealed record GuildDonationHistoryReply(IReadOnlyList<GuildDonationEntry>? Entries = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildDonationHistory;
}

internal sealed class GuildDonationHistoryEncoder : GameCmdMessageEncoder<GuildDonationHistoryReply>
{
    internal const int Capacity = 50;
    private const int NameWidth = 32;
    private const int RowWidth = sizeof(int) + NameWidth + 2 * sizeof(int);
    private const int GoldDonationKind = 3;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDonationHistoryReply message)
    {
        if (message.Entries is not { } entries)
            return OnePayload(message, GameCmdEnvelope.Refuse(0));

        // A21 copies all 50 slots and skips any row whose kind is zero.
        var body = new byte[Capacity * RowWidth];
        var count = Math.Min(entries.Count, Capacity);
        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            var row = body.AsSpan(i * RowWidth, RowWidth);
            BinaryPrimitives.WriteInt32LittleEndian(row, checked((int)entry.DonatedAt.ToUnixTimeSeconds()));
            var length = Math.Min(entry.DonorName.Length, NameWidth - 1);
            // Keep the C-string terminator and leave any UTF-8 sequence crossing it out.
            while (length < entry.DonorName.Length && length > 0 && (entry.DonorName[length] & 0xC0) == 0x80)
                length--;
            entry.DonorName.AsSpan(0, length).CopyTo(row.Slice(sizeof(int), NameWidth));
            BinaryPrimitives.WriteInt32LittleEndian(row[(sizeof(int) + NameWidth)..], entry.Points);
            BinaryPrimitives.WriteInt32LittleEndian(row[^sizeof(int)..], GoldDonationKind);
        }

        return OnePayload(message, GameCmdEnvelope.Accept(body));
    }
}

public sealed class GuildDonationHistoryRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildDonationHistoryRequest>, IProtocolCommandMessageHandler<GameGuildDonationHistoryCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildDonationHistoryRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildDonationHistoryCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildDonationHistoryCommand(NetworkDispatchContext Dispatch, GuildDonationHistoryRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildDonationHistoryCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildQueryDomainService guilds,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildDonationHistoryCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildDonationHistoryCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var reply = new GuildDonationHistoryReply();
        if (command.Request.IsEmpty && sessions.TryGetInWorld(connection.ConnectionId, out var viewer))
        {
            var recipient = new SelectedSessionRecipient(viewer, connection, command.Request.Metadata);
            reply = await units.ReadOnlyAsync(context, new("guild-donation-history", [recipient]), async token =>
                new GuildDonationHistoryReply(await guilds.ReadDonationHistoryAsync(
                    viewer.CharacterId, GuildDonationHistoryEncoder.Capacity, token).ConfigureAwait(false)), ct)
                .ConfigureAwait(false);
        }

        await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        return reply.Entries is not null;
    }
}

internal static class GuildDonationHistoryRegistration
{
    internal static void AddGuildDonationHistorySlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildDonationHistoryDecoder>(ClientMessageType.GameGuildDonationHistory);
        services.AddGameCmdMessageEncoder<GuildDonationHistoryReply, GuildDonationHistoryEncoder>();
        services.AddProtocolCommandHandler<GameGuildDonationHistoryCommand, bool, GameGuildDonationHistoryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildDonationHistory, typeof(GuildDonationHistoryRequest)));
        services.RegisterProtocolMessageHandler<GuildDonationHistoryRequestHandler, GuildDonationHistoryRequest, GameGuildDonationHistoryCommand>(
            ClientMessageType.GameGuildDonationHistory);
    }
}
