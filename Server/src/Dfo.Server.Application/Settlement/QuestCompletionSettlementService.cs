using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Settlement;

internal sealed class QuestCompletionSettlementService(IQuestCompletionDomainService completion,
    IQuestDefinitionDomainService definitions, IApplyEffectsDomainService apply, IEventService events,
    IMirrorAradQuestDomainService mirror) : IQuestCompletionSettlementDomainService
{
    public async ValueTask<QuestCompletionReceipt> FinishAsync(AccountId account, CharacterId character, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, CancellationToken ct, SelectedSessionIdentity? session = null)
    {
        var context = new EffectExecutionContext(account, character, at, session);
        var definition = await definitions.FindAsync(quest, ct).ConfigureAwait(false);
        if (definition?.Grade == QuestGrade.Crack)
        {
            var prepared = await mirror.PrepareCompletionAsync(account, character, quest, selection, count, at, session, ct).ConfigureAwait(false);
            if (!prepared.Prepared) return new(false, quest, null, null, null, null, null) { MirrorCompletion = true };
            var applied = new ApplyEffectsResult([]);
            if (!prepared.AlreadyClaimed)
            {
                applied = await apply.ApplyAsync(context, prepared.Effects, ct).ConfigureAwait(false);
                await events.DirectAsync(prepared.QuotaChange ?? throw new InvalidOperationException("Missing mirror quota decision."), ct).ConfigureAwait(false);
                await mirror.CommitCompletionAsync(account, character, prepared, ct).ConfigureAwait(false);
            }
            return new(true, quest, prepared.Objective, prepared.Award, applied,
                await mirror.SnapshotAsync(account, character, ct).ConfigureAwait(false), null) { MirrorCompletion = true };
        }
        var ordinary = await completion.PrepareAsync(account, character, quest, selection, count, at, ct, session).ConfigureAwait(false);
        if (!ordinary.Prepared) return new(false, quest, null, null, null, ordinary.Projection, null);
        var plan = ordinary.Plan ?? throw new InvalidOperationException("Missing prepared quest plan.");
        var result = await apply.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        await events.EnqueueAsync(new QuestCompleted(character, quest), ct).ConfigureAwait(false);
        var projection = plan.Projection is null ? null : await mirror.MergeAsync(character, plan.Projection, ct).ConfigureAwait(false);
        return new(true, quest, ordinary.Objective, plan.Award, result, projection, plan.GrowType) { Awakening = plan.Awakening };
    }
}
