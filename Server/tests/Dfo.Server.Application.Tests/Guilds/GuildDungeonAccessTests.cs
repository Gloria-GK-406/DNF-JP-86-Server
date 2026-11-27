using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Guilds;

public sealed class GuildDungeonAccessTests
{
    private const int Dungeon = 8500;
    private readonly Dictionary<AccountId, SelectedSessionIdentity> sessions = [];
    private readonly Dictionary<CharacterId, GuildMembership> memberships = [];
    private readonly Dictionary<SelectedSessionIdentity, TownAreaMember> standings = [];
    private readonly DungeonEntryIntentDomainService intents = new(ProtocolAlgorithmFixture.NoSaoFloors.Create());
    private readonly GuildHallAccessDomainService halls;
    private readonly GuildDungeonDomainService dungeons;

    public GuildDungeonAccessTests()
    {
        object ReadMember(object?[]? args) =>
            ValueTask.FromResult(memberships.GetValueOrDefault((CharacterId)args![0]!));
        halls = new(TestRequired.Absent<IGuildMembershipQueryDomainService>((_, a) => ReadMember(a)),
            TestRequired.Absent<IOnlineCharacterRuntimeDomainService>((_, a) =>
                standings.GetValueOrDefault((SelectedSessionIdentity)a![0]!)));
        var catalog = new GuildDungeonCatalog(0, new Dictionary<int, GuildDungeonDefinition>
            { [Dungeon] = new(Dungeon, 100, 5, 100, [], [new(100, 5, 1, 0)]) })
        {
            PartyBuffs = new Dictionary<int, int> { [2] = 301, [4] = 405 },
        };
        dungeons = new(new(TestRequired.Absent<IGuildDungeonContentSource>((_, _) => catalog)),
            TestRequired.Absent<IGuildDungeonStore>((m, a) => m.Name switch
            {
                nameof(IGuildDungeonStore.ResolvePeriodAsync) => ValueTask.FromResult((GuildDungeonPeriod)a![1]!),
                nameof(IGuildDungeonStore.ReadProgressAsync) =>
                    ValueTask.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int>()),
                _ => throw new InvalidOperationException(m.Name),
            }),
            TestRequired.Absent<IGuildDomainStore>((_, a) => ReadMember(a)), TestRequired.Absent<IGuildPointAccount>(),
            TestRequired.Absent<ISelectedSessionDomainService>((m, a) => m.Name switch
            {
                nameof(ISelectedSessionDomainService.Find) => sessions.GetValueOrDefault((AccountId)a![0]!),
                nameof(ISelectedSessionDomainService.IsInWorld) => sessions.ContainsValue((SelectedSessionIdentity)a![0]!),
                _ => throw new InvalidOperationException(m.Name),
            }), intents,
            TestRequired.Absent<IDungeonAdmissionDomainService>((_, a) =>
                ((Func<IDungeonAdmissionReceipt, CancellationToken, ValueTask<DungeonEntryAttempt>>)a![2]!)(
                    TestRequired.Absent<IDungeonAdmissionReceipt>(), default)),
            TestRequired.Absent<IDungeonVisitDomainService>((_, a) => ValueTask.FromResult(
                DungeonEntryAttempt.Accepted(new((DungeonLayout)a![1]!, new Dictionary<CharacterId, DungeonRoomVisit>())))),
            TestRequired.Absent<IDungeonStructureRuleService>((_, _) =>
                ValueTask.FromResult<DungeonLayout?>(new(Dungeon, 0, 1, 1, default, default, 1, 1,
                    new Dictionary<MazeCell, DungeonCellDefinition>()))),
            TestRequired.Absent<IItemDeliveryDomainService>(), TestRequired.Absent<IEventService>(),
            TimeProvider.System, TestRequired.Absent<ICharacterDomainService>());
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(2, 301)]
    [InlineData(3, null)]
    [InlineData(4, 405)]
    public Task Entry_selects_the_party_buff_and_preserves_gate_memberships(int count, int? expectedBuff) => TestGameplay.RunAsync(async () =>
    {
        var members = Enumerable.Range(1, count).Select(id => Add(id)).ToArray();
        var entry = await SelectAsync(members);
        standings.Clear(); // Production selection has left town.
        var accepted = await dungeons.EnterAsync(entry, default);
        var layout = Assert.IsType<DungeonPartyEntry>(accepted.Entry).Layout;
        var mode = Assert.IsType<DungeonRunMode.GuildDungeon>(layout.Mode);
        Assert.Equal(expectedBuff, mode.PartyBuffId);
        foreach (var member in members)
            Assert.True(mode.Includes(memberships[member.CharacterId]));
        var last = members[^1];
        var prior = memberships[last.CharacterId];
        memberships[last.CharacterId] = prior with { Member = prior.Member with { MembershipId = 9000 } };
        Assert.False(mode.Includes(memberships[last.CharacterId]));
        var run = new DungeonRunState(layout, default, 1, new HashSet<MazeCell>(), DateTimeOffset.UtcNow,
            Participants: entry.Participants, ClearOpen: true, CurrentCellState: new(default, 1, 1, 0, [], []));
        Assert.False(await dungeons.SettleAsync(last.AccountId, last.CharacterId, run, DateTimeOffset.UtcNow, default));
    });

    [Theory]
    [InlineData("instance")]
    [InlineData("area")]
    [InlineData("channel")]
    [InlineData("cancel")]
    [InlineData("reselect")]
    [InlineData("session")]
    [InlineData("membership")]
    public Task Entry_rejects_an_invalid_or_superseded_peer_gate(string change) => TestGameplay.RunAsync(async () =>
    {
        var leader = Add(1);
        var peer = Add(2, instance: change == "instance" ? 9 : 7, area: change == "area" ? (byte)0 : (byte)1,
            channel: change == "channel" ? 2 : 1);
        var entry = await SelectAsync([leader, peer]);
        standings.Clear();
        if (change == "cancel") intents.ClearExact(peer);
        if (change == "reselect") intents.Record(peer, entry.Facts.Intent with
            { GuildSelection = new(entry.Facts.Intent.GuildSelection!.Origins) });
        if (change == "session") sessions[peer.AccountId] = peer with { ConnectionGeneration = 2 };
        if (change == "membership")
        {
            var old = memberships[peer.CharacterId];
            memberships[peer.CharacterId] = old with { Member = old.Member with { MembershipId = 9000 } };
        }

        var refusal = await dungeons.EnterAsync(entry, default);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, refusal.Failure);
        Assert.Equal((byte)1, refusal.BlockedSlot);
    });

    private SelectedSessionIdentity Add(int id, int instance = 7, byte area = 1, int channel = 1)
    {
        var session = SelectedSessionIdentity.Create(new(id), new(id), new($"connection-{id}"), 1, 10001);
        var guild = new Guild(new(7), "guild", "", 1);
        memberships[session.CharacterId] = new(guild,
            new(guild.Id, session.CharacterId, id == 1, DateTimeOffset.UnixEpoch) { MembershipId = id });
        sessions.Add(session.AccountId, session);
        standings.Add(session, new(session, new(channel, GuildHallTown.TownId, area, instance),
            new(GuildHallTown.TownId, area, 0, 0, 0), true));
        return session;
    }

    private async Task<DungeonEntryContext> SelectAsync(SelectedSessionIdentity[] members)
    {
        var captured = await halls.CaptureGatesAsync(members, default);
        var intent = DungeonEntryIntent.Interactive with { GuildSelection = captured };
        foreach (var member in members) intents.Record(member, intent);
        return new(Dungeon, DungeonEntryFacts.None with { Intent = intent }, 0, 0,
            members.Select((s, i) => new DungeonRunParticipant(s.AccountId, s.CharacterId, (byte)i, i == 0)).ToArray());
    }
}
