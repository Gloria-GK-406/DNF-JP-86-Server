using System.Collections.Immutable;

namespace Dfo.Server.Data.Content;

public enum CeraShopItemKind { Unknown, Stackable, Equipment, Avatar, Creature, CreatureEquipment, Package, Service }

/// <summary>Content refusals; a declaration with diagnostics must not be sold silently.</summary>
public sealed record CeraShopDiagnostic(string Code, string Source, string Detail);

public sealed record CeraShopPrice(int Gold, int OtherPayment, int Cera, int Period,
    ImmutableArray<int> RawFields);

public sealed record CeraShopProductDefinition(int ProductId, int ItemId, int Count,
    string Category, CeraShopPrice? Price, bool IsService, bool BuyOnlyCera,
    bool BuyOnlyCeraPoint, ImmutableArray<CeraShopDiagnostic> Diagnostics, string Source, int? ServiceSlot = null, int ServiceDays = 0);

public sealed record CeraShopComponent(int ItemId, int Count, int UsablePeriod);
public sealed record CeraShopSelectionGroup(ImmutableArray<CeraShopComponent> Choices);
public sealed record CeraShopAvatarDeclaration(string EquipmentType, ImmutableArray<CeraShopPrice> PriceOptions,
    ImmutableArray<int> SelectableAbilities);
public sealed record CeraShopCreatureDeclaration(int SpeciesId, int StartLevel);

public sealed record CeraShopItemDefinition(int ItemId, string Source, CeraShopItemKind Kind,
    ushort Durability, bool Sealed, int? MinimumPurchaseLevel,
    ImmutableArray<CeraShopComponent> Components, ImmutableArray<CeraShopSelectionGroup> SelectionGroups,
    CeraShopAvatarDeclaration? Avatar, CeraShopCreatureDeclaration? Creature,
    ImmutableArray<CeraShopDiagnostic> Diagnostics);
