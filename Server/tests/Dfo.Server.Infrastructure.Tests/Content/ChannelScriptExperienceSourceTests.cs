using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Which channel pays extra for which dungeons, read out of the channel script.
/// </summary>
/// <remarks>
/// The condition is the point. This server paid the channel's rate on every kill on every map
/// until 2026-08-29, because the rate had been recovered from four captured kills that happened to
/// be in a dungeon the channel carried, and a rate that fits every sample it came from says
/// nothing about when it applies. So the negative cases here carry as much weight as the positive
/// one.
/// </remarks>
public sealed class ChannelScriptExperienceSourceTests
{
    /// <summary>A channel pays for a dungeon in the group it is bound to.</summary>
    [Fact]
    public void AChannelPaysForADungeonInItsOwnGroup()
    {
        var catalog = ChannelScriptExperienceSource.Parse(Script);

        Assert.Equal(0.05, catalog.RateFor(channelId: 10, dungeonId: 144));
    }

    /// <summary>And pays nothing for one outside it.</summary>
    /// <remarks>
    /// The same dungeon, the same rate declared, a different channel. This is the whole rule, and
    /// it is what was missing: dungeon 144 is 拉德送葬曲's, and running it on the Sky Castle
    /// channel earns the ordinary amount.
    /// </remarks>
    [Fact]
    public void AChannelPaysNothingForADungeonOutsideItsGroup()
    {
        var catalog = ChannelScriptExperienceSource.Parse(Script);

        Assert.Equal(0.05, catalog.RateFor(channelId: 11, dungeonId: 157));
        Assert.Equal(0, catalog.RateFor(channelId: 11, dungeonId: 144));
    }

    /// <summary>A channel the file never lists pays nothing.</summary>
    [Fact]
    public void AnUnlistedChannelPaysNothing() =>
        Assert.Equal(0, ChannelScriptExperienceSource.Parse(Script).RateFor(999, 144));

    /// <summary>A channel bound to a group of no dungeons pays nothing.</summary>
    /// <remarks>
    /// Several real rows name a group the file never describes. That is not a contradiction, it is
    /// a channel with no dungeons of its own -- and the answer is the same either way.
    /// </remarks>
    [Fact]
    public void AChannelWithNoDungeonsOfItsOwnPaysNothing() =>
        Assert.Equal(0, ChannelScriptExperienceSource.Parse(Script).RateFor(50, 144));

    /// <summary>A row declaring zero pays zero.</summary>
    [Fact]
    public void AChannelThatDeclaresZeroPaysZero() =>
        Assert.Equal(0, ChannelScriptExperienceSource.Parse(Script).RateFor(1, 11000));

    /// <summary>The section's opening count is not read as a channel.</summary>
    /// <remarks>
    /// It is a bare number on its own line, and it collides with a real channel number. Reading it
    /// as a row would give channel 1 whatever the count happens to say.
    /// </remarks>
    [Fact]
    public void TheSectionsOpeningCountIsNotAChannel()
    {
        var catalog = ChannelScriptExperienceSource.Parse(Script);

        var one = Assert.Single(catalog.Entries, entry => entry.ChannelId == 1);
        Assert.Equal("[deathtower]", one.DungeonGroup);
    }

    /// <summary>A row this cannot read disqualifies its channel rather than being guessed at.</summary>
    [Theory]
    [InlineData("   10  `<4::name>`   1")]
    [InlineData("   10  `<4::name>`   x   `[granfloris]`   5")]
    [InlineData("   10  `<4::name>`   1   `[granfloris]`   abc")]
    [InlineData("   10  `<4::name>`   1   `[granfloris]`   150")]
    [InlineData("   10  `<4::name>`   1   `[granfloris]`   -5")]
    public void ARowThatCannotBeReadPaysNothing(string row)
    {
        var catalog = ChannelScriptExperienceSource.Parse(
            "[dungeon]\n`[granfloris]`\n144\n[/dungeon]\n[server]\n1\n" + row + "\n[/server]\n");

        Assert.Equal(0, catalog.RateFor(10, 144));
    }

    /// <summary>A channel declared twice pays nothing, rather than one of its two answers.</summary>
    [Fact]
    public void AChannelDeclaredTwicePaysNothing()
    {
        var catalog = ChannelScriptExperienceSource.Parse(
            "[dungeon]\n`[granfloris]`\n144\n[/dungeon]\n[server]\n1\n"
            + "   10  `<4::name>`   1   `[granfloris]`   5\n"
            + "   10  `<4::name>`   1   `[granfloris]`   20\n[/server]\n");

        Assert.Equal(0, catalog.RateFor(10, 144));
    }

    /// <summary>A group declared twice pays nothing for any dungeon either copy lists.</summary>
    [Fact]
    public void AGroupDeclaredTwicePaysNothing()
    {
        var catalog = ChannelScriptExperienceSource.Parse(
            "[dungeon]\n`[granfloris]`\n144\n[/dungeon]\n"
            + "[dungeon]\n`[granfloris]`\n145\n[/dungeon]\n"
            + "[server]\n1\n   10  `<4::name>`   1   `[granfloris]`   5\n[/server]\n");

        Assert.Equal(0, catalog.RateFor(10, 144));
        Assert.Equal(0, catalog.RateFor(10, 145));
    }

    /// <summary>Text with nothing to read yields a catalogue in which nobody pays.</summary>
    /// <remarks>
    /// Never an exception. A server whose channel script is unreadable still awards experience;
    /// it simply awards no channel reward, and says so once in the log.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense with no sections at all")]
    [InlineData("[dungeon]\n`[granfloris]`\n144\n[/dungeon]\n")]
    [InlineData("[server]\n1\n[/server]\n")]
    public void TextThatDeclaresNothingLeavesEveryChannelPayingNothing(string text)
    {
        var catalog = ChannelScriptExperienceSource.Parse(text);

        Assert.Equal(0, catalog.RateFor(10, 144));
    }

    /// <summary>
    /// The file this server actually ships declares what the capture measured.
    /// </summary>
    /// <remarks>
    /// The bridge between the sample and the rule. The four captured awards were taken in dungeon
    /// 144 and each paid five percent more than the kill was worth; this asserts that the shipped
    /// archive really does put 144 in a group whose channel declares five, which is what turns a
    /// number that fits four samples into a rule with a condition.
    /// </remarks>
    [SkippableFact]
    public void TheShippedScriptDeclaresTheRateTheCaptureMeasured()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "Dfo.Server.Host", "Resources", "Channel",
            "channel_info.etc");
        Skip.IfNot(File.Exists(path), "The host's channel_info.etc is not beside this test run.");

        var catalog = ChannelScriptExperienceSource.Parse(File.ReadAllText(path));
        var carrying = catalog.Entries.Where(entry => entry.Covers(144)).ToArray();

        var channel = Assert.Single(carrying);
        Assert.Equal(0.05, channel.Rate);
    }

    /// <summary>
    /// The shape of the shipped file, cut down to the rows these tests reason about.
    /// </summary>
    /// <remarks>
    /// Transcribed rather than loaded, so the negative cases stay stable if a deployment edits its
    /// own channels. The one case that must read the real file says so.
    /// </remarks>
    private const string Script = """
        [dungeon]
        `[granfloris]`
        <4::channel_info_dname_1>
        144
        145
        146
        [/dungeon]

        [dungeon]
        `[sky_catle]`
        <4::channel_info_dname_2>
        156
        157
        158
        [/dungeon]

        [dungeon]
        `[deathtower]`
        <4::channel_info_dname_7>
        11000
        11001
        [/dungeon]

        [server]
        1
           1   `<4::chn_channel_info_001>`   11   `[deathtower]`   0   0   0   0   0   ``
           10  `<4::chn_channel_info_003>`   1    `[granfloris]`   5   0   0   0   0   ``
           11  `<4::chn_channel_info_004>`   1    `[sky_catle]`    5   0   0   0   0   ``
           50  `<4::channel_info_cname_701>` 24   `[none]`         0   0  10   1   0   ``
        [/server]
        """;
}
