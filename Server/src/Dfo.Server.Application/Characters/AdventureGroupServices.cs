using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>Turns character levels into an adventure group.</summary>
internal sealed class AdventureGroupRuleService(ICharacterManageContentSource content) : IAdventureGroupRuleService
{
    public AdventureGroupEvaluation Evaluate(IEnumerable<int> characterLevels)
    {
        ArgumentNullException.ThrowIfNull(characterLevels);
        var table = content.Table;
        var cap = table.LevelThresholds[table.MaxLevel - 1];
        long points = 0;
        var highest = 0;
        foreach (var level in characterLevels)
        {
            points += CharacterPoints(table, level);
            highest = Math.Max(highest, level);
        }

        var total = (int)Math.Min(points, cap);
        byte groupLevel = 0;
        for (var index = 0; index < table.MaxLevel && total >= table.LevelThresholds[index]; index++)
            groupLevel = checked((byte)(index + 1));

        return new(new(groupLevel, total), highest);
    }

    public double ClearExperienceRate(AdventureGroupEvaluation evaluation, int characterLevel)
    {
        var level = evaluation.Standing.Level;
        if (level == 0 || characterLevel >= evaluation.HighestCharacterLevel)
            return 0;

        return content.Table.ExperienceBonusPercent.TryGetValue(level, out var percent) ? percent / 100d : 0;
    }

    /// <summary>The points one character contributes: the bonus of every level up to its own.</summary>
    private static long CharacterPoints(CharacterManageTable table, int characterLevel)
    {
        long points = 0;
        foreach (var band in table.PointBands)
        {
            var top = Math.Min(band.MaxLevel, characterLevel);
            if (top >= band.MinLevel)
                points += (long)(top - band.MinLevel + 1) * band.Point;
        }

        return points;
    }
}

/// <summary>The account's adventure group, worked out from its characters each time.</summary>
/// <remarks>
/// The roster says which characters the account has; each one's level is the character's own
/// (<see cref="ICharacter.ProgressAsync"/>, worked out from its experience), not the SQL
/// <c>level</c> column, which is only a compatibility projection.
/// </remarks>
internal sealed class AdventureGroupDomainService(
    ICharacterTable roster, ICharacterDomainService characters, IAdventureGroupRuleService rules)
    : IAdventureGroupDomainService
{
    public async ValueTask<AdventureGroupStanding> ReadAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var levels = await LevelsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return rules.Evaluate(levels.Select(member => member.Level)).Standing;
    }

    public async ValueTask<double> ClearExperienceRateAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var levels = await LevelsAsync(accountId, cancellationToken).ConfigureAwait(false);
        var self = levels.FirstOrDefault(member => member.Id == characterId);
        return self.Id != characterId
            ? 0
            : rules.ClearExperienceRate(rules.Evaluate(levels.Select(member => member.Level)), self.Level);
    }

    public async ValueTask<AdventureGroupLevelRise?> LevelRiseAsync(
        CharacterLevelChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.After <= change.Before)
            return null;

        if (await roster.FindOwnerAsync(change.CharacterId, cancellationToken).ConfigureAwait(false) is not { } owner)
            return null;

        var levels = await LevelsAsync(owner, cancellationToken).ConfigureAwait(false);
        if (levels.All(member => member.Id != change.CharacterId))
            return null;

        // The changed character at the event's own before and after; the rest as they stand now.
        var from = rules.Evaluate(LevelsWith(levels, change.CharacterId, change.Before)).Standing.Level;
        var to = rules.Evaluate(LevelsWith(levels, change.CharacterId, change.After)).Standing.Level;
        return to > from ? new AdventureGroupLevelRise(owner, from, to) : null;
    }

    /// <summary>The account's characters that have not been deleted, each at its current level.</summary>
    private async ValueTask<IReadOnlyList<(CharacterId Id, int Level)>> LevelsAsync(
        AccountId accountId, CancellationToken cancellationToken)
    {
        var members = await roster.ReadByAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var levels = new List<(CharacterId, int)>(members.Count);
        foreach (var member in members)
        {
            var id = new CharacterId(member.CharacterId);
            // A character gone between the two reads is no longer the account's.
            if (await characters.Get(id).ProgressAsync(cancellationToken).ConfigureAwait(false) is { } progress)
                levels.Add((id, progress.Level));
        }

        return levels;
    }

    private static IEnumerable<int> LevelsWith(
        IReadOnlyList<(CharacterId Id, int Level)> levels, CharacterId characterId, int level) =>
        levels.Select(member => member.Id == characterId ? level : member.Level);
}
