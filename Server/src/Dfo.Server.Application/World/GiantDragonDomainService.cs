using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class GiantDragonDomainService(IGiantDragonRuleService rules, IDungeonAdmissionDomainService admission,
    IDungeonStructureRuleService structures, IDungeonVisitDomainService visits, IDungeonRunDomainService runs,
    ISelectedSessionDomainService sessions, IOnlineCharacterRuntimeDomainService runtime,
    ICharacterQuestLogDomainService quests) : IGiantDragonDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.RequestMode != 0 || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (!rules.IsOpen(context.DungeonId)) return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        GiantDragonEntryRule entry;
        try { entry = await rules.EntryAsync(context.DungeonId, ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        foreach (var member in context.Participants)
        {
            if (!sessions.TryGetInWorld(member.CharacterId, out var selected) || selected.AccountId != member.AccountId
                || runtime.PositionOf(selected) is not { } position || position.TownId != entry.TownId || position.AreaId != entry.AreaId)
                return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot);
            if (runs.Find(member.CharacterId) is { } previous
                && !(context.Facts.Intent.Kind == DungeonEntryIntentKind.OrdinaryRechallenge && previous.IsSettled))
                return new(null, DungeonEntryFailure.AlreadyInDungeon, member.Slot);
            if (entry.QuestId is { } quest)
            {
                var log = quests.LogOf(member.AccountId, member.CharacterId);
                var active = await log.FindCarriedAsync(quest, ct).ConfigureAwait(false) is not null;
                var cleared = (await log.ClearedAsync(ct).ConfigureAwait(false)).Any(x => x.QuestId == quest && x.Mark != 0);
                if (context.DungeonId == 3540 ? !active : !cleared && !(entry.AllowActiveQuest && active))
                    return new(null, DungeonEntryFailure.MissingPrerequisite, member.Slot);
            }
        }
        // The restored selector exposes this quest-only dungeon directly. Its active quest
        // was checked above; choose the quest maze explicitly rather than an ordinary maze.
        var facts = context.DungeonId == 3540 ? context.Facts with { Intent = DungeonEntryIntent.Directed(3540) } : context.Facts;
        return await admission.ExecuteAsync(context with { Facts = facts }, new() { RequireMinimumLevel = true }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await structures.ResolveAsync(context.DungeonId, facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            if (layout is not null) layout = layout with { GiantDragonTimeReward = entry.TimeReward };
            return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
                : await visits.EnterGrantedPartyAsync(context.Participants, layout, facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
