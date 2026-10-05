namespace Dfo.Server.Abstractions.World.IllusionTower;

// Temporary client probe: preserve the carried inventory and refuse tower pickups.
public sealed record TowerInventoryExperiment(bool KeepCarriedInventory = false);
