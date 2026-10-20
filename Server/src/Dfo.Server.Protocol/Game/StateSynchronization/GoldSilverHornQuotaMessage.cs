using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

// A21 loader binds current 4123 to the silver slot; the legacy gold slot is unconfigured.
internal sealed record GoldSilverHornQuotaMessage(int Used)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x042B;
    internal override byte[] EncodeBody()
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), checked((ushort)Used));
        return body;
    }
}
