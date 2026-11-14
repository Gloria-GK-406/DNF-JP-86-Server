using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.ServerBroadcast;

// Facts the archive marks as worth telling the whole server. Each is recorded by the domain service
// that settled it, in the same transaction, only when the content declares it notable (a PVF notice
// level or broadcast flag) or a server rule threshold is met; whether and how it is told is decided
// after commit by the broadcast subscriber. Level and grow-type changes use the existing events.

/// <summary>An equipment reinforcement or amplification at or past the archive's notice level.</summary>
/// <param name="Level">The level the line names: the new level on success, the level before the attempt on failure.</param>
public sealed record EquipmentUpgradeAnnounced(CharacterId Actor, int ItemId, EquipmentUpgradeKind Kind,
    byte Level, bool Succeeded) : IDomainEvent;

/// <summary>A black diamond vending machine prize whose pool row carries the broadcast flag.</summary>
public sealed record VendingPrizeAnnounced(CharacterId Actor, int ItemId, int Count) : IDomainEvent;

/// <summary>A reward drawn from a box (package, magic box or lottery) that the archive or rule marks notable.</summary>
public sealed record BoxRewardAnnounced(CharacterId Actor, int BoxItemId, int ItemId) : IDomainEvent;

/// <summary>A forging (separate upgrade) attempt at or past the server's announcement stage.</summary>
/// <param name="Stage">The forging stage attempted.</param>
public sealed record EquipmentForgingAnnounced(CharacterId Actor, int ItemId, byte ReinforcementLevel,
    byte Stage, bool Succeeded) : IDomainEvent;

/// <summary>A recipe (design) craft whose recipe declares a broadcast type.</summary>
public sealed record RecipeCraftAnnounced(CharacterId Actor, int ItemId, byte ReinforcementLevel) : IDomainEvent;

/// <summary>A Tower of Despair milestone floor (every tenth) cleared.</summary>
public sealed record DespairTowerFloorAnnounced(CharacterId Actor, int Floor) : IDomainEvent;

/// <summary>An item of at least the rule's rarity picked up in an ancient dungeon.</summary>
public sealed record AncientDungeonItemAnnounced(CharacterId Actor, int DungeonId, int ItemId) : IDomainEvent;
