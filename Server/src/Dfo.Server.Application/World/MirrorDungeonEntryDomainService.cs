using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class MirrorDungeonEntryDomainService(IMirrorDungeonRuleService rules,
    IDungeonStructureRuleService structures, IDungeonVisitDomainService visits,
    ICharacterDomainService characters, IDungeonAdmissionDomainService admission, TimeProvider clock, IDailyResetRuleService daily) : IMirrorDungeonEntryDomainService
{
    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (context.MirrorQuestId is not > 0 || context.RequestMode != 0 || context.PracticeMode != 0
            || context.Participants.Count is < 1 or > 4 || context.Participants.Count(x => x.IsLeader) != 1)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        MirrorDungeonEntryRule? rule;
        try { rule = await rules.ResolveAsync(context.DungeonId, context.MirrorQuestId.Value, ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (rule is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Invalid mirror dungeon/quest pair.");
        var catalog = rule.Catalog;
        var intent = context.Facts.Intent;
        if (intent.Kind != DungeonEntryIntentKind.Interactive
            && (intent.Kind != DungeonEntryIntentKind.Directed || intent.DirectedDungeonId != catalog.WrapperDungeonId))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite, "Mirror selection context is not current.");
        var facts = context.Facts with { Difficulty = rule.Difficulty, Intent = DungeonEntryIntent.Interactive };
        return await admission.ExecuteAsync(context with { Facts = facts }, new()
        {
            Mirror = true, MirrorCatalog = catalog, RequiredQuestIds = catalog.RequiredQuestIds,
            Quota = new(UsageQuotaFamily.MirrorDungeon, daily.PeriodDay(clock.GetUtcNow()), catalog.DailyEntryLimit),
        }, async (receipt, token) =>
        {
            var levels = new List<byte>();
            foreach (var member in context.Participants)
                levels.Add((await characters.Get(member.CharacterId).ProgressAsync(token).ConfigureAwait(false))!.Level);
            DungeonLayout? layout;
            try { layout = await structures.ResolveAsync(context.DungeonId, facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, "Mirror historical maze unavailable.");
            var level = rules.ResolveRunLevel(levels);
            layout = layout with { Mirror = new(catalog.WrapperDungeonId, catalog.WorldMapId, context.MirrorQuestId.Value),
                FixedMonsterLevel = level, BasisLevel = level };
            return await visits.EnterGrantedPartyAsync(context.Participants, layout, facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
