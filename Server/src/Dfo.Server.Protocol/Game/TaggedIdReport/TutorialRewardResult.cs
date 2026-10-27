using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>One item a tutorial step declares.</summary>
public sealed record DeclaredRewardItem(int ItemId, int Count);

/// <summary>What actually reached the inventory, and where.</summary>
public sealed record PlacedRewardItem(int SlotIndex, int ItemId, int Count);

/// <summary>Everything one tutorial step managed to hand over.</summary>
public sealed record TutorialRewardResult(
    IReadOnlyList<PlacedRewardItem> Placed,
    IReadOnlyList<InventoryRuntimeValueState>? AccountMaterials = null);
