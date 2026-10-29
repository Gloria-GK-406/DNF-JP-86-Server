using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class LostLandDungeonDomainService(ILostLandRuleService rules,
    ICharacterFatigueDomainService fatigue, IUsageQuotaDomainService quotas, IDailyResetRuleService days,
    IDungeonAdmissionDomainService admission, IDungeonVisitDomainService visits,
    IDungeonRunDomainService runs, TimeProvider clock, LostLandRuntimePolicy policy) : ILostLandDungeonDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public ValueTask<LostLandQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken cancellationToken) =>
        ReadQuotaAtAsync(character, clock.GetUtcNow(), cancellationToken);

    public async ValueTask<LostLandQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken cancellationToken)
    {
        // Use the fatigue owner's game-day boundary; refunds do not erase its consumed total.
        var period = DateOnly.FromDateTime(days.PeriodStart(at).ToOffset(TimeSpan.FromHours(8)).DateTime);
        var balance = await fatigue.BalanceAtAsync(character, at, cancellationToken).ConfigureAwait(false);
        if (balance.ConsumedToday < 0) throw new InvalidDataException("Invalid LostLand consumed fatigue.");
        // Acceptance uses a virtual 1/1 view, preserving the normal ledger for later restoration.
        if (policy.UnlimitedEntriesForTesting) return new(period, 1, balance.ConsumedToday, 0);
        var maximum = await rules.MaximumEntriesAsync(balance.ConsumedToday, cancellationToken).ConfigureAwait(false);
        var used = await quotas.ReadAsync(character, UsageQuotaKey.LostLand(period), cancellationToken).ConfigureAwait(false);
        if (balance.ConsumedToday < 0 || used?.UsedCount < 0) throw new InvalidDataException("Invalid LostLand entry counters.");
        return new(period, maximum, balance.ConsumedToday, used?.UsedCount ?? 0);
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
        LostLandQuotaSnapshot quota;
        try
        {
            definition = await rules.DefinitionAsync(cancellationToken).ConfigureAwait(false);
            quota = await ReadQuotaAsync(context.Participants[0].CharacterId, cancellationToken).ConfigureAwait(false);
            layout = await rules.LayoutAsync(context.Facts, Random.Shared.Next(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false, RequiredItemsOverride = definition.Dungeon.RequiredItems,
            Quota = policy.UnlimitedEntriesForTesting ? null
                : new(UsageQuotaFamily.LostLand, quota.Period, quota.Maximum, context.DungeonId),
        }, (receipt, token) => visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token), cancellationToken).ConfigureAwait(false);
    }
}
