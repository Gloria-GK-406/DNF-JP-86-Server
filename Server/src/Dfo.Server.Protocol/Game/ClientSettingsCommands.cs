using System.Buffers.Binary;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

internal static class ClientSettingsPayload
{
    public static byte[] Decode(ReadOnlySpan<byte> body, int? exactLength = null)
    {
        if (body.Length < 4) throw new InvalidDataException("Settings require a four-byte length prefix.");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (length > 65536 || length != body.Length - 4 || exactLength is { } exact && length != exact)
            throw new InvalidDataException($"Invalid settings length {length}; body={body.Length} expected={exactLength}.");
        return body[4..].ToArray();
    }
}
