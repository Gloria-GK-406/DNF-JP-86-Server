using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.SendChat;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Chat;

/// <summary>
/// Chat lines through the real decoder, dispatcher, command, projection and outbox, over a real
/// SQLite character store. Character 1 ("shop1") is the speaker in every case.
/// </summary>
public sealed class SendChatCommandTests
{
    private const int FirstChannel = 10011;
    private const int DuelChannel = 10100;
    private const short MegaphoneSlot = 65;
    private static readonly TownAreaKey SpeakerArea = new(1, 0, 0);
    private static readonly TownAreaKey OtherArea = new(1, 0, 1);

    [Fact]
    public async Task Area_line_reaches_the_speakers_area_including_the_speaker_and_is_not_answered()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        var c = w.Add(3, FirstChannel, OtherArea);
        var d = w.Add(4, FirstChannel, SpeakerArea);
        w.LeaveTown(d);

        await w.SendAsync(w.Speaker, Chat(3, "hello"));

        foreach (var reached in new[] { w.Speaker, b })
        {
            var line = Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(reached)));
            Assert.Equal((byte)3, line.Mode);
            Assert.Equal(w.Speaker.WireUserId, line.SenderUserId);
            Assert.Equal("hello"u8.ToArray(), line.Text);
        }
        Assert.Empty(w.MessagesFor(c));
        Assert.Empty(w.MessagesFor(d));
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
        Assert.Equal([0x00, 0x0C, 0x00], w.FramesFor(b).Single()[..3]);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(46)]
    public async Task The_other_area_modes_reach_the_area_and_keep_their_mode(byte mode)
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(mode, "x"));

        Assert.Equal(mode, Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(b))).Mode);
        Assert.Single(w.MessagesFor(w.Speaker));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Party_line_reaches_every_member_in_the_world_even_in_a_dungeon_and_no_one_outside(byte mode)
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);
        w.FormParty(w.Speaker, b);
        w.LeaveTown(b);

        await w.SendAsync(w.Speaker, Chat(mode, "party"));

        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(w.Speaker)));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(b)));
        Assert.Empty(w.MessagesFor(c));
    }

    [Fact]
    public async Task Party_line_without_a_party_reaches_no_one_and_is_not_refused()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(2, "alone"));

        Assert.Empty(w.AllMessages);
        _ = b;
    }

    [Fact]
    public async Task Guild_line_reaches_no_one_and_is_not_refused()
    {
        using var w = new ChatWorld();
        _ = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(6, "guild"));

        Assert.Empty(w.AllMessages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task Whisper_reaches_only_its_target_with_the_speakers_name(byte mode)
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, OtherArea);
        var c = w.Add(3, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(mode, "psst", target: "chat2"));

        var whisper = Assert.IsType<WhisperMessage>(Assert.Single(w.MessagesFor(b)));
        Assert.Equal(mode, whisper.Mode);
        Assert.Equal("shop1"u8.ToArray(), whisper.SenderName);
        Assert.Equal("psst"u8.ToArray(), whisper.Text);
        Assert.Empty(w.MessagesFor(w.Speaker));
        Assert.Empty(w.MessagesFor(c));
    }

    [Fact]
    public async Task Whisper_to_an_offline_or_unknown_name_is_silent_and_the_next_line_still_goes_through()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);
        w.Release(b);
        var c = w.Add(3, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(1, "anyone?", target: "chat2"));
        await w.SendAsync(w.Speaker, Chat(1, "anyone?", target: "nobody"));
        await w.SendAsync(w.Speaker, Chat(1, "me?", target: "shop1"));
        Assert.Empty(w.AllMessages);

        await w.SendAsync(w.Speaker, Chat(3, "after"));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(c)));
        Assert.IsType<ChatLineMessage>(Assert.Single(w.MessagesFor(w.Speaker)));
    }

    [Fact]
    public async Task Channel_megaphone_uses_one_item_37_and_reaches_the_speakers_channel_only()
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var b = w.Add(2, FirstChannel, OtherArea);
        w.LeaveTown(b);
        var c = w.Add(3, DuelChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(13, "sale", aux: (uint)MegaphoneSlot));

        Assert.Equal(2, w.Fixture.Count(37));
        var channel = await w.ChannelIdAsync(FirstChannel);
        var line = Assert.IsType<MegaphoneMessage>(Assert.Single(w.MessagesFor(b)));
        Assert.Equal((byte)13, line.Mode);
        Assert.Equal(channel, line.ChannelId);
        Assert.Equal((byte)1, line.ServerId);
        Assert.Equal(w.Speaker.WireUserId, line.SenderUserId);
        Assert.Equal("shop1"u8.ToArray(), line.SenderName);
        Assert.Empty(w.MessagesFor(c));
        // The speaker's own line comes first, then the slot's absolute state.
        var own = w.MessagesFor(w.Speaker);
        Assert.Equal(2, own.Count);
        Assert.IsType<MegaphoneMessage>(own[0]);
        Assert.Equal("InventoryDeltaMessage", own[1].GetType().Name);
        Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
    }

    [Theory]
    [InlineData(11, 36)]
    [InlineData(12, 898)]
    [InlineData(57, 10151255)]
    public async Task Server_megaphone_uses_its_own_item_and_reaches_everyone_in_the_world_on_every_channel(byte mode, int item)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, item, 2);
        var b = w.Add(2, FirstChannel, OtherArea);
        var c = w.Add(3, DuelChannel, SpeakerArea);
        w.LeaveTown(c);
        var gone = w.Add(4, DuelChannel, SpeakerArea);
        w.Release(gone);

        await w.SendAsync(w.Speaker, Chat(mode, "everyone", aux: (uint)MegaphoneSlot));

        Assert.Equal(1, w.Fixture.Count(item));
        foreach (var reached in new[] { w.Speaker, b, c })
            Assert.Equal(mode, Assert.IsType<MegaphoneMessage>(w.MessagesFor(reached)[0]).Mode);
        Assert.Empty(w.MessagesFor(gone));
    }

    [Fact]
    public async Task Last_megaphone_clears_its_slot_after_the_line()
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 36, 1);

        await w.SendAsync(w.Speaker, Chat(11, "last", aux: (uint)MegaphoneSlot));

        Assert.Equal(0, w.Fixture.Count(36));
        var own = w.MessagesFor(w.Speaker);
        Assert.IsType<MegaphoneMessage>(own[0]);
        Assert.Equal("InventoryDeltaMessage", own[1].GetType().Name);
    }

    [Theory]
    [InlineData(13, 0, 37)]
    [InlineData(13, 36, 37)]
    [InlineData(11, 37, 36)]
    [InlineData(12, 36, 898)]
    public async Task Megaphone_from_an_empty_or_wrong_slot_is_refused_and_nothing_is_used(byte mode, int seeded, int expected)
    {
        using var w = new ChatWorld();
        if (seeded != 0) await w.Fixture.Seed(MegaphoneSlot, seeded, 3);
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(mode, "free?", aux: (uint)MegaphoneSlot));

        w.AssertRefused(0x0011);
        Assert.Empty(w.MessagesFor(b));
        if (seeded != 0) Assert.Equal(3, w.Fixture.Count(seeded));
        Assert.Equal(0, w.Fixture.Count(expected));
    }

    [Theory]
    [InlineData(70000u)]
    [InlineData(uint.MaxValue)]
    public async Task Megaphone_slot_outside_the_bag_is_refused(uint aux)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);

        await w.SendAsync(w.Speaker, Chat(13, "where", aux: aux));

        w.AssertRefused(0x0011);
        Assert.Equal(3, w.Fixture.Count(37));
    }

    public static TheoryData<byte> RefusedModes()
    {
        var modes = new TheoryData<byte> { 0, 4, 0x0A, 43, 48, 14, 15, 34, 50, 56, 36, 37, 38, 5, 8, 200, 255 };
        for (byte mode = 0x10; mode <= 0x1E; mode++) modes.Add(mode);
        for (byte mode = 52; mode <= 55; mode++) modes.Add(mode);
        return modes;
    }

    [Theory]
    [MemberData(nameof(RefusedModes))]
    public async Task Every_mode_outside_the_first_batch_is_refused_without_delivery(byte mode)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var b = w.Add(2, FirstChannel, SpeakerArea);
        w.FormParty(w.Speaker, b);

        await w.SendAsync(w.Speaker, Chat(mode, "nope", target: "chat2", aux: (uint)MegaphoneSlot));

        w.AssertRefused(0x0011);
        Assert.Empty(w.MessagesFor(b));
        Assert.Equal(3, w.Fixture.Count(37));
    }

    [Fact]
    public async Task Item_link_line_is_refused_under_its_own_type_without_delivery()
    {
        using var w = new ChatWorld();
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendRawAsync(w.Speaker, 0x01A9, [3, 0, 0, 0xDE, 0xAD]);

        w.AssertRefused(0x01A9);
        Assert.IsType<ItemLinkChatRefusal>(Assert.Single(w.AllMessages));
        Assert.Empty(w.MessagesFor(b));
    }

    [Theory]
    [InlineData(3, 0, false)]
    [InlineData(3, 255, true)]
    [InlineData(3, 256, false)]
    [InlineData(1, 255, true)]
    [InlineData(1, 256, false)]
    [InlineData(13, 511, true)]
    [InlineData(13, 512, false)]
    public async Task Text_must_be_present_and_within_the_frames_limit(byte mode, int length, bool delivered)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await w.SendAsync(w.Speaker, Chat(mode, new string('a', length), target: "chat2", aux: (uint)MegaphoneSlot));

        if (delivered)
        {
            Assert.Equal(length, Text(Assert.Single(w.MessagesFor(b))).Length);
            Assert.DoesNotContain(w.AllMessages, message => message is SendChatRefusal);
        }
        else
        {
            w.AssertRefused(0x0011);
            Assert.Empty(w.MessagesFor(b));
            Assert.Equal(3, w.Fixture.Count(37));
        }
    }

    [Theory]
    [InlineData("0300000000000000")]
    [InlineData("03000000000000FFFFFFFF")]
    [InlineData("03000000000000010400006161")]
    [InlineData("0300000000000005000000616263")]
    [InlineData("01000000000000010000006141000000")]
    [InlineData("0100000000000001000000610100000062")]
    public async Task A_frame_that_cannot_be_decoded_delivers_nothing_and_changes_nothing(string hex)
    {
        using var w = new ChatWorld();
        await w.Fixture.Seed(MegaphoneSlot, 37, 3);
        var b = w.Add(2, FirstChannel, SpeakerArea);

        await Assert.ThrowsAsync<InvalidDataException>(() => w.SendRawAsync(w.Speaker, 0x0011, Convert.FromHexString(hex)));

        Assert.Empty(w.AllMessages);
        Assert.Equal(3, w.Fixture.Count(37));
        _ = b;
    }

    private static byte[] Text(Dfo.Server.Data.Networking.IServerNetworkMessage message) => message switch
    {
        ChatLineMessage line => line.Text,
        WhisperMessage whisper => whisper.Text,
        MegaphoneMessage megaphone => megaphone.Text,
        _ => throw new InvalidOperationException(message.GetType().Name),
    };

    /// <summary>A CMD 0x0011 body as the A21 client writes it.</summary>
    internal static byte[] Chat(byte mode, string text, string? target = null, uint aux = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(mode);
        writer.Write((ushort)0);
        writer.Write(aux);
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write(bytes.Length); writer.Write(bytes);
        if (ChatWireModes.CarriesTarget(mode))
        {
            var name = Encoding.UTF8.GetBytes(target ?? "");
            writer.Write(name.Length); writer.Write(name);
            writer.Write((byte)0);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private sealed class ChatWorld : IDisposable
    {
        private readonly Dictionary<CharacterId, NetworkConnectionContext> connections = [];
        private ushort sequence;

        public ChatWorld()
        {
            Fixture = new ShopFixture();
            connections[Fixture.Session.CharacterId] = Fixture.Connection;
        }

        public ShopFixture Fixture { get; }

        public SelectedSessionIdentity Speaker => Fixture.Session;

        private IServiceProvider Services => Fixture.Services;

        public IReadOnlyList<Dfo.Server.Data.Networking.IServerNetworkMessage> AllMessages => Fixture.Sender.Messages;

        public IReadOnlyList<Dfo.Server.Data.Networking.IServerNetworkMessage> MessagesFor(SelectedSessionIdentity session) =>
            Fixture.Sender.Outbound.MessagesFor(session.ConnectionId);

        public IReadOnlyList<byte[]> FramesFor(SelectedSessionIdentity session) =>
            [.. Fixture.Sender.Outbound.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == session.ConnectionId)
                .SelectMany(entry => Fixture.Sender.Outbound.FramesOf(entry))];

        public void AssertRefused(ushort type)
        {
            var frame = Assert.Single(FramesFor(Speaker));
            Assert.Equal((byte)1, frame[0]);
            Assert.Equal(type, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
            Assert.Equal(new byte[] { 0, 0x11 }, frame[15..]);
        }

        public SelectedSessionIdentity Add(int character, int listenerPort, TownAreaKey area)
        {
            var account = 100 + character;
            Fixture.Sql($"""
                INSERT INTO accounts(account_id,m_id) VALUES({account},'chat{account}');
                INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES({character + 100},{account},'chat{character}',12,1,0);
                INSERT INTO character_stats(character_id,carried_gold) VALUES({character + 100},0);
                """);
            var connection = new NetworkConnectionContext(new($"chat-{character}"), NetworkEndpointKind.Game, listenerPort);
            Fixture.Sender.Outbound.Connections.Add(connection);
            var session = TestGameplay.Run(Services, () =>
            {
                var registry = Services.GetRequiredService<OnlineStateRegistry>();
                var user = registry.RegisterConnection(connection).OnlineUserId;
                registry.Authenticate(user, new(account));
                var selected = SelectedSessionIdentity.Create(new(account), new(character + 100), connection.ConnectionId,
                    registry.GetRequiredUser(user).Generation, listenerPort);
                registry.Select(selected);
                registry.EnterWorld(selected);
                var position = new CharacterPositionState(area.TownId, area.AreaId, 100, 200, 0);
                Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().Begin(selected, position, 100);
                Services.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(selected, area, position, true));
                return selected;
            });
            connections[session.CharacterId] = connection;
            return session;
        }

        public void LeaveTown(SelectedSessionIdentity session) =>
            TestGameplay.Run(Services, () => Assert.NotNull(Services.GetRequiredService<ITownAreaDomainService>()
                .LeaveExact(session, TownAreaDepartureReason.DungeonEntry)));

        public void Release(SelectedSessionIdentity session) =>
            TestGameplay.Run(Services, () =>
            {
                Assert.True(Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().ReleaseExact(session));
                Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(session, TownAreaDepartureReason.SessionEnded);
            });

        public void FormParty(SelectedSessionIdentity leader, params SelectedSessionIdentity[] members) =>
            TestGameplay.Run(Services, () =>
            {
                var parties = Services.GetRequiredService<IPartyService>();
                var inviter = Snapshot(leader);
                foreach (var member in members)
                {
                    Assert.True(parties.RequestInvite(inviter, Snapshot(member), new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
                    Assert.True(parties.AcceptInvite(Snapshot(member), inviter).Succeeded);
                }
            });

        public async Task<byte> ChannelIdAsync(int listenerPort) =>
            checked((byte)(await Services.GetRequiredService<IChannelEntryDomainService>().ResolveAsync(listenerPort, default)).ChannelId);

        public Task SendAsync(SelectedSessionIdentity speaker, byte[] body) => SendRawAsync(speaker, 0x0011, body);

        public async Task SendRawAsync(SelectedSessionIdentity speaker, ushort type, byte[] body)
        {
            var next = ++sequence;
            var metadata = new GameMessageMetadata(1, type, 0, next) { InboundOrdinal = next, SequenceIsFresh = true }.WithRawBody(body);
            var registration = Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == type);
            var decoded = ((IGameMessageDecoder)Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)
                ?? throw new InvalidOperationException("The decoder declined the body.");
            await Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
                new NetworkDispatchContext(connections[speaker.CharacterId]), decoded, default);
        }

        private static PartyCharacterSnapshot Snapshot(SelectedSessionIdentity session) =>
            new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(1, 0, 0));

        public void Dispose() => Fixture.Dispose();
    }
}
