using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// How much gold a character may carry, against the archive that declares it.
/// </summary>
/// <remarks>
/// Every figure here is transcribed from <c>etc/(r)goldlimitbylevel.etc</c> rather than produced by
/// this server's own reading. That is the point: a reading can be wrong and self-consistent at the
/// same time, and only an archive value tells those two apart.
/// <para>
/// <b>Worth pinning more than most.</b> This number becomes the ceiling every arrival of gold is
/// clamped to. A reading that answered zero -- which this file legitimately declares for level zero
/// -- would clamp every player's gold away, silently, while every other case stayed green.
/// </para>
/// </remarks>
public sealed class PvfGoldCarryLimitSourceTests
{
    /// <summary>The band the endgame is fought in.</summary>
    private const int EndgameLevel = 86;

    /// <summary>
    /// The floor rises with the level, in the figures the archive names.
    /// </summary>
    /// <remarks>
    /// <c>[gold limit from level]</c> is a hundred pairs. Four of them are transcribed here: the
    /// first real level, the twenty that jumps an order of magnitude, the forty that jumps again,
    /// and the sixty from which the table stops rising. A reader that took the pairs in the wrong
    /// order, or that took a level as a limit, puts different numbers here.
    /// </remarks>
    [SkippableTheory]
    [InlineData(1, 100_000)]
    [InlineData(20, 30_000_000)]
    [InlineData(40, 200_000_000)]
    [InlineData(60, 400_000_000)]
    public void A_level_carries_the_floor_the_archive_names(int level, int expected)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(expected, new PvfGoldCarryLimitSource(provider).BaseLimitFor(level));
    }

    /// <summary>
    /// A level the table stops short of keeps the last floor it named.
    /// </summary>
    /// <remarks>
    /// The archive names levels up to ninety-nine and the game is fought at eighty-six, so the
    /// endgame is inside the table -- but nothing stops a deployment raising the cap past it. The
    /// nearest declared level below is the answer, which is the same rule that answers inside the
    /// table and needs no special case at its end.
    /// </remarks>
    [SkippableFact]
    public void A_level_past_the_tables_end_keeps_the_last_floor()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfGoldCarryLimitSource(provider);

        Assert.Equal(source.BaseLimitFor(99), source.BaseLimitFor(400));
    }

    /// <summary>
    /// The level this server is fought at carries a floor no player will reach.
    /// </summary>
    /// <remarks>
    /// Asserted on its own because it is the figure that decides whether the cap is a rule players
    /// meet or a backstop they never touch. Four hundred million against a card that pays tens of
    /// gold: a backstop. If this ever read small, the cap would start silently eating income, and
    /// this case is where that would show.
    /// </remarks>
    [SkippableFact]
    public void The_endgame_floor_is_far_above_anything_this_server_pays()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(
            400_000_000, new PvfGoldCarryLimitSource(provider).BaseLimitFor(EndgameLevel));
    }
}
