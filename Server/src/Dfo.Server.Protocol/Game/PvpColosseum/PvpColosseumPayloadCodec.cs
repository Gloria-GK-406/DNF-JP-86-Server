using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpColosseum;

internal sealed record EnterColosseumRequest(GameMessageMetadata Metadata, PvpColosseumKind? Kind) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEnterPvpColosseum;
}

internal sealed record UpdateColosseumRewardRequest(GameMessageMetadata Metadata, bool WellFormed) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUpdatePvpColosseumReward;
}

internal sealed record EnterColosseumResponse(PvpColosseumEntry? Entry) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEnterPvpColosseumResponse;
}

internal sealed record ColosseumContextNotice(ushort UserId, PvpColosseumLobbyState State) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpColosseumContext;
}

internal sealed record ColosseumRewardsNotice(IReadOnlyList<PvpColosseumPrize> Prizes) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpColosseumRewards;
}

internal sealed class EnterColosseumDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new EnterColosseumRequest(metadata, body switch
        {
            [0, 0] => PvpColosseumKind.Individual,
            [0, 7] => PvpColosseumKind.TotalMatch,
            _ => null,
        });
}

internal sealed class UpdateColosseumRewardDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new UpdateColosseumRewardRequest(metadata, body.IsEmpty);
}

internal sealed class EnterColosseumResponseEncoder : GameCmdMessageEncoder<EnterColosseumResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EnterColosseumResponse message)
    {
        if (message.Entry is not { } entry) return OnePayload(message, GameCmdEnvelope.Refuse(7));
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, entry.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], entry.Price);
        return OnePayload(message, GameCmdEnvelope.Accept(payload));
    }
}

internal sealed class ColosseumContextEncoder : GameNotiMessageEncoder<ColosseumContextNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ColosseumContextNotice message)
    {
        var body = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.UserId);
        Write(body.AsSpan(2, 8), message.State.Individual, message.State.IndividualEntries);
        Write(body.AsSpan(10, 8), message.State.TotalMatch, message.State.TotalMatchEntries);
        return OnePayload(message, body);
    }

    private static void Write(Span<byte> body, PvpColosseumRun? run, int entries)
    {
        body[0] = checked((byte)(run?.Wins ?? 0));
        body[1] = checked((byte)(run?.Losses ?? 0));
        body[2] = run is { Complete: false } ? (byte)1 : (byte)0;
        body[3] = checked((byte)Math.Min(byte.MaxValue, entries));
    }
}

internal sealed class ColosseumRewardsEncoder : GameNotiMessageEncoder<ColosseumRewardsNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ColosseumRewardsNotice message)
    {
        var body = new byte[4 + message.Prizes.Count * 12];
        BinaryPrimitives.WriteInt32LittleEndian(body, message.Prizes.Count);
        for (var index = 0; index < message.Prizes.Count; index++)
        {
            var prize = message.Prizes[index];
            var entry = body.AsSpan(4 + index * 12, 12);
            BinaryPrimitives.WriteInt32LittleEndian(entry, prize.ItemId);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], prize.Count);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], prize.DisplayType);
        }
        return OnePayload(message, body);
    }
}
