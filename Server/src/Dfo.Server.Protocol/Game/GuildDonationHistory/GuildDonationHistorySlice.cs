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

public sealed record GuildDonationHistoryRequest(ClientMessageType MessageType, GameMessageMetadata Metadata, bool IsEmpty)
    : IClientNetworkMessage;

internal abstract class EmptyGuildDonationHistoryDecoder(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildDonationHistoryRequest(type, metadata, body.IsEmpty);
}

internal sealed class GuildDonationHistoryDecoder() : EmptyGuildDonationHistoryDecoder(ClientMessageType.GameGuildDonationHistory);
internal sealed class GuildMileageHistoryDecoder() : EmptyGuildDonationHistoryDecoder(ClientMessageType.GameGuildMileageHistory);

internal sealed record GuildDonationHistoryReply(ServerMessageType MessageType, IReadOnlyList<GuildDonationEntry>? Entries = null)
    : IGameCmdMessage;

internal sealed class GuildDonationHistoryEncoder : GameCmdMessageEncoder<GuildDonationHistoryReply>
{
    internal const int Capacity = 50;
    private const int RowWidth = 44;
    private const int ContributionNameWidth = 32;
    private const int MileageNameWidth = 30;
    private const int ContributionGoldDonationKind = 3;
    private const byte MileageGoldDonationKind = 4;

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDonationHistoryReply message)
    {
        if (message.Entries is not { } entries)
            return OnePayload(message, GameCmdEnvelope.Refuse(0));

        var mileage = message.MessageType == ServerMessageType.GameGuildMileageHistory;
        var count = Math.Min(entries.Count, Capacity);
        // The GM window reads a count. The other history window copies 50 slots and skips zero kinds.
        var rowOffset = mileage ? sizeof(int) : 0;
        var body = new byte[rowOffset + (mileage ? count : Capacity) * RowWidth];
        if (mileage)
            BinaryPrimitives.WriteInt32LittleEndian(body, count);

        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            var row = body.AsSpan(rowOffset + i * RowWidth, RowWidth);
            WriteIdentity(row, entry, mileage ? MileageNameWidth : ContributionNameWidth);
            if (mileage)
            {
                row[34] = MileageGoldDonationKind;
                BinaryPrimitives.WriteInt32LittleEndian(row[40..], entry.Points);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(row[36..], entry.Points);
                BinaryPrimitives.WriteInt32LittleEndian(row[40..], ContributionGoldDonationKind);
            }
        }

        return OnePayload(message, GameCmdEnvelope.Accept(body));
    }

    private static void WriteIdentity(Span<byte> row, GuildDonationEntry entry, int nameWidth)
    {
        BinaryPrimitives.WriteInt32LittleEndian(row, checked((int)entry.DonatedAt.ToUnixTimeSeconds()));
        var length = Math.Min(entry.DonorName.Length, nameWidth - 1);
        // Keep the C-string terminator and leave any UTF-8 sequence crossing it out.
        while (length < entry.DonorName.Length && length > 0 && (entry.DonorName[length] & 0xC0) == 0x80)
            length--;
        entry.DonorName.AsSpan(0, length).CopyTo(row.Slice(sizeof(int), nameWidth));
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
        var replyType = command.Request.MessageType == ClientMessageType.GameGuildMileageHistory
            ? ServerMessageType.GameGuildMileageHistory
            : ServerMessageType.GameGuildDonationHistory;
        var reply = new GuildDonationHistoryReply(replyType);
        if (command.Request.IsEmpty && sessions.TryGetInWorld(connection.ConnectionId, out var viewer))
        {
            var recipient = new SelectedSessionRecipient(viewer, connection, command.Request.Metadata);
            reply = await units.ReadOnlyAsync(context, new("guild-donation-history", [recipient]), async token =>
                new GuildDonationHistoryReply(replyType, await guilds.ReadDonationHistoryAsync(
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
        services.AddGameCmdMessageDecoder<GuildMileageHistoryDecoder>(ClientMessageType.GameGuildMileageHistory);
        services.AddGameCmdMessageEncoder<GuildDonationHistoryReply, GuildDonationHistoryEncoder>();
        services.AddProtocolCommandHandler<GameGuildDonationHistoryCommand, bool, GameGuildDonationHistoryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildDonationHistory, typeof(GuildDonationHistoryRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildMileageHistory, typeof(GuildDonationHistoryRequest)));
        services.RegisterProtocolMessageHandler<GuildDonationHistoryRequestHandler, GuildDonationHistoryRequest, GameGuildDonationHistoryCommand>(
            ClientMessageType.GameGuildDonationHistory);
        services.RegisterProtocolMessageHandler<GuildDonationHistoryRequestHandler, GuildDonationHistoryRequest, GameGuildDonationHistoryCommand>(
            ClientMessageType.GameGuildMileageHistory);
    }
}
