using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

/// <summary>
/// The fatigue seam's only implementation keeps no account, which is the whole of what it does.
/// </summary>
/// <remarks>
/// Beside the test of the call site rather than among the application tests, because the two
/// halves of this placeholder only mean anything together: entry reports what it costs, and
/// nothing whatever comes of the report. Read either one alone and the other is what you would
/// have to assume.
/// </remarks>
public sealed class UnaccountedFatigueTests
{
    /// <summary>
    /// A reported spend leaves every number the seam can be asked for exactly where it was.
    /// </summary>
    /// <remarks>
    /// The used count is the one this is about -- it is what a real accounting would move, and it
    /// is what the entry acknowledgement carries -- but the total and the remainder are asserted
    /// with it so that "unchanged" is the whole answer rather than one field of it.
    /// </remarks>
    [Fact]
    public async Task A_reported_spend_moves_no_number_the_seam_answers()
    {
        var fatigue = new UnaccountedCharacterFatigueDomainService();

        var before = await fatigue.BalanceOfAsync(Character, CancellationToken.None);
        await fatigue.SpendAsync(Character, 7, CancellationToken.None);
        var after = await fatigue.BalanceOfAsync(Character, CancellationToken.None);

        Assert.Equal(before.Used, after.Used);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Remaining, after.Remaining);
    }

    /// <summary>
    /// And what it answers, after reports in both directions, is the pair the entry frame carries.
    /// </summary>
    /// <remarks>
    /// The two numbers are written out rather than read off the implementation's own constants, for
    /// the reason the call site's test writes its number out: an expectation taken from the thing
    /// under test agrees with it about whatever it becomes. 188 and 0 are what
    /// <c>CharacterRosterService</c> hardcodes into every entering character's fatigue state and
    /// what the acknowledgement therefore puts on the wire, so they are the pair this has to keep
    /// answering while fatigue is unimplemented.
    /// </remarks>
    [Fact]
    public async Task The_pair_it_answers_is_the_pair_the_entry_frame_carries()
    {
        var fatigue = new UnaccountedCharacterFatigueDomainService();

        await fatigue.SpendAsync(Character, 7, CancellationToken.None);
        await fatigue.RefundAsync(Character, 3, CancellationToken.None);
        var balance = await fatigue.BalanceOfAsync(Character, CancellationToken.None);

        Assert.Equal((ushort)188, balance.Total);
        Assert.Equal((ushort)0, balance.Used);
        Assert.Equal((ushort)188, balance.Remaining);
    }

    /// <summary>Whoever is asked about; the seam does not distinguish them and does not pretend to.</summary>
    private static readonly CharacterId Character = new(0x1234);
}
