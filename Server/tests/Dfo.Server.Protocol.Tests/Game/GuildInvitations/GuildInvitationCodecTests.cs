using Dfo.Server.Data.Guilds;
using Dfo.Server.Protocol.Game.GuildInvitations;

namespace Dfo.Server.Protocol.Tests.Game.GuildInvitations;

public sealed class GuildInvitationCodecTests
{
    [Fact]
    public void Invitation_writes_three_strings_before_the_counts_and_active_content()
    {
        var notice = new GuildInvited("公会", "会长"u8.ToArray(), "招募", 258, 3,
            [GuildContentKind.PermanentAttributes, GuildContentKind.PartyTeleport]);
        var frame = Assert.Single(new GuildInvitedEncoder().Encode(notice));

        Assert.Equal(
            "06000000E585ACE4BC9A06000000E4BC9AE995BF06000000E68B9BE58B9F030000000201020507",
            Convert.ToHexString(frame.AsSpan(15)));
    }

    [Theory]
    [InlineData(false, "00")]
    [InlineData(true, "01")]
    public void Inviter_result_includes_the_invitees_name(bool accepted, string result)
    {
        var notice = new GuildInviteResolved(accepted, "队员"u8.ToArray());
        var frame = Assert.Single(new GuildInviteResolvedEncoder().Encode(notice));

        Assert.Equal(result + "06000000E9989FE59198", Convert.ToHexString(frame.AsSpan(15)));
    }
}
