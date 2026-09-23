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
internal sealed class MonsterDropDomainService(
    IDropRuleService dropRules,
    IDropRateAdjuster adjuster,
    IQuestMonsterDropDomainService questDrops,
    ILogger<MonsterDropDomainService> logger) : IMonsterDropDomainService
{
    public ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(
        int monsterId, MonsterDropContext context, CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        return ValueTask.FromResult(this.Roll(new MonsterDropRequest(monsterId, context)));
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

    /// <summary>
    /// What this kill produced, before anything is numbered.
    /// </summary>
    /// <remarks>
    /// The rates are asked for, raised on the character's behalf if anything is owed, and handed
    /// back to be drawn at. The raising happens here rather than inside the rules because what a
    /// character is owed is state that crosses runs, and the rules are not allowed to go and find
    /// it.
    /// </remarks>
    private IReadOnlyList<RolledDrop> Roll(MonsterDropRequest kill)
    {
        if (dropRules.RatesFor(kill) is not { } rates)
        {
            logger.LogWarning(
                "Monster drop character={CharacterId}: the rate table covers no level "
                    + "{Level}, so no ordinary loot was generated",
                kill.CharacterId.Value,
                kill.MonsterLevel);
            return [];
        }

        return dropRules.DrawFor(kill, adjuster.Adjust(kill.CharacterId, rates));
    }
}
