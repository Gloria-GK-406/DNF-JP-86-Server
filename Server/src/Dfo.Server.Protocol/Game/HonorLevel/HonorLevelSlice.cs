using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.HonorLevel;

/// <summary>
/// NOTI 0x0289, the account-wide honour standing shown on the roster screen.
/// </summary>
/// <remarks>
/// Outbound only -- the client never asks for it; the legacy server pushed it as part of the same
/// batch that answered the roster request, and this slice reproduces that.
///
/// Confidence here is lower than for the rest of this batch and the difference is worth stating.
/// The body layout comes from the legacy builder and the legacy comment claims the client renders
/// the first field as a level. But unlike the other types in this batch, 0x0289 has no receive
/// handler in the client census, so nothing independent corroborates that the client consumes it
/// at all. The census is static-only and misses runtime registrations, so its absence is not proof
/// either way. Sending it is safe regardless: a type the client has not registered is dropped by
/// its own registry lookup, exactly as this server drops unregistered inbound keys.
/// </remarks>
public sealed record HonorLevelInfoNotice(AccountHonorLevel Honor) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameHonorLevelInfo;
}

internal sealed class HonorLevelInfoNoticeEncoder : GameNotiMessageEncoder<HonorLevelInfoNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        HonorLevelInfoNotice message)
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, message.Honor.Level);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), message.Honor.Experience);
        return OnePayload(message, body);
    }
}
