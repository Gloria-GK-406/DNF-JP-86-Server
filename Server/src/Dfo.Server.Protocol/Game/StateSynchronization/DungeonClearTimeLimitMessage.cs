using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>A21 025C: the first operand resets LostLand's remaining seconds; the second is preserved.</summary>
internal sealed record DungeonClearTimeLimitMessage(int RemainingSeconds, int SecondParameter)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x025C;
    internal override byte[] EncodeBody()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)RemainingSeconds));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), checked((uint)SecondParameter));
        return body;
    }
}
