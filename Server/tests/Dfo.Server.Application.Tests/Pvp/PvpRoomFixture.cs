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

    /// <param name="scope">
    /// The real transaction scope, when the test drives code that opens its own transactions on it, such as
    /// selected-session cleanup; otherwise each use case gets a bare transaction of its own.
    /// </param>
    public PvpRoomFixture(DomainTransactionScope? scope = null)
    {
        Scope = scope;
        IDomainTransactionAccessor transactions = scope is null ? Transactions : scope;
        Sessions = TestSessions.Directory(out state);
        var matchmakingRules = new PvpMatchmakingRuleService();
        BattleRules = new PvpSettlementRuleService(new FixedGrades(), PvpSettlementRuleServiceTests.Days, matchmakingRules);
        Records = new PvpRecordStore(transactions);
        Events = new RecordedEvents(transactions);
        var candidates = TestRequired.Absent<IPvpTotalMatchCandidateQueryDomainService>((method, _) =>
            method.Name == nameof(IPvpTotalMatchCandidateQueryDomainService.ReadAsync)
                ? ValueTask.FromResult<PvpTotalMatchLineup?>(null) : null);
        var duel = new PvpDuelDomainService(Records, PvpRatingSeason.First, matchmakingRules);
        var traditional = new PvpTraditionalDuelDomainService(Records, PvpRatingSeason.First, candidates, matchmakingRules);
        var missionRules = new PvpDailyMissionRuleService(TestRequired.Absent<IPvpDailyMissionDefinitionSource>());
        var daily = new PvpDailyMissions(missionRules, TestRequired.Absent<IPvpDailyMissionStore>(), BattleRules, Clock,
            transactions, TestRequired.Absent<IItemDomainStore>(), TestRequired.Absent<ICharacterIdentityQueryDomainService>(),
            matchmakingRules);
        var colosseum = new PvpColosseumDomainService(TestRequired.Absent<IPvpColosseumStore>(),
            new PvpColosseumRuleService(TestRequired.Absent<IPvpColosseumRuleSource>()), BattleRules, Clock,
            duel, traditional, transactions, TestRequired.Absent<IItemDomainStore>(), TestRequired.Absent<IItemDeliveryDomainService>(), matchmakingRules);
        Rooms = new PvpRoomDomainService(transactions, Sessions, new Channels(this), Parties, Relations, Lobby, Town,
            new Positions(), new PvpRoomRuleService(new Maps()), BattleRules, Wake, Clock, NullLogger<PvpRoomDomainService>.Instance,
            candidates, TestRequired.Absent<ICharacterIdentityQueryDomainService>(), traditional,
            TestRequired.Absent<ICharacterSkillRuleService>(),
            TestRequired.Absent<ICharacterSkillStateDomainService>(), colosseum, matchmakingRules, missionRules);
        Settlement = new PvpSettlementDomainService(Rooms, Records, BattleRules, duel, traditional, daily, Events, colosseum);
    }

    public DomainTransactionScope? Scope { get; }

    /// <summary>The duel channel rooms on <see cref="Port"/> are opened on; a free-duel channel unless a test says otherwise.</summary>
    public ChannelEntryIdentity DuelChannel { get; set; } = Channel;

    public IPvpSettlementRuleService BattleRules { get; }
    public PvpRecordStore Records { get; }
    public RecordedEvents Events { get; }
    public PvpSettlementDomainService Settlement { get; }

    /// <summary>The fixture's battle time limit for every first-phase mode.</summary>
    public static readonly TimeSpan BattleTime = TimeSpan.FromMinutes(3);

    /// <summary>The fixture's grade table: grade 1 from 100, grade 2 from 200, grade 3 (the top) from 600.</summary>
    public static readonly PvpGradeCatalog Grades = new(
    [
        new(0, 0, 100, 50, 0),
        new(1, 100, 200, 50, -20),
        new(2, 200, 600, 40, -20),
        new(3, 600, 1000, 30, -20),
    ], 3, BattleTime, TimeSpan.FromMinutes(2));

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
    public Task<T> Commit<T>(Func<PvpRoomDomainService, ValueTask<T>> call) => Run(() => call(Rooms));

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

    /// <summary>
    /// Runs one use case in a transaction that also holds <paramref name="failing"/> when given, so a commit
    /// that fails after every participant prepared can be shown to take everything back.
    /// </summary>
    public Task<T> Run<T>(Func<ValueTask<T>> call, ITransactionParticipant? failing = null) =>
        TestGameplay.RunAsync(async () =>
        {
            if (Scope is { } scope)
            {
                await using var lease = scope.Begin();
                if (failing is not null) scope.Current!.Enlist(failing, () => failing);
                var outcome = await call();
                await lease.CommitAsync(default);
                return outcome;
            }
            var transaction = new DomainTransaction();
            Transactions.Current = transaction;
            try
            {
                if (failing is not null) transaction.Enlist(failing, () => failing);
                var result = await call();
                await transaction.CommitAsync(default);
                return result;
            }
            finally
            {
                Transactions.Current = null;
                await transaction.RollbackAsync();
            }
        });

    /// <summary>A battle that has started on <paramref name="mode"/>: it is fighting from the start (spec SA-1).</summary>
    public async Task<(PvpRoomSnapshot Room, SelectedSessionIdentity[] Fighters)> FightAsync(
        int fighters = 2, PvpBattleMode mode = PvpBattleMode.Individual, int first = 1)
    {
        var sessions = Enumerable.Range(first, fighters).Select(Join).ToArray();
        var room = await CreateAsync(sessions[0], mode);
        foreach (var guest in sessions.Skip(1))
        {
            await EnterAsync(guest, room);
            await ReadyAsync(guest);
        }
        var started = Assert.IsType<PvpRoomOutcome.Changed>(await ReadyAsync(sessions[0]));
        Assert.Equal(PvpRoomPhase.Fighting, started.After!.Phase);
        return (started.After, sessions);
    }

    public Task<PvpSettlementOutcome> SettleAsync(PvpMatchId match) => Run(() => Settlement.SettleAsync(match, default));

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

    private sealed class FixedGrades : IPvpGradeDefinitionSource
    {
        public PvpGradeCatalog Load() => Grades;
    }

    /// <summary>
    /// Duel records in memory, written only when the use case commits -- as the database's are -- and with the
    /// store's own once-per-battle rule.
    /// </summary>
    public sealed class PvpRecordStore(IDomainTransactionAccessor transactions) : IPvpRecordDomainStore
    {
        private static readonly PvpRecord Fresh = new(0, 0, null, 0, 0, 0, 0, 0);

        public Dictionary<CharacterId, PvpRecord> Records { get; } = [];
        public Dictionary<(CharacterId Character, int Season), PvpRatingStanding> Ratings { get; } = [];
        public Dictionary<Guid, (PvpMatchSettlementRecord Settlement, PvpFighterResult[] Results)> Matches { get; } = [];

        /// <summary>Makes every settlement write fail, as a database that refuses it would.</summary>
        public bool Fails { get; set; }

        public PvpRecord Of(SelectedSessionIdentity session) => Records.GetValueOrDefault(session.CharacterId) ?? Fresh;

        public ValueTask<PvpRecord> ReadAsync(CharacterId character, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Records.GetValueOrDefault(character) ?? Fresh);

        public ValueTask<PvpRatingStanding> ReadRatingAsync(CharacterId character, int seasonId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ratings.GetValueOrDefault((character, seasonId)) ?? PvpRatingStanding.Initial);

        public ValueTask<PvpRatingStanding> ReadTotalMatchRatingAsync(CharacterId character, int seasonId,
            CancellationToken cancellationToken) => ValueTask.FromResult(PvpRatingStanding.Initial);

        public ValueTask<PvpDailyCounts> DailyCountsAsync(CharacterId character, DateOnly gameDay, CancellationToken cancellationToken)
        {
            var counted = Matches.Values.Where(match => match.Settlement.GameDay == gameDay)
                .SelectMany(match => match.Results).Where(result => result.Character == character && result.CountsTowardDaily).ToArray();
            return ValueTask.FromResult(new PvpDailyCounts(counted.Count(x => x.Outcome == PvpOutcome.Win),
                counted.Count(x => x.Outcome == PvpOutcome.Loss)));
        }

        public ValueTask<PvpSettleOutcome> SettleAsync(PvpMatchSettlementRecord settlement, CancellationToken cancellationToken)
        {
            var transaction = transactions.Current ?? throw new InvalidOperationException("Settling needs a transaction.");
            if (Fails) throw new InvalidOperationException("The settlement write failed.");
            if (Matches.TryGetValue(settlement.MatchId, out var stored))
                return ValueTask.FromResult<PvpSettleOutcome>(new PvpSettleOutcome.AlreadySettled(stored.Results));
            var results = settlement.Fighters.OrderBy(x => x.Seat).Select(fighter =>
            {
                var before = Records.GetValueOrDefault(fighter.Character) ?? Fresh;
                var streak = !settlement.RecordsResult || !fighter.UpdatesRecord ? before.WinStreak
                    : fighter.Outcome == PvpOutcome.Win ? before.WinStreak + 1 : 0;
                return new PvpFighterResult(fighter.Character, fighter.Seat, fighter.Team, fighter.Outcome, fighter.Kills,
                    fighter.Deaths, fighter.Fled, fighter.Decision.CountsTowardDaily, before.Grade, fighter.Decision.Grade,
                    fighter.Decision.ExperienceChange, fighter.Decision.Experience, streak) { Rating = fighter.Rating };
            }).ToArray();
            transaction.Enlist(this, () => new Writes(this)).Add(settlement, results);
            return ValueTask.FromResult<PvpSettleOutcome>(new PvpSettleOutcome.Created(results));
        }

        private sealed class Writes(PvpRecordStore store) : ITransactionParticipant
        {
            private readonly List<(PvpMatchSettlementRecord Settlement, PvpFighterResult[] Results)> pending = [];
            public bool CommitCanFail => false;
            public void Add(PvpMatchSettlementRecord settlement, PvpFighterResult[] results) => pending.Add((settlement, results));
            public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                foreach (var (settlement, results) in pending)
                {
                    store.Matches[settlement.MatchId] = (settlement, results);
                    foreach (var result in results)
                        if (result.Rating is { } rating)
                            store.Ratings[(result.Character, rating.SeasonId)] = rating.Standing;
                    if (!settlement.RecordsResult) continue;
                    foreach (var result in results)
                    {
                        if (!settlement.Fighters.Single(fighter => fighter.Character == result.Character).UpdatesRecord) continue;
                        var before = store.Records.GetValueOrDefault(result.Character) ?? Fresh;
                        store.Records[result.Character] = before with
                        {
                            Grade = result.Grade,
                            Experience = result.Experience,
                            Wins = before.Wins + (result.Outcome == PvpOutcome.Win ? 1 : 0),
                            Losses = before.Losses + (result.Outcome == PvpOutcome.Loss ? 1 : 0),
                            Draws = before.Draws + (result.Outcome == PvpOutcome.Draw ? 1 : 0),
                            WinStreak = result.WinStreak,
                            PeakWinStreak = Math.Max(before.PeakWinStreak, result.WinStreak),
                        };
                    }
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>Events enqueued by committed use cases only, as the event store keeps them.</summary>
    public sealed class RecordedEvents(IDomainTransactionAccessor transactions) : Dfo.Server.Abstractions.DomainEvents.IEventService
    {
        public List<object> Published { get; } = [];

        public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, Dfo.Server.Abstractions.DomainEvents.IDomainEvent
        {
            var transaction = transactions.Current ?? throw new InvalidOperationException("Enqueue needs a transaction.");
            transaction.Enlist(this, () => new Batch(this)).Pending.Add(payload);
            return ValueTask.FromResult(Guid.NewGuid());
        }

        public ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, Dfo.Server.Abstractions.DomainEvents.IDomainEvent => throw new NotSupportedException();

        public ValueTask<Dfo.Server.Abstractions.Notifications.NotificationDelivery> ProjectAsync<TNotification>(
            Dfo.Server.Abstractions.Notifications.NotificationAudience audience, TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : class, Dfo.Server.Abstractions.Notifications.INotification<TNotification> =>
            throw new NotSupportedException();

        private sealed class Batch(RecordedEvents events) : ITransactionParticipant
        {
            public List<object> Pending { get; } = [];
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
            public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask CommitAsync(CancellationToken cancellationToken)
            {
                events.Published.AddRange(Pending);
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>A database write that fails at commit, after every other participant prepared.</summary>
    public sealed class FailingWrite : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The database write failed.");
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class Channels(PvpRoomFixture fixture) : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(listenerPort == Port ? fixture.DuelChannel : new ChannelEntryIdentity(11, listenerPort, ChannelEntryKind.General));

        public ValueTask<ChannelAdmissionRefusal?> RefuseAsync(ChannelEntryIdentity channel, AccountId accountId,
            CharacterId characterId, int characterLevel, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ChannelAdmissionRefusal?>(null);

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
