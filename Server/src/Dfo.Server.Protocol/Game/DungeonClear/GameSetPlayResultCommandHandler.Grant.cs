using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Effects;
namespace Dfo.Server.Protocol.Game.DungeonClear;
internal sealed partial class GameSetPlayResultCommandHandler
{
    private ValueTask<ClearExperienceResult> GrantAsync(CommandExecutionContext execution,
        SelectedSessionRecipient actor, DungeonRunState run, ClearCircumstances clear, int settlementLevel, CancellationToken ct) =>
        units.WriteAsync(execution, new("grant-clear-experience", [actor], Footprint: CommandFootprintMode.NonRequest),
            (unit, token) => GrantBodyAsync(actor.Session.AccountId, actor.Session.CharacterId, run.Layout.DungeonId, clear, actor.Session, run.RunId.Value, settlementLevel, null, unit.Footprint, token), ct);
    internal async ValueTask<ClearExperienceResult> GrantBodyAsync(AccountId accountId, CharacterId characterId, int dungeonId, ClearCircumstances clear,
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
        var award = await characters.Get(characterId).AddExpAsync(new ClearExperienceContext(
                new ExperienceOccasion(ExperienceEvent.DungeonClear, accountId,
                    characterId, dungeonId), clear), now, session,
            footprint, cancellationToken).ConfigureAwait(false);
        var changed = award is not null && runId != Guid.Empty
            && await attendance.RecordClearAsync(accountId,runId,dungeonId,settlementLevel,now,cancellationToken);
        var unlocked = runs.Find(characterId)?.Layout.Mode is DungeonRunMode.Mirror or DungeonRunMode.Despair ? null
            : await difficulties.UnlockAfterClearAsync(characterId, dungeonId,
            (DungeonDifficulty)clear.Difficulty, cancellationToken).ConfigureAwait(false);
        return new(award, award?.Breakdown.Base ?? 0, award?.Breakdown.ClearRank ?? 0,changed,unlocked) { Despair = tower };
    }
}
