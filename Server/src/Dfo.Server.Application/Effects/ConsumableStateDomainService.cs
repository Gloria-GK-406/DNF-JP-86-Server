using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Effects;

internal sealed class ConsumableStateDomainService(ICharacterFatigueDomainService fatigue, ICharacterFatigueTable fatigueTable,
    IConsumableUsageTable usage, IQuestDomainStore quests, ICharacterSkillTable skills,
    IDomainTransactionAccessor transactions) : IConsumableStateDomainService
{
    private IDomainTransaction Transaction => transactions.Current ?? throw new InvalidOperationException("State mutations require a transaction.");
    public async ValueTask<FatigueStateFacts> FatigueAsync(CharacterId id, CancellationToken ct) =>
        new((await fatigue.BalanceOfAsync(id, ct).ConfigureAwait(false)).Total, await fatigueTable.ReadAsync(id, ct).ConfigureAwait(false));
    public ValueTask<ConsumableUsageRow?> UsageAsync(CharacterId id, int itemId, CancellationToken ct) => usage.ReadAsync(id, itemId, ct);
    public async ValueTask<QuestStateFacts> QuestsAsync(AccountId account, CharacterId character, CancellationToken ct)
    {
        var session = quests.SessionIn(Transaction);
        var owner = new OwnedCharacterId(account, character);
        return new((await session.Carried.ReadAllAsync(owner, ct).ConfigureAwait(false)).OrderBy(x => x.Slot).ToImmutableArray(),
            (await session.Tracked.ReadAllAsync(owner, ct).ConfigureAwait(false)).OrderBy(x => x.SlotIndex).ToImmutableArray(),
            (await session.Cleared.ReadAllAsync(owner, ct).ConfigureAwait(false)).OrderBy(x => x.QuestId).ToImmutableArray());
    }
    public async ValueTask SetFatigueAsync(CharacterId id, SetFatigueStateEffect effect, CancellationToken ct)
    {
        _ = Transaction;
        if (await fatigueTable.TryWriteAsync(id, effect.Expected, effect.Value, ct).ConfigureAwait(false) != 1) throw Changed();
    }
    public async ValueTask SetUsageAsync(CharacterId id, SetItemUsageStateEffect effect, CancellationToken ct)
    {
        _ = Transaction;
        if (await usage.TryWriteAsync(id, effect.ItemId, effect.Expected, effect.Value.UsedCount,
                effect.Value.LastUsedAt!.Value, ct).ConfigureAwait(false) != 1) throw Changed();
    }
    public async ValueTask SetCarriedAsync(AccountId account, CharacterId id, SetCarriedQuestEffect effect, CancellationToken ct)
    {
        var session = quests.SessionIn(Transaction); var owner = new OwnedCharacterId(account, id);
        var row = (await session.Carried.ReadAllAsync(owner, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Slot == effect.Expected.Slot);
        if (row is null || row.QuestId != effect.Expected.QuestId || row.TriggerValue != effect.Expected.TriggerValue) throw Changed();
        if (effect.Value is null) session.Carried.Clear(owner, row.Slot);
        else session.Carried.Write(owner, effect.Value with { ExpectedTriggerValue = row.ExpectedTriggerValue ?? row.TriggerValue });
    }
    public async ValueTask RemoveTrackedAsync(AccountId account, CharacterId id, RemoveTrackedQuestEffect effect, CancellationToken ct)
    {
        var session = quests.SessionIn(Transaction); var owner = new OwnedCharacterId(account, id);
        if (!(await session.Tracked.ReadAllAsync(owner, ct).ConfigureAwait(false)).Contains(effect.Expected)) throw Changed();
        session.Tracked.Clear(owner, effect.Expected.SlotIndex);
    }
    public async ValueTask SetClearedAsync(AccountId account, CharacterId id, SetClearedQuestEffect effect, CancellationToken ct)
    {
        var session = quests.SessionIn(Transaction); var owner = new OwnedCharacterId(account, id);
        var row = (await session.Cleared.ReadAllAsync(owner, ct).ConfigureAwait(false)).SingleOrDefault(x => x.QuestId == effect.QuestId);
        if (row?.Mark != effect.ExpectedMark) throw Changed();
        session.Cleared.Write(owner, new(effect.QuestId, effect.Mark));
    }
    public async ValueTask SetSkillAsync(CharacterId id, SetLearnedSkillEffect effect, CancellationToken ct)
    {
        _ = Transaction;
        var row = (await skills.ReadLevelsAsync(id, ct).ConfigureAwait(false)).SingleOrDefault(x => x.PageIndex == effect.Page && x.Slot == effect.Slot);
        if (effect.ExpectedLevel is null)
        {
            if (row is not null || effect.Level is null) throw Changed();
            await skills.InsertAsync(id, effect.Page, effect.Slot, effect.SkillId, effect.Level.Value, ct).ConfigureAwait(false);
        }
        else
        {
            if (row is null || row.SkillId != effect.SkillId || row.Level != effect.ExpectedLevel) throw Changed();
            var count = effect.Level is { } level
                ? await skills.SetLevelAsync(id, effect.Page, effect.Slot, level, ct).ConfigureAwait(false)
                : await skills.RemoveAsync(id, effect.Page, effect.Slot, ct).ConfigureAwait(false);
            if (count != 1) throw Changed();
        }
    }
    private static InvalidOperationException Changed() => new("State target changed since its effect was decided.");
}
