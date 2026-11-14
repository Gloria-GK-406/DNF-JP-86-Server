using System.Collections.Immutable;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Pvp;

/// <summary>
/// The room owner over real sessions, a real lobby and real room rules, with the world it reads around it
/// (channel, parties, blacklists, town moves, clock) held in memory so each test can arrange it.
/// </summary>
internal sealed class PvpRoomFixture
{
    public const int Port = 10068;
    public static readonly ChannelEntryIdentity Channel = new(68, Port, ChannelEntryKind.FreeDuel);
    public static readonly CharacterPositionState Standing = new(10, 0, 120, 240, 1);

    private readonly OnlineStateRegistry state;
    private int connections;

    public PvpRoomFixture()
    {
        Sessions = TestSessions.Directory(out state);
        Rooms = new PvpRoomDomainService(Transactions, Sessions, new Channels(), Parties, Relations, Lobby, Town,
            new Positions(), new PvpRoomRuleService(new Maps()), Wake, Clock, NullLogger<PvpRoomDomainService>.Instance);
    }

    public Ambient Transactions { get; } = new();
    public SelectedSessionDomainService Sessions { get; }
    public PvpLobbyDomainService Lobby { get; } = new();
    public PartyMembership Parties { get; } = new();
    public Blacklists Relations { get; } = new();
    public TownMoves Town { get; } = new();
    public CountingWake Wake { get; } = new();
    public ManualClock Clock { get; } = new();
    public PvpRoomDomainService Rooms { get; }

    /// <summary>A character in the world on the duel listener, standing in town and ready in the lobby.</summary>
    public SelectedSessionIdentity Join(int character)
    {
        var session = Sessions.InWorld(state, new AccountId(character), new CharacterId(character),
            $"pvp-{character}-{++connections}", Port);
        TestGameplay.Run(() => Lobby.MarkReady(new PvpLobbyParticipant(session.CharacterId, Port, session)));
        Town.InTown.Add(session.CharacterId);
        return session;
    }

    /// <summary>Runs one use case and commits it, as the command it stands in for would.</summary>
    public Task<T> Commit<T>(Func<PvpRoomDomainService, ValueTask<T>> call) =>
        TestGameplay.RunAsync(async () =>
        {
            var transaction = new DomainTransaction();
            Transactions.Current = transaction;
            try
            {
                var result = await call(Rooms);
                await transaction.CommitAsync(default);
                return result;
            }
            finally
            {
                Transactions.Current = null;
                await transaction.RollbackAsync();
            }
        });

    public async Task<PvpRoomSnapshot> CreateAsync(SelectedSessionIdentity owner, PvpBattleMode mode = PvpBattleMode.Individual,
        byte[]? password = null)
    {
        var outcome = await Commit(rooms => rooms.CreateAsync(Creation(owner, mode, password), default));
        return Assert.IsType<PvpRoomOutcome.Changed>(outcome).After!;
    }

    public static PvpRoomCreation Creation(SelectedSessionIdentity owner, PvpBattleMode mode = PvpBattleMode.Individual,
        byte[]? password = null, int map = 1) =>
        new(owner, 0, [0x41, 0x42], password is null ? null : [.. password], map, mode);

    public async Task<PvpRoomSnapshot> EnterAsync(SelectedSessionIdentity session, PvpRoomSnapshot room, byte[]? password = null)
    {
        var outcome = await Commit(rooms => rooms.EnterAsync(session, room.Number, password is null ? null : [.. password], default));
        return Assert.IsType<PvpRoomOutcome.Changed>(outcome).After!;
    }

    public Task<PvpRoomOutcome> SeatAsync(SelectedSessionIdentity actor, int seat, PvpSeatAction action, PvpTeam team = PvpTeam.Individual) =>
        Commit(rooms => rooms.SetSeatAsync(actor, seat, action, team, default));

    public Task<PvpRoomOutcome> ReadyAsync(SelectedSessionIdentity actor, bool ready = true) =>
        Commit(rooms => rooms.SetReadyAsync(actor, ready, default));

    public PvpRoomSnapshot Committed(PvpRoomSnapshot room) => Rooms.Find(room.Id)!;

    public sealed class Ambient : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current { get; set; }
    }

    public sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public sealed class CountingWake : IPvpDeadlineWake
    {
        public int Count { get; private set; }
        public void Wake() => Count++;
    }

    private sealed class Maps : IPvpMapDefinitionSource
    {
        public PvpMapCatalog Load() => new([0, 101, 102, 103, 104, 105]);
    }

    private sealed class Channels : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(listenerPort == Port ? Channel : new ChannelEntryIdentity(11, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    public sealed class Blacklists : ICharacterRelationQueryDomainService
    {
        private readonly HashSet<(CharacterId Owner, CharacterId Target)> entries = [];

        public void Add(SelectedSessionIdentity owner, SelectedSessionIdentity target) => entries.Add((owner.CharacterId, target.CharacterId));

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(kind == CharacterRelationKind.Blacklist && entries.Contains((owner, target)));

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>Who stands in town, and every move the room owner asked for.</summary>
    public sealed class TownMoves : ITownTransitDomainService
    {
        public HashSet<CharacterId> InTown { get; } = [];
        public List<TownAreaDepartureReason> DepartureReasons { get; } = [];
        public List<TownReturnRequest> ReturnRequests { get; } = [];
        public int Moves => DepartureReasons.Count + ReturnRequests.Count;

        public IReadOnlyList<TownDepartureResult> LeaveTown(IReadOnlyList<SelectedSessionIdentity> sessions, TownAreaDepartureReason reason)
        {
            var results = new List<TownDepartureResult>();
            foreach (var session in sessions)
            {
                DepartureReasons.Add(reason);
                if (!InTown.Remove(session.CharacterId)) continue;
                results.Add(new TownDepartureResult(session,
                    new TownAreaMembershipChange(TownAreaMembershipChangeKind.Left, session.CharacterId, null, null, reason),
                    new TownAreaKey(Channel.ChannelId, Standing.TownId, Standing.AreaId), true));
            }
            return results;
        }

        public ValueTask<IReadOnlyList<TownReturnAdmission>> ReturnToTownAsync(
            IReadOnlyList<TownReturnRequest> requests, CancellationToken cancellationToken)
        {
            ReturnRequests.AddRange(requests);
            foreach (var request in requests) InTown.Add(request.Session.CharacterId);
            return ValueTask.FromResult<IReadOnlyList<TownReturnAdmission>>([.. requests.Select(request => new TownReturnAdmission(request,
                new TownAreaMembershipChange(TownAreaMembershipChangeKind.Joined, request.Session.CharacterId, null, null), null))]);
        }

        public ValueTask<TownCrossingRefusal?> RefuseCrossingAsync(SelectedSessionIdentity session, CharacterPositionState standing,
            byte townId, byte areaId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public CharacterPositionState StoredPositionAfter(CharacterPositionState standing, CharacterPositionState destination) =>
            throw new NotSupportedException();

        public ValueTask<TownAreaMembershipChange?> CrossAsync(SelectedSessionIdentity session, int channelId, byte townId, byte areaId,
            short x, short y, bool sharesTownPresence, CancellationToken cancellationToken) => throw new NotSupportedException();

        public TownAreaMembershipChange? Cross(SelectedSessionIdentity session, TownAreaKey destination, short x, short y,
            bool sharesTownPresence, byte? direction = null) => throw new NotSupportedException();
    }

    /// <summary>Every character's kept town position is <see cref="Standing"/>.</summary>
    private sealed class Positions : IOnlineCharacterRuntimeDomainService
    {
        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => Standing;
        public CharacterPositionState? PositionOf(CharacterId characterId) => Standing;
        public TownAreaMembershipChange Begin(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position,
            bool sharesTownPresence, byte hpPercent) => throw new NotSupportedException();
        public bool EndExact(SelectedSessionIdentity session) => throw new NotSupportedException();
        public TownAreaMember? StandingOf(SelectedSessionIdentity session) => throw new NotSupportedException();
        public TownAreaMembershipChange? CrossInto(SelectedSessionIdentity session, TownAreaKey destination, short x, short y,
            bool sharesTownPresence, byte? direction = null) => throw new NotSupportedException();
        public TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction) => throw new NotSupportedException();
        public TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason) => throw new NotSupportedException();
        public TownAreaMembershipChange? ReturnToTown(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position,
            bool sharesTownPresence) => throw new NotSupportedException();
        public byte HpPercentOf(CharacterId characterId) => throw new NotSupportedException();
        public bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key) => throw new NotSupportedException();
        public void StartCooldown(SelectedSessionIdentity session, ItemCooldownKey key, TimeSpan duration) => throw new NotSupportedException();
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) => throw new NotSupportedException();
        public double SpiritPotionBonusRateOf(CharacterId characterId) => throw new NotSupportedException();
    }

    /// <summary>Characters added here are in a real party; everyone else is a party of their own.</summary>
    public sealed class PartyMembership : IPartyService
    {
        public HashSet<CharacterId> InParty { get; } = [];

        public PartyKey KeyOf(CharacterId characterId) =>
            InParty.Contains(characterId) ? PartyKey.Real(characterId.Value) : PartyKey.Virtual(characterId);

        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => throw new NotSupportedException();
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) =>
            throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) =>
            throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) =>
            throw new NotSupportedException();
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) =>
            throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
        public PartySnapshot FindParty(PartyCharacterSnapshot character) => throw new NotSupportedException();
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => throw new NotSupportedException();
    }
}
