using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class SealedTreasureDomainService(ISealedTreasureRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, IDungeonRunQueryDomainService runs,
    IEventManagementDomainService activities, TimeProvider clock) : ISealedTreasureDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.RequestMode != 0 || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (!await activities.IsOpenAsync(rules.EventId, clock.GetUtcNow(), ct).ConfigureAwait(false))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        Dfo.Server.Abstractions.Content.DungeonDefinition definition;
        try { definition = await rules.DefinitionAsync(ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (context.Participants.Count != definition.PartyLimit)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Participants.Any(x => runs.Find(x.CharacterId) is not null))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        return await admission.ExecuteAsync(context, new() { RequireMinimumLevel = true }, async (receipt, token) =>
        {
            return await visits.ResolveAndEnterAsync(layouts, context.Participants, context.DungeonId, context.Facts, receipt,
                layout => layout with { Mode = new DungeonRunMode.Plain(DungeonRunKind.SealedTreasure) }, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
