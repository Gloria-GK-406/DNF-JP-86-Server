using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

/// <summary>Entry-frozen exploration rules; mutable room and actor state belongs to the run.</summary>
public sealed record LostLandEntryPlan(int FirstLimitSeconds, int SecondLimitSeconds, TimeSpan FirstPeriod, TimeSpan SecondPeriod,
    ImmutableArray<LostLandWarp> Warps, ImmutableArray<LostLandPatrol> Patrols)
{
    public TimeSpan FeverDuration { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsDeclaredWarp(MazeCell source, MazeCell target) =>
        Warps.Any(warp => warp.Source == source && warp.Target == target);
}

/// <summary>Run-owned charge and deadline; room changes do not create another fever cycle.</summary>
public sealed record LostLandFeverState(byte Percent = 0, DateTimeOffset? ActiveUntil = null);

public readonly record struct LostLandFeverExpiry(DungeonRunId RunId, DateTimeOffset ActiveUntil);

public sealed record LostLandQuotaSnapshot(DateOnly Period, int Maximum, int ConsumedFatigue, int Used)
{
    public int Remaining => Math.Max(0, Maximum - Used);
}

public interface ILostLandRuleService
{
    bool Supports(int dungeonId);
    ValueTask<LostLandDefinition> DefinitionAsync(CancellationToken cancellationToken);
    ValueTask<int> MaximumEntriesAsync(int consumedFatigue, CancellationToken cancellationToken);
    ValueTask<DungeonLayout> LayoutAsync(DungeonEntryFacts facts, int seed, CancellationToken cancellationToken);
}

public interface ILostLandDungeonDomainService
{
    bool Supports(int dungeonId);
    ValueTask<LostLandQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken cancellationToken);
    ValueTask<LostLandQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken cancellationToken);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken);
}

/// <summary>Local acceptance overrides; defaults preserve the archive's normal rules.</summary>
public sealed record LostLandRuntimePolicy(int? LimitSecondsOverride = null, bool UnlimitedEntriesForTesting = false);

public sealed record LostLandExplorationState(DateTimeOffset EndsAt, bool IsEnding = false, bool NoticeRegistered = false)
{
    public bool HasExpired(DateTimeOffset at) => IsEnding || at >= EndsAt;
}
public readonly record struct LostLandCompletion(DungeonRunId RunId, DateTimeOffset EndsAt);
