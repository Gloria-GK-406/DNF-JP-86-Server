namespace Dfo.Server.Abstractions.Content;

/// <summary>The three operands of the stackable template's [cooltime maintenance] declaration.</summary>
public readonly record struct ItemCooldownMaintenance(bool KeepOnRevive, bool KeepOnDungeonReset, bool PersistOnServer);
