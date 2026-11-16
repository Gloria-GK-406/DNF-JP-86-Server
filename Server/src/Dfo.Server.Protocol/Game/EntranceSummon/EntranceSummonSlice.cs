using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EntranceSummon;

/// <summary>
/// CMD 0x0211 <c>SUMMON_MONSTER</c>: the party leader's client asking for the monster a boss room
/// entrance condition calls in, after NOTI 0x0138.
/// </summary>
/// <remarks>
/// Nineteen bytes, built at A21 0x00E46954..0x00E469F3: <c>u16 kind</c> (0 monster, 1 APC),
/// <c>u32 code</c>, <c>u32 request id</c> (a client running number, at least 1), <c>u32 map</c>,
/// <c>u16 x</c>, <c>u16 y</c> (the room's maze cell), <c>u8 count</c>.
/// </remarks>
public sealed record EntranceSummonRequest(GameMessageMetadata Metadata, DungeonEntranceSummonRequest Summon)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameEntranceSummon;
}

/// <summary>CMD 0x0211's answer: the summoned actors, or a refusal, under the request's own id.</summary>
internal sealed record EntranceSummonResponse(int RequestId, IReadOnlyList<DungeonEntranceSummoned> Actors) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEntranceSummonResult;
    public bool Accepted => this.Actors.Count > 0;
}

internal static class EntranceSummonPayloadCodec
{
    public const int BodyLength = 19;

    /// <summary>The one refusal reason this server answers with; the client only needs the request back.</summary>
    public const byte RefusalReason = 1;

    public static DungeonEntranceSummonRequest Decode(ReadOnlySpan<byte> body)
    {
        if (body.Length < BodyLength)
            throw new InvalidDataException($"SUMMON_MONSTER requires {BodyLength} body bytes; received '{body.Length}'.");
        return new(
            BitConverter.ToUInt16(body[0..]),
            BitConverter.ToInt32(body[2..]),
            BitConverter.ToInt32(body[6..]),
            BitConverter.ToInt32(body[10..]),
            BitConverter.ToUInt16(body[14..]),
            BitConverter.ToUInt16(body[16..]),
            body[18]);
    }

    /// <summary>
    /// Accepted: <c>u8 1, u32 request id, u8 n, n x (u16 sequence, u32 code, u8 grade, u16 level)</c>.
    /// Refused: <c>u8 0, u8 reason, u32 request id</c>.
    /// </summary>
    /// <remarks>
    /// A21 handler 0x01109050 reads exactly this, with no trailing bytes: the reference server's
    /// fifteen-byte answer is the n = 1 case. The client pairs it with its request by id
    /// (0x00E461E0), requires the code to match (0x00E43130) and spawns each entry at its own copy
    /// of the map's summon position under the sequence given here (0x00E432B0).
    /// </remarks>
    public static byte[] Encode(EntranceSummonResponse response)
    {
        using var stream = new MemoryStream(7 + response.Actors.Count * 9);
        using var writer = new BinaryWriter(stream);
        if (!response.Accepted)
        {
            writer.Write((byte)0);
            writer.Write(RefusalReason);
            writer.Write(response.RequestId);
            return stream.ToArray();
        }
        writer.Write((byte)1);
        writer.Write(response.RequestId);
        writer.Write(checked((byte)response.Actors.Count));
        foreach (var actor in response.Actors)
        {
            writer.Write(actor.Sequence);
            writer.Write(actor.MonsterCode);
            writer.Write(actor.Grade);
            writer.Write(actor.Level);
        }
        return stream.ToArray();
    }
}

internal sealed class EntranceSummonDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new EntranceSummonRequest(metadata, EntranceSummonPayloadCodec.Decode(body));
}

internal sealed class EntranceSummonEncoder : GameCmdMessageEncoder<EntranceSummonResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EntranceSummonResponse message) =>
        OnePayload(message, EntranceSummonPayloadCodec.Encode(message));
}

/// <summary>Hands the summon request to its command.</summary>
public sealed class EntranceSummonHandler(ICommandBus commands)
    : ClientMessageHandler<EntranceSummonRequest>, IProtocolCommandMessageHandler<GameEntranceSummonCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        EntranceSummonRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameEntranceSummonCommand(context, message), cancellationToken).ConfigureAwait(false);
}
