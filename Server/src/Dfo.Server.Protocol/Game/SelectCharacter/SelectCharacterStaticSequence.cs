using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

internal enum SelectCharacterStaticCondition
{
    Always,
    KnightShield,
    PetWelcome,
}

internal sealed record SelectCharacterStaticPacketDescriptor(
    int Row,
    byte Command,
    ushort Type,
    int Occurrence = 0,
    InventoryContainerType? InventoryContainer = null,
    SelectCharacterStaticCondition Condition = SelectCharacterStaticCondition.Always);

internal static class SelectCharacterStaticSequence
{
    public static IReadOnlyList<SelectCharacterStaticPacketDescriptor> Descriptors { get; } =
    [
        Raw(1, 1, 0x0004),
        Item(2, InventoryContainerType.Main),
        Item(3, InventoryContainerType.Avatar),
        Item(4, InventoryContainerType.PersonalCargo),
        Item(5, InventoryContainerType.Pet),
        Item(6, InventoryContainerType.AccountCargo),
        Raw(7, 0, 0x0069), Raw(8, 0, 0x0002, 0), Raw(9, 0, 0x0002, 1),
        Raw(10, 0, 0x0245, condition: SelectCharacterStaticCondition.KnightShield),
        Raw(11, 0, 0x0013), Raw(12, 0, 0x0015), Raw(13, 0, 0x0164),
        Raw(14, 0, 0x0286), Raw(15, 0, 0x00AD), Raw(16, 0, 0x01C7),
        Raw(17, 0, 0x006C), Raw(18, 0, 0x0005), Raw(19, 0, 0x0187),
        Raw(20, 0, 0x01B9), Raw(21, 0, 0x0166, 0), Raw(22, 0, 0x0166, 1),
        Raw(23, 0, 0x0166, 2), Raw(24, 0, 0x0166, 3), Raw(25, 0, 0x0166, 4),
        Raw(26, 0, 0x0167), Raw(27, 0, 0x015F), Raw(28, 0, 0x00AC),
        Raw(29, 0, 0x00AE), Raw(30, 0, 0x02DA), Raw(31, 0, 0x017B),
        Raw(32, 0, 0x0381, 0), Raw(33, 0, 0x0381, 1), Raw(34, 0, 0x021F),
        Raw(35, 0, 0x0357), Raw(36, 0, 0x00FB), Raw(37, 0, 0x00CD),
        Raw(38, 0, 0x019F), Raw(39, 0, 0x00B1), Raw(40, 0, 0x0300),
        Raw(41, 1, 0x0312), Raw(42, 0, 0x03D8), Raw(43, 0, 0x019D),
        Raw(44, 0, 0x025B), Raw(45, 0, 0x0331), Raw(46, 0, 0x01EB),
        Raw(47, 0, 0x0061), Raw(48, 0, 0x0158), Raw(49, 0, 0x02D5, 0),
        Raw(50, 0, 0x02D5, 1), Raw(51, 0, 0x02D5, 2), Raw(52, 0, 0x01A8),
        Raw(53, 0, 0x007C), Raw(54, 0, 0x0002, 2), Raw(55, 0, 0x0035),
        Raw(56, 0, 0x0111), Raw(57, 0, 0x0016),
        Raw(58, 0, 0x0077, condition: SelectCharacterStaticCondition.PetWelcome),
    ];

    public static bool IsApplicable(
        SelectCharacterStaticPacketDescriptor descriptor,
        SelectCharacterResult result) => descriptor.Condition switch
        {
            SelectCharacterStaticCondition.Always => true,
            SelectCharacterStaticCondition.KnightShield =>
                result.Character.Identity.Job == 12
                && result.CharacterEntry.Skills.KnightShieldDeck is not null,
            SelectCharacterStaticCondition.PetWelcome => HasPetWelcome(result),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        };

    public static bool HasDarkKnightCombo(SelectCharacterResult result) =>
        result.Character.Identity.Job == 9
        && result.CharacterEntry.Skills.DarkKnightCombos is not null;

    private static bool HasPetWelcome(SelectCharacterResult result)
    {
        var welcome = result.World.Content.PetWelcome;
        return welcome is not null && welcome.Lines.Count > 0;
    }

    private static SelectCharacterStaticPacketDescriptor Raw(
        int row,
        byte command,
        ushort type,
        int occurrence = 0,
        SelectCharacterStaticCondition condition = SelectCharacterStaticCondition.Always) =>
        new(row, command, type, occurrence, null, condition);

    private static SelectCharacterStaticPacketDescriptor Item(
        int row,
        InventoryContainerType container) =>
        new(row, 0, 0x000D, 0, container);
}
