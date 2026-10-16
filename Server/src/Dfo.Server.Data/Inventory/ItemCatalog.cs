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
    IReadOnlyDictionary<byte, ushort> AmplifyValues, int MaxForgeLevel = 0, int SocketCount = 0, string? SocketClass = null);
public sealed record ItemCatalogDetails(ItemCatalogEntry Item, string Kind, bool DefaultSealed, ushort Durability,
    ItemEquipmentCapabilities? Equipment);

/// <summary>Client equipment quality tiers: 0 最下级, 1 下级, 2 中级, 3 上级, 4 最上级.</summary>
public static class EquipmentQualityTiers { public const int Lowest = 0; public const int Highest = 4; }

/// <remarks>Members added after the first release are omitted at their defaults so stored JSON and payload hashes stay byte-identical.
/// <see cref="Quality"/> is the client quality tier 0..4 (最下级..最上级); null means 4.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemEquipmentOptions(string Seal = "default", string Growth = "none", int Level = 0, int AmplifyType = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Forge = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Socket = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Quality = null);
