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
    private static readonly ExpertStoreDomainService Stores = new(TestRequired.TransactionScope());

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

    /// <remarks>
    /// Spec AC-002: the lobby holds readiness only; rooms have one owner, the duel room domain service. The check
    /// walks every field type (generic arguments included) and every member of the lobby's contract for a
    /// <c>PvpRoom*</c> type, and is shown to catch a lobby that kept rooms.
    /// </remarks>
    [Fact]
    public void The_lobby_keeps_no_room_state()
    {
        Assert.Empty(RoomTypesHeldBy(typeof(PvpLobbyDomainService)));
        Assert.DoesNotContain(typeof(IPvpLobbyDomainService).GetMembers(),
            member => member.Name.Contains("Room", StringComparison.Ordinal)
                || (member is System.Reflection.MethodInfo method
                    && method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)
                        .Any(type => Mentions(type, IsRoomType))));

        // Counter-example: the same check fails for a lobby that keeps a room table.
        Assert.NotEmpty(RoomTypesHeldBy(typeof(LobbyThatKeepsRooms)));
    }

    private static Type[] RoomTypesHeldBy(Type type) =>
        [.. type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Where(fieldType => Mentions(fieldType, IsRoomType))];

    private static bool IsRoomType(Type type) => type.Name.StartsWith("PvpRoom", StringComparison.Ordinal);

    private static bool Mentions(Type type, Func<Type, bool> match) =>
        match(type)
        || (type.HasElementType && Mentions(type.GetElementType()!, match))
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, match)));

    private sealed class LobbyThatKeepsRooms
    {
#pragma warning disable CS0169 // Only read by reflection.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Dfo.Server.Abstractions.Pvp.PvpRoomSnapshot>? rooms;
#pragma warning restore CS0169
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
}
