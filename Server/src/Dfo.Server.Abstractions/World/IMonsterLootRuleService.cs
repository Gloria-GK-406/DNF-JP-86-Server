using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>How a pre-rolled item is consumed when its Cell actor dies.</summary>
public enum RolledDropDelivery
{
    Ground,
    DirectQuestItem,
}

/// <summary>One pre-rolled kill outcome, before ground pieces have been numbered.</summary>
/// <param name="ItemId">
/// What it is. <b>Zero is gold</b>, and its <paramref name="Count"/> is the amount rather than a
/// number of pieces -- which is the client's own reading, confirmed against captured frames where
/// a kill paid 38, 44 and 131 under identifier zero.
/// </param>
/// <param name="Rarity">
/// What quality it was drawn at. Gold has none and reads as common. Carried rather than discarded
/// because a drop that a bonus produced has to be able to say so later, and because a log that
/// cannot name the quality cannot be used to check the rates.
/// </param>
/// <param name="Durability">
/// How much wear a piece of equipment is created with, from its own declaration. Zero for
/// everything that is not equipment. It is not on the wire -- the kill's frame has no field for
/// it -- but the piece has to reach the bag carrying it, because a piece banked at zero durability
/// is a broken one.
/// </param>
public readonly record struct RolledDrop(
    int ItemId,
    int Count,
    DropRarity Rarity,
    DropItemKind Kind,
    ushort Durability = 0,
    CharacterId? OwnerCharacterId = null,
    RolledDropDelivery Delivery = RolledDropDelivery.Ground,
    int QuestId = 0,
    int GrantLimit = 0)
{
    /// <summary>What the wire calls gold.</summary>
    public const int GoldItemId = 0;

    /// <summary>Whether this is gold rather than an item.</summary>
    public bool IsGold => this.ItemId == GoldItemId;
}

/// <summary>One thing a kill produced, under the number the client will name it by.</summary>
public readonly record struct MonsterDrop(ushort Number, RolledDrop Rolled)
{
    public int ItemId => this.Rolled.ItemId;

    public int Count => this.Rolled.Count;

    public bool IsGold => this.Rolled.IsGold;
}

/// <summary>One kill, as much of it as deciding its drops depends on.</summary>
/// <param name="ActorSequence">
/// Which actor of the room died. It is part of what makes this occasion this occasion, so the
/// same kill asked about twice answers the same way.
/// </param>
/// <param name="PartyMemberCount">
/// How many frozen participants entered this run. It is part of the Cell creation input because
/// the archive declares a party multiplier for it.
/// </param>
public sealed record MonsterKill(
    CharacterId CharacterId,
    DungeonCellCreationContext Context,
    ushort ActorSequence,
    int MonsterLevel,
    int MonsterRank,
    int PartyMemberCount = 1,
    int MonsterCode = 0);

/// <summary>Quest-declared automatic grants for each participant of this Cell.</summary>
public interface IQuestMonsterDropRuleService
{
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterKill kill, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the deterministic loot attached to an actor while its cell is being constructed.
/// </summary>
public interface IMonsterLootRuleService
{
    ValueTask<IReadOnlyList<RolledDrop>> ResolveAsync(
        MonsterKill kill, CancellationToken cancellationToken);
}
