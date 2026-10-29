using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class LostLandDungeonDomainService(ILostLandRuleService rules,
    ICharacterFatigueDomainService fatigue, IUsageQuotaDomainService quotas, IDailyResetRuleService days,
    IDungeonAdmissionDomainService admission, IDungeonVisitDomainService visits,
    IDungeonRunDomainService runs, TimeProvider clock) : ILostLandDungeonDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public ValueTask<LostLandQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken cancellationToken) =>
        ReadQuotaAtAsync(character, clock.GetUtcNow(), cancellationToken);

    public async ValueTask<LostLandQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken cancellationToken)
        => (await DecideQuotaAtAsync(character, at, cancellationToken).ConfigureAwait(false)).View;

    private async ValueTask<LostLandQuotaDecision> DecideQuotaAtAsync(CharacterId character, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        // Use the fatigue owner's game-day boundary; refunds do not erase its consumed total.
        var period = DateOnly.FromDateTime(days.PeriodStart(at).ToOffset(TimeSpan.FromHours(8)).DateTime);
        var balance = await fatigue.BalanceAtAsync(character, at, cancellationToken).ConfigureAwait(false);
        var used = await quotas.ReadAsync(character, UsageQuotaKey.LostLand(period), cancellationToken).ConfigureAwait(false);
        return await rules.QuotaAsync(period, balance.ConsumedToday, used?.UsedCount ?? 0, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken)
    {
        if (!Supports(context.DungeonId) || context.Participants.Count != 1 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.MirrorQuestId is not null || (int)context.Facts.Difficulty != 0
            || context.Facts.Intent.DirectedDungeonId is { } directed && directed != context.DungeonId
            || context.Facts.Intent.Kind is DungeonEntryIntentKind.Cycle or DungeonEntryIntentKind.OrdinaryRechallenge)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (runs.Find(context.Participants[0].CharacterId) is not null)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        DungeonLayout layout;
        LostLandDefinition definition;
        LostLandQuotaDecision quota;
        try
        {
            definition = await rules.DefinitionAsync(cancellationToken).ConfigureAwait(false);
            quota = await DecideQuotaAtAsync(context.Participants[0].CharacterId, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            layout = await rules.LayoutAsync(context.Facts, Random.Shared.Next(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false, RequiredItemsOverride = definition.Dungeon.RequiredItems,
            Quota = quota.Admission,
        }, (receipt, token) => visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token), cancellationToken).ConfigureAwait(false);
    }
}
