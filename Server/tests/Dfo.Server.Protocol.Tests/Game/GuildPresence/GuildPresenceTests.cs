using System.Buffers.Binary;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GuildPresence;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.GuildPresence;

public sealed class GuildPresenceTests
{
    [Theory]
    [InlineData((byte)10, (byte)1)]
    [InlineData((byte)0, (byte)0)]
    public void Presence_encodes_the_native_state_channel_and_dstring(byte channel, byte state)
    {
        var frame = Assert.Single(new GuildMemberPresenceEncoder().Encode(
            new GuildMemberPresenceMessage(channel, "成员"u8.ToArray())));
        Assert.Equal((byte)0, frame[0]);
        Assert.Equal((ushort)0x0040, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        byte[] expected = [state, channel, 6, 0, 0, 0, .. "成员"u8];
        Assert.Equal(expected, frame[15..]);
    }

    [Fact]
    public void Presence_audience_is_derived_from_membership_and_excludes_the_subject()
    {
        var member = new GuildRosterEntry(new(new(42), new(11), true, DateTimeOffset.UnixEpoch), new(1), [0x61], 30, 0, 0);
        var peer = new GuildRosterEntry(new(new(42), new(12), false, DateTimeOffset.UnixEpoch), new(2), [0x62], 30, 0, 0);
        Assert.Null(GuildPresenceAudience.FromRoster(new(13), [member, peer]));
        Assert.Null(GuildPresenceAudience.FromRoster(new(11), [member]));
        var audience = Assert.IsType<GuildPresenceAudience>(GuildPresenceAudience.FromRoster(new(11), [member, peer]));
        Assert.Equal(member.Name, audience.Name);
        Assert.Equal(peer.Membership.Character, Assert.Single(audience.OtherMembers));
    }

    [Fact]
    public async Task Stale_arrivals_and_departures_do_not_overwrite_a_new_world_session()
    {
        var old = SelectedSessionIdentity.Create(new(1), new(11), new("old"), 1, 10010);
        var current = SelectedSessionIdentity.Create(new(1), new(11), new("current"), 2, 10011);
        var sessions = TestRequired.Absent<ISelectedSessionDomainService>((method, args) => method.Name switch
        {
            nameof(ISelectedSessionDomainService.IsInWorld) => Equals(args![0], current),
            nameof(ISelectedSessionDomainService.TryGetInWorld) => true,
            _ => throw new NotSupportedException(method.Name),
        });
        var handler = new GuildMemberPresenceHandler(sessions);
        var recipient = new NotificationRecipient(new(12), new("peer"));
        Assert.Empty(await handler.ComposeAsync(recipient, new(old.CharacterId, [0x61], 10, old), default));
        Assert.Empty(await handler.ComposeAsync(recipient, new(old.CharacterId, [0x61], 0), default));
        Assert.Empty(await handler.ComposeAsync(recipient with { CharacterId = current.CharacterId },
            new(current.CharacterId, [0x61], 11, current), default));
        var notice = Assert.IsType<GuildMemberPresenceMessage>(Assert.Single(
            await handler.ComposeAsync(recipient, new(current.CharacterId, [0x61], 11, current), default)));
        Assert.Equal((byte)11, notice.Channel);
    }
}
