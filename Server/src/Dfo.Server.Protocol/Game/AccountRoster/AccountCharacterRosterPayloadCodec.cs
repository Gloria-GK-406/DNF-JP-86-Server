using System.Buffers.Binary;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.AccountRoster;

internal static class AccountCharacterRosterPayloadCodec
{
    public static byte[] Encode(AccountCharacterRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var body = new List<byte>();
        body.Add(0x02);
        WriteUInt16(body, roster.SlotLimit);
        WriteUInt16(body, roster.SlotLimit);
        body.Add(0x00);
        WriteInt32(body, 0);
        WriteUInt16(body, 0);
        WriteInt32(body, 0);
        WriteUInt16(body, checked((ushort)roster.Characters.Count));

        foreach (var character in roster.Characters)
        {
            WriteUInt16(body, character.SlotIndex);
            WriteBytesWithLength(body, character.Name);
            body.Add(0x00);
            body.Add(0x00);
            body.Add(character.Job);
            body.Add(character.GrowType);
            body.Add(character.Level);
            WriteUInt32(body, 0);
            WriteUInt32(body, 0);
            WriteUInt16(body, 0);
            body.Add(0x00);
            WriteRosterTail(body, character.CloneTitleItemId);
        }

        return body.ToArray();
    }

    private static void WriteRosterTail(List<byte> body, uint cloneTitleItemId)
    {
        WriteUInt32(body, cloneTitleItemId);
        body.AddRange(new byte[4]);
        WriteUInt32(body, 0);
        WriteUInt32(body, 0);
        body.Add(0x00);
        WriteUInt32(body, 0);
        body.AddRange([0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00]);
    }

    private static void WriteBytesWithLength(List<byte> body, byte[]? bytes)
    {
        bytes ??= [];
        WriteInt32(body, bytes.Length);
        body.AddRange(bytes);
    }

    private static void WriteUInt16(List<byte> body, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        body.AddRange(bytes);
    }

    private static void WriteInt32(List<byte> body, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        body.AddRange(bytes);
    }

    private static void WriteUInt32(List<byte> body, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        body.AddRange(bytes);
    }
}
