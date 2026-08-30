using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>One thing a kill produced, before it has been given a number.</summary>
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
    int ItemId, int Count, DropRarity Rarity, DropItemKind Kind, ushort Durability = 0)
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
/// How many were in the party. One for a character playing alone, which is every character for
/// now; it is a parameter rather than a constant because the archive declares a multiplier for it
/// and hiding that behind a hard-coded one would make the table's own group unreachable.
/// </param>
public sealed record MonsterKill(
    CharacterId CharacterId,
    DungeonRunState Run,
    ushort ActorSequence,
    int MonsterLevel,
    int MonsterRank,
    int PartyMemberCount = 1);

/// <summary>
/// What a monster's death is worth in things on the floor.
/// </summary>
/// <remarks>
/// One interface for one way of playing. The card flip, the abyss and the dimensional dungeons
/// each produce and deliver something differently shaped -- one piece against a list, into the bag
/// against onto the floor -- and folding them together would give a signature that is the least
/// common denominator of all of them plus a mode enum. They share the rules and the pools
/// underneath instead, which is where the sharing is real.
/// </remarks>
public interface IMonsterDropService
{
    /// <summary>
    /// What this kill dropped, numbered and recorded against the run.
    /// </summary>
    /// <remarks>
    /// May be empty, which is what most kills are.
    /// </remarks>
    ValueTask<IReadOnlyList<MonsterDrop>> ForKillAsync(
        MonsterKill kill, CancellationToken cancellationToken);
}
