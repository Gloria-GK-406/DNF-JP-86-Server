using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonClearSettlementDomainService(
    IDungeonRunSettlementDomainService runs,
    ICharacterExperienceAwardDomainService experience,
    ICharacterDungeonDifficultyDomainService difficulties,
    IWeeklyAttendanceDomainService attendance,
    IQuestProgressDomainService quests,
    IDespairTowerDomainService despair,
    IAccountLuckyStarDomainService luckyStars,
    TimeProvider clock) : IDungeonClearSettlementDomainService
{
    public async ValueTask<ClearExperienceResult> GrantAsync(AccountId accountId, CharacterId characterId, int dungeonId, ClearCircumstances clear,
        SelectedSessionIdentity? session, Guid runId, int settlementLevel, DateTimeOffset? settledAt, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        var now = settledAt ?? clock.GetUtcNow();
        DespairTowerSettlement? tower = null;
        if (runs.Find(characterId) is { Layout.Mode: DungeonRunMode.Despair } towerRun)
        {
            tower = await (despair ?? throw new InvalidOperationException("Despair settlement is unavailable."))
                .SettleAsync(accountId, characterId, new(runId), cancellationToken).ConfigureAwait(false);
            if (tower is null) return new(null, 0, 0);
        }
        if (runs.Find(characterId) is { Layout.Mode: DungeonRunMode.Mirror } mirror
            && mirror.RunId.Value == runId && mirror.ClearOpen)
            await quests.ReportAsync(accountId, characterId,
                [new QuestDungeonCleared(dungeonId, mirror.Difficulty, mirror.RunId, mirror.ClearedAt ?? now)], cancellationToken).ConfigureAwait(false);
        if (runId != Guid.Empty)
            await luckyStars.GrantForClearAsync(accountId, characterId, new(runId), cancellationToken).ConfigureAwait(false);
        var award = await experience.AwardAsync(new ClearExperienceContext(
                new ExperienceOccasion(ExperienceEvent.DungeonClear, accountId,
                    characterId, dungeonId), clear), now, session,
            footprint, cancellationToken).ConfigureAwait(false);
        var changed = award is not null && runId != Guid.Empty
            && await attendance.RecordClearAsync(accountId,runId,dungeonId,settlementLevel,now,cancellationToken);
        var unlocked = runs.Find(characterId)?.Layout.Mode.Capabilities.UnlocksDifficulty is false ? null
            : await difficulties.UnlockAfterClearAsync(characterId, dungeonId,
            (DungeonDifficulty)clear.Difficulty, cancellationToken).ConfigureAwait(false);
        return new(award, award?.Breakdown.Base ?? 0, award?.Breakdown.ClearRank ?? 0,changed,unlocked) { Despair = tower };
    }
}
