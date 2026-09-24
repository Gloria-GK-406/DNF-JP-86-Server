using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.InformNotice;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class NoticeStateTests
{
    [Fact]
    public void Both_notice_namespaces_keep_exact_layout_and_full_replacement_shape()
    {
        var primary = Assert.IsType<NoticeSeenRequest>(new NoticeSeenDecoder().Decode(GameMessageMetadata.Empty, [8,0,0,0]));
        var secondary = Assert.IsType<InformNoticeSecondReport>(new InformNoticeSecondDecoder().Decode(GameMessageMetadata.Empty, [8,0,0,0]));
        Assert.NotEqual(primary.Group, secondary.Group);
        Assert.Equal(primary.Value, secondary.Value);
        Assert.Equal(new byte[] { 0 }, NoticeStateCodec.Encode([]));
        Assert.Equal(new byte[] { 3,0,8,127 }, NoticeStateCodec.Encode([127,8,0,8]));
        var snapshot = new SecondaryNoticeStateMessage([16,15]);
        Assert.Equal(0x01B9, snapshot.Type);
        Assert.Equal(new byte[] { 2,15,16 }, snapshot.EncodeBody());
        Assert.Equal(129, NoticeStateCodec.Encode(Enumerable.Range(0,128).Select(i => (byte)i).ToArray()).Length);
    }

    [Fact]
    public void Character_replay_uses_seen_notices_and_never_growth_weapon_stages()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var changed = source with
        {
            CharacterEntry = source.CharacterEntry with
            {
                Settings = source.CharacterEntry.Settings with { SeenNotices = [15,16] },
                Progression = source.CharacterEntry.Progression with { GrowthWeaponStages = [99] },
            },
        };
        var message = Assert.Single(Dfo.Server.Protocol.Game.SelectCharacter.SelectedCharacterStateMessageFactory.Create(changed)
            .OfType<SecondaryNoticeStateMessage>());
        Assert.Equal(new byte[] { 2,15,16 }, message.EncodeBody());
    }

    [Theory]
    [InlineData("")]
    [InlineData("080000")]
    [InlineData("0800000000")]
    [InlineData("80000000")]
    [InlineData("00010000")]
    [InlineData("FFFFFFFF")]
    public void Invalid_ids_and_lengths_cannot_reach_persistence(string hex)
    {
        var body = Convert.FromHexString(hex);
        Assert.Throws<InvalidDataException>(() => new NoticeSeenDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Throws<InvalidDataException>(() => new InformNoticeSecondDecoder().Decode(GameMessageMetadata.Empty, body));
    }
}
