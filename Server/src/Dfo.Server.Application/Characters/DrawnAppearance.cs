using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Characters;

internal static class EquipmentAppearanceParts
{
    private const int LastDrawnSlot = 13;

    private const int ExtraDrawnSlot = 24;

    private const int ExpansionLength = 4;

    public static bool DrawsFrom(int wornSlotIndex) =>
        (wornSlotIndex >= 0 && wornSlotIndex <= LastDrawnSlot) || wornSlotIndex == ExtraDrawnSlot;

    public static IReadOnlyList<CharacterRosterAppearanceState> Of(
        IReadOnlyList<InventoryItemState> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        return
        [
            .. worn
                .Where(item => DrawsFrom(item.SlotIndex))
                .OrderBy(item => item.SlotIndex)
                .Select(Part),
        ];
    }

    private static CharacterRosterAppearanceState Part(InventoryItemState item) => new(
        checked((byte)item.SlotIndex),
        AvatarDisplay(item),
        ExpansionLength,
        Colors(item),
        checked((byte)(((item.Core.Attribute & 0x1F) * 2) + (item.Core.AmplifyType != 0 ? 1 : 0))),
        LinkItemId: AvatarDisplay(item) != item.Core.ItemId ? item.Core.ItemId : 0,
        EnchantValue: 0u,
        Flag20: item.Core.EnchantUpgradeCount);

    private static int AvatarDisplay(InventoryItemState item) => item.Core.ItemKind == 8
        ? (item.Core.AppearanceItemId ?? item.AvatarDetail?.ClearAvatarId) is > 0 and var appearance ? appearance : item.Core.ItemId
        : item.Core.ItemId;

    private static byte[] Colors(InventoryItemState item)
    {
        var bytes = new byte[ExpansionLength];
        if (item.Core.ItemKind == 8 && item.AvatarDetail is { } avatar)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes, avatar.Color1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), avatar.Color2);
        }
        return bytes;
    }
}
