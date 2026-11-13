using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    /// <summary>RQ-038: the entry batch is composed from the selected character's real friend list and blacklist.</summary>
    [Fact]
    public async Task Entry_is_composed_with_the_selected_characters_own_friends_and_blacklist()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var relations = new SelectedCharacterRelations();
            var fixture = await Fixture.CreateAsync(relations: relations, identities: relations);
            var selected = fixture.Result.Character.Identity.CharacterId;
            relations.Owner = selected;

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None);

            var handed = fixture.StateMessages.Relations!;
            var friend = Assert.Single(handed.Friends.Entries);
            Assert.Equal(("pal", (ushort)0, (byte)40, 3, (byte)1),
                (Encoding.UTF8.GetString(friend.Name), friend.Channel, friend.Level, friend.Job, friend.GrowType));
            Assert.Equal("foe", Encoding.UTF8.GetString(Assert.Single(handed.Blacklist.Entries).Name));
            Assert.Equal([CharacterRelationKind.Friend, CharacterRelationKind.Blacklist], relations.Asked);
        });
    }

    /// <summary>RQ-050: after entry is published, the selected character's friends in the world are told it came online.</summary>
    [Fact]
    public async Task Entry_tells_the_friends_in_the_world_that_the_character_came_online()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observers = new Observers();
            var fixture = await Fixture.CreateAsync(relations: observers);
            var selected = fixture.Result.Character.Identity.CharacterId;
            var online = EnterWorld(fixture, 4243, "Game:friend");
            _ = EnterWorld(fixture, 4244, "Game:stranger");
            observers.Of[selected] = [new CharacterId(4243), new CharacterId(4245)];

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None);

            var (audience, notification) = Assert.Single(fixture.Notifications.Published,
                entry => entry.Notification is FriendCameOnline);
            Assert.Equal([online.CharacterId], Assert.IsType<NotificationAudience.TheseCharacters>(audience).CharacterIds);
            Assert.Equal(selected, ((FriendCameOnline)notification).Character);
        });
    }

    [Fact]
    public async Task Entry_with_no_friend_in_the_world_tells_nobody()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observers = new Observers();
            var fixture = await Fixture.CreateAsync(relations: observers);
            observers.Of[fixture.Result.Character.Identity.CharacterId] = [new CharacterId(4245)];

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None);

            Assert.DoesNotContain(fixture.Notifications.Published, entry => entry.Notification is FriendCameOnline);
        });
    }

    /// <summary>RQ-051 on the replacement route: the older session's friends hear it leave before they hear the new one arrive.</summary>
    [Fact]
    public async Task A_selection_that_replaces_an_older_session_announces_its_departure_to_friends_before_the_arrival()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var observers = new Observers();
            var fixture = await Fixture.CreateAsync(relations: observers);
            Assert.True(fixture.Users.TryGetByConnection(fixture.ConnectionId, out var before));
            var old = SelectedSessionIdentity.Create(
                fixture.AccountId, new CharacterId(500), fixture.ConnectionId,
                before!.Generation, StubChannelEntryPolicy.GeneralChannel.ListenerPort);
            fixture.Directory.Select(old);
            fixture.Active.EnterWorld(old);
            var friend = EnterWorld(fixture, 4243, "Game:friend");
            observers.Of[fixture.Result.Character.Identity.CharacterId] = [friend.CharacterId];
            var departure = new SelectedSessionFriendDeparture(old.CharacterId, "old"u8.ToArray(), [friend.CharacterId]);
            fixture.Cleanup.Plan = session => new SelectedSessionCleanupNotificationPlan(session.ConnectionId, [], [], [])
            {
                FriendDeparture = departure,
            };

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None);

            var published = fixture.Notifications.Published;
            var left = published.FindIndex(entry => entry.Notification is FriendWentOffline);
            var arrived = published.FindIndex(entry => entry.Notification is FriendCameOnline);
            Assert.True(left >= 0 && arrived > left, $"offline at {left}, online at {arrived}");
            Assert.Equal([friend.CharacterId], Assert.IsType<NotificationAudience.TheseCharacters>(published[left].Audience).CharacterIds);
            var offline = (FriendWentOffline)published[left].Notification;
            Assert.Equal((old.CharacterId, "old"), (offline.Character, Encoding.UTF8.GetString(offline.Name)));
        });
    }

    /// <summary>Another account's character, in the world on its own connection.</summary>
    private static SelectedSessionIdentity EnterWorld(Fixture fixture, int character, string connection)
    {
        var state = fixture.Users.State;
        var online = state.RegisterConnection(new NetworkConnectionContext(
            new NetworkConnectionId(connection), NetworkEndpointKind.Game, StubChannelEntryPolicy.GeneralChannel.ListenerPort));
        state.Authenticate(online.OnlineUserId, new AccountId(character));
        var session = SelectedSessionIdentity.Create(new AccountId(character), new CharacterId(character),
            online.Connection.ConnectionId, state.GetRequiredUser(online.OnlineUserId).Generation,
            StubChannelEntryPolicy.GeneralChannel.ListenerPort);
        state.Select(session);
        state.EnterWorld(session);
        return session;
    }

    /// <summary>Who keeps which character as a friend; nobody keeps or lists anyone else.</summary>
    private sealed class Observers : ICharacterRelationQueryDomainService
    {
        public Dictionary<CharacterId, IReadOnlyList<CharacterId>> Of { get; } = [];

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterRelation>>([]);

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(kind == CharacterRelationKind.Friend && Of.TryGetValue(target, out var list) ? list : (IReadOnlyList<CharacterId>)[]);

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

    }

    /// <summary>Character 777 "pal" is a friend and 778 "foe" is blacklisted, both offline; only the selected character holds them.</summary>
    private sealed class SelectedCharacterRelations : ICharacterRelationQueryDomainService, ICharacterIdentityQueryDomainService
    {
        public CharacterId Owner { get; set; }

        public List<CharacterRelationKind> Asked { get; } = [];

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken)
        {
            Assert.Equal(Owner, owner);
            Asked.Add(kind);
            return ValueTask.FromResult<IReadOnlyList<CharacterRelation>>(
                [new(new(kind == CharacterRelationKind.Friend ? 777 : 778), DateTimeOffset.UnixEpoch)]);
        }

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterId>>([]);

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();


        public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
            IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(new Dictionary<CharacterId, CharacterPublicIdentity>
            {
                [new(777)] = new(new(777), "pal"u8.ToArray(), 40, 3, 1),
                [new(778)] = new(new(778), "foe"u8.ToArray(), 50, 4, 0),
            }.Where(entry => characters.Contains(entry.Key)).ToDictionary());
    }
}
