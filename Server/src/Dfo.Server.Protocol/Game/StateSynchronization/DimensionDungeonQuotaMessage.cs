using System.Buffers.Binary;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

// UpstreamStatic: DimensionGateEntranceInfoBodyBuilder; A21's receiver reads two u32 values.
internal sealed record DimensionDungeonQuotaMessage(DimensionQuotaSnapshot State) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0326;
    internal override byte[] EncodeBody()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)State.Remaining));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), checked((uint)State.ExtraRemaining));
        return body;
    }
}
