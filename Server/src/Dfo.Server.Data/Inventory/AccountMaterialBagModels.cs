using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Inventory;

public enum AccountMaterialKind
{
    BlackCube,
    WhiteCube,
    RedCube,
    BlueCube,
    ClearCube,
    GoldCube,
    CommonSoul,
    AdvancedSoul,
    RareSoul,
    UniqueSoul,
    LegendarySoul,
}

public sealed record AccountMaterialDefinition(
    AccountMaterialKind Kind,
    int ItemId,
    int VirtualSlotIndex);

public static class AccountMaterialCatalog
{
    private static readonly AccountMaterialDefinition[] Mappings =
    [
        new(AccountMaterialKind.BlackCube, 3033, 354),
        new(AccountMaterialKind.WhiteCube, 3034, 355),
        new(AccountMaterialKind.RedCube, 3035, 356),
        new(AccountMaterialKind.BlueCube, 3036, 357),
        new(AccountMaterialKind.ClearCube, 3037, 358),
        new(AccountMaterialKind.GoldCube, 3262, 359),
        new(AccountMaterialKind.CommonSoul, 10100115, 360),
        new(AccountMaterialKind.AdvancedSoul, 10100116, 361),
        new(AccountMaterialKind.RareSoul, 10099773, 362),
        new(AccountMaterialKind.UniqueSoul, 10099774, 363),
        new(AccountMaterialKind.LegendarySoul, 10099775, 364),
    ];

    public static IReadOnlyList<AccountMaterialDefinition> Definitions { get; } =
        Array.AsReadOnly(Mappings);

    public static bool ContainsItem(int itemId) =>
        Definitions.Any(definition => definition.ItemId == itemId);
}

public sealed record AccountMaterialBagState(
    AccountId AccountId,
    int BlackCube,
    int WhiteCube,
    int RedCube,
    int BlueCube,
    int ClearCube,
    int GoldCube,
    int CommonSoul,
    int AdvancedSoul,
    int RareSoul,
    int UniqueSoul,
    int LegendarySoul)
{
    public int Count(AccountMaterialKind kind) => kind switch
    {
        AccountMaterialKind.BlackCube => BlackCube,
        AccountMaterialKind.WhiteCube => WhiteCube,
        AccountMaterialKind.RedCube => RedCube,
        AccountMaterialKind.BlueCube => BlueCube,
        AccountMaterialKind.ClearCube => ClearCube,
        AccountMaterialKind.GoldCube => GoldCube,
        AccountMaterialKind.CommonSoul => CommonSoul,
        AccountMaterialKind.AdvancedSoul => AdvancedSoul,
        AccountMaterialKind.RareSoul => RareSoul,
        AccountMaterialKind.UniqueSoul => UniqueSoul,
        AccountMaterialKind.LegendarySoul => LegendarySoul,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account material kind."),
    };
}

public sealed record AccountMaterialChange(
    AccountMaterialKind Kind,
    int ItemId,
    int VirtualSlotIndex,
    int AddedCount,
    int FinalCount);
