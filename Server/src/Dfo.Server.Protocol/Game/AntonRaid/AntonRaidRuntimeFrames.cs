using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record AntonRaidRuntimeNotice(ServerMessageType MessageType, ReadOnlyMemory<byte> Body) : IGameNotiMessage
{
    internal static AntonRaidRuntimeNotice Write(ServerMessageType type, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return new(type, stream.ToArray());
    }

    internal static AntonRaidRuntimeNotice State(AntonRaidStage stage, byte phase) =>
        Write(ServerMessageType.GameAntonRaidState, writer =>
        {
            writer.Write(stage == AntonRaidStage.Failed ? (byte)4 : (byte)stage);
            writer.Write(stage == AntonRaidStage.Failed ? (byte)1 : stage == AntonRaidStage.Attacking ? phase : (byte)0);
        });

    internal static AntonRaidRuntimeNotice Tickets(IReadOnlyList<AntonRaidTicket> tickets) =>
        Write(ServerMessageType.GameAntonRaidTickets, writer =>
        {
            writer.Write(tickets.Count);
            foreach (var ticket in tickets)
            {
                writer.Write(ticket.Character is { } character ? SelectedSessionIdentity.ToWireUserId(character) : (ushort)0);
                writer.Write(ticket.Ready);
                writer.Write((ushort)Math.Min(ticket.Count, ushort.MaxValue));
            }
        });

    internal static IReadOnlyList<AntonRaidRuntimeNotice> Timer(DateTimeOffset until, DateTimeOffset now, byte kind = 0)
    {
        var remaining = checked((uint)Math.Max(0, Math.Ceiling((until - now).TotalSeconds)));
        return
        [
            Write(ServerMessageType.GameAntonRaidSetTimer, writer =>
            {
                writer.Write((byte)0);
                writer.Write(0u);
                writer.Write(checked((uint)until.ToUnixTimeSeconds()));
                writer.Write(remaining);
            }),
            Write(ServerMessageType.GameAntonRaidRemaining, writer =>
            {
                writer.Write(kind);
                writer.Write(remaining);
            }),
        ];
    }
}

internal sealed class AntonRaidRuntimeNoticeEncoder : GameNotiMessageEncoder<AntonRaidRuntimeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidRuntimeNotice message) =>
        OnePayload(message, message.Body);
}
