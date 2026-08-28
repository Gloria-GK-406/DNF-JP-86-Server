using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The table every quest's reward is priced from, and the arithmetic over it.
/// </summary>
/// <remarks>
/// The one number here that is not read off the archive is the answer: <b>101</b>, which is what
/// the reference server put on the wire on 2026-08-27 when the real client handed quest 1016 in.
/// Everything else -- the level table, the difficulty letter, the penalty bands -- is the archive's,
/// and this test is the join between them.
///
/// It matters that the answer came from a capture rather than from the reference server's own
/// test suite. That suite hands its packet builder <c>Exp = 10</c> for this same quest, which is
/// the captured value with a digit lost, and its only test of the arithmetic feeds a two-row table
/// it made up. Between them they look like corroboration and are not.
/// </remarks>
public sealed class PvfQuestParameterSourceTests
{
    [SkippableFact]
    public async Task Quest1016PaysWhatTheReferenceServerPutOnTheWire()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider)
            .GetAsync(1016, CancellationToken.None);
        Assert.NotNull(quest);

        var parameters = new PvfQuestParameterSource(provider).Load();

        // Difficulty M, epic, level band opening at 1, on the level-1 character that meets 赛丽亚:
        // 100 x 1010/1000 x 100/100.
        Assert.Equal('M', quest!.DifficultyCode.Trim()[0]);
        Assert.Equal(QuestGrade.Epic, quest.Grade);
        Assert.Equal(1, quest.RequiredLevel!.Value.Minimum);
        Assert.False(quest.PaysAtCharacterLevel);
        Assert.Equal(
            101u,
            parameters.ExperienceFor(
                characterLevel: 1, rewardLevel: 1, difficulty: 'M', isEpic: true, ignoreLevel: false));
    }

    /// <remarks>
    /// The archive interleaves a <c>-1</c> after every value in this table. Dropping them is what
    /// makes entry <c>n-1</c> level <c>n</c>'s; keeping them would halve every quest's level and
    /// pay the tutorial 100 instead of 101 -- close enough to look right and wrong everywhere else.
    /// </remarks>
    [SkippableFact]
    public void TheLevelTableIsReadWithoutTheArchivesSeparators()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var experience = new PvfQuestParameterSource(provider).Load().ExperienceByLevel;

        Assert.Equal([100, 100, 126, 187, 293], experience.Take(5));
        Assert.DoesNotContain(experience, value => value < 0);
        Assert.True(experience.Count >= 200, $"only {experience.Count} levels are priced");
    }

    /// <summary>The letters and their permille bonuses, as the archive lists them.</summary>
    [SkippableFact]
    public void DifficultyLettersCarryTheirOwnBonus()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var difficulty = new PvfQuestParameterSource(provider).Load().DifficultyPermille;

        Assert.Equal(10, difficulty['M']);
        Assert.Equal(45, difficulty['I']);
        Assert.Equal(250, difficulty['U']);
        Assert.Equal(400, difficulty['A']);
    }

    /// <remarks>
    /// The epic bands are LARGER than the ordinary ones, not smaller: an epic quest pays more once
    /// outgrown, where an ordinary one pays less. Read off the archive rather than assumed, because
    /// the assumption goes the other way.
    /// </remarks>
    [SkippableFact]
    public void OutgrowingAQuestCutsItsPayExceptWhenItIsEpic()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var parameters = new PvfQuestParameterSource(provider).Load();

        Assert.Equal(100, parameters.PenaltyPercentFor(0, isEpic: false));
        Assert.Equal(100, parameters.PenaltyPercentFor(6, isEpic: false));
        Assert.Equal(80, parameters.PenaltyPercentFor(7, isEpic: false));
        Assert.Equal(30, parameters.PenaltyPercentFor(12, isEpic: false));
        Assert.Equal(120, parameters.PenaltyPercentFor(7, isEpic: true));
        Assert.Equal(140, parameters.PenaltyPercentFor(12, isEpic: true));
    }

    /// <summary>A quest under a level below its own band is still paid in full.</summary>
    /// <remarks>
    /// There is no band for it in the archive, and the offer rules never show a character a quest
    /// above their level, so the case is unreachable from a real client and reachable from a
    /// crafted frame. Paying in full is what the reference server does with it.
    /// </remarks>
    [SkippableFact]
    public void ACharacterBelowAQuestsLevelIsNotPenalised()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(
            100, new PvfQuestParameterSource(provider).Load().PenaltyPercentFor(-20, isEpic: false));
    }

    /// <summary>
    /// A quest that pays at the character's level ignores its own band and takes no penalty.
    /// </summary>
    [SkippableFact]
    public void PayingAtTheCharactersLevelReadsTheirRowAndSkipsThePenalty()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var parameters = new PvfQuestParameterSource(provider).Load();

        var atOwnLevel = parameters.ExperienceFor(
            characterLevel: 40, rewardLevel: 1, difficulty: 'M', isEpic: false, ignoreLevel: false);
        var atCharacterLevel = parameters.ExperienceFor(
            characterLevel: 40, rewardLevel: 1, difficulty: 'M', isEpic: false, ignoreLevel: true);

        // Level 40 is worth vastly more than level 1, and the ignoring quest is the one that gets it.
        Assert.True(
            atCharacterLevel > atOwnLevel * 100,
            $"paying at the character's level gave {atCharacterLevel} against {atOwnLevel}");
        Assert.Equal(
            (uint)(parameters.ExperienceByLevel[39] * 1010L / 1000L), atCharacterLevel);
    }

    /// <summary>A level nobody priced pays nothing rather than throwing.</summary>
    [SkippableFact]
    public void ALevelOffTheEndOfTheTablePaysNothing()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var parameters = new PvfQuestParameterSource(provider).Load();

        Assert.Equal(
            0u,
            parameters.ExperienceFor(
                characterLevel: 1, rewardLevel: 0, difficulty: 'M', isEpic: false, ignoreLevel: false));
        Assert.Equal(
            0u,
            parameters.ExperienceFor(
                characterLevel: 1,
                rewardLevel: parameters.ExperienceByLevel.Count + 1,
                difficulty: 'M',
                isEpic: false,
                ignoreLevel: false));
    }

    /// <summary>An unlisted letter takes the ordinary bonus rather than none.</summary>
    [SkippableFact]
    public void AnUnlistedDifficultyTakesTheDefaultBonus()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var parameters = new PvfQuestParameterSource(provider).Load();

        Assert.Equal(
            parameters.ExperienceFor(1, 1, 'M', isEpic: false, ignoreLevel: false),
            parameters.ExperienceFor(1, 1, '?', isEpic: false, ignoreLevel: false));
    }

    [SkippableFact]
    public void RepeatedReadsAnswerFromOneParse()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestParameterSource(provider);

        Assert.Same(source.Load(), source.Load());
    }
}
