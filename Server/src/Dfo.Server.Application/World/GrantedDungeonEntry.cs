using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>The tail every special entry shares once admission has granted it.</summary>
internal static class GrantedDungeonEntry
{
    /// <summary>
    /// Resolves the maze, freezes the run kind's plan on it and enters the party. An unreadable maze refuses as
    /// misconfigured and a missing one as not found, so admission rolls the grant back either way.
    /// </summary>
    public static async ValueTask<DungeonEntryAttempt> ResolveAndEnterAsync(this IDungeonVisitDomainService visits,
        IDungeonStructureRuleService layouts, IReadOnlyList<DungeonRunParticipant> participants, int dungeonId,
        DungeonEntryFacts facts, IDungeonAdmissionReceipt receipt, Func<DungeonLayout, DungeonLayout> plan, CancellationToken ct)
    {
        DungeonLayout? layout;
        try { layout = await layouts.ResolveAsync(dungeonId, facts, Random.Shared.Next(), ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
            : await visits.EnterGrantedPartyAsync(participants, plan(layout), facts, receipt, ct).ConfigureAwait(false);
    }
}
