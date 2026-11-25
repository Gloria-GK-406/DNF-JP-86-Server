using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.HonorLevel;

/// <summary>
/// NOTI 0x0289, the account-wide honor level and progress within that level.
/// </summary>
/// <remarks>
/// Outbound only. A21 VA 0x008A8660 consumes the two u32 fields.
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
