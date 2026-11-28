using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record TeleportConsumableDefinition(IReadOnlyList<int> TownIds);
public interface ITeleportConsumableDefinitionSource
{
    ValueTask<TeleportConsumableDefinition?> GetAsync(int itemId, CancellationToken ct);
}
public sealed record TeleportConsumableRule(TeleportConsumableDefinition Destination, ItemUseRule Use);
public sealed record TeleportConsumablePlan(ItemUsability Status, IReadOnlyList<DomainEffect> Effects);
public interface ITeleportConsumableRuleService
{
    ValueTask<TeleportConsumableRule?> RuleForAsync(int itemId, CancellationToken ct);
    ValueTask<TeleportConsumablePlan> BuildAsync(TeleportConsumableRule rule, BusinessSlot source,
        ItemReference item, ItemUseCandidate facts, CharacterPositionState standing,
        byte townId, ChannelEntryIdentity channel, TownAreaKey sourceArea, DateTimeOffset at, CancellationToken ct);
}
public sealed record MoveToTownEffect(CharacterPositionState Expected, CharacterPositionState Destination,
    bool PersistsPosition, TownAreaKey SourceArea, int ChannelId) : DomainEffect;

/// <summary>The town membership and store changes published by this transaction on commit.</summary>
public interface ITownCrossingResult
{
    TownAreaMembershipChange? Change { get; }
}

public sealed record TownPositionChange(CharacterPositionState Before, CharacterPositionState After) : EffectChange
{
    public ITownCrossingResult? Crossing { get; init; }
}

public sealed record TeleportConsumableReceipt(SelectedSessionIdentity Session, BusinessSlot Source,
    int ItemId, int Remaining, TownPositionChange Movement, TimeSpan Cooldown, DateTimeOffset At,
    ItemSnapshot? RemainingCore, ItemIdentity? Identity)
{
    public CharacterPositionState Position => Movement.After;
}
public sealed record TeleportConsumableResult(ItemUsability Status, TeleportConsumableReceipt? Receipt = null);
public interface ITeleportConsumableDomainService
{
    ValueTask<TeleportConsumableResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        int expectedItemId, byte townId, ChannelEntryIdentity channel, IEffectFootprintRecorder footprint, CancellationToken ct);
}
