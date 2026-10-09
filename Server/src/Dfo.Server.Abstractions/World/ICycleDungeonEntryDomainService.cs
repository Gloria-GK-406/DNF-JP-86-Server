using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record CycleDungeonCandidate(int DungeonId, int QuestId);
public sealed record CycleDungeonRegion(int WorldMapId, int OpenQuestId, int DeclaredDifficultyLevel,
    IReadOnlyList<CycleDungeonCandidate> Candidates, IReadOnlyList<QuestRewardItemGrant> Rewards);

public interface ICycleDungeonDefinitionSource
{
    IReadOnlyList<CycleDungeonRegion> Load();
}

public interface ICycleDungeonRuleService
{
    ValueTask<CycleDungeonRegion?> EligibleAsync(int dungeonId, int questId, WorldContentSubject subject,
        CancellationToken cancellationToken);
    IReadOnlyList<QuestRewardItemGrant> RewardsForQuest(int questId);
}

public sealed record CycleDungeonPreparation(bool Accepted, int QuestId = 0, string? Refusal = null);

/// <summary>Prepare a generation-owned selection; quest acceptance and entry remain separate commands.</summary>
public interface ICycleDungeonEntryDomainService
{
    ValueTask<CycleDungeonPreparation> PrepareAsync(SelectedSessionIdentity session, int dungeonId, int questId,
        CancellationToken cancellationToken);
    ValueTask<bool> CanAcceptAsync(AccountId accountId, CharacterId characterId, int questId,
        CancellationToken cancellationToken);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken);
}
