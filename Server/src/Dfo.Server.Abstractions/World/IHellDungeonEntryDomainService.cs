using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.World;

public enum DungeonEntryKind { Normal, ManualHell, RandomHell }

/// <summary>Business facts for one entry, independent of packet/connection identities.</summary>
public sealed record DungeonEntryContext(
    int DungeonId, DungeonEntryFacts Facts, byte RequestMode, byte PracticeMode,
    IReadOnlyList<DungeonRunParticipant> Participants, int? MirrorQuestId = null);

public interface IDungeonEntryDecisionDomainService
{
    DungeonEntryKind ResolveEntryKind(DungeonEntryContext context);
}

public sealed record HellTicketAlternative(int ItemId, int Count);
public sealed record HellActorGroup(int Index, byte Difficulty, IReadOnlyList<DungeonRoomActor> Actors);
public sealed record HellGroupCandidate(int GroupIndex, int Weight, ushort Order);
public sealed record HellRoomDefinition(int MapId, MazeCell Cell, IReadOnlyList<HellGroupCandidate> Groups);
public sealed record HellDungeonDefinition(
    bool Enabled, IReadOnlyList<int> RequiredQuestIds,
    IReadOnlyList<HellTicketAlternative> FreePasses, IReadOnlyList<int> TicketIds,
    IReadOnlyDictionary<int, HellRoomDefinition> Rooms);
public sealed record HellDifficultyWeight(byte Difficulty, int Weight)
{
    public int RewardRollCount { get; init; }
}
public sealed record HellPartyDefinition(
    IReadOnlyList<HellDifficultyWeight> ManualDifficulties,
    IReadOnlyList<HellActorGroup> Groups);

public interface IHellDungeonDefinitionSource
{
    ValueTask<HellDungeonDefinition?> GetAsync(int dungeonId, CancellationToken cancellationToken);
    ValueTask<HellPartyDefinition> GetPartyAsync(CancellationToken cancellationToken);
}

public sealed record HellEntryActor(ushort Order, int GroupIndex, DungeonRoomActor Actor);
/// <summary>Chosen once at entry and retained by the shared run through its layout.</summary>
public sealed record HellEntryPlan(byte Difficulty, MazeCell Cell, int MapId, IReadOnlyList<HellEntryActor> Actors);

public interface IHellDungeonEntryRuleService
{
    ValueTask<HellDungeonDefinition?> GetRulesAsync(int dungeonId, CancellationToken cancellationToken);
    ValueTask<HellEntryPlan?> PrepareAsync(HellDungeonDefinition rules, DungeonLayout layout,
        int seed, CancellationToken cancellationToken);
    IReadOnlyList<HellTicketAlternative> TicketAlternatives(HellDungeonDefinition rules, byte minimumLevel);
}

public interface IHellDungeonEntryDomainService
{
    ValueTask<DungeonEntryAttempt> EnterManualAsync(DungeonEntryContext context, CancellationToken cancellationToken);
    ValueTask<DungeonEntryAttempt> EnterRandomAsync(DungeonEntryContext context, CancellationToken cancellationToken);
}

public sealed record DungeonEntryInventoryChange(
    CharacterId CharacterId, BusinessSlot Slot, InventoryItemCoreState? Remaining);
