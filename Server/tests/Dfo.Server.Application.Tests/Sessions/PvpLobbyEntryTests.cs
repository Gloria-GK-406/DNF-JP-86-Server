using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class PvpLobbyEntryTests
{
    private static readonly ExpertStoreDomainService Stores = new();

    /// <remarks>
    /// The distinction this pins is the one the Spec calls out: no lobby at all versus a
    /// lobby nobody is in. A consumer that cannot tell them apart would offer duelling on
    /// channels that do not have it.
    /// </remarks>
    [Fact]
    public async Task An_ordinary_channel_has_no_lobby_rather_than_an_empty_one()
    {
        var result = await Service(new PvpLobbyDomainService())
            .LoadAsync(Subject(StubChannelEntryPolicy.GeneralChannel), CancellationToken.None);

        Assert.Null(result.PvpLobby);
    }

    [Fact]
    public async Task The_duelling_channel_has_a_lobby_even_when_nobody_is_waiting()
    {
        var result = await Service(new PvpLobbyDomainService())
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.NotNull(result.PvpLobby);
        Assert.Empty(result.PvpLobby!.Participants);
        Assert.Empty(result.PvpLobby.Rooms);
    }

    [Fact]
    public async Task The_lobby_lists_the_other_characters_who_are_ready()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.MarkReady(Ready(10));
        lobby.MarkReady(Ready(11));
        lobby.MarkReady(Ready(99));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.Equal(
            [10, 11],
            result.PvpLobby!.Participants.Select(participant => participant.CharacterId.Value));
    }

    [Fact]
    public async Task Characters_ready_on_another_listener_are_not_in_this_lobby()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.MarkReady(Ready(10));
        lobby.MarkReady(Ready(11, listenerPort: 10011));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.Equal(
            [10],
            result.PvpLobby!.Participants.Select(participant => participant.CharacterId.Value));
    }

    [Fact]
    public async Task The_lobby_lists_the_rooms_on_offer()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.Offer(Room(2));
        lobby.Offer(Room(1));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.Equal([1, 2], result.PvpLobby!.Rooms.Select(room => room.RoomId));
    }

    /// <remarks>
    /// A room that has not finished being announced is withheld, because a character who
    /// joined it now would race the announcement it is still making.
    /// </remarks>
    [Fact]
    public async Task A_room_still_being_announced_is_not_offered()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.Offer(Room(1, published: false));
        lobby.Offer(Room(2));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.Equal([2], result.PvpLobby!.Rooms.Select(room => room.RoomId));
    }

    [Fact]
    public async Task Rooms_on_another_listener_are_not_offered()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.Offer(Room(1, listenerPort: 10011));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        Assert.Empty(result.PvpLobby!.Rooms);
    }

    [Fact]
    public async Task A_room_carries_what_the_lobby_shows_about_it()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.Offer(new PvpRoom(
            RoomId: 7,
            OwnerCharacterId: new CharacterId(10),
            ListenerPort: 10068,
            NameType: 3,
            MapIndex: 42,
            HasPassword: true,
            BattleMode: 2,
            Published: true));

        var result = await Service(lobby)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

        var room = Assert.Single(result.PvpLobby!.Rooms);
        Assert.Equal(10, room.OwnerCharacterId.Value);
        Assert.Equal(3, room.NameType);
        Assert.Equal(42, room.MapIndex);
        Assert.True(room.HasPassword);
        Assert.Equal(2, room.BattleMode);
    }

    [Fact]
    public void Withdrawing_removes_a_room_from_the_offers()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.Offer(Room(1));

        Assert.True(lobby.Withdraw(1));
        Assert.False(lobby.Withdraw(1));
        Assert.Empty(lobby.OfferedOn(10068));
    }

    [Fact]
    public void Clearing_readiness_removes_a_participant()
    {
        var lobby = new PvpLobbyDomainService();
        lobby.MarkReady(Ready(10));

        Assert.True(lobby.ClearReady(new CharacterId(10)));
        Assert.False(lobby.ClearReady(new CharacterId(10)));
        Assert.Empty(lobby.ReadyOn(10068, new CharacterId(99)));
    }

    private static SessionAdjacentEntryDomainService Service(IPvpLobbyDomainService lobby) =>
        new(
            new SessionPresenceDomainService(),
            Dfo.Server.Testing.TestSessions.Directory(),
            Stores,
            lobby);

    private static SessionAdjacentSubject Subject(ChannelEntryIdentity channel) =>
        new(new CharacterId(99), new AccountId(1), channel, TownId: 1, AreaId: 0);

    private static PvpLobbyParticipant Ready(int characterId, int listenerPort = 10068) =>
        new(new CharacterId(characterId), listenerPort);

    private static PvpRoom Room(ushort roomId, int listenerPort = 10068, bool published = true) =>
        new(roomId, new CharacterId(1), listenerPort, 0, 0, false, 0, published);
}
