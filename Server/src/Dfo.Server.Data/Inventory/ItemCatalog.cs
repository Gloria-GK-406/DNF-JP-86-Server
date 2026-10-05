using System.Text.Json.Serialization;
namespace Dfo.Server.Data.Inventory;

public sealed record ItemCatalogFilter(string? Q = null, int? ItemId = null, string? Category = null,
    string? EquipmentType = null, int? MinimumLevel = null, int? MaximumLevel = null, int? Rarity = null,
    int Page = 1, int PageSize = 20, bool StackableOnly = false);
public sealed record ItemCatalogEntry(int ItemId, string Name, string Category, string? EquipmentType,
    int? Level, int? Rarity);
public sealed record ItemCatalogPage(IReadOnlyList<ItemCatalogEntry> Items, int Page, int PageSize, bool HasMore);
public sealed record ItemEquipmentCapabilities(int MaxReinforcementLevel, int MaxAmplificationLevel,
    bool CanAmplify, string? ReinforcementReason, string? AmplificationReason,
    IReadOnlyDictionary<byte, ushort> AmplifyValues);
public sealed record ItemCatalogDetails(ItemCatalogEntry Item, string Kind, bool DefaultSealed, ushort Durability,
    ItemEquipmentCapabilities? Equipment);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemEquipmentOptions(string Seal = "default", string Growth = "none", int Level = 0, int AmplifyType = 0);
