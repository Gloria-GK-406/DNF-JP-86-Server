using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// Resolves the loot plan attached to a monster while its cell is being created.
/// </summary>
/// <remarks>
/// Numbering and floor ownership belong to the dungeon run. This service reads rules and
/// current character facts, then freezes their result before the cell is published.
/// </remarks>
internal sealed partial class MonsterDropDomainService(
    IDropRuleService dropRules,
    IDropRateAdjuster adjuster,
    IQuestMonsterDropDomainService questDrops,
    ILogger<MonsterDropDomainService> logger) : IMonsterDropDomainService
{
    public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(
        int monsterId, MonsterDropContext context, CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        var request = new MonsterDropRequest(monsterId, context);
        var result = new List<RolledDrop>();
        result.AddRange(this.GenerateOrdinary(request));
        result.AddRange(GenerateExtra(request));
        result.AddRange(GenerateMonsterDeclared(request));
        result.AddRange(GenerateIndependent(request));
        result.AddRange(GenerateWorld(request));
        result.AddRange(GenerateSpecialMode(request));
        return ValueTask.FromResult<IReadOnlyList<RolledDrop>>(result);
    }

    public ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(
        int monsterId, CharacterId characterId, MonsterDropContext context,
        CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        var matches = context.Participants.Where(p => p.CharacterId == characterId).ToArray();
        if (matches.Length != 1)
            throw new ArgumentException("Personal drop recipient must belong to the run.", nameof(characterId));
        return questDrops.DrawAsync(new MonsterDropRequest(monsterId, context), matches[0], cancellationToken);
    }

    private static void Validate(MonsterDropContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Participants.IsDefaultOrEmpty
            || !context.Participants.Any(participant => participant.CharacterId == context.CharacterId))
            throw new ArgumentException("Drop context must include its generating character.", nameof(context));
        if (context.Participants.Select(participant => participant.CharacterId).Distinct().Count()
            != context.Participants.Length)
            throw new ArgumentException("Drop participants must have unique character identities.", nameof(context));
    }

}
