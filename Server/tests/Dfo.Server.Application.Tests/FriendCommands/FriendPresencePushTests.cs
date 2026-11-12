using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.FriendCommands;

/// <summary>
/// The pushes a character's observers receive (Spec RQ-050 – RQ-055), through the real notification
/// bus, the real encoders and outbox, and a real SQLite store. Character 1 ("shop1", channel 1) is the
/// observer in every case unless a case says otherwise; what it receives is read off its connection.
/// </summary>
public sealed class FriendPresencePushTests
{
    private const ushort FriendInfo = 0x0111;
    private const ushort FriendPresence = 0x0112;
    private const ushort BlacklistList = 0x0078;
    private const string EmptyBlacklist = "0100";

    [Fact]
    public async Task An_observer_is_told_a_friend_came_online_with_the_channel_it_entered()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);
        w.EnterWorld(201, 10100);

        await w.ProjectAsync([new CharacterId(1)], new FriendCameOnline(new CharacterId(201)));

        Assert.Equal([((byte)0, FriendPresence)], w.Identities(w.Observer));
        Assert.Equal([Presence(3, "pal", blacklisted: false)], w.Bodies(w.Observer));
    }

    [Fact]
    public async Task An_observer_on_the_same_channel_is_still_given_the_channel_number()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);
        w.EnterWorld(201, 10011);

        await w.ProjectAsync([new CharacterId(1)], new FriendCameOnline(new CharacterId(201)));

        Assert.Equal([Presence(1, "pal", blacklisted: false)], w.Bodies(w.Observer));
    }

    [Fact]
    public async Task The_presence_mark_says_whether_the_recipient_blacklisted_the_friend()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);
        w.Blacklist(201);
        w.EnterWorld(201, 10100);

        await w.ProjectAsync([new CharacterId(1)], new FriendCameOnline(new CharacterId(201)));
        await w.ProjectAsync([new CharacterId(1)], new FriendWentOffline(new CharacterId(201), "pal"u8.ToArray()));

        Assert.Equal([Presence(3, "pal", blacklisted: true), Presence(0, "pal", blacklisted: true)], w.Bodies(w.Observer));
    }

    [Fact]
    public async Task An_observer_is_told_a_friend_left_with_channel_zero()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);

        await w.ProjectAsync([new CharacterId(1)], new FriendWentOffline(new CharacterId(201), "pal"u8.ToArray()));

        Assert.Equal([((byte)0, FriendPresence)], w.Identities(w.Observer));
        Assert.Equal([Presence(0, "pal", blacklisted: false)], w.Bodies(w.Observer));
    }

    /// <summary>R1: the list refresh carries a CMD frame (0x0078) and the projection path sends it as CMD.</summary>
    [Fact]
    public async Task A_level_change_sends_each_observer_its_full_list_then_its_blacklist()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.AddCharacter(202, "stranger");
        w.Friend(201);
        w.EnterWorld(201, 10100);
        var stranger = w.EnterWorld(202, 10011);
        w.Fixture.Sql("UPDATE characters SET level=31 WHERE character_id=201");

        await w.LevelChangedAsync(201, 30, 31);

        Assert.Equal([((byte)0, FriendInfo), ((byte)1, BlacklistList)], w.Identities(w.Observer));
        Assert.Equal([FullList(Entry(3, "pal", level: 31)), EmptyBlacklist], w.Bodies(w.Observer));
        Assert.Empty(w.Bodies(stranger.ConnectionId));
        Assert.Empty(w.Bodies(new NetworkConnectionId("friend-201")));
    }

    [Fact]
    public async Task A_grow_type_change_sends_each_observer_its_full_list_then_its_blacklist()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);
        w.Fixture.Sql("UPDATE characters SET grow_type=1 WHERE character_id=201");

        await w.GrowTypeChangedAsync(201, 1);

        Assert.Equal([((byte)0, FriendInfo), ((byte)1, BlacklistList)], w.Identities(w.Observer));
        Assert.Equal([FullList(Entry(0, "pal", grow: 1)), EmptyBlacklist], w.Bodies(w.Observer));
    }

    [Fact]
    public async Task Nobody_is_refreshed_when_the_changed_character_is_nobodys_friend()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.AddCharacter(202, "own");
        w.Friend(202, owner: 201);
        w.EnterWorld(201, 10100);

        await w.LevelChangedAsync(201, 30, 31);

        Assert.DoesNotContain(w.Fixture.Sender.Outbound.Entries, entry => !entry.IsClose);
    }

    [Fact]
    public async Task Subsequent_changes_leave_the_last_list_showing_the_final_state()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.AddCharacter(202, "other");
        w.Friend(201);
        w.Friend(202);
        w.AddDeletingAccount(201);

        w.Fixture.Sql("UPDATE characters SET level=31 WHERE character_id=201");
        await w.LevelChangedAsync(201, 30, 31);
        w.Fixture.Sql("UPDATE characters SET level=35 WHERE character_id=201");
        await w.LevelChangedAsync(201, 31, 35);
        Assert.Equal(FullList(Entry(0, "pal", level: 35), Entry(0, "other")),
            w.Bodies(w.Observer).Last(body => body.StartsWith("00000000", StringComparison.Ordinal)));

        await w.DeleteAsync("pal");

        Assert.Equal(FullList(Entry(0, "other")),
            w.Bodies(w.Observer).Last(body => body.StartsWith("00000000", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Deleting_a_character_tells_its_online_observers_it_is_gone_then_sends_their_lists()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "doomed");
        w.AddCharacter(202, "other");
        w.AddCharacter(203, "watcher");
        w.Friend(201);
        w.Friend(201, owner: 202);
        w.Friend(201, owner: 203);
        w.AddDeletingAccount(201);
        var other = w.EnterWorld(202, 10100);

        await w.DeleteAsync("doomed");

        Assert.Equal([((byte)0, FriendInfo), ((byte)0, FriendInfo), ((byte)1, BlacklistList)], w.Identities(w.Observer));
        Assert.Equal(["02000000" + "01000000" + "01" + Counted("doomed"), FullList(), EmptyBlacklist], w.Bodies(w.Observer));
        Assert.Equal(["02000000" + "01000000" + "01" + Counted("doomed"), FullList(), EmptyBlacklist], w.Bodies(other.ConnectionId));
        Assert.Equal(0, w.Fixture.Scalar("SELECT COUNT(*) FROM character_relations WHERE owner_id=201 OR target_id=201"));
        // The deleting connection hears only its own acknowledgement.
        Assert.Equal([((byte)1, (ushort)0x0006)], w.Identities(PushWorld.DeletingConnection));
    }

    [Fact]
    public async Task A_refused_deletion_clears_no_relation_and_tells_nobody()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "doomed");
        w.Friend(201);
        w.AddDeletingAccount(201);

        await w.DeleteAsync("wrong");

        Assert.Empty(w.Bodies(w.Observer));
        Assert.Equal(1, w.Fixture.Scalar("SELECT COUNT(*) FROM character_relations WHERE target_id=201"));
    }

    [Fact]
    public async Task An_observer_that_reconnected_is_reached_on_its_new_session_only()
    {
        using var w = new PushWorld();
        w.AddCharacter(201, "pal");
        w.Friend(201);
        w.EnterWorld(201, 10100);
        var old = w.Observer;
        var current = w.Reconnect();

        await w.ProjectAsync([new CharacterId(1)], new FriendCameOnline(new CharacterId(201)));

        Assert.Empty(w.Bodies(old));
        Assert.Equal([Presence(3, "pal", blacklisted: false)], w.Bodies(current));
    }

    [Fact]
    public void Level_and_grow_type_changes_are_consumed_live_and_coalesced_per_character()
    {
        using var w = new PushWorld();
        var registrations = w.Fixture.Services.GetServices<DomainSubscriberRegistration>()
            .Where(value => value.HandlerType == typeof(FriendObserverRefreshSubscriber)).ToArray();

        Assert.Equal([typeof(CharacterGrowTypeChanged), typeof(CharacterLevelChanged)],
            registrations.Select(value => value.PayloadType).OrderBy(type => type.Name));
        Assert.All(registrations, value => Assert.Equal(DomainEventDeliveryPolicy.LiveOnly, value.Policy));
        Assert.Equal("201", registrations.Single(value => value.PayloadType == typeof(CharacterLevelChanged))
            .CoalescingKey(new CharacterLevelChanged(new CharacterId(201), 1, 2)));
        Assert.Equal("201", registrations.Single(value => value.PayloadType == typeof(CharacterGrowTypeChanged))
            .CoalescingKey(new CharacterGrowTypeChanged(new CharacterId(201), 1)));
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    private static string Counted(string name) => Convert.ToHexString(BitConverter.GetBytes(name.Length)) + Hex(name);

    /// <summary>NOTI 0x0112: <c>u16 ch, u8 server, dstr name, u8 bl</c>.</summary>
    private static string Presence(ushort channel, string name, bool blacklisted) =>
        Convert.ToHexString(BitConverter.GetBytes(channel)) + "01" + Counted(name) + (blacklisted ? "01" : "00");

    /// <summary>One friend as NOTI 0x0111 carries it for a fixture character: job 12.</summary>
    private static string Entry(ushort channel, string name, byte level = 30, byte grow = 0) =>
        "01" + Convert.ToHexString(BitConverter.GetBytes(channel)) + Counted(name)
        + level.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "0C000000"
        + grow.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "00" + "00000000";

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

    private sealed class PushWorld : IDisposable
    {
        public static readonly NetworkConnectionId DeletingConnection = new("deleting");
        private ushort sequence;

        public PushWorld() => Fixture = new ShopFixture(services =>
        {
            services.RemoveAll<IChannelEntryDomainService>();
            services.AddSingleton<IChannelEntryDomainService>(new FixedChannels());
        });

        public ShopFixture Fixture { get; }

        private IServiceProvider Services => Fixture.Services;

        /// <summary>The observer's connection: the fixture's own until a case reconnects it.</summary>
        public NetworkConnectionId Observer { get; private set; } = new("shop-test");

        public void AddCharacter(int character, string name) =>
            Fixture.Sql($"""
                INSERT INTO accounts(account_id,m_id) VALUES({character},'fr{character}');
                INSERT INTO characters(character_id,account_id,name,job,level,slot_index,delete_flag) VALUES({character},{character},'{name}',12,30,0,0);
                """);

        public void Friend(int target, int owner = 1) =>
            Fixture.Sql($"INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES({owner},{target},1,{target});");

        public void Blacklist(int target, int owner = 1) =>
            Fixture.Sql($"INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES({owner},{target},2,{target});");

        /// <summary>Puts another fixture character in the world on its own connection.</summary>
        public SelectedSessionIdentity EnterWorld(int character, int listenerPort) =>
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
                return session;
            });

        /// <summary>A connection authenticated as the character's account with nothing selected, as the roster screen is.</summary>
        public void AddDeletingAccount(int account) =>
            TestGameplay.Run(Services, () =>
            {
                var registry = Services.GetRequiredService<OnlineStateRegistry>();
                var online = registry.RegisterConnection(new NetworkConnectionContext(DeletingConnection, NetworkEndpointKind.Game, 10011));
                registry.Authenticate(online.OnlineUserId, new AccountId(account));
            });

        /// <summary>The observer's old session ends and character 1 is selected again on a new connection.</summary>
        public NetworkConnectionId Reconnect() =>
            TestGameplay.Run(Services, () =>
            {
                Assert.True(Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(Fixture.Session));
                var registry = Services.GetRequiredService<OnlineStateRegistry>();
                var online = registry.RegisterConnection(new NetworkConnectionContext(new NetworkConnectionId("shop-reconnected"), NetworkEndpointKind.Game, 10011));
                registry.Authenticate(online.OnlineUserId, new AccountId(1));
                var session = SelectedSessionIdentity.Create(new AccountId(1), new CharacterId(1),
                    online.Connection.ConnectionId, registry.GetRequiredUser(online.OnlineUserId).Generation, 10011);
                registry.Select(session);
                registry.EnterWorld(session);
                Observer = session.ConnectionId;
                return session.ConnectionId;
            });

        public Task ProjectAsync<T>(IReadOnlyList<CharacterId> audience, T notification) where T : class, INotification<T> =>
            TestGameplay.RunAsync(Services, async () =>
                _ = await Services.GetRequiredService<IEventService>().ProjectAsync(
                    new NotificationAudience.TheseCharacters(audience), notification, default));

        public Task LevelChangedAsync(int character, int before, int after) =>
            ConsumeAsync(new CharacterLevelChanged(new CharacterId(character), before, after));

        public Task GrowTypeChangedAsync(int character, int grow) =>
            ConsumeAsync(new CharacterGrowTypeChanged(new CharacterId(character), grow));

        /// <summary>The registered live subscriber, handed one fresh event as the consumption job hands it.</summary>
        private Task ConsumeAsync<T>(T payload) where T : class, IDomainEvent =>
            TestGameplay.RunAsync(Services, async () =>
            {
                var registration = Services.GetServices<DomainSubscriberRegistration>()
                    .Single(value => value.PayloadType == typeof(T) && value.HandlerType == typeof(FriendObserverRefreshSubscriber));
                await using var scope = Services.CreateAsyncScope();
                await registration.Invoke(scope.ServiceProvider,
                    new DomainEventEnvelope<T>(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), payload), default);
            });

        /// <summary>CMD 0x0006 from the deleting connection, through the real decoder and dispatcher.</summary>
        public async Task DeleteAsync(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            byte[] body = [.. BitConverter.GetBytes((ushort)0), .. BitConverter.GetBytes(bytes.Length), .. bytes];
            var next = ++sequence;
            var metadata = new GameMessageMetadata(1, 0x0006, 0, next) { InboundOrdinal = next, SequenceIsFresh = true }.WithRawBody(body);
            var registration = Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x0006);
            var decoded = ((IGameMessageDecoder)Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)
                ?? throw new InvalidOperationException("The decoder declined the body.");
            await Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
                new NetworkDispatchContext(new NetworkConnectionContext(DeletingConnection, NetworkEndpointKind.Game, 10011)), decoded, default);
        }

        private IReadOnlyList<byte[]> Frames(NetworkConnectionId connection) =>
            [.. Fixture.Sender.Outbound.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == connection)
                .SelectMany(entry => Fixture.Sender.Outbound.FramesOf(entry)!)];

        /// <summary>Each frame's body after the 15-byte header, in hex.</summary>
        public IReadOnlyList<string> Bodies(NetworkConnectionId connection) => [.. Frames(connection).Select(frame => Convert.ToHexString(frame[15..]))];

        /// <summary>Each frame's selector (1 CMD, 0 NOTI) and type.</summary>
        public IReadOnlyList<(byte, ushort)> Identities(NetworkConnectionId connection) =>
            [.. Frames(connection).Select(frame => (frame[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))))];

        public void Dispose() => Fixture.Dispose();
    }
}
