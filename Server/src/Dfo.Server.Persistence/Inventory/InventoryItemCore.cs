using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Inventory;

/// <summary>
/// The eighty-two bytes an item is stored as, and what they mean.
/// </summary>
/// <remarks>
/// This stays on the persistence side. Every field of it is already a domain value, so pulling it
/// apart into columns would let the database be read by a person -- which is worth doing, and is
/// not this change. Until then the packing is a storage detail and lives with storage; what the
/// tables hand upward is the meaning, not the bytes.
/// </remarks>
internal static class InventoryItemCore
{
    /// <summary>How long one is, checked on the way in as well as on the way out.</summary>
    public const int Size = 82;

    public static byte[] Encode(InventoryItemCoreState value)
    {
        var core = new byte[Size];
        core[0] = value.ItemKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), value.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), value.Value);
        core[9] = value.Attribute;
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(10), value.Durability);
        core[12] = value.SealFlag;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(13), value.EnchantCardId);
        core[17] = value.EnchantUpgradeCount; core[18] = value.AmplifyType;
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(19), value.AmplifyValue);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(21), value.Marker16);
        WriteChronicle(core.AsSpan(25), value.ChronicleOption0);
        WriteChronicle(core.AsSpan(33), value.ChronicleOption1);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(41), value.ExpireTime);
        core[45] = value.EmblemSocketCount;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(46), value.EmblemId1);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(50), value.EmblemId2);
        BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(54), value.Rune);
        WriteRandom(core.AsSpan(56), value.RandomOption0); WriteRandom(core.AsSpan(59), value.RandomOption1);
        WriteRandom(core.AsSpan(62), value.RandomOption2); core[65] = value.RandomOptionState;
        core[66] = value.RandomOptionChangedIndex; core[67] = value.RandomOptionChangeState;
        WriteRandom(core.AsSpan(68), value.RandomOptionChange);
        core[71] = value.GenuineUpgrade; core[72] = value.EmancipateEquipmentLevel;
        core[73] = value.TradeRestriction; BinaryPrimitives.WriteUInt16LittleEndian(core.AsSpan(74), value.TailUnknown0);
        core[76] = value.TailUnknown1; core[77] = value.TailUnknown2; core[78] = value.TailUnknown3;
        core[79] = value.RemainUseCount; core[80] = value.SortLockFlag; core[81] = value.EquipmentLockId;
        return core;
    }

    public static InventoryItemCoreState Decode(ReadOnlySpan<byte> core) => new(
        core[0],
        BinaryPrimitives.ReadInt32LittleEndian(core[1..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[5..]),
        core[9],
        BinaryPrimitives.ReadUInt16LittleEndian(core[10..]),
        core[12],
        BinaryPrimitives.ReadInt32LittleEndian(core[13..]),
        core[17],
        core[18],
        BinaryPrimitives.ReadUInt16LittleEndian(core[19..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[21..]),
        DecodeChronicleOption(core[25..]),
        DecodeChronicleOption(core[33..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[41..]),
        core[45],
        BinaryPrimitives.ReadInt32LittleEndian(core[46..]),
        BinaryPrimitives.ReadInt32LittleEndian(core[50..]),
        BinaryPrimitives.ReadUInt16LittleEndian(core[54..]),
        DecodeRandomOption(core[56..]),
        DecodeRandomOption(core[59..]),
        DecodeRandomOption(core[62..]),
        core[65],
        core[66],
        core[67],
        DecodeRandomOption(core[68..]),
        core[71],
        core[72],
        core[73],
        BinaryPrimitives.ReadUInt16LittleEndian(core[74..]),
        core[76],
        core[77],
        core[78],
        core[79],
        core[80],
        core[81]);

    private static void WriteChronicle(Span<byte> target, ChronicleOptionState value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target, value.Id);
        target[4] = value.CharacterJob; target[5] = value.FirstGrowType;
        target[6] = value.EquipmentType; target[7] = value.OptionNumber;
    }

    private static void WriteRandom(Span<byte> target, RandomOptionState value)
    {
        target[0] = value.Type; target[1] = value.Value1; target[2] = value.Value2;
    }

    private static ChronicleOptionState DecodeChronicleOption(ReadOnlySpan<byte> option) => new(
        BinaryPrimitives.ReadInt32LittleEndian(option),
        option[4],
        option[5],
        option[6],
        option[7]);

    private static RandomOptionState DecodeRandomOption(ReadOnlySpan<byte> option) =>
        new(option[0], option[1], option[2]);
}
