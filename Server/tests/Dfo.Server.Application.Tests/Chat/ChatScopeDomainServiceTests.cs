using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.Guilds;
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
    public async Task Area_reaches_everyone_in_the_world_standing_in_the_anchors_area_and_no_one_else()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        _ = w.Enter(3, FirstChannel, area: new(1, 2, 2));
        var d = w.Enter(4, FirstChannel, area: new(1, 2, 1));
        w.LeaveTown(d);
        var stale = w.Enter(5, FirstChannel, area: new(1, 2, 1));
        w.Release(stale);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.AreaOf(a.CharacterId))));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.AreaOf(d.CharacterId)));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.AreaOf(new CharacterId(99))));
    }

    [Fact]
    public async Task Party_reaches_every_member_in_the_world_wherever_they_stand_and_no_party_reaches_no_one()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        var offline = w.Enter(4, FirstChannel, area: new(1, 2, 1));
        w.FormParty(a, b, offline);
        w.LeaveTown(b);
        w.Release(offline);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.PartyOf(a.CharacterId))));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.PartyOf(c.CharacterId)));
    }

    [Fact]
    public async Task Channel_reaches_everyone_in_the_world_on_the_anchors_listener_wherever_they_stand()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, FirstChannel, area: new(1, 3, 1));
        w.LeaveTown(b);
        _ = w.Enter(3, DuelChannel, area: new(3, 2, 1));
        var selectedOnly = w.Select(4, FirstChannel);

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.ChannelOf(a.CharacterId))));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.ChannelOf(selectedOnly.CharacterId)));
    }

    [Fact]
    public async Task Server_reaches_everyone_in_the_world_across_every_listener()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, DuelChannel, area: new(3, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        w.LeaveTown(c);
        _ = w.Select(4, FirstChannel);
        var gone = w.Enter(5, FirstChannel, area: new(1, 2, 1));
        w.Release(gone);

        Assert.Equal([a.CharacterId, b.CharacterId, c.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.Server())));
    }

    [Fact]
    public async Task Character_reaches_the_target_only_while_it_is_in_the_world()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var gone = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        w.Release(gone);
        var selectedOnly = w.Select(3, FirstChannel);

        Assert.Equal([a.CharacterId], await w.MembersOfAsync(new ChatScope.Character(a.CharacterId)));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.Character(gone.CharacterId)));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.Character(selectedOnly.CharacterId)));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.Character(new CharacterId(99))));
    }

    [Fact]
    public async Task Guild_reaches_current_members_across_channels_only_while_in_world()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var otherChannel = w.Enter(2, DuelChannel, area: new(3, 2, 1));
        var otherGuild = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        var selected = w.Select(4, FirstChannel);
        var offline = w.Enter(5, FirstChannel, area: new(1, 2, 1));
        var outsider = w.Enter(6, FirstChannel, area: new(1, 2, 1));
        w.Release(offline);
        w.GuildRosters[a.CharacterId] = [a.CharacterId, otherChannel.CharacterId, selected.CharacterId, offline.CharacterId];
        w.GuildRosters[otherGuild.CharacterId] = [otherGuild.CharacterId];

        Assert.Equal([a.CharacterId, otherChannel.CharacterId], await w.MembersOfAsync(new ChatScope.GuildOf(a.CharacterId)));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.GuildOf(outsider.CharacterId)));
        w.GuildRosters[a.CharacterId] = [a.CharacterId];
        Assert.Equal([a.CharacterId], await w.MembersOfAsync(new ChatScope.GuildOf(a.CharacterId)));
    }

    [Fact]
    public async Task Conversation_reaches_its_members_only_from_the_sessions_they_opened_it_on()
    {
        var w = new World();
        var a = w.Enter(1, FirstChannel, area: new(1, 2, 1));
        var b = w.Enter(2, DuelChannel, area: new(3, 2, 1));
        var c = w.Enter(3, FirstChannel, area: new(1, 2, 1));
        var open = TestGameplay.Run(() => w.Conversations.Open(new(a, [0x41]), new(b, [0x42])))!;
        var other = TestGameplay.Run(() => w.Conversations.Open(new(c, [0x43]), new(a, [0x41])))!;

        Assert.Equal([a.CharacterId, b.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.Conversation(open.Id))));
        w.Release(b);
        Assert.Equal([a.CharacterId], await w.MembersOfAsync(new ChatScope.Conversation(open.Id)));
        var later = w.Enter(2, FirstChannel, area: new(1, 2, 1));
        Assert.NotEqual(later, b);
        Assert.Equal([a.CharacterId], await w.MembersOfAsync(new ChatScope.Conversation(open.Id)));
        Assert.Equal([a.CharacterId, c.CharacterId], Sorted(await w.MembersOfAsync(new ChatScope.Conversation(other.Id))));
        Assert.Empty(await w.MembersOfAsync(new ChatScope.Conversation(new(999))));
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
            var guilds = TestRequired.Absent<IGuildDomainStore>((method, args) =>
                method.Name == nameof(IGuildDomainStore.ReadRosterCharacterIdsAsync)
                    ? ValueTask.FromResult(GuildRosters.GetValueOrDefault((CharacterId)args![0]!) ?? []) : null);
            Chat = new ChatScopeDomainService(sessions, sessions, areas, parties, Conversations, guilds);
        }

        private IChatScopeDomainService Chat { get; }

        public Dictionary<CharacterId, IReadOnlyList<CharacterId>> GuildRosters { get; } = [];

        public ValueTask<IReadOnlyList<CharacterId>> MembersOfAsync(ChatScope scope) =>
            Chat.MembersOfAsync(scope, CancellationToken.None);

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
