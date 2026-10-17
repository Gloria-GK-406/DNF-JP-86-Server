using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Marriage;

internal sealed partial class MarriageDomainService
{
    public async ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account, CharacterId character, CancellationToken ct) =>
        await QuestRelationAsync(account, character, ct).ConfigureAwait(false) is null ? [] : trials.QuestIds;

    public async ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, CancellationToken ct) =>
        await QuestRelationAsync(account, character, ct).ConfigureAwait(false) is { } relation
        && await trials.CanAcceptAsync(account, character, quest, relation.Id, ct).ConfigureAwait(false);

    public async ValueTask BindAsync(AccountId account, CharacterId character, int quest, CancellationToken ct)
    {
        var relation = await QuestRelationAsync(account, character, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wedding quest lost its engagement before acceptance.");
        await trials.BindAsync(character, quest, relation.Id, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, CancellationToken ct) =>
        await QuestRelationAsync(account, character, ct).ConfigureAwait(false) is { } relation
        && await trials.CanCompleteAsync(account, character, quest, relation.Id, ct).ConfigureAwait(false);

    /// <summary>Counts a verified pair clear towards both partners' trial when the two participants are exactly this relation.</summary>
    internal async ValueTask RecordTrialClearAsync(DungeonMapCleared clear, CancellationToken ct)
    {
        if (!await trials.IsTrialClearAsync(clear, ct).ConfigureAwait(false)) return;
        var relation = await QuestRelationAsync(clear.Participants[0].AccountId, clear.Participants[0].CharacterId, ct).ConfigureAwait(false);
        if (relation is null || !clear.Participants.Select(x => x.CharacterId).ToHashSet()
            .SetEquals([relation.Proposer.CharacterId, relation.Recipient.CharacterId])) return;
        if (clear.Participants.Any(x => !CoupleLedger.Belongs(relation, x.AccountId, x.CharacterId))) return;
        await trials.RecordClearAsync(clear, relation.Id, ct).ConfigureAwait(false);
    }
}
