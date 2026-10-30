using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.World;

/// <summary>Wedding-couple admission. Runs, monsters, drops and entry charges stay in the common dungeon pipeline.</summary>
internal sealed class CoupleDungeonRule(IMarriageRelations relations)
{
    public const int DungeonId = 3541;

    /// <summary>The difficulty a married character sees unlocked, whatever the account's ledger records.</summary>
    public const Dfo.Server.Data.World.DungeonDifficulty MarriedDifficulty = (Dfo.Server.Data.World.DungeonDifficulty)2;

    public async ValueTask<bool> EligibleAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (context.DungeonId != DungeonId || context.Participants.Count != 2 || context.PracticeMode != 0 || context.RequestMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle
            || context.Facts.Intent.DirectedDungeonId is { } directed && directed != DungeonId
            || context.Participants[0].CharacterId == context.Participants[1].CharacterId
            || context.Participants[0].AccountId == context.Participants[1].AccountId) return false;
        var first = context.Participants[0]; var second = context.Participants[1];
        return await relations.ForParticipantAsync(first.AccountId, first.CharacterId, ct).ConfigureAwait(false) is { Stage: MarriageStage.Married } relation
            && IMarriageRelations.Belongs(relation, second.AccountId, second.CharacterId);
    }
}

internal sealed class CoupleDungeonDomainService(CoupleDungeonRule rule, IDungeonAdmissionDomainService admission,
    IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits) : ICoupleDungeonDomainService
{
    public bool Supports(int dungeonId) => dungeonId == CoupleDungeonRule.DungeonId;
    public ValueTask<bool> EligibleAsync(DungeonEntryContext context, CancellationToken ct) => rule.EligibleAsync(context, ct);
    public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => admission.ExecuteAsync(context,
        new() { RequireMinimumLevel = true }, async (receipt, token) =>
        {
            return await visits.ResolveAndEnterAsync(layouts, context.Participants, context.DungeonId, context.Facts, receipt,
                layout => layout with { Mode = new DungeonRunMode.Plain(DungeonRunKind.Couple) }, token).ConfigureAwait(false);
        }, ct);
}
