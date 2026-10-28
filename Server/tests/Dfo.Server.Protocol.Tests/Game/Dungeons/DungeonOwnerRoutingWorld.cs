using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Dungeons;

/// <summary>Where a requester stands when a dungeon-owned command arrives.</summary>
public enum RoutingPlace { NoSession, Town, Ordinary, Boss, Illusion }

/// <summary>
/// One component world for the dungeon-owned commands: the real CMD handlers, real sessions,
/// parties and ordinary runs, and two tower services that answer by exact session and record
/// every action asked of them. Each request builds its own handler, as production scopes one
/// handler instance per command.
/// </summary>
/// <remarks>
/// Tower actions answer "unchanged" (pickups answer "no room") so that each tower channel stops
/// right after its first domain write; the test then reads which tower was asked, by whom, and
/// what was registered. The participation registry is supplied the way production keeps it:
/// a tower or ordinary run registers its members by character, independent of the exact session.
/// </remarks>
internal sealed class DungeonOwnerRoutingWorld
{
    internal const int ObjectCode = 8961;
    private int nextCharacter = 1;
    private readonly OnlineStateRegistry state;

    public DungeonOwnerRoutingWorld()
    {
        Sessions = TestSessions.Directory(out state);
        // The protocol projection numbers the wire party, as party notifications require.
        Parties = new PartyClientProjectionService(TestParties.Create(), new PartyClientIdRegistry());
        Runs = TestParties.Runs(parties: Parties);
        Boss = new BossTowers(Sessions);
        Illusion = new IllusionTowers(Sessions);
        Participation = new Registrations(Boss, Illusion, Runs);
    }

    public ISelectedSessionDomainService Sessions { get; }
    public IPartyService Parties { get; }
    public IDungeonRunDomainService Runs { get; }
    public DungeonRunDeliveryRegistry Registry { get; } = new();
    public BossTowers Boss { get; }
    public IllusionTowers Illusion { get; }
    public Registrations Participation { get; }
    public RecordingOutbox Outbox { get; } = new();
    public RecordingUnits Units { get; } = new();
    public RecordingViews Views { get; } = new();
    public RecordingProjections Projections { get; } = new();

    /// <summary>A connection that never published a session.</summary>
    public static readonly NetworkConnectionId Unpublished = new("routing-unpublished");

    public SelectedSessionIdentity Join()
    {
        var id = nextCharacter++;
        return Sessions.InWorld(state, new AccountId(id), new CharacterId(id), $"routing-{id}");
    }

    /// <summary>The same connection selects the same character again; the old generation keeps its tower seat.</summary>
    public SelectedSessionIdentity Reselect(SelectedSessionIdentity previous) => Sessions.ReselectOnSameConnection(state, previous);

    public SelectedSessionIdentity Place(RoutingPlace place) => place switch
    {
        RoutingPlace.Town => Join(),
        RoutingPlace.Ordinary => EnterOrdinary(Join()),
        RoutingPlace.Boss => Boss.Enter(Join()),
        RoutingPlace.Illusion => Illusion.Enter(Join()),
        _ => throw new ArgumentOutOfRangeException(nameof(place)),
    };

    /// <summary>The connection a request is sent from: the placed session's, or an unpublished one.</summary>
    public NetworkConnectionId PlaceConnection(RoutingPlace place) =>
        place == RoutingPlace.NoSession ? Unpublished : Place(place).ConnectionId;

    public SelectedSessionIdentity EnterOrdinary(SelectedSessionIdentity session)
    {
        var run = Runs.Begin(session.CharacterId, Layout(), DungeonDifficulty.Normal);
        Registry.Register(run.RunId, [session]);
        return session;
    }

    public void EnterOrdinaryParty(SelectedSessionIdentity leader, SelectedSessionIdentity member)
    {
        var run = Runs.BeginParty([
            new DungeonRunParticipant(leader.AccountId, leader.CharacterId, 0, true),
            new DungeonRunParticipant(member.AccountId, member.CharacterId, 1, false)], Layout(), DungeonDifficulty.Normal);
        Registry.Register(run.RunId, [leader, member]);
    }

    /// <summary>The leader invites the member and the member accepts; slots follow that order.</summary>
    public void FormParty(SelectedSessionIdentity leader, SelectedSessionIdentity member)
    {
        Assert.True(Parties.RequestInvite(Character(leader), Character(member), new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(Parties.AcceptInvite(Character(member), Character(leader)).Succeeded);
    }

    public PartySnapshot PartyOf(SelectedSessionIdentity session) => Parties.FindParty(Character(session));

    public static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, new PartyDirectoryScope(1, 0, 0));

    public ValueTask SendAsync(SelectedSessionIdentity session, IClientNetworkMessage input) => SendAsync(session.ConnectionId, input);

    public async ValueTask SendAsync(NetworkConnectionId connection, IClientNetworkMessage input)
    {
        var dispatch = new NetworkDispatchContext(new NetworkConnectionContext(connection, NetworkEndpointKind.Game, 10011));
        _ = input switch
        {
            GetItemRequest value => await Run<GameGetItemCommandHandler, GameGetItemCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            CharacterDeathRequest value => await Run<GameCharacterDeathCommandHandler, GameCharacterDeathCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            MonsterDeathRequest value => await Run<GameMonsterDeathCommandHandler, GameMonsterDeathCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            FinishLoadingRequest value => await Run<GameMapLoadingCommandHandler, GameFinishLoadingCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            MoveMapRequest value => await Run<GameMapLoadingCommandHandler, GameMoveMapCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            EplpCommandRequest value => await Run<GamePartyTransitionCommandHandler, GameEplpCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            GiveUpGameRequest value => await Run<GamePartyTransitionCommandHandler, GameGiveUpGameCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            LeavePartyRequest value => await Run<GamePartyTransitionCommandHandler, GameLeavePartyCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            RemovePartyMemberRequest value => await Run<GamePartyTransitionCommandHandler, GameRemovePartyMemberCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            ChangePartyLeaderRequest value => await Run<GamePartyTransitionCommandHandler, GameChangePartyLeaderCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            BackToVillageRequest value => await Run<GameDungeonSelectionCommandHandler, GameBackToVillageCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            EnterSelectDungeonRequest value => await Run<GameDungeonSelectionCommandHandler, GameEnterSelectDungeonCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            UseRevivalCoinRequest value => await Run<GameUseRevivalCoinCommandHandler, GameUseRevivalCoinCommand>(new(dispatch, value), (h, c, x) => h.HandleAsync(c, x, default)),
            _ => throw new NotSupportedException(input.GetType().Name),
        };
    }

    private ValueTask<bool> Run<THandler, TCommand>(TCommand command,
        Func<THandler, CommandExecutionContext, TCommand, ValueTask<bool>> handle)
        where THandler : class where TCommand : IProtocolCommand<bool> =>
        handle(Handler<THandler>(), ComponentContext.For(command, typeof(THandler)), command);

    private T Handler<T>() where T : class => ProtocolAlgorithmFixture.Create<T>(
        Sessions, Parties, Runs, Registry, Boss, Illusion, Participation, Outbox, Units, Views, Projections);

    /// <summary>Nothing registered, no unit written and no tower asked: what a dropped request leaves behind.</summary>
    public void AssertUntouched()
    {
        Assert.Empty(Outbox.Frames);
        Assert.Empty(Units.Names);
        Assert.Empty(Boss.Actions);
        Assert.Empty(Illusion.Actions);
        Assert.Empty(Views.Loaded);
    }

    public static GameMessageMetadata Stale(ushort type) =>
        new GameMessageMetadata(1, type, 0, 9) { InboundOrdinal = 2, SequenceIsFresh = false };

    private static DungeonLayout Layout() => new(10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0), 61000, 1,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000) });

    private static TowerRoomSnapshot Room() => new(1, 60001, 1, 1, TowerRoomPhase.Fighting, [], [], ImmutableHashSet<CharacterId>.Empty);

    internal sealed record TowerAction(string Name, SelectedSessionIdentity Session, Guid RunId);

    internal sealed class BossTowers(ISelectedSessionDomainService sessions) : IBossTowerDomainService
    {
        private readonly List<BossTowerSnapshot> runs = [];
        public List<TowerAction> Actions { get; } = [];
        /// <summary>Whether a leader's selection request releases the run (the tower's own rule).</summary>
        public bool ReleasesForSelection { get; set; }

        public SelectedSessionIdentity Enter(params SelectedSessionIdentity[] members)
        {
            runs.Add(new(Guid.NewGuid(), 3505, [.. members.Select((member, slot) => new TowerMember(member, (byte)slot, slot == 0, false))],
                Room(), [], null, DateTimeOffset.UnixEpoch));
            return members[0];
        }
        public BossTowerSnapshot? RunOf(CharacterId character) => runs.SingleOrDefault(run => run.Members.Any(member => member.Session.CharacterId == character));

        public bool Supports(int dungeonId) => dungeonId == 3505;
        public BossTowerSnapshot? Find(SelectedSessionIdentity session) => runs.SingleOrDefault(run => run.Members.Any(member => member.Session == session));
        public bool IsInTower(CharacterId characterId) => RunOf(characterId) is not null;
        public ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader, uint seed,
            CancellationToken ct, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> LoadAsync(BossTowerActionContext context, CancellationToken ct) => Unchanged(nameof(LoadAsync), context);
        public ValueTask<BossTowerActionResult> StartAsync(BossTowerActionContext context, CancellationToken ct) => Unchanged(nameof(StartAsync), context);
        public ValueTask<BossTowerActionResult> KillAsync(BossTowerActionContext context, ushort uid, IEffectFootprintRecorder footprint, CancellationToken ct) => Unchanged(nameof(KillAsync), context);
        public ValueTask<BossTowerActionResult> NextLevelAsync(BossTowerActionContext context, byte targetX, byte targetY, CancellationToken ct) => Unchanged(nameof(NextLevelAsync), context);
        public ValueTask<BossTowerPickupResult> PickupAsync(BossTowerActionContext context, ushort uid, CancellationToken ct)
        {
            Record(nameof(PickupAsync), context);
            return ValueTask.FromResult(new BossTowerPickupResult(new(false, Find(context.Session)), uid, NoRoom: true));
        }
        public ValueTask<BossTowerActionResult> DieAsync(BossTowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken ct) => Unchanged(nameof(DieAsync), context);
        public ValueTask<BossTowerActionResult> LeaveAsync(BossTowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken ct) => Unchanged(nameof(LeaveAsync), context);
        public ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(BossTowerActionContext context, SelectedSessionIdentity leader, CancellationToken ct) => Unchanged(nameof(SynchronizeLeaderAsync), context);
        public ValueTask<bool> CanRetryAsync(BossTowerActionContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> SelectResultAsync(BossTowerActionContext context, byte option, CancellationToken ct) => Unchanged(nameof(SelectResultAsync), context);
        public ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(BossTowerActionContext context, int requestedDungeonId, CancellationToken ct)
        {
            Record(nameof(ReleaseForSelectionAsync), context);
            if (!ReleasesForSelection || Find(context.Session) is not { } run) return ValueTask.FromResult(new BossTowerActionResult(false, Find(context.Session)));
            runs.Remove(run);
            return ValueTask.FromResult(new BossTowerActionResult(true, run));
        }
        public IReadOnlyList<BossTowerActionContext> FindReturnsDue(DateTimeOffset now) => [];
        public bool CanShop(SelectedSessionIdentity session, int npcId, int shopId) => false;

        private void Record(string name, BossTowerActionContext context)
        {
            // Only exact current members can reach a tower action; anything else is a routing defect.
            Assert.True(sessions.IsInWorld(context.Session) && Find(context.Session)?.RunId == context.RunId);
            Actions.Add(new(name, context.Session, context.RunId));
        }
        private ValueTask<BossTowerActionResult> Unchanged(string name, BossTowerActionContext context)
        {
            Record(name, context);
            return ValueTask.FromResult(new BossTowerActionResult(false, Find(context.Session)));
        }
    }

    internal sealed class IllusionTowers(ISelectedSessionDomainService sessions) : IIllusionTowerDomainService
    {
        private readonly List<IllusionTowerSnapshot> runs = [];
        public List<TowerAction> Actions { get; } = [];

        public SelectedSessionIdentity Enter(params SelectedSessionIdentity[] members)
        {
            runs.Add(new(Guid.NewGuid(), 11001, [.. members.Select((member, slot) => new TowerMember(member, (byte)slot, slot == 0, false))],
                Room(), [], null, DateTimeOffset.UnixEpoch) { TotalLevels = 3 });
            return members[0];
        }
        public IllusionTowerSnapshot? RunOf(CharacterId character) => runs.SingleOrDefault(run => run.Members.Any(member => member.Session.CharacterId == character));

        public IllusionTowerSnapshot? Find(SelectedSessionIdentity session) => runs.SingleOrDefault(run => run.Members.Any(member => member.Session == session));
        public bool IsInTower(CharacterId characterId) => RunOf(characterId) is not null;
        public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members) => throw new NotSupportedException();
        public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader, uint seed,
            CancellationToken cancellationToken, int dungeonId = 11001) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken cancellationToken) => Unchanged(nameof(LoadAsync), context);
        public ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken cancellationToken) => Unchanged(nameof(StartAsync), context);
        public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => Unchanged(nameof(KillAsync), context);
        public ValueTask<TowerPickupResult> PickupAsync(TowerActionContext context, ushort groundUid, CancellationToken token)
        {
            Record(nameof(PickupAsync), context);
            return ValueTask.FromResult(new TowerPickupResult(new(false, Find(context.Session)), groundUid, null, NoRoom: true));
        }
        public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken cancellationToken) => Unchanged(nameof(NextLevelAsync), context);
        public ValueTask<TowerActionResult> DieAsync(TowerActionContext context, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => Unchanged(nameof(DieAsync), context);
        public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => Unchanged(nameof(LeaveAsync), context);
        public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken cancellationToken) => Unchanged(nameof(SynchronizeLeaderAsync), context);
        public ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken cancellationToken) => Unchanged(nameof(SelectReturnAsync), context);
        public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now) => [];
        public ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult(dungeonId == 11001);

        private void Record(string name, TowerActionContext context)
        {
            Assert.True(sessions.IsInWorld(context.Session) && Find(context.Session)?.RunId == context.RunId);
            Actions.Add(new(name, context.Session, context.RunId));
        }
        private ValueTask<TowerActionResult> Unchanged(string name, TowerActionContext context)
        {
            Record(name, context);
            return ValueTask.FromResult(new TowerActionResult(false, Find(context.Session)));
        }
    }

    /// <summary>
    /// The occupancy registry as production keeps it: by character, never by session generation.
    /// <see cref="Residual"/> holds an ordinary registration whose run can no longer be found by
    /// character, as after a party departure that has not yet ended the character's part.
    /// </summary>
    internal sealed class Registrations(BossTowers boss, IllusionTowers illusion, IDungeonRunDomainService runs) : IDungeonParticipationDomainService
    {
        public Dictionary<CharacterId, DungeonParticipationOwner> Residual { get; } = [];
        public DungeonParticipationOwner? Find(CharacterId character) =>
            boss.RunOf(character) is { } bossRun ? new(DungeonParticipationMode.BossTower, bossRun.RunId)
            : illusion.RunOf(character) is { } illusionRun ? new(DungeonParticipationMode.IllusionTower, illusionRun.RunId)
            : runs.Find(character) is { } ordinary ? new(DungeonParticipationMode.Ordinary, ordinary.RunId.Value)
            : Residual.GetValueOrDefault(character);
        public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner) => throw new NotSupportedException();
        public void Release(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();
        public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();
    }

    internal sealed record Frame(NetworkConnectionId Connection, IGameServerMessage Message);

    internal sealed class RecordingOutbox : IGameCommandOutbox
    {
        public List<Frame> Frames { get; } = [];
        public IReadOnlyList<IGameServerMessage> To(SelectedSessionIdentity session) => To(session.ConnectionId);
        public IReadOnlyList<IGameServerMessage> To(NetworkConnectionId connection) =>
            [.. Frames.Where(frame => frame.Connection == connection).Select(frame => frame.Message)];
        public ValueTask RegisterCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => Add(connectionId, [message]);
        public ValueTask RegisterNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => Add(connectionId, [message]);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext expectedConnection, IReadOnlyList<IGameServerMessage> messages, CancellationToken cancellationToken) =>
            Add(expectedConnection.ConnectionId, messages);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            _ = Add(connectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets) _ = Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }
        private ValueTask Add(NetworkConnectionId connection, IEnumerable<IGameServerMessage> messages)
        {
            Frames.AddRange(messages.Select(message => new Frame(connection, message)));
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class RecordingUnits : ICommandWorkUnits
    {
        private readonly ProtocolAlgorithmFixture.Units inner = new();
        public List<string> Names { get; } = [];
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull
        {
            Names.Add(declaration.Name);
            return inner.WriteAsync(context, declaration, action, token);
        }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull => inner.ReadOnlyAsync(context, declaration, action, token);
    }

    /// <summary>
    /// No selected character is loadable here. The ordinary town-side flows (EPLP, back to town,
    /// selection, give-up) begin by loading the requester's current view, so the loads recorded
    /// here show the request reached that flow and stopped at its first read.
    /// </summary>
    internal sealed class RecordingViews : ISelectedCharacterCurrentViewDomainService
    {
        public List<SelectedSessionIdentity> Loaded { get; } = [];
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
        {
            Loaded.Add(session);
            return ValueTask.FromResult<SelectCharacterResult?>(null);
        }
    }

    internal sealed class RecordingProjections : ICommandProjections
    {
        public List<object> Projected { get; } = [];
        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience, TNotification notification,
            CancellationToken cancellationToken = default) where TNotification : class, INotification<TNotification>
        {
            Projected.Add(notification);
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }
}
