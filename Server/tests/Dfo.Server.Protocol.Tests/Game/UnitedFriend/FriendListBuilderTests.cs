using System.Text;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.UnitedFriend;

[Trait("Category", "Unit")]
public sealed class FriendListBuilderTests
{
    private static readonly CharacterId Owner = new(1);

    [Fact]
    public async Task Entries_keep_the_relation_order_and_take_each_targets_current_facts()
    {
        var w = new World();
        w.Friend(3, "three", level: 70, job: 12, grow: 2);
        w.Friend(2, "two", level: 5, job: 1, grow: 0);

        var listing = await w.BuildAsync();

        Assert.Equal(["three", "two"], listing.Message.Entries.Select(entry => Encoding.UTF8.GetString(entry.Name)));
        var first = listing.Message.Entries[0];
        Assert.Equal(((byte)70, 12, (byte)2), (first.Level, first.Job, first.GrowType));
        Assert.Empty(listing.Skipped);
        Assert.Equal(CharacterRelationKind.Friend, w.Relations.AskedKind);
    }

    [Fact]
    public async Task An_online_friend_carries_the_channel_of_its_listener_and_an_offline_one_zero()
    {
        var w = new World();
        w.Friend(2, "near");
        w.Friend(3, "far");
        w.Friend(4, "away");
        w.Online(2, port: 10011);
        w.Online(3, port: 10100);

        var listing = await w.BuildAsync();

        // Online on the same channel as the owner is still its channel number (AM-2).
        Assert.Equal([(ushort)1, (ushort)3, (ushort)0], listing.Message.Entries.Select(entry => entry.Channel));
    }

    [Fact]
    public async Task A_target_no_longer_live_is_simply_absent()
    {
        var w = new World();
        w.Friend(2, "two");
        w.Relations.Add(9);

        var listing = await w.BuildAsync();

        Assert.Equal("two", Encoding.UTF8.GetString(Assert.Single(listing.Message.Entries).Name));
        Assert.Empty(listing.Skipped);
    }

    [Fact]
    public async Task Entries_the_client_cannot_read_are_left_out_and_reported()
    {
        var w = new World();
        w.Friend(2, new string('a', 256));
        w.Friend(3, "");
        w.Friend(4, "lvl", level: 256);
        w.Friend(5, "grow", grow: -1);
        w.Friend(6, new string('b', 255));

        var listing = await w.BuildAsync();

        Assert.Equal(new string('b', 255), Encoding.UTF8.GetString(Assert.Single(listing.Message.Entries).Name));
        Assert.Equal([new CharacterId(2), new CharacterId(3), new CharacterId(4), new CharacterId(5)], listing.Skipped.Select(skipped => skipped.Character));
    }

    [Fact]
    public async Task A_name_already_in_the_list_is_left_out_because_the_client_would_drop_the_rest()
    {
        var w = new World();
        w.Friend(2, "same");
        w.Friend(3, "same");
        w.Friend(4, "other");

        var listing = await w.BuildAsync();

        Assert.Equal(["same", "other"], listing.Message.Entries.Select(entry => Encoding.UTF8.GetString(entry.Name)));
        Assert.Equal(new CharacterId(3), Assert.Single(listing.Skipped).Character);
    }

    [Fact]
    public async Task No_friends_is_an_empty_list_without_reading_identities()
    {
        var w = new World();

        var listing = await w.BuildAsync();

        Assert.Empty(listing.Message.Entries);
        Assert.Equal(0, w.Identities.Reads);
    }

    [Fact]
    public async Task A_refresh_is_the_full_list_and_then_the_blacklist()
    {
        var w = new World();
        w.Friend(2, "pal");
        w.Blacklisted(3, "foe", new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero));

        var refresh = await FriendListRefresh.BuildAsync(Owner, w.Relations, w.Identities, w.Sessions, w.Channels,
            TimeZoneInfo.CreateCustomTimeZone("test+9", TimeSpan.FromHours(9), "test+9", "test+9"), default);

        Assert.Collection(refresh.Messages,
            first => Assert.Equal("pal", Encoding.UTF8.GetString(Assert.Single(Assert.IsType<FriendListMessage>(first).Entries).Name)),
            second => Assert.Equal(new DateOnly(2026, 4, 1), Assert.Single(Assert.IsType<BlacklistListResponse>(second).Entries).Since));
        Assert.Empty(refresh.SkippedFriends);
        Assert.Empty(refresh.SkippedBlacklist);
    }

    private sealed class World
    {
        private readonly Dictionary<CharacterId, CharacterPublicIdentity> live = [];
        private readonly Dfo.Server.Application.Users.OnlineStateRegistry state;

        public World()
        {
            Sessions = TestSessions.Directory(out state);
            Identities = new FakeIdentities(live);
        }

        public FakeRelations Relations { get; } = new();

        public FakeIdentities Identities { get; }

        public Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService Sessions { get; }

        public FakeChannels Channels { get; } = new();

        public void Friend(int id, string name, int level = 30, int job = 0, int grow = 0)
        {
            live[new(id)] = new(new(id), Encoding.UTF8.GetBytes(name), level, job, grow);
            Relations.Add(id);
        }

        public void Blacklisted(int id, string name, DateTimeOffset since)
        {
            live[new(id)] = new(new(id), Encoding.UTF8.GetBytes(name), 30, 0, 0);
            Relations.Blacklist.Add(new(new(id), since));
        }

        public void Online(int id, int port) =>
            Sessions.InWorld(state, new AccountId(id), new CharacterId(id), $"friend-{id}", port);

        public ValueTask<FriendListing> BuildAsync() =>
            FriendListBuilder.BuildAsync(Owner, Relations, Identities, Sessions, Channels, default);
    }

    private sealed class FakeRelations : ICharacterRelationQueryDomainService
    {
        private readonly List<CharacterRelation> friends = [];

        public List<CharacterRelation> Blacklist { get; } = [];

        public CharacterRelationKind? AskedKind { get; private set; }

        public void Add(int target) => friends.Add(new(new(target), DateTimeOffset.UnixEpoch.AddDays(friends.Count)));

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken)
        {
            Assert.Equal(Owner, owner);
            if (kind == CharacterRelationKind.Friend) AskedKind = kind;
            return ValueTask.FromResult<IReadOnlyList<CharacterRelation>>(kind == CharacterRelationKind.Friend ? friends : Blacklist);
        }

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int Limit(CharacterRelationKind kind) => 64;
    }

    private sealed class FakeIdentities(IReadOnlyDictionary<CharacterId, CharacterPublicIdentity> live) : ICharacterIdentityQueryDomainService
    {
        public int Reads { get; private set; }

        public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
            IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(
                live.Where(entry => characters.Contains(entry.Key)).ToDictionary());
        }
    }

    /// <summary>Listener 10011 is channel 1 and 10100 is channel 3.</summary>
    private sealed class FakeChannels : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(listenerPort switch { 10011 => 1, 10100 => 3, _ => 0 }, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => throw new NotSupportedException();

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
