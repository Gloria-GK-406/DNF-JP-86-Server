using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.World;

public enum LostLandConsumableKind { Transformation, Teleport }
public sealed record LostLandConsumableDefinition(LostLandConsumableKind Kind,
    MazeCell? Destination, ImmutableArray<int> DisabledMaps);
public interface ILostLandConsumableDefinitionSource
{
    ValueTask<LostLandConsumableDefinition?> GetAsync(int itemId, CancellationToken token);
}
public interface ILostLandConsumableRuleService
{
    ValueTask<bool> SupportsAsync(int itemId, CancellationToken token);
    ValueTask<ILostLandConsumableRule?> PrepareAsync(int itemId, LostLandConsumableKind kind, MazeCell? origin,
        LostLandConsumableRoomFacts room, CancellationToken token);
}
public sealed record LostLandConsumableRoomFacts(int MapId, MazeCell Cell,
    IReadOnlyDictionary<MazeCell, DungeonCellDefinition> Cells);
public interface ILostLandConsumableRule
{
    ItemCooldownKey CooldownKey { get; }
    MazeCell? Destination { get; }
    ItemUsePlan? Build(BusinessSlot source, ItemReference item, ItemUseCandidate? character, DateTimeOffset at);
}
public interface ILostLandConsumableDomainService
{
    ValueTask<bool> SupportsAsync(int itemId, CancellationToken token);
    ValueTask<LostLandConsumableResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        LostLandConsumableKind kind, MazeCell? origin, IEffectFootprintRecorder footprint, CancellationToken token);
}

public sealed record LostLandConsumableResult(OpeningInventoryReceipt Inventory, DungeonRoomVisit? Movement);
