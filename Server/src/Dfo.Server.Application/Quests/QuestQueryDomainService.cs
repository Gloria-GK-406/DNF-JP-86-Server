using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestQueryDomainService(
    ICharacterQuestLogDomainService logs, IQuestFactsDomainService facts, IQuestStateRuleService state,
    IQuestAvailabilityRuleService availability, ICharacterTable owners, IDungeonRunQueryDomainService runs,
    IMirrorAradQuestDomainStore mirror, IMirrorAradQuestRuleService mirrorRules) : IQuestQueryDomainService
{
    public bool IsCycleClear(DungeonRunState? run) => CycleQuestSubmission.IsCycleClear(run);

    public bool CanSubmitCycleQuest(DungeonRunState? run, IReadOnlyList<AcceptedQuestState> accepted) =>
        CycleQuestSubmission.IsReady(run, accepted);

    private async ValueTask AuthorizeAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) != account)
            throw new InvalidDataException("Quest query character ownership does not match.");
    }

    public async ValueTask<IReadOnlyList<AcceptedQuestState>> ReadAcceptedAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        await AuthorizeAsync(account, character, ct).ConfigureAwait(false);
        var log = logs.LogOf(account, character);
        var carried = await log.CarriedAsync(ct).ConfigureAwait(false);
        var cleared = await log.ClearedAsync(ct).ConfigureAwait(false);
        var evaluated = carried.Count == 0 ? [] : await state.EvaluateAsync(carried, cleared,
            await facts.HoldingsAsync(character, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        var result = evaluated.ToList();
        if (await MirrorAcceptedAsync(account, character, ct).ConfigureAwait(false) is { } current)
        {
            result.RemoveAll(x => x.Quest.QuestId == current.Quest.QuestId);
            result.Add(current);
        }
        return result.OrderBy(x => x.Quest.Slot).ToArray();
    }

    public async ValueTask<AvailableQuestEntryState> ReadAvailableAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await availability.ReadAvailableAsync(await facts.SubjectAsync(account, character, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    private async ValueTask<AcceptedQuestState?> MirrorAcceptedAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        var run = runs.Find(character);
        if (run?.Layout.Mode is not DungeonRunMode.Mirror { Plan: var entry }
            || !run.Participants.Any(x => x.AccountId == account && x.CharacterId == character)) return null;
        var objective = await mirrorRules.ResolveAsync(run.Layout.DungeonId, entry.QuestId, ct).ConfigureAwait(false);
        if (objective is null) return null;
        var record = await mirror.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (record is null || record.QuestId != entry.QuestId || record.RewardClaimed || record.GivenUp) return null;
        var canComplete = mirrorRules.CanFinish(objective, record, run);
        var trigger = canComplete ? 0 : record.TriggerValue;
        return new(new(short.MaxValue, entry.QuestId, unchecked((int)trigger)), []) { CompletionAllowed = canComplete };
    }
}
