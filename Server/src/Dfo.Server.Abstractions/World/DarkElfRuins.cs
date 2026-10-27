using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

/// <summary>One directory row. The two unnamed PVF values retain their original meaning boundary.</summary>
public sealed record DarkElfRuinsDungeon(int Group, int DungeonId, int License, int Field4, int Field5)
{
    public IReadOnlyList<int> GuardianMazes { get; init; } = [];
    public IReadOnlyList<int> OrdinaryMazes { get; init; } = [];
    public IReadOnlyDictionary<int, DarkElfRuinsMazeFeatures> MazeFeatures { get; init; } = new Dictionary<int, DarkElfRuinsMazeFeatures>();
}
public sealed record DarkElfRuinsObjectGroup(int SelectCount, IReadOnlyList<DungeonObjectSpawn> Objects);
public sealed record DarkElfRuinsMazeFeatures(IReadOnlyList<DarkElfRuinsObjectGroup> ObjectGroups,
    IReadOnlyDictionary<MazeCell, MazeCell> Warps);
public sealed record DarkElfRuinsRate(int Entries, int PerTenThousand);
public sealed record DarkElfRuinsDrop(int ItemId, int Weight);
public sealed record DarkElfRuinsTierReward(int License, IReadOnlyList<ItemDeliveryGrant> Clear,
    IReadOnlyList<DarkElfRuinsDrop> GuardianDrops);
public sealed record DarkElfRuinsCatalog(IReadOnlyList<DarkElfRuinsDungeon> Dungeons,
    IReadOnlyDictionary<int, IReadOnlyList<int>> OpenDungeons,
    IReadOnlyDictionary<int, byte> Difficulties, int DailyEntries, int MonthlyGuardians,
    IReadOnlyList<DarkElfRuinsRate> GuardianRates, IReadOnlyList<int> GuardianLicenses,
    IReadOnlyList<DarkElfRuinsTierReward> Rewards, IReadOnlyDictionary<int, ItemDeliveryGrant> DailyRewards);

public interface IDarkElfRuinsContentSource { DarkElfRuinsCatalog Load(); }

public sealed record DarkElfRuinsEntryRule(DarkElfRuinsDungeon Directory, DungeonDefinition Dungeon,
    byte Difficulty, bool Open, DarkElfRuinsTierReward Rewards, ItemDeliveryGrant DailyReward);
public sealed record DarkElfRuinsMazeChoice(int Index, bool Guardian);

/// <summary>Static archive inputs and decisions, without character state.</summary>
public interface IDarkElfRuinsRuleService
{
    bool Supports(int dungeonId);
    DarkElfRuinsCatalog Catalog { get; }
    int EntryQuestId { get; }
    int ReviveLimit { get; }
    bool IsGuardian(int monsterCode);
    System.Collections.Immutable.ImmutableHashSet<int> RequiredCombatantCodes { get; }
    int MaximumParticipants { get; }
    ValueTask<DarkElfRuinsEntryRule> EntryAsync(int dungeonId, DayOfWeek day, CancellationToken ct);
    int GuardianRate(int monthlyEntryOrdinal);
    DarkElfRuinsProgress Advance(DarkElfRuinsProgress progress, int clearedLicense, bool revived);
    DarkElfRuinsMazeChoice ChooseMaze(DarkElfRuinsEntryRule rule, IReadOnlyList<DarkElfRuinsProgress> participants, int seed);
    ItemDeliveryGrant? GuardianDrop(DarkElfRuinsTierReward rewards, int ticket);
    DungeonLayout PrepareLayout(DungeonLayout layout, byte characterLevel, DarkElfRuinsEntryPlan plan);
    DarkElfRuinsReward FreezeReward(DarkElfRuinsEntryRule rule, bool guardianDefeated, int elapsedMilliseconds, int seed);
}
public sealed record DarkElfRuinsInventoryChanged(CharacterId CharacterId,
    IReadOnlyList<BusinessSlot> Slots) : IDomainEvent;

/// <summary>All regions share progression; dates preserve counters across reset boundaries.</summary>
public sealed record DarkElfRuinsProgress(int License = 1, int Clears = 0, DateOnly Day = default,
    int Entries = 0, DateOnly Month = default, int MonthlyEntries = 0, int Guardians = 0, long Revision = 0);
public sealed record DarkElfRuinsEntryPlan(DateOnly Day, bool Guardian, int License);
public sealed record DarkElfRuinsReward(int DungeonId, bool GuardianDefeated, int ElapsedMilliseconds,
    ItemDeliveryGrant Daily, ItemDeliveryGrant[] Clear, ItemDeliveryGrant[] Guardian, bool Claimed = false,
    int PresentationStage = 0);
public sealed record DarkElfRuinsSelection(byte DayIndex, byte Remaining, int License, int Clears,
    IReadOnlyList<int> DungeonIds);

public interface IDarkElfRuinsDomainStore
{
    ValueTask<DarkElfRuinsProgress> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask<bool> CompareExchangeAsync(CharacterId character, DarkElfRuinsProgress expected,
        DarkElfRuinsProgress replacement, CancellationToken ct);
    ValueTask<DarkElfRuinsReward?> RewardAsync(CharacterId character, Guid run, CancellationToken ct);
    ValueTask<bool> FreezeRewardAsync(CharacterId character, Guid run, DarkElfRuinsReward reward, CancellationToken ct);
    ValueTask<bool> ClaimRewardAsync(CharacterId character, Guid run, CancellationToken ct);
    ValueTask<int> AdvancePresentationAsync(CharacterId character, Guid run, CancellationToken ct);
    ValueTask<IReadOnlyList<Guid>> PendingRewardsAsync(CharacterId character, CancellationToken ct);
}

public interface IDarkElfRuinsDomainService : IDungeonBossClearHook
{
    bool Supports(int dungeonId);
    ValueTask<DarkElfRuinsSelection> SelectionAsync(CharacterId character, CancellationToken ct);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<DarkElfRuinsReward?> ResultAsync(SelectedSessionIdentity session, CancellationToken ct);
    ValueTask<bool> ClaimAsync(SelectedSessionIdentity session, CancellationToken ct);
    ValueTask RecoverRewardsAsync(SelectedSessionIdentity session, CancellationToken ct);
    bool CanRevive(CharacterId character, DungeonRunId run);
}
