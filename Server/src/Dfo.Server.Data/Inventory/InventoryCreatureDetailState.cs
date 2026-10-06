namespace Dfo.Server.Data.Inventory;

public sealed record InventoryCreatureDetailState(
    int Field04,
    byte ModeFlag,
    int ProgressValue,
    int Mode1Field0A,
    int Mode1Field0B,
    int FieldAfterValue,
    byte[] CreatureText,
    byte TailFlag);
