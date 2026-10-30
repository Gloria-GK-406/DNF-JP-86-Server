using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;

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
    IQuestEnemyDropDomainService questDrops,
    ILogger<MonsterDropDomainService> logger,
    IWorldDropRuleService worldDrops,
    IMonsterDeclaredDropRuleService declaredDrops,
    IExtraDropRuleService extraDrops,
    IIndependentDropRuleService independentDrops,
    IPremiumServiceDomainService premiums, ICharacterDomainService characters,
    IDimensionDungeonRuleService dimensions) : IMonsterDropDomainService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> GenerateGroundAsync(
        int monsterId, MonsterDropContext context, CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        var request = new MonsterDropRequest(monsterId, context);
        var result = new List<RolledDrop>();
        if (context.SourceType == IndependentDropSourceType.APC)
            return await IndependentAsync(request, 0, cancellationToken).ConfigureAwait(false);
        result.AddRange(this.GenerateRandomDrops(request));
        result.AddRange(GenerateMonsterDeclared(request));
        var growthBonus = context.MonsterRank == 3
            ? await premiums.GetBossEquipmentDropBonusPercentAsync(
                context.Participants.Select(p => p.AccountId).ToArray(), cancellationToken).ConfigureAwait(false)
            : 0;
        result.AddRange(await IndependentAsync(request, growthBonus, cancellationToken).ConfigureAwait(false));
        result.AddRange(worldDrops.Draw(request));
        result.AddRange(GenerateSpecialMode(request));
        return result;
    }

    public async ValueTask<IReadOnlyList<RolledDrop>> GeneratePersonalAsync(
        int monsterId, CharacterId characterId, MonsterDropContext context,
        CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        var matches = context.Participants.Where(p => p.CharacterId == characterId).ToArray();
        if (matches.Length != 1)
            throw new ArgumentException("Personal drop recipient must belong to the run.", nameof(characterId));
        var result = new List<RolledDrop>(await questDrops.DrawAsync(
            QuestEnemyDropRequest.FromMonster(new MonsterDropRequest(monsterId, context)), matches[0], cancellationToken).ConfigureAwait(false));
        if (context.SourceType == IndependentDropSourceType.Monster && dimensions.IsDimension(context.DungeonId))
        {
            var seed = DropRandom.SeedFor(context.Seed, context.MapId, context.Cell.X, context.Cell.Y, context.ActorSequence)
                ^ unchecked((ulong)characterId.Value * 0x9E3779B97F4A7C15UL) ^ 0x44494D454E53494FUL;
            result.AddRange(dimensions.DrawMonster(new(monsterId, context), seed)
                .Select(item => item with { OwnerCharacterId = characterId }));
        }
        return result;
    }

    public ValueTask<IReadOnlyList<RolledDrop>> GenerateIndependentAsync(
        int monsterId, MonsterDropContext context, int growthBonusPercent, CancellationToken cancellationToken)
    {
        Validate(context, cancellationToken);
        return IndependentAsync(new(monsterId, context), growthBonusPercent, cancellationToken);
    }

    /// <remarks>
    /// The level is that of the generating character (the run leader for shared ground loot), not the
    /// monster's, and it is read only when the table declares a row for this source.
    /// </remarks>
    private async ValueTask<IReadOnlyList<RolledDrop>> IndependentAsync(
        MonsterDropRequest request, int growthBonusPercent, CancellationToken cancellationToken)
    {
        if (!independentDrops.Covers(request)) return [];
        var progress = await characters.Get(request.CharacterId).ProgressAsync(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException(
                $"Independent drops: missing character {request.CharacterId}.");
        return independentDrops.Draw(request, progress.Level, growthBonusPercent);
    }

    private static void Validate(MonsterDropContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(context.SourceType))
            throw new ArgumentOutOfRangeException(nameof(context), "Unknown drop source type.");
        if (context.RandomDropCount is < 0 or > 127)
            throw new ArgumentOutOfRangeException(nameof(context), "Random drop count must be 0..127.");
        if (context.Participants.IsDefaultOrEmpty
            || !context.Participants.Any(participant => participant.CharacterId == context.CharacterId))
            throw new ArgumentException("Drop context must include its generating character.", nameof(context));
        if (context.Participants.Select(participant => participant.CharacterId).Distinct().Count()
            != context.Participants.Length)
            throw new ArgumentException("Drop participants must have unique character identities.", nameof(context));
    }

}
