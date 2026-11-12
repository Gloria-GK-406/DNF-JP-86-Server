using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Chat;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Chat;

[Trait("Category", "Unit")]
public sealed class ChatScopeDomainServiceTests
{
    private const int FirstChannel = 10011;
    private const int DuelChannel = 10100;

    [Fact]
    public void Area_reaches_everyone_in_the_world_standing_in_the_anchors_area_and_no_one_else()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        _ = w.Enter(3, FirstChannel, area: new(1, 2, 2));
        var d = w.Enter(4, FirstChannel, area: new(1, 2, 1));
        w.LeaveTown(d);
        var stale = w.Enter(5, FirstChannel, area: new(1, 2, 1));
        w.Release(stale);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.AreaOf(a.CharacterId))));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.AreaOf(d.CharacterId)));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.AreaOf(new CharacterId(99))));
    }

    [Fact]
    public void Party_reaches_every_member_in_the_world_wherever_they_stand_and_no_party_reaches_no_one()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        var offline = w.Enter(4, FirstChannel, area: new(1, 2, 1));
        w.FormParty(a, b, offline);
        w.LeaveTown(b);
        w.Release(offline);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.PartyOf(a.CharacterId))));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.PartyOf(c.CharacterId)));
    }

    [Fact]
    public void Channel_reaches_everyone_in_the_world_on_the_anchors_listener_wherever_they_stand()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 3, 1));
        w.LeaveTown(b);
        _ = w.Enter(3, DuelChannel, area: new(3, 2, 1));
        var selectedOnly = w.Select(4, FirstChannel);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.ChannelOf(a.CharacterId))));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.ChannelOf(selectedOnly.CharacterId)));
    }

    [Fact]
    public void Server_reaches_everyone_in_the_world_across_every_listener()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, DuelChannel, area: new(3, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        w.LeaveTown(c);
        _ = w.Select(4, FirstChannel);
        var gone = w.Enter(5, FirstChannel, area: new(1, 2, 1));
        w.Release(gone);

        Assert.Equal([a.CharacterId, b.CharacterId, c.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.Server())));
    }

    [Fact]
    public void Character_reaches_the_target_only_while_it_is_in_the_world()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var gone = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        w.Release(gone);
        var selectedOnly = w.Select(3, FirstChannel);

        Assert.Equal([a.CharacterId], w.Chat.MembersOf(new ChatScope.Character(a.CharacterId)));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.Character(gone.CharacterId)));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.Character(selectedOnly.CharacterId)));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.Character(new CharacterId(99))));
    }

    [Fact]
    public void Guild_reaches_no_one_because_there_is_no_guild_system()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        _ = w.Enter(2, FirstChannel, area: new(1, 2, 1));

        Assert.Empty(w.Chat.MembersOf(new ChatScope.GuildOf(a.CharacterId)));
    }

    [Fact]
    public void Conversation_reaches_its_members_only_from_the_sessions_they_opened_it_on()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, DuelChannel, area: new(3, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        var open = TestGameplay.Run(() => w.Conversations.Open(new(a, [0x41]), new(b, [0x42])))!;
        var other = TestGameplay.Run(() => w.Conversations.Open(new(c, [0x43]), new(a, [0x41])))!;

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.Conversation(open.Id))));
        w.Release(b);
        Assert.Equal([a.CharacterId], w.Chat.MembersOf(new ChatScope.Conversation(open.Id)));
        var later = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        Assert.NotEqual(later, b);
        Assert.Equal([a.CharacterId], w.Chat.MembersOf(new ChatScope.Conversation(open.Id)));
        Assert.Equal([a.CharacterId, c.CharacterId], Sorted(w.Chat.MembersOf(new ChatScope.Conversation(other.Id))));
        Assert.Empty(w.Chat.MembersOf(new ChatScope.Conversation(new(999))));
    }

    private static CharacterId[] Sorted(IReadOnlyList<CharacterId> members) =>
        [.. members.OrderBy(member => member.Value)];

    private sealed class World
    {
        private readonly OnlineStateRegistry state;
        private readonly SelectedSessionDomainService sessions;
        private readonly OnlineCharacterLocationDomainService areas = new();
        private readonly PartyService parties;

        public World()
        {
            sessions = TestSessions.Directory(out state);
            parties = TestParties.Create(areas);
            Chat = new ChatScopeDomainService(sessions, sessions, areas, parties, Conversations);
        }

        public IChatScopeDomainService Chat { get; }

        public ChatConversationDomainService Conversations { get; } = new();

        public SelectedSessionIdentity Select(int character, int listenerPort) =>
            sessions.Selected(state, new(character), new(character), $"chat-scope-{character}", listenerPort);

        public SelectedSessionIdentity Enter(int character, int listenerPort, TownAreaKey area)
        {
            var session = sessions.InWorld(state, new(character), new(character), $"chat-scope-{character}", listenerPort);
            TestGameplay.Run(() => areas.Begin(session, area,
                new CharacterPositionState(area.TownId, area.AreaId, 100, 200, 0), sharesTownPresence: true));
            return session;
        }

        public void LeaveTown(SelectedSessionIdentity session) =>
            TestGameplay.Run(() => Assert.NotNull(areas.Depart(session, TownAreaDepartureReason.DungeonEntry)));

        public void Release(SelectedSessionIdentity session) =>
            TestGameplay.Run(() => Assert.True(sessions.ReleaseExact(session)));

        public void FormParty(SelectedSessionIdentity leader, params SelectedSessionIdentity[] members) =>
            TestGameplay.Run(() =>
            {
                var inviter = Snapshot(leader);
                foreach (var member in members)
                {
                    Assert.True(parties.RequestInvite(inviter, Snapshot(member), new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
                    Assert.True(parties.AcceptInvite(Snapshot(member), inviter).Succeeded);
                }
            });

        private static PartyCharacterSnapshot Snapshot(SelectedSessionIdentity session) =>
            new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(11, 2, 1));
    }
}
