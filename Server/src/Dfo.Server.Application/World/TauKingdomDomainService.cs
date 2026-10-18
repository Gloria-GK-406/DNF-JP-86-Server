using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class TauKingdomDomainService(ITauKingdomRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, ICharacterDomainService characters, IEventManagementDomainService activities,
    IDailyRefillConfigurationDomainService refills, IDailyRefillDomainService refill,
    ISelectedSessionDomainService sessions, ISelectedSessionRuntimeDirectory published,
    IAuthenticatedGamePresenceQuery online, IEventService events, TimeProvider clock) : ITauKingdomDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.Participants.Count != 1 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Facts.Difficulty != DungeonDifficulty.Normal)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.DifficultyNotUnlocked);
        if (!(await activities.ListAsync(clock.GetUtcNow(), ct).ConfigureAwait(false)).Entries
                .Any(x => x.EventId == rules.EventId && x.Status == "active"))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        TauKingdomDefinition definition;
        try { definition = await rules.DefinitionAsync(ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        var character = await characters.Get(context.Participants[0].CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (character is null || character.Level < definition.Dungeon.MinimumRequiredLevel)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.LevelTooLow);
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            RequiredItemsOverride = definition.Dungeon.RequiredItems,
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(context.DungeonId, context.Facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
            layout = layout with { FixedMonsterLevel = character.Level, BasisLevel = character.Level, ConsumesFatigue = false };
            return await visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session || !published.IsPublishedExact(session) || !online.IsActiveExact(session))
            return false;
        var at = clock.GetUtcNow();
        var configured = (await refills.EffectiveAsync(at, ct).ConfigureAwait(false))
            .Where(x => x.ItemId == rules.TicketItemId).ToArray();
        var result = await refill.ApplyIndependentRefillAsync(session.AccountId, session.CharacterId, configured, at, ct).ConfigureAwait(false);
        if (result.Grants.Count > 0)
            await events.EnqueueAsync(new TauKingdomTicketRefilled(session.CharacterId,
                [.. result.Grants.Select(x => x.Slot).Distinct()]), ct).ConfigureAwait(false);
        return true;
    }
}
