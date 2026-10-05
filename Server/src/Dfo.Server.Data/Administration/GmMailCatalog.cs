namespace Dfo.Server.Data.Administration;

public sealed record GmMailItemFilter(string? Q = null, int? ItemId = null, string? Category = null,
    string? EquipmentType = null, int? MinimumLevel = null, int? MaximumLevel = null, int? Rarity = null,
    int Page = 1, int PageSize = 20, bool StackableOnly = false);
public sealed record GmMailCatalogEntry(int ItemId, string Name, string Category, string? EquipmentType,
    int? Level, int? Rarity);
public sealed record GmMailCatalogPage(IReadOnlyList<GmMailCatalogEntry> Items, int Page, int PageSize, bool HasMore);
public sealed record GmMailEquipmentCapabilities(int MaxReinforcementLevel, int MaxAmplificationLevel,
    bool CanAmplify, string? ReinforcementReason, string? AmplificationReason,
    IReadOnlyDictionary<byte, ushort> AmplifyValues);
public sealed record GmMailItemDetails(GmMailCatalogEntry Item, string Kind, bool DefaultSealed, ushort Durability,
    GmMailEquipmentCapabilities? Equipment);
