using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Testing;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Application.Tests.Pvp;

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
        await TestGameplay.RunAsync(async () =>
        {
            var result = await Service(new PvpLobbyDomainService())
                .LoadAsync(Subject(StubChannelEntryPolicy.GeneralChannel), CancellationToken.None);

            Assert.Null(result.PvpLobby);
        });
    }

    [Fact]
    public async Task The_duelling_channel_has_a_lobby_even_when_nobody_is_waiting()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var result = await Service(new PvpLobbyDomainService())
                .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

            Assert.NotNull(result.PvpLobby);
            Assert.Empty(result.PvpLobby!.Participants);
            Assert.Empty(result.PvpLobby.Rooms);
        });
    }

    [Fact]
    public async Task The_lobby_lists_the_other_characters_who_are_ready()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Fact]
    public async Task Characters_ready_on_another_listener_are_not_in_this_lobby()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var lobby = new PvpLobbyDomainService();
            lobby.MarkReady(Ready(10));
            lobby.MarkReady(Ready(11, listenerPort: 10011));

            var result = await Service(lobby)
                .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None);

            Assert.Equal(
                [10],
                result.PvpLobby!.Participants.Select(participant => participant.CharacterId.Value));
        });
    }

    [Fact]
    public async Task The_lobby_lists_the_committed_rooms_by_number()
    {
        var rooms = new PvpRoomFixture();
        await rooms.CreateAsync(rooms.Join(1));
        await rooms.CreateAsync(rooms.Join(2));

        var result = await TestGameplay.RunAsync(() => Service(new PvpLobbyDomainService(), rooms.Rooms)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None).AsTask());

        Assert.Equal([1, 2], result.PvpLobby!.Rooms.Select(room => (int)room.RoomNumber));
    }

    [Fact]
    public async Task Rooms_on_another_listener_are_not_offered()
    {
        var rooms = new PvpRoomFixture();
        await rooms.CreateAsync(rooms.Join(1));

        var result = await TestGameplay.RunAsync(() => Service(new PvpLobbyDomainService(), rooms.Rooms)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel with { ListenerPort = 10011 }), CancellationToken.None).AsTask());

        Assert.Empty(result.PvpLobby!.Rooms);
    }

    [Fact]
    public async Task A_room_carries_what_the_lobby_shows_about_it()
    {
        var rooms = new PvpRoomFixture();
        var owner = rooms.Join(10);
        var room = await rooms.CreateAsync(owner, PvpBattleMode.Team, password: [7]);
        await rooms.Commit(service => service.SetMapAsync(owner, 3, default));
        await rooms.SeatAsync(owner, 5, PvpSeatAction.Close);

        var result = await TestGameplay.RunAsync(() => Service(new PvpLobbyDomainService(), rooms.Rooms)
            .LoadAsync(Subject(StubChannelEntryPolicy.FreeDuelChannel), CancellationToken.None).AsTask());

        var shown = Assert.Single(result.PvpLobby!.Rooms);
        Assert.Equal(room.Number, shown.RoomNumber);
        Assert.Equal([0x41, 0x42], shown.Name);
        Assert.True(shown.Waiting);
        Assert.Equal(0, shown.OwnerSeat);
        Assert.Equal(3, shown.MapIndex);
        Assert.True(shown.HasPassword);
        Assert.Equal((byte)PvpBattleMode.Team, shown.BattleMode);
        Assert.Equal(new PvpRoomSeatState(PvpRoomSeatOccupancy.Taken, 1, owner.WireUserId, false), shown.Seats[0]);
        Assert.Equal(PvpRoomSeatOccupancy.Closed, shown.Seats[5].Occupancy);
        Assert.Equal(PvpRoomSeatOccupancy.Open, shown.Seats[1].Occupancy);
    }

    [Fact]
    public void Clearing_readiness_removes_a_participant()
    {
        TestGameplay.Run(() =>
        {
            var lobby = new PvpLobbyDomainService();
            lobby.MarkReady(Ready(10));

            Assert.True(lobby.ClearReady(new CharacterId(10)));
            Assert.False(lobby.ClearReady(new CharacterId(10)));
            Assert.Empty(lobby.ReadyOn(10068, new CharacterId(99)));
        });
    }

    private static SessionAdjacentEntryDomainService Service(IPvpLobbyDomainService lobby, IPvpRoomQueryDomainService? rooms = null) =>
        new(
            new SessionPresenceDomainService(),
            Dfo.Server.Testing.TestSessions.Directory(),
            Stores,
            lobby,
            rooms ?? new PvpRoomFixture().Rooms);

    private static SessionAdjacentSubject Subject(ChannelEntryIdentity channel) =>
        new(new CharacterId(99), new AccountId(1), channel, TownId: 1, AreaId: 0);

    private static PvpLobbyParticipant Ready(int characterId, int listenerPort = 10068) =>
        new(new CharacterId(characterId), listenerPort);
}
