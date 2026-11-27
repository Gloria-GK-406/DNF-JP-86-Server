using System.Buffers.Binary;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Protocol.Game.GuildInfo;

namespace Dfo.Server.Protocol.Tests.Game.GuildInfo;

public sealed class GuildInfoCodecTests
{
    [Fact]
    public void Medal_aura_settings_and_character_display_use_the_same_field_order()
    {
        var aura = new GuildMedalAura(2, 5);
        var guild = new Guild(new(17), "Iris", "", 1) { MedalAura = aura };
        var info = Assert.Single(new GuildInfoEncoder().Encode(new GuildInfoMessage(guild, true, 2)));
        Assert.Equal("0200000005000000", Convert.ToHexString(info[^8..]));
        var actor = new Dfo.Server.Protocol.Game.StateSynchronization.TownReadyStateMessage(new(61), aura).EncodeBody();
        Assert.Equal("0601003D00FFFFFFFF29426268020000000500000000000000", Convert.ToHexString(actor));
        var cleared = new Dfo.Server.Protocol.Game.StateSynchronization.TownReadyStateMessage(new(61), GuildMedalAura.None).EncodeBody();
        Assert.Equal("FFFFFFFFFFFFFFFF", Convert.ToHexString(cleared.AsSpan(13, 8)));
    }

    [Fact]
    public void Panel_projects_purchased_attributes_and_remaining_teleport_uses()
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "Iris", "", 1), true, 3, Contents:
            [
                new(GuildContentKind.PermanentAttributes, 1, 169),
                new(GuildContentKind.PartyTeleport, 13, 10, 9),
            ])));
        var purchases = frame.AsSpan(15 + 4 + 4 + 36 + (6 * 28));
        Assert.Equal("0205FFFFFF7F00A900000007FFFFFF7F0109000000", Convert.ToHexString(purchases[..21]));
    }

    [Fact]
    public void Panel_writes_available_gm_separately_from_personal_and_guild_contribution()
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "测试工会", "", 1, 10, GmPoints: 13, SpentGmPoints: 20), false, 3,
                GuildContribution: new(400, 300), PersonalContribution: new(200, 100))));
        var body = frame.AsSpan(15);
        Assert.Equal(13u, BinaryPrimitives.ReadUInt32LittleEndian(body[^42..]));
        Assert.Equal(400u, BinaryPrimitives.ReadUInt32LittleEndian(body[^32..]));
        Assert.Equal(300u, BinaryPrimitives.ReadUInt32LittleEndian(body[^28..]));
        Assert.Equal(200u, BinaryPrimitives.ReadUInt32LittleEndian(body[^24..]));
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(body[^20..]));
    }

    [Fact]
    public void Panel_writes_attendance_separately_from_guild_member_count()
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "Iris", "Promotion", 1), true, 3, AttendanceCount: 2)));
        var body = frame.AsSpan(15);
        var prefix = body[(4 + BinaryPrimitives.ReadInt32LittleEndian(body))..];
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]));
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(prefix[30..]));
        Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(prefix[32..]));
    }

    [Fact]
    public void Rank_records_contain_fixed_names_and_permission_masks_before_purchases()
    {
        var configured = GuildRankSettings.Defaults.ToArray();
        configured[1] = configured[1] with { PermissionMask = 0x8DF6 };
        configured[2] = configured[2] with
        {
            PermissionMask = GuildRankSettings.AppointMembersPermission | GuildRankSettings.TransferLeadershipPermission,
        };
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "Iris", "", 1), true, 3, Ranks: configured)));
        var ranks = frame.AsSpan(15 + sizeof(int) + 4 + 36);
        const int width = sizeof(uint) + 24;
        Assert.Equal(0x9DF6u, BinaryPrimitives.ReadUInt32LittleEndian(ranks[width..]));
        Assert.Equal(GuildRankSettings.AppointMembersPermission, BinaryPrimitives.ReadUInt32LittleEndian(ranks[(width * 2)..]));
        Assert.Equal("会长"u8.ToArray(), ranks.Slice(width + sizeof(uint), "会长"u8.Length).ToArray());
        Assert.Equal(0, ranks[width + sizeof(uint) + "会长"u8.Length]);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(ranks[(width * 4)..]));
        Assert.Equal("普通成员"u8.ToArray(), ranks.Slice(width * 4 + sizeof(uint), "普通成员"u8.Length).ToArray());
        Assert.Equal(0, ranks[width * 6]);
    }

    [Fact]
    public void Panel_writes_ally_id_name_and_mark_before_the_gm_balance()
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(new GuildInfoMessage(
            new(new(2), "Iris", "", 1, GmPoints: 13), true, 3, Ally: new(new(7), "Lily", 5, new(42)))));
        var alliance = frame.AsSpan(15 + 4 + 4 + 36 + (6 * 28) + 1 + 4);
        Assert.Equal("0107000000040000004C696C79050D000000", Convert.ToHexString(alliance[..18]));
    }

    [Fact]
    public void Panel_writes_announcement_and_mark_in_distinct_slots()
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "Iris", "Promotion", 3, Announcement: "公会公告", Mark: 7), true, 3)));
        var prefix = frame.AsSpan(15 + 4 + "Iris"u8.Length);
        Assert.Equal(3, prefix[4]);
        Assert.Equal("公会公告"u8.Length, BinaryPrimitives.ReadInt32LittleEndian(prefix[10..]));
        Assert.Equal("公会公告"u8.ToArray(), prefix.Slice(14, "公会公告"u8.Length).ToArray());
        Assert.Equal(7, prefix[17 + "公会公告"u8.Length]);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 11)]
    public void Panel_sends_the_viewers_settings_flag_and_the_channel_lookup_pair(bool isLeader, int channel)
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "测试工会", "宣传", 1, channel), isLeader, 3)));
        Assert.Equal((byte)0, frame[0]);
        Assert.Equal((ushort)0x0046, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame.AsSpan(15);
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(body);
        Assert.Equal("测试工会"u8.ToArray(), body.Slice(4, nameLength).ToArray());
        var prefix = body[(4 + nameLength)..];
        Assert.Equal(isLeader ? (byte)1 : (byte)0, prefix[0]);
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]));
        Assert.Equal(channel > 0 ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(body[^16..]));
        Assert.Equal((uint)channel, BinaryPrimitives.ReadUInt32LittleEndian(body[^12..]));
    }
}
