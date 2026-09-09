namespace Dfo.Server.Data.Inventory;

/// <summary>Facts supplied when an item first enters the world, not when it moves.</summary>
public sealed record InventoryItemCreationFacts(
    InventoryAvatarDetailState? Avatar = null,
    InventoryCreatureDetailState? Creature = null);

public sealed record InventoryCreatureDetailState(
    int Field04,
    byte ModeFlag,
    int ProgressValue,
    int Mode1Field0A,
    int Mode1Field0B,
    int FieldAfterValue,
    byte[] CreatureText,
    byte TailFlag);
