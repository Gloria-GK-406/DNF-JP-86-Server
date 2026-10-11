using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class HellDungeonEntryDomainService(IHellDungeonEntryRuleService hellRules,
    IDungeonStructureRuleService structures, IDungeonVisitDomainService visits,
    IDungeonAdmissionDomainService admission) : IHellDungeonEntryDomainService
{
    public ValueTask<DungeonEntryAttempt> EnterRandomAsync(DungeonEntryContext context, CancellationToken ct) =>
        ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable));

    public async ValueTask<DungeonEntryAttempt> EnterManualAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (context.RequestMode != 1 || context.PracticeMode != 0)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable);
        HellDungeonDefinition? rules;
        DungeonDefinition? definition;
        try
        {
            rules = await hellRules.GetRulesAsync(context.DungeonId, ct).ConfigureAwait(false);
            if (rules is not { Enabled: true }) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellUnavailable);
            definition = await structures.GetDefinitionAsync(context.DungeonId, ct).ConfigureAwait(false);
            if (definition is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid, error.Message); }
        var result = await admission.ExecuteAsync(context, new()
        {
            RequiredQuestIds = rules.RequiredQuestIds,
            HellTickets = hellRules.TicketAlternatives(rules, definition.MinimumRequiredLevel),
            RequireMinimumLevel = true,
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try
            {
                var seed = Random.Shared.Next();
                layout = await structures.ResolveTopologyAsync(context.DungeonId, context.Facts, seed, token).ConfigureAwait(false);
                if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid);
                var plan = await hellRules.PrepareAsync(rules, layout, seed, token).ConfigureAwait(false);
                if (plan is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid);
                layout = await structures.PrepareLayoutAsync(layout with { EntryKind = DungeonEntryKind.ManualHell, Hell = plan,
                    StartRoomMapId = layout.StartCell == plan.Cell ? plan.MapId : layout.StartRoomMapId }, token).ConfigureAwait(false);
            }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.HellConfigurationInvalid, error.Message); }
            return await visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return result.Failure == DungeonEntryFailure.ConfigurationInvalid ? result with { Failure = DungeonEntryFailure.HellConfigurationInvalid } : result;
    }
}
