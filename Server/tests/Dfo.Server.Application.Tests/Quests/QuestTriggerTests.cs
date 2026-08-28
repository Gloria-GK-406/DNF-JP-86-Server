using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;

namespace Dfo.Server.Application.Tests.Quests;

/// <summary>
/// The trigger word's arithmetic, and who is allowed to apply it.
/// </summary>
/// <remarks>
/// Both are transcribed from the reference server -- <c>QuestTrigger.ApplyClientMutation</c> and
/// <c>QuestClientTriggerAuthority.Resolve</c> -- because no capture pins either. The one case that
/// is confirmed end to end is the tutorial's: a <c>meet npc</c> quest at 1, reported with the
/// whole-word selector and no increment, lands on 0.
/// </remarks>
public sealed class QuestTriggerTests
{
    /// <summary>Clicking 赛丽亚: quest 1016 goes from 1 to 0, which is its objective met.</summary>
    [Fact]
    public void TheTutorialsReportTakesItsQuestToComplete()
    {
        var next = new QuestTrigger(1).Advance(selector: 0, increment: false);

        Assert.Equal(new QuestTrigger(0), next);
        Assert.True(next.IsComplete);
    }

    /// <summary>Zero is the objective met, not the absence of progress.</summary>
    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    [InlineData(0x0000_0200u, false)]
    public void CompleteIsTheWholeWordBeingZero(uint packed, bool complete) =>
        Assert.Equal(complete, new QuestTrigger(packed).IsComplete);

    [Theory]
    [InlineData(3u, 0, false, 2u)]
    [InlineData(3u, 0, true, 4u)]
    [InlineData(3u, 1, false, 4u)]
    [InlineData(3u, 1, true, 4u)]
    public void TheWholeWordSelectorsMoveTheWholeWord(
        uint from, byte selector, bool increment, uint expected) =>
        Assert.Equal(
            new QuestTrigger(expected), new QuestTrigger(from).Advance(selector, increment));

    /// <remarks>
    /// The client repeats a report it was not answered for, and this server was not answering
    /// them for a while. A quest already at zero must therefore absorb the repeats rather than
    /// wrap to 0xFFFFFFFF and un-finish itself.
    /// </remarks>
    [Fact]
    public void AWordAtZeroStaysThereWhenToldOneFewer() =>
        Assert.Equal(
            new QuestTrigger(0), new QuestTrigger(0).Advance(selector: 0, increment: false));

    [Fact]
    public void AWordAtItsMaximumStaysThereWhenToldOneMore() =>
        Assert.Equal(
            new QuestTrigger(uint.MaxValue),
            new QuestTrigger(uint.MaxValue).Advance(selector: 1, increment: true));

    /// <summary>Nine bits each, low channel first.</summary>
    [Fact]
    public void ChannelsArePackedNineBitsApart()
    {
        var word = new QuestTrigger(0)
            .ReplaceChannel(0, 5)
            .ReplaceChannel(1, 6)
            .ReplaceChannel(2, 7);

        Assert.Equal(5 | (6 << 9) | (7 << 18), (int)word.PackedValue);
        Assert.Equal([5, 6, 7], [word.GetChannel(0), word.GetChannel(1), word.GetChannel(2)]);
    }

    [Theory]
    [InlineData(0x10, 0)]
    [InlineData(0x20, 1)]
    [InlineData(0x40, 2)]
    public void AChannelSelectorMovesOnlyItsOwnChannel(byte selector, int channel)
    {
        var before = new QuestTrigger(0).ReplaceChannel(0, 4).ReplaceChannel(1, 4)
            .ReplaceChannel(2, 4);

        var after = before.Advance(selector, increment: false);

        for (var index = 0; index < QuestTrigger.ChannelCount; index++)
        {
            Assert.Equal(index == channel ? 3 : 4, after.GetChannel(index));
        }
    }

    /// <summary>A selector may name several channels, and moves each of them.</summary>
    [Fact]
    public void ASelectorNamingTwoChannelsMovesBoth()
    {
        var after = new QuestTrigger(0).ReplaceChannel(0, 2).ReplaceChannel(1, 2)
            .ReplaceChannel(2, 2)
            .Advance(0x50, increment: true);

        Assert.Equal([3, 2, 3], [after.GetChannel(0), after.GetChannel(1), after.GetChannel(2)]);
    }

    /// <summary>Each channel saturates on its own, not only the word as a whole.</summary>
    [Fact]
    public void AChannelAtZeroDoesNotBorrowFromTheOneAboveIt()
    {
        var after = new QuestTrigger(0).ReplaceChannel(1, 1)
            .Advance(0x10, increment: false);

        Assert.Equal(0, after.GetChannel(0));
        Assert.Equal(1, after.GetChannel(1));
    }

    /// <remarks>
    /// The reference server's rule: 0 and 1 address the word, and anything else has to be some
    /// non-empty combination of the three channel bits and nothing besides. A selector outside
    /// that set is a report this server has not read, which is refused rather than applied to
    /// channel 0 by default.
    /// </remarks>
    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x01, true)]
    [InlineData(0x10, true)]
    [InlineData(0x20, true)]
    [InlineData(0x40, true)]
    [InlineData(0x70, true)]
    [InlineData(0x02, false)]
    [InlineData(0x80, false)]
    [InlineData(0x11, false)]
    [InlineData(0xFF, false)]
    public void OnlyTheWordAndChannelSelectorsAreReportable(byte selector, bool reportable) =>
        Assert.Equal(reportable, QuestTrigger.IsReportable(selector));

    /// <remarks>
    /// One test per group rather than a theory: the answer type is internal to the assembly under
    /// test, and a public theory method cannot take it as a parameter.
    /// </remarks>
    [Fact]
    public void WhoCountsAnObjectiveComesFromItsWord()
    {
        Assert.All(
            ["meet npc", "use item", "MEET NPC"],
            objective => Assert.Equal(
                QuestObjectiveAuthority.Client,
                QuestObjectives.AuthorityOver(objective, selector: 0)));
        Assert.All(
            ["hunt monster", "clear map"],
            objective => Assert.Equal(
                QuestObjectiveAuthority.Server,
                QuestObjectives.AuthorityOver(objective, selector: 0)));
        Assert.Equal(
            QuestObjectiveAuthority.Derived,
            QuestObjectives.AuthorityOver("seeking", selector: 0));
    }

    /// <remarks>
    /// The safe direction. The archive's objective vocabulary is not closed and this server has not
    /// confirmed all of it, and a word it does not know must not become one the client is allowed
    /// to count -- that would let a crafted report finish a quest by asking.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some objective nobody has read yet")]
    public void AnObjectiveThisServerDoesNotKnowIsCountedHere(string? objective) =>
        Assert.Equal(
            QuestObjectiveAuthority.Server, QuestObjectives.AuthorityOver(objective, selector: 0));

    /// <summary>A question is answered with the whole-word selector and nothing else.</summary>
    [Fact]
    public void AQuestionTakesOnlyTheWholeWordSelector()
    {
        Assert.Equal(
            QuestObjectiveAuthority.Client, QuestObjectives.AuthorityOver("question", 0x00));
        Assert.Equal(
            QuestObjectiveAuthority.None, QuestObjectives.AuthorityOver("question", 0x20));
    }

    /// <summary>
    /// Two objectives in one: the middle channel is the visit and the client counts it; anything
    /// else is the item count and is read off the bag.
    /// </summary>
    [Fact]
    public void SeekAndMeetSplitsByChannel()
    {
        Assert.Equal(
            QuestObjectiveAuthority.Client,
            QuestObjectives.AuthorityOver("seek n meet npc", 0x20));
        Assert.All(
            new byte[] { 0x10, 0x00 },
            selector => Assert.Equal(
                QuestObjectiveAuthority.Derived,
                QuestObjectives.AuthorityOver("seek n meet npc", selector)));
    }

    /// <summary>A selector no objective takes is refused before the objective is even read.</summary>
    [Fact]
    public void AnUnreportableSelectorIsRefusedWhateverTheObjective() =>
        Assert.Equal(
            QuestObjectiveAuthority.None, QuestObjectives.AuthorityOver("meet npc", selector: 0x11));
}
