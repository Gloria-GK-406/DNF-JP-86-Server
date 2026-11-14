using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

namespace Dfo.Pvf.Tests;

public sealed class SectionRepeatTests
{
    private static readonly PvfField<int> Field = PvfField.Int32("[value]", optional: true);
    private static readonly PvfSection[] Repeated =
        [new("[value]", [new(PvfTokenKind.Int32, 1)]), new("[other]", []), new("[value]", [new(PvfTokenKind.Int32, 2)])];

    [Fact]
    public void Each_repeat_policy_is_stated_by_the_caller()
    {
        Assert.Throws<InvalidDataException>(() => PvfSections.Single(Repeated, Field, PvfRepeats.Reject, "test"));
        Assert.Null(PvfSections.Single(Repeated, Field, PvfRepeats.AsUndeclared, "test"));
        Assert.Equal(2, PvfSections.Single(Repeated, Field, PvfRepeats.LastWins, "test")!.Values[0].Value);
        Assert.Equal(1, PvfSections.Single(Repeated.Take(1).ToArray(), Field, PvfRepeats.Reject, "test")!.Values[0].Value);
    }
}
