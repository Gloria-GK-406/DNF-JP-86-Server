using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.FriendCommands;

/// <summary>
/// CMD 0x0122 / 0x0123 through the real decoder, dispatcher, command and outbox, over a real SQLite
/// character store. Character 1 ("shop1") is the requester in every case, on listener 10011.
/// </summary>
public sealed class FriendCommandTests
{
    private const ushort Add = 0x0122;
    private const ushort Delete = 0x0123;

    /// <summary>The requester's own blacklist is empty in these tests, so every follow-up is this body.</summary>
    private const string EmptyBlacklist = "0100";

    [Fact]
    public async Task Adding_an_offline_character_answers_ok_then_the_full_list_then_the_blacklist()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Add, AddBody("target"));

        Assert.Equal(["01", FullList(Entry(0, "target")), EmptyBlacklist], w.Bodies());
        Assert.Equal([((byte)1, Add), ((byte)0, (ushort)0x0111), ((byte)1, (ushort)0x0078)], w.Identities());
        Assert.Equal(1, w.Rows(1, 201));
        Assert.Equal(0, w.Rows(201, 1));
        Assert.Empty(w.FramesToOthers());
    }

    [Fact]
    public async Task An_online_friend_is_listed_with_the_channel_it_is_on()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "near");
        w.AddCharacter(202, "far");
        w.Friend(201);
        w.EnterWorld(201, 10011);
        w.EnterWorld(202, 10100);

        await w.SendAsync(Add, AddBody("far"));

        Assert.Equal(FullList(Entry(1, "near"), Entry(3, "far")), w.Bodies()[1]);
        Assert.Empty(w.FramesToOthers());
    }

    [Fact]
    public async Task The_follow_up_blacklist_is_the_requesters_own()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.AddCharacter(202, "foe");
        w.Blacklist(202, new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

        await w.SendAsync(Add, AddBody("target"));

        Assert.Equal("01" + "01" + "03000000" + Hex("foe") + "7E" + "08" + "08", w.Bodies()[2]);
    }

    [Fact]
    public async Task Adding_a_friend_already_listed_succeeds_without_a_second_row()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Add, AddBody("target"));
        await w.SendAsync(Add, AddBody("target"));

        Assert.Equal(["01", FullList(Entry(0, "target")), EmptyBlacklist], w.Bodies().Skip(3));
        Assert.Equal(1, w.Rows(1, 201));
    }

    [Fact]
    public async Task Adding_oneself_is_refused_with_the_silent_reason()
    {
        using var w = new FriendWorld();

        await w.SendAsync(Add, AddBody("shop1"));

        Assert.Equal("0001", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Fixture.Scalar("SELECT COUNT(*) FROM character_relations"));
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("gone")]
    public async Task Adding_a_name_no_live_character_has_is_refused_as_missing(string name)
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "gone", deleted: true);

        await w.SendAsync(Add, AddBody(name));

        Assert.Equal("0015", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(1, 201));
    }

    [Fact]
    public async Task Adding_onto_a_full_list_is_refused_as_full()
    {
        using var w = new FriendWorld();
        for (var id = 300; id < 364; id++)
        {
            w.AddCharacter(id, $"filler{id}");
            w.Friend(id);
        }
        w.AddCharacter(201, "target");

        await w.SendAsync(Add, AddBody("target"));

        Assert.Equal("0004", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(1, 201));
    }

    [Fact]
    public async Task Adding_from_another_server_group_is_refused_with_the_cross_group_reason()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Add, AddBody("target", server: 2));

        Assert.Equal("0007", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(1, 201));
    }

    [Theory]
    [InlineData("")]
    [InlineData("FFFF01")]
    [InlineData("FFFF010400000061")]
    public async Task A_malformed_add_is_refused_silently_and_a_malformed_delete_with_0x15(string hex)
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.Friend(201);

        await w.SendAsync(Add, Convert.FromHexString(hex));
        await w.SendAsync(Delete, Convert.FromHexString(hex.Length >= 4 ? hex[4..] : hex));

        Assert.Equal(["0001", "0015"], w.Bodies());
        Assert.Equal(1, w.Rows(1, 201));
    }

    [Fact]
    public async Task Removing_a_friend_answers_ok_then_the_removal_then_the_full_list_then_the_blacklist()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.AddCharacter(202, "stays");
        w.Friend(201);
        w.Friend(202);
        w.Friend(1, owner: 201);

        await w.SendAsync(Delete, DeleteBody("target"));

        Assert.Equal(
            [
                "01",
                "02000000" + "01000000" + "01" + "06000000" + Hex("target"),
                FullList(Entry(0, "stays")),
                EmptyBlacklist,
            ],
            w.Bodies());
        Assert.Equal([((byte)1, Delete), ((byte)0, (ushort)0x0111), ((byte)0, (ushort)0x0111), ((byte)1, (ushort)0x0078)], w.Identities());
        Assert.Equal(0, w.Rows(1, 201));
        Assert.Equal(1, w.Rows(201, 1));
        Assert.Empty(w.FramesToOthers());
    }

    [Theory]
    [InlineData("target")]
    [InlineData("nobody")]
    public async Task Removing_what_is_not_a_friend_is_refused_with_0x15(string name)
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Delete, DeleteBody(name));

        Assert.Equal("0015", Assert.Single(w.Bodies()));
    }

    [Fact]
    public async Task Removing_from_another_server_group_is_refused_with_0x15()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.Friend(201);

        await w.SendAsync(Delete, DeleteBody("target", server: 2));

        Assert.Equal("0015", Assert.Single(w.Bodies()));
        Assert.Equal(1, w.Rows(1, 201));
    }

    [Fact]
    public async Task Out_of_the_world_add_and_delete_are_refused()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.AddCharacter(202, "other");
        w.Friend(202);
        w.LeaveWorld();

        await w.SendAsync(Add, AddBody("target"));
        await w.SendAsync(Delete, DeleteBody("other"));

        Assert.Equal(["0001", "0015"], w.Bodies());
        Assert.Equal(0, w.Rows(1, 201));
        Assert.Equal(1, w.Rows(1, 202));
    }

    [Fact]
    public async Task The_last_list_after_an_add_and_a_delete_reflects_the_final_state()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "one");
        w.AddCharacter(202, "two");

        await w.SendAsync(Add, AddBody("one"));
        await w.SendAsync(Add, AddBody("two"));
        await w.SendAsync(Delete, DeleteBody("one"));

        Assert.Equal(FullList(Entry(0, "two")), w.Bodies()[^2]);
    }

    private static byte[] AddBody(string name, byte server = 1) => [0xFF, 0xFF, server, .. Counted(name)];

    private static byte[] DeleteBody(string name, byte server = 1) => [server, .. Counted(name)];

    private static byte[] Counted(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [.. BitConverter.GetBytes(bytes.Length), .. bytes];
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    /// <summary>One friend as NOTI 0x0111 carries it for a fixture character: level 30, job 12, grow 0.</summary>
    private static string Entry(ushort channel, string name) =>
        "01" + Convert.ToHexString(BitConverter.GetBytes(channel)) + Convert.ToHexString(BitConverter.GetBytes(name.Length)) + Hex(name)
        + "1E" + "0C000000" + "00" + "00" + "00000000";

    private static string FullList(params string[] entries) =>
        "00000000" + Convert.ToHexString(BitConverter.GetBytes(entries.Length)) + string.Concat(entries);

    /// <summary>Listener 10011 is channel 1 and 10100 is channel 3; the fixture has no channel catalogue of its own.</summary>
    private sealed class FixedChannels : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(listenerPort switch { 10011 => 1, 10100 => 3, _ => 0 }, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Mail, whispers and 1:1 conversations resolve a name through the mailbox query, friends and
    /// blacklists through the character domain; the first delegates to the second, so all of them
    /// find the same live character and none finds a deleted one.
    /// </summary>
    [Fact]
    public async Task Every_name_lookup_resolves_the_same_live_character()
    {
        using var w = new FriendWorld();
        w.AddCharacter(201, "target");
        w.AddCharacter(202, "gone", deleted: true);
        var mail = w.Fixture.Services.GetRequiredService<Dfo.Server.Abstractions.Mail.IMailboxQueryDomainService>();
        var characters = w.Fixture.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterIdentityQueryDomainService>();

        var byMail = await mail.FindRecipientAsync(Encoding.UTF8.GetBytes("target"));
        var byCharacter = await characters.FindByNameAsync(Encoding.UTF8.GetBytes("target"), CancellationToken.None);

        Assert.Equal(new CharacterId(201), byMail?.CharacterId);
        Assert.Equal(byCharacter?.Character, byMail?.CharacterId);
        Assert.Equal(byCharacter?.Name, byMail?.Name);
        Assert.Null(await mail.FindRecipientAsync(Encoding.UTF8.GetBytes("gone")));
        Assert.Null(await characters.FindByNameAsync(Encoding.UTF8.GetBytes("gone"), CancellationToken.None));
    }

    private sealed class FriendWorld : IDisposable
    {
        private ushort sequence;

        public FriendWorld() => Fixture = new ShopFixture(services =>
        {
            services.RemoveAll<IChannelEntryDomainService>();
            services.AddSingleton<IChannelEntryDomainService>(new FixedChannels());
        });

        public ShopFixture Fixture { get; }

        private IServiceProvider Services => Fixture.Services;

        public void AddCharacter(int character, string name, bool deleted = false) =>
            Fixture.Sql($"""
                INSERT INTO accounts(account_id,m_id) VALUES({character},'fr{character}');
                INSERT INTO characters(character_id,account_id,name,job,level,slot_index,delete_flag) VALUES({character},{character},'{name}',12,30,0,{(deleted ? 1 : 0)});
                """);

        /// <summary>A row as an earlier add by <paramref name="owner"/> would have left it.</summary>
        public void Friend(int target, int owner = 1) =>
            Fixture.Sql($"INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES({owner},{target},1,{target});");

        public void Blacklist(int target, DateTimeOffset at) =>
            Fixture.Sql($"INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES(1,{target},2,{at.ToUnixTimeSeconds()});");

        public int Rows(int owner, int target) =>
            Fixture.Scalar($"SELECT COUNT(*) FROM character_relations WHERE owner_id={owner} AND target_id={target} AND kind=1");

        /// <summary>Puts another fixture character in the world on its own connection.</summary>
        public void EnterWorld(int character, int listenerPort) =>
            TestGameplay.Run(Services, () =>
            {
                var registry = Services.GetRequiredService<OnlineStateRegistry>();
                var online = registry.RegisterConnection(new NetworkConnectionContext(
                    new NetworkConnectionId($"friend-{character}"), NetworkEndpointKind.Game, listenerPort));
                registry.Authenticate(online.OnlineUserId, new AccountId(character));
                var session = SelectedSessionIdentity.Create(new AccountId(character), new CharacterId(character),
                    online.Connection.ConnectionId, registry.GetRequiredUser(online.OnlineUserId).Generation, listenerPort);
                registry.Select(session);
                registry.EnterWorld(session);
            });

        public void LeaveWorld() =>
            TestGameplay.Run(Services, () =>
            {
                Assert.True(Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(Fixture.Session));
                Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(Fixture.Session, TownAreaDepartureReason.SessionEnded);
            });

        private IReadOnlyList<byte[]> Frames() =>
            [.. Fixture.Sender.Outbound.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == Fixture.Connection.ConnectionId)
                .SelectMany(entry => Fixture.Sender.Outbound.FramesOf(entry))];

        public IReadOnlyList<object> FramesToOthers() =>
            [.. Fixture.Sender.Outbound.Entries.Where(entry => entry.Connection.ConnectionId != Fixture.Connection.ConnectionId)];

        /// <summary>Each frame's body after the 15-byte header, in hex.</summary>
        public IReadOnlyList<string> Bodies() => [.. Frames().Select(frame => Convert.ToHexString(frame[15..]))];

        /// <summary>Each frame's selector (1 CMD, 0 NOTI) and type.</summary>
        public IReadOnlyList<(byte, ushort)> Identities() =>
            [.. Frames().Select(frame => (frame[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))))];

        public async Task SendAsync(ushort type, byte[] body)
        {
            var next = ++sequence;
            var metadata = new GameMessageMetadata(1, type, 0, next) { InboundOrdinal = next, SequenceIsFresh = true }.WithRawBody(body);
            var registration = Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == type);
            var decoded = ((IGameMessageDecoder)Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)
                ?? throw new InvalidOperationException("The decoder declined the body.");
            await Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
                new NetworkDispatchContext(Fixture.Connection), decoded, default);
        }

        public void Dispose() => Fixture.Dispose();
    }
}
