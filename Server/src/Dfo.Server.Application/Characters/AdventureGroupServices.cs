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
internal sealed class AdventureGroupDomainService(ICharacterTable characters, IAdventureGroupRuleService rules)
    : IAdventureGroupDomainService
{
    public async ValueTask<AdventureGroupStanding> ReadAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var roster = await characters.ReadByAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        return rules.Evaluate(roster.Select(row => row.Level)).Standing;
    }

    public async ValueTask<double> ClearExperienceRateAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var roster = await characters.ReadByAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var self = roster.FirstOrDefault(row => row.CharacterId == characterId.Value);
        return self is null ? 0 : rules.ClearExperienceRate(rules.Evaluate(roster.Select(row => row.Level)), self.Level);
    }

    public async ValueTask<AdventureGroupLevelRise?> LevelRiseAsync(
        CharacterLevelChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.After <= change.Before)
            return null;

        if (await characters.FindOwnerAsync(change.CharacterId, cancellationToken).ConfigureAwait(false) is not { } owner)
            return null;

        var roster = await characters.ReadByAccountAsync(owner, cancellationToken).ConfigureAwait(false);
        if (roster.All(row => row.CharacterId != change.CharacterId.Value))
            return null;

        var from = rules.Evaluate(LevelsWith(roster, change.CharacterId, change.Before)).Standing.Level;
        var to = rules.Evaluate(LevelsWith(roster, change.CharacterId, change.After)).Standing.Level;
        return to > from ? new AdventureGroupLevelRise(owner, from, to) : null;
    }

    private static IEnumerable<int> LevelsWith(
        IReadOnlyList<CharacterRosterRow> roster, CharacterId characterId, int level) =>
        roster.Select(row => row.CharacterId == characterId.Value ? level : row.Level);
}
