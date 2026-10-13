using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestQueryDomainService(IQuestContentRuleService content,
    ICharacterQuestLogDomainService logs, IQuestFactsDomainService facts, IQuestStateRuleService state,
    IQuestAvailabilityRuleService availability, ICharacterTable owners,
    ICycleDungeonEntryDomainService? cycles = null, IMirrorAradQuestDomainStore? mirror = null,
    IDungeonRunDomainService? runs = null, IMirrorAradQuestRuleService? mirrorRules = null) : IQuestQueryDomainService
{
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
        var temporary = await MirrorAsync(account, character, null, ct).ConfigureAwait(false);
        if (temporary?.Accepted is { } current)
        {
            result.RemoveAll(x => x.Quest.QuestId == current.Quest.QuestId);
            result.Add(current);
        }
        return result.OrderBy(x => x.Quest.Slot).ToArray();
    }

    public async ValueTask<AvailableQuestEntryState> ReadAvailableAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await availability.ReadAvailableAsync(await facts.SubjectAsync(account, character, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    public async ValueTask<QuestStatus> QueryAsync(AccountId account, CharacterId character, int questId, CancellationToken ct)
    {
        await AuthorizeAsync(account, character, ct).ConfigureAwait(false);
        var definition = await content.FindAsync(questId, ct).ConfigureAwait(false);
        if (definition is null) return new(questId, false, QuestLifecycle.NotAccepted, false);
        if (definition.Grade == QuestGrade.Crack)
            return await MirrorAsync(account, character, questId, ct).ConfigureAwait(false)
                ?? new(questId, true, QuestLifecycle.NotAccepted, false);
        var accepted = (await ReadAcceptedAsync(account, character, ct).ConfigureAwait(false))
            .FirstOrDefault(x => x.Quest.QuestId == questId);
        if (accepted is not null) return new(questId, true, QuestLifecycle.Accepted, false, accepted);
        var log = logs.LogOf(account, character);
        var completed = (await log.ClearedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == questId && x.Mark != 0);
        var canAccept = definition.Grade == QuestGrade.Circle
            ? cycles is not null && await cycles.CanAcceptAsync(account, character, questId, ct).ConfigureAwait(false)
            : (await ReadAvailableAsync(account, character, ct).ConfigureAwait(false)).Quests.Any(x => x.QuestId == questId);
        return new(questId, true, completed ? QuestLifecycle.Completed : QuestLifecycle.NotAccepted, canAccept);
    }

    private async ValueTask<QuestStatus?> MirrorAsync(AccountId account, CharacterId character, int? requested, CancellationToken ct)
    {
        var run = runs?.Find(character);
        if (run?.Layout.Mirror is not { } entry || mirror is null || mirrorRules is null
            || requested is { } id && id != entry.QuestId
            || !run.Participants.Any(x => x.AccountId == account && x.CharacterId == character)) return null;
        var objective = await mirrorRules.ResolveAsync(run.Layout.DungeonId, entry.QuestId, ct).ConfigureAwait(false);
        if (objective is null) return null;
        var record = await mirror.ReadAsync(character, run.RunId, ct).ConfigureAwait(false);
        if (record is null) return new(entry.QuestId, true, QuestLifecycle.NotAccepted, true);
        if (record.QuestId != entry.QuestId) return null;
        if (record.RewardClaimed) return new(entry.QuestId, true, QuestLifecycle.Completed, false);
        if (record.GivenUp) return new(entry.QuestId, true, QuestLifecycle.NotAccepted, false);
        var canComplete = mirrorRules.CanFinish(objective, record, run);
        var trigger = canComplete ? 0 : record.TriggerValue;
        return new(entry.QuestId, true, QuestLifecycle.Accepted, false,
            new(new(short.MaxValue, entry.QuestId, unchecked((int)trigger)), []) { CompletionAllowed = canComplete });
    }
}
