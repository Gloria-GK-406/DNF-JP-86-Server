using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The world-wide reward rates, read from the archive rather than carried in code.
/// </summary>
/// <remarks>
/// These are the only inputs to a kill's worth that the archive declares for every dungeon at once,
/// and they are the reason the calculation is checkable: the captured awards of 2026-08-27 were
/// produced with the difficulty rate below, and reproducing them depends on reading it rather than
/// assuming it.
/// </remarks>
public sealed class PvfExperienceParameterSourceTests
{
    /// <remarks>
    /// The expectations are written as widened singles rather than as decimal literals, because
    /// that is what the archive holds: 1.3 stored as a single and read as a double is
    /// 1.2999999523162842. It does not change any captured award -- 56.55 and 56.5499998 floor
    /// alike -- and is asserted this way because it is what the file says, not because a
    /// difference is known to hang on it.
    /// </remarks>
    [SkippableFact]
    public void TheDifficultyRatesAreReadAsTheArchiveWritesThem()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var rates = new PvfExperienceParameterSource(provider).Load().DifficultyRates;

        Assert.Equal([(double)1.3f, 2, (double)2.5f, 3, 4], rates);
    }

    /// <remarks>
    /// The two sections disagree about how they are written, which is why both numeric forms are
    /// read: every difficulty rate is a single -- 2, 3 and 4 among them, stored as 2.0f -- and
    /// every party rate is a plain integer. A reader that took only one kind would come away with
    /// one table empty, and an empty table is a parse failure that looks like a rate of 1.
    /// </remarks>
    [SkippableFact]
    public void BothWrittenFormsOfARateAreRead()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var parameters = new PvfExperienceParameterSource(provider).Load();
        var rates = parameters.DifficultyRates;

        Assert.Equal(5, rates.Count);
        Assert.Contains((double)1.3f, rates);
        Assert.Contains(2.0, rates);
        Assert.Equal([1, 2, 3, 4], parameters.PartyMemberRates);
    }

    [SkippableFact]
    public void ThePartyRatesAreReadAsTheArchiveWritesThem()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var parameters = new PvfExperienceParameterSource(provider).Load();

        Assert.Equal([1, 2, 3, 4], parameters.PartyMemberRates);
        Assert.Equal(1.0, parameters.PartyMemberRate(1));
    }

    /// <summary>The captured runs were taken at difficulty 0, whose rate is 1.3.</summary>
    /// <remarks>
    /// Pinned because every captured award depends on it: without 1.3 the four numbers this whole
    /// calculation is checked against cannot come out.
    /// </remarks>
    [SkippableFact]
    public void TheCapturedRunsDifficultyRateIsWhatTheCapturesWereTakenAt()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal((double)1.3f, new PvfExperienceParameterSource(provider).Load().DifficultyRate(0));
    }

    /// <summary>The world-wide monster rate, which the archive currently sets to 1.</summary>
    /// <remarks>
    /// Pinned because every captured award depends on it as much as on the difficulty rate, and it
    /// was the one live input to the calculation that nothing asserted: a change here would move
    /// all four and no test would have said so.
    /// </remarks>
    [SkippableFact]
    public void TheWorldWideMonsterRateIsWhatTheCapturesWereTakenAt()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(1.0, new PvfExperienceParameterSource(provider).Load().MonsterOverallRate);
    }

    /// <remarks>
    /// One, not the first entry. A difficulty this server has not been told about must not silently
    /// receive the easiest difficulty's bonus.
    /// </remarks>
    [SkippableFact]
    public void ADifficultyTheArchiveDoesNotListIsNotScaled()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var parameters = new PvfExperienceParameterSource(provider).Load();

        Assert.Equal(1.0, parameters.DifficultyRate(99));
        Assert.Equal(1.0, parameters.DifficultyRate(-1));
        Assert.Equal(1.0, parameters.PartyMemberRate(0));
        Assert.Equal(1.0, parameters.PartyMemberRate(99));
    }

    [SkippableFact]
    public void RepeatedReadsAnswerFromOneParse()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfExperienceParameterSource(provider);

        Assert.Same(source.Load(), source.Load());
    }

    /// <summary>The clear tables are read, and they are not the reference server's constants.</summary>
    /// <remarks>
    /// This assertion earns its place by disagreeing with something. The reference server carries
    /// <c>0, 0.01, 0.02, 0.03, 0.05</c> as its fallback for the rank rates and this archive
    /// declares <c>0.05, 0.1, 0.12, 0.15, 0.2</c> -- an order of magnitude apart at every rank.
    /// Transcribing its numbers would make every clear pay a tenth of what it should and produce
    /// no error anywhere.
    /// </remarks>
    [SkippableFact]
    public void TheClearTablesAreReadFromTheArchiveAndNotFromTheReferenceServer()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var parameters = new PvfExperienceParameterSource(provider).Load();

        Assert.Equal(1, parameters.ClearOverallRate);
        Assert.Equal(
            [(double)0.05f, (double)0.1f, (double)0.12f, (double)0.15f, (double)0.2f],
            parameters.ClearRankRates);
        Assert.NotEqual([0, 0.01, 0.02, 0.03, 0.05], parameters.ClearRankRates);
    }
}
