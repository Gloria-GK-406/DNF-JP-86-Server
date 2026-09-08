using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;

namespace Dfo.Server.Protocol.Game.AccountRoster;

internal static class AccountCharacterRosterPayloadCodec
{
    /// <summary>The two bytes that follow the level in every observed A21 roster entry.</summary>
    /// <remarks>
    /// Written as the bytes they were seen as, because one character cannot say what they are.
    /// The entry-state path models this same region as two 32-bit progress values followed by a
    /// 16-bit reserved field, and under that reading these bytes would be the low half of a first
    /// progress value of 267. Two things lean the other way -- the pair appears nowhere else in
    /// the same character's entry frames, and the reference server writes them as literals -- but
    /// neither settles it.
    ///
    /// The cheapest thing that would: a second character whose progress differs. Until then,
    /// reproducing the observed bytes is the honest choice, and a wrong guess here costs at most a
    /// wrong value in a field nobody can name.
    /// </remarks>
    internal const byte FirstByteAfterLevel = 0x0B;

    internal const byte SecondByteAfterLevel = 0x01;

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
            body.Add(FirstByteAfterLevel);
            body.Add(SecondByteAfterLevel);
            WriteUInt32(body, 0);
            WriteUInt32(body, 0);
            // Refused rather than drawn as nothing. A null list means the listing this roster came
            // from was never asked to work out what anyone is wearing, and the cheap listing is
            // what four other callers use; drawing it here would put every character back on the
            // screen undressed, silently, which is the defect this frame was fixed for.
            var appearances = character.Appearances
                ?? throw new InvalidDataException(
                    $"Character {character.CharacterId} reached the roster frame from a listing "
                    + "that did not work out what it is wearing.");
            body.Add(checked((byte)appearances.Count));
            foreach (var appearance in appearances)
            {
                WriteAppearance(body, appearance);
            }

            WriteRosterTail(body, character.CloneTitleItemId);
        }

        return body.ToArray();
    }

    /// <summary>One display part, twenty-three bytes.</summary>
    /// <remarks>
    /// The stored blob holds these already in this shape, so this writes back what was read. The
    /// literal four is the expansion field's own length, which the client reads before the four
    /// bytes that follow it.
    /// </remarks>
    private static void WriteAppearance(List<byte> body, CharacterRosterAppearanceState appearance)
    {
        var expansion = appearance.ExpansionData ?? [];
        if (expansion.Length != 4 || appearance.ExpansionLength != 4)
        {
            throw new InvalidDataException(
                "A roster appearance expansion is the four bytes that were loaded, not a variable field.");
        }

        body.Add(appearance.Slot);
        WriteInt32(body, appearance.DisplayItemId);
        WriteInt32(body, appearance.ExpansionLength);
        body.AddRange(expansion);
        body.Add(appearance.State);
        WriteInt32(body, appearance.LinkItemId);
        WriteUInt32(body, appearance.EnchantValue);
        body.Add(appearance.Flag20);
    }

    /// <summary>The thirty-six bytes that close every A21 roster entry.</summary>
    /// <remarks>
    /// Four bytes shorter here until A21: the older client took eleven trailing bytes ending
    /// <c>03 00 00 04 00 00 00 00</c>, and A21 takes fifteen ending <c>00*6 FF 00*8</c>. Getting
    /// this length wrong does not corrupt one field -- it shifts every entry after it, so a
    /// roster with two characters loses the second one rather than drawing it oddly.
    /// </remarks>
    private static void WriteRosterTail(List<byte> body, uint cloneTitleItemId)
    {
        WriteUInt32(body, cloneTitleItemId);
        body.AddRange(new byte[4]);
        WriteUInt32(body, 0);
        WriteUInt32(body, 0);
        body.Add(0x00);
        WriteUInt32(body, 0);
        body.AddRange(new byte[6]);
        body.Add(0xFF);
        body.AddRange(new byte[4]);
        WriteUInt32(body, 0);
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
