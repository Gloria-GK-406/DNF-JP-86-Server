using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

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

/// <summary>Participant identity frozen at entry; no inventory or quest snapshot.</summary>
public readonly record struct MonsterDropParticipant(AccountId AccountId, CharacterId CharacterId);

/// <summary>
/// Values that identify this generation occasion. Mutable character facts are read inside the
/// domain service. Participants are frozen run members, not the current town party.
/// </summary>
public sealed record MonsterDropContext(
    DungeonRunId RunId,
    int DungeonId,
    int Seed,
    MazeCell Cell,
    int MapId,
    DungeonDifficulty Difficulty,
    ushort ActorSequence,
    int MonsterLevel,
    int MonsterRank,
    CharacterId CharacterId,
    ImmutableArray<MonsterDropParticipant> Participants)
{
    public int PartyMemberCount => this.Participants.Length;
}

/// <summary>Input shared by drop sources behind the generation boundary.</summary>
public sealed record MonsterDropRequest(int MonsterCode, MonsterDropContext Context)
{
    public CharacterId CharacterId => this.Context.CharacterId;
    public ushort ActorSequence => this.Context.ActorSequence;
    public int MonsterLevel => this.Context.MonsterLevel;
    public int MonsterRank => this.Context.MonsterRank;
    public int PartyMemberCount => this.Context.PartyMemberCount;
}

/// <summary>Reads current quest and inventory facts for the identified participants.</summary>
public interface IQuestMonsterDropDomainService
{
    ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(
        MonsterDropRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Generates the unnumbered drops attached to a monster at cell creation. Reads current facts,
/// but never writes inventory, quest progress, ground state or protocol messages. Ordinary and
/// quest sources run in that order; new sources belong behind this boundary.
/// </summary>
public interface IMonsterDropDomainService
{
    ValueTask<IReadOnlyList<RolledDrop>> GenerateAsync(
        int monsterId, MonsterDropContext context, CancellationToken cancellationToken);
}
