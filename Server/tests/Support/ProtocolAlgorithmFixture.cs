using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.World;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Testing;

// Component tests supply the domain boundaries under observation. Unused collaborators fail
// explicitly, so an added production dependency cannot silently turn into a no-op in this fixture.
internal static class ProtocolAlgorithmFixture
{
    internal static T Create<T>(params object?[] supplied) where T : class
    {
        var values = Complete(supplied).ToList();
        var ctor = typeof(T).GetConstructors().Single();
        return (T)ctor.Invoke(ctor.GetParameters().Select(parameter =>
            values.LastOrDefault(parameter.ParameterType.IsInstanceOfType) ?? (parameter.HasDefaultValue ? parameter.DefaultValue
                : parameter.ParameterType == typeof(IDungeonParticipationDomainService) ? Participation(values) : Default(parameter.ParameterType))).ToArray());
    }
    // Handlers locate their dungeon through the occupancy registry. Unless a test supplies one, it
    // is read off the dungeon services the test did supply, so a placed member is never unregistered.
    private static DerivedDungeonParticipation Participation(List<object> values) => new(
        values.OfType<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService>().LastOrDefault() ?? Default<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService>(),
        values.OfType<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>().LastOrDefault() ?? Default<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(),
        values.OfType<IDungeonRunDomainService>().LastOrDefault() ?? Default<IDungeonRunDomainService>(),
        values.OfType<ISelectedSessionDomainService>().LastOrDefault());
    internal static object[] Complete(object?[] supplied)
    {
        var values = supplied.OfType<object>().ToList();
        foreach (var value in supplied)
        {
            if (value is IGameMessageSender sender) values.Add(new Outbox(sender));
            if (value is IEventService events) values.Add(new global::Dfo.Server.Testing.TestCommandProjections(events));
            if (value is ISelectedSessionDomainService sessions && !values.Any(candidate => candidate is IOnlineUserQueryDomainService))
                values.Add(new Users(sessions));
            if (value is INetworkConnectionCloser closer) values.Add(new Closures(closer));
        }
        if (!values.Any(value => value is bool)) values.Add(false);
        // Who stands where and the session's own position are one store; a runtime the test did not
        // supply is built over the town areas it did, so both answer the same placement.
        if (!values.Any(value => value is IOnlineCharacterRuntimeDomainService))
        {
            var areas = values.OfType<Dfo.Server.Abstractions.World.ITownAreaDomainService>().LastOrDefault();
            var locations = areas as Dfo.Server.Application.World.OnlineCharacterLocationDomainService
                ?? areas?.GetType().GetProperty("Locations")?.GetValue(areas) as Dfo.Server.Application.World.OnlineCharacterLocationDomainService;
            if (areas is null)
            {
                locations = new Dfo.Server.Application.World.OnlineCharacterLocationDomainService();
                values.Add(locations);
            }
            if (locations is not null)
                values.Add(new Dfo.Server.Application.Characters.OnlineCharacterRuntimeDomainService(TimeProvider.System, locations));
        }
        // The party reads where its members stand from the same town areas.
        if (!values.Any(value => value is Dfo.Server.Abstractions.Parties.IPartyService)
            && values.OfType<Dfo.Server.Abstractions.World.ITownAreaDomainService>().LastOrDefault() is { } partyAreas)
        {
            values.Add(new Dfo.Server.Application.Parties.PartyService(
                (Dfo.Server.Abstractions.Parties.IPartyMemberStatusDomainService)Default(typeof(Dfo.Server.Abstractions.Parties.IPartyMemberStatusDomainService)),
                new Dfo.Server.Application.Parties.PartyModeRuleService(), TimeProvider.System,
                (Dfo.Server.Abstractions.Parties.IPartyMemberSessionEndpointSource)Default(typeof(Dfo.Server.Abstractions.Parties.IPartyMemberSessionEndpointSource)),
                partyAreas));
        }
        return values.ToArray();
    }
    private static readonly HashSet<Type> EmptyWorldContracts =
    [
        typeof(Dfo.Server.Abstractions.World.IGuardianAltarDomainService),
        typeof(Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService),
        typeof(Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService),
        typeof(Dfo.Server.Abstractions.World.IDungeonRunDomainService),
        typeof(IOnlineCharacterRuntimeDomainService),
        typeof(Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService),
        typeof(Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService),
        typeof(Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService),
        typeof(Dfo.Server.Abstractions.World.ISaoTowerDomainService),
        typeof(Dfo.Server.Abstractions.World.ITauKingdomDomainService),
        typeof(Dfo.Server.Abstractions.World.ISouthernDaleDomainService),
        typeof(Dfo.Server.Abstractions.World.IHerasDungeonDomainService),
        typeof(Dfo.Server.Abstractions.World.ICoupleDungeonDomainService),
        typeof(Dfo.Server.Abstractions.World.ISealedTreasureDomainService),
        typeof(Dfo.Server.Abstractions.World.IGoldSilverHornDomainService),
        typeof(Dfo.Server.Abstractions.World.IGiantDragonDomainService),
        typeof(Dfo.Server.Abstractions.World.IAltarDungeonDomainService),
        typeof(Dfo.Server.Abstractions.World.ITournamentDungeonDomainService),
        typeof(Dfo.Server.Abstractions.World.IDespairTowerDomainService),
    ];
    internal static T TownField<T>(object? town) where T : class => town is null ? Default<T>() : Field<T>(town);
    internal static IOnlineUserQueryDomainService NoOnline() => new NoUsers();
    internal static Dfo.Server.Abstractions.World.IDimensionDungeonDomainService NoDimension(Dfo.Server.Abstractions.World.IDungeonVisitDomainService visits) =>
        Create<Dfo.Server.Application.World.DimensionDungeonDomainService>(visits, new NoDimensionRules());
    internal static T Default<T>() => (T)Default(typeof(T));
    internal static IOnlineUserQueryDomainService QueryFor(ISelectedSessionDomainService sessions, Func<NetworkConnectionId, NetworkConnectionContext>? physical = null) => new Users(sessions, physical);
    internal static object Default(Type type)
    {
        if (type == typeof(Dfo.Server.Protocol.Game.StateSynchronization.LotteryCounterExperimentOptions)) return new Dfo.Server.Protocol.Game.StateSynchronization.LotteryCounterExperimentOptions(false, 0, 0);
        if (type == typeof(Dfo.Server.Abstractions.Parties.IPartyService)) return new Dfo.Server.Application.Parties.PartyService(
            (Dfo.Server.Abstractions.Parties.IPartyMemberStatusDomainService)Default(typeof(Dfo.Server.Abstractions.Parties.IPartyMemberStatusDomainService)),
            new Dfo.Server.Application.Parties.PartyModeRuleService(), TimeProvider.System,
            (Dfo.Server.Abstractions.Parties.IPartyMemberSessionEndpointSource)Default(typeof(Dfo.Server.Abstractions.Parties.IPartyMemberSessionEndpointSource)),
            (Dfo.Server.Abstractions.World.ITownAreaDomainService)Default(typeof(Dfo.Server.Abstractions.World.ITownAreaDomainService)));
        if (type == typeof(Dfo.Server.Abstractions.World.ITownAreaDomainService)) return new Dfo.Server.Application.World.OnlineCharacterLocationDomainService();
        if (type == typeof(ISelectedSessionDomainService)) return TestSessions.Directory();
        if (type == typeof(Dfo.Server.Abstractions.Networking.IDungeonRunProjectionDomainService)) return new Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry();
        if (type == typeof(ISelectedCharacterCurrentViewDomainService)) return new NoCurrentView();
        if (type == typeof(Dfo.Server.Abstractions.World.ITownMovementDomainService)) return new EveryTown();
        if (EmptyWorldContracts.Contains(type)) return DispatchProxy.Create(type, typeof(EmptyWorld));
        if (type == typeof(TimeProvider)) return TimeProvider.System;
        // A registered-collection dependency (hooks keyed by kind and the like) defaults to none registered.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(IOutboundOrdering)) return new Ordering();
        if (type == typeof(IPartyProjectionStateDomainService)) return new PartyProjectionStreamRegistry();
        if (type == typeof(Dfo.Server.Abstractions.World.IDungeonEntryIntentDomainService)) return new Dfo.Server.Application.World.DungeonEntryIntentDomainService();
        if (type == typeof(Dfo.Server.Abstractions.Quests.IQuestQueryDomainService)) return new NoQuests();
        if (type == typeof(Dfo.Server.Abstractions.World.IDungeonCardPresentationControl)) return new CardSignals();
        if (type == typeof(Dfo.Server.Abstractions.World.IHellDungeonEntryDomainService)) return new NoHell();
        if (type == typeof(Dfo.Server.Abstractions.Inventory.IEpicBookDomainService)) return new NoEpicPieces();
        if (type == typeof(IWeaknessDomainService)) return new NoWeakness();
        if (type == typeof(ICommandWorkUnits)) return new Units();
        if (type == typeof(ILogger)) return NullLogger.Instance;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
            return typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments).GetField("Instance")!.GetValue(null)!;
        if (type.IsInterface) return DispatchProxy.Create(type, typeof(Unused));
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.FullName);
    }
    internal static T Field<T>(object instance) where T : class =>
        Fields(instance.GetType()).Select(field => field.GetValue(instance)).OfType<T>().Distinct(ReferenceEqualityComparer.Instance).Cast<T>().Single();
    private static IEnumerable<FieldInfo> Fields(Type? type) => type is null ? [] : type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Concat(Fields(type.BaseType));
    internal static object[] TownDependencies(object town) => town.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(field => field.GetValue(town))
        .OfType<object>().ToArray();
    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException($"The component fixture did not provide {method?.DeclaringType?.Name}.{method?.Name}.");
    }
    internal sealed class CardSignals : Dfo.Server.Abstractions.World.IDungeonCardPresentationControl
    {
        internal readonly List<(string Phase, Dfo.Server.Data.Characters.CharacterId Character, DungeonRunId Run)> Calls = [];
        public void ScheduleAfterSettlement(SelectedSessionIdentity session, DungeonRunId run) => Calls.Add(("settled", session.CharacterId, run));
        public void ScheduleAutoFlip(SelectedSessionIdentity session, DungeonRunId run) => Calls.Add(("auto", session.CharacterId, run));
        public void Cancel(Dfo.Server.Data.Characters.CharacterId character, DungeonRunId run) => Calls.Add(("cancel", character, run));
    }
    private sealed class NoUsers : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => throw new NotSupportedException();
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user) { user = null; return false; }
    }
    private sealed class NoHell : Dfo.Server.Abstractions.World.IHellDungeonEntryDomainService
    {
        public ValueTask<Dfo.Server.Abstractions.World.DungeonEntryAttempt> EnterManualAsync(Dfo.Server.Abstractions.World.DungeonEntryContext context, CancellationToken token) =>
            ValueTask.FromResult(Dfo.Server.Abstractions.World.DungeonEntryAttempt.Refused(Dfo.Server.Abstractions.World.DungeonEntryFailure.HellUnavailable));
        public ValueTask<Dfo.Server.Abstractions.World.DungeonEntryAttempt> EnterRandomAsync(Dfo.Server.Abstractions.World.DungeonEntryContext context, CancellationToken token) => EnterManualAsync(context, token);
    }
    private sealed class NoDimensionRules : Dfo.Server.Abstractions.World.IDimensionDungeonRuleService
    {
        public bool IsDimension(int dungeon) => false;
        public int Initialize() => throw new NotSupportedException();
        public Dfo.Server.Abstractions.UsageQuotas.DimensionQuotaSnapshot Snapshot(DateOnly period, int used) => throw new NotSupportedException();
        public IReadOnlyList<Dfo.Server.Abstractions.World.RolledDrop> DrawMonster(Dfo.Server.Abstractions.World.MonsterDropRequest request, ulong seed) => throw new NotSupportedException();
        public Dfo.Server.Abstractions.World.ClearRewardCardFace? DrawCard(int dungeon, Dfo.Server.Abstractions.World.ClearRewardCardFace ordinary, ulong seed) => throw new NotSupportedException();
    }
    private sealed class NoWeakness : IWeaknessDomainService
    {
        public IWeakness For(Dfo.Server.Data.Characters.CharacterId character) => new Zero();
        public TimeSpan RecoveryInterval => TimeSpan.FromSeconds(1);
        public IReadOnlyList<Dfo.Server.Data.Characters.CharacterId> AdvanceRecovery() => throw new NotSupportedException();
        private sealed class Zero : IWeakness
        {
            public byte Value => 0;
            public ValueTask<int> RecoveryGoldCost(CancellationToken token = default) => throw new NotSupportedException();
            public ValueTask SetWeakness() => throw new NotSupportedException();
            public void Recover() => throw new NotSupportedException();
            public void PauseRecovery() => throw new NotSupportedException();
            public void ResumeRecovery() => throw new NotSupportedException();
        }
    }
    private sealed class NoCurrentView : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<Dfo.Server.Data.Game.SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token) =>
            ValueTask.FromResult<Dfo.Server.Data.Game.SelectCharacterResult?>(null);
    }
    private sealed class EveryTown : Dfo.Server.Abstractions.World.ITownMovementDomainService
    {
        public ValueTask<bool> DeclaresAreaAsync(int town, int area, CancellationToken token) => ValueTask.FromResult(true);
        public ValueTask<bool> SharesTownPresenceAsync(int town, int area, CancellationToken token) => ValueTask.FromResult(true);
        public ValueTask<bool> CanEnterTownAsync(Dfo.Server.Data.Channel.ChannelEntryIdentity channel, int town, CancellationToken token) => ValueTask.FromResult(true);
    }
    private sealed class NoQuests : Dfo.Server.Abstractions.Quests.IQuestQueryDomainService
    {
        public ValueTask<IReadOnlyList<Dfo.Server.Abstractions.Quests.AcceptedQuestState>> ReadAcceptedAsync(
            Dfo.Server.Data.Accounts.AccountId account, Dfo.Server.Data.Characters.CharacterId character, CancellationToken token) =>
            ValueTask.FromResult<IReadOnlyList<Dfo.Server.Abstractions.Quests.AcceptedQuestState>>([]);
        public ValueTask<Dfo.Server.Data.World.AvailableQuestEntryState> ReadAvailableAsync(Dfo.Server.Data.Accounts.AccountId account,
            Dfo.Server.Data.Characters.CharacterId character, CancellationToken token) => throw new NotSupportedException();
    }
    public class EmptyWorld : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.DeclaringType == typeof(IDespairTowerDomainService) && method.Name == nameof(IDespairTowerDomainService.ReadStatusAsync))
                return ValueTask.FromResult(new DespairTowerStatus(1, 0, []));
            if (method.DeclaringType == typeof(ISaoTowerDomainService) && method.Name == nameof(ISaoTowerDomainService.ReadStatusAsync))
                return ValueTask.FromResult<SaoTowerProgress?>(null);
            if (method.Name is "SupportsDungeonAsync" or "PositionOf" or "IsTower" or "Find" or "ReturnFor" or "IsInTower" or "Supports" or "CanLeaveAsync" or "CanCrossAsync" or "AppearanceAsync" or "InstanceAsync" or "ReadAsync" or "QueryAsync" or "CloseExact")
            {
                if (method.Name is "CanLeaveAsync" or "CanCrossAsync") return ValueTask.FromResult(true);
                if (method.ReturnType == typeof(void)) return null;
                return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
            }
            throw new InvalidOperationException($"The empty component world cannot execute {method.Name}.");
        }
    }
    internal static T Decorate<T>(T original, Func<MethodInfo, object?[], (bool Handled, object? Result)> intercept) where T : class
    {
        var proxy = DispatchProxy.Create<T, Forward>();
        ((Forward)(object)proxy).Original = original;
        ((Forward)(object)proxy).Intercept = intercept;
        return proxy;
    }
    public class Forward : DispatchProxy
    {
        internal object Original = null!;
        internal Func<MethodInfo, object?[], (bool Handled, object? Result)> Intercept = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var decision = Intercept(method!, args!);
            if (decision.Handled) return decision.Result;
            try { return method!.Invoke(Original, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
    internal sealed class Outbox(IGameMessageSender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token) => sender.SendCmdAsync(id, message, token);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token) => sender.SendNotiAsync(id, message, token);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext context, IReadOnlyList<IGameServerMessage> messages, CancellationToken token) => sender.SendBatchAsync(context, messages, token);
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token) => sender.SendExplicitAsync(id, delivery, token);
    }
    internal sealed class Closures(INetworkConnectionCloser closer) : ICommandConnectionClosures
    {
        public ValueTask RequestCloseAsync(NetworkConnectionContext context, CancellationToken token) => closer.RequestCloseExactAsync(context, token);
        public ValueTask ResolveAndRequestCloseAsync(NetworkConnectionId id, CancellationToken token) => closer.RequestCloseAsync(id, token);
    }
    // Fixtures with no epic-piece catalog must still distinguish ordinary items from pieces.
    private sealed class NoEpicPieces : Dfo.Server.Abstractions.Inventory.IEpicBookDomainService
    {
        public EpicPieceDefinition? FindPiece(int id) => null;
        public ValueTask<EpicPieceAddition> AddPiecesAsync(Dfo.Server.Data.Accounts.AccountId account, int id, int count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<EpicPieceBalance>> GetBalancesAsync(Dfo.Server.Data.Accounts.AccountId account, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<EpicBookEntry>> GetBookAsync(Dfo.Server.Data.Accounts.AccountId account, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<EpicPieceBalance?> TrySpendPiecesAsync(Dfo.Server.Data.Accounts.AccountId account, int id, int count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask InitializeAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Users(ISelectedSessionDomainService sessions, Func<NetworkConnectionId, NetworkConnectionContext>? physical = null) : IOnlineUserQueryDomainService
    {
        // Pure component worlds still need stable physical identity throughout one connection generation.
        private readonly Dictionary<(NetworkConnectionId Id, long Generation), OnlineUserSnapshot> users = [];
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => users.Values.Single(user => user.OnlineUserId == id);
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user)
        {
            var session = sessions.FindByConnection(id);
            if (session is null) { user = null; return false; }
            var key = (id, session.ConnectionGeneration);
            if (!users.TryGetValue(key, out var existing))
                existing = new(OnlineUserId.New(), physical?.Invoke(id) ?? new(id, NetworkEndpointKind.Game, session.ListenerPort), session.AccountId,
                    session.CharacterId, OnlineUserState.CharacterActive, session.ConnectionGeneration);
            user = existing with { AccountId = session.AccountId, CurrentCharacterId = session.CharacterId };
            users[key] = user;
            return true;
        }
    }
    private sealed class Ordering : IOutboundOrdering
    {
        public IOutboundOrderingScope Begin(string name, OutboundDelivery delivery) => new Scope(delivery);
        private sealed class Scope(OutboundDelivery delivery) : IOutboundOrderingScope
        { public OutboundDelivery Delivery { get; set; } = delivery; public void Dispose() { } }
    }
    internal sealed class Units : ICommandWorkUnits
    {
        public async ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull
        {
            var request = context.Request ?? throw new InvalidOperationException("A component command needs explicit input.");
            var tx = new DomainTransaction();
            var unit = new CommandWorkUnit(tx, request, new Owners(declaration.FootprintOwners ?? declaration.Participants.Select(value => value.Session).ToArray()));
            try { var result = await action(unit, token); await tx.CommitAsync(token); return result; }
            catch { if (!tx.IsSettled) await tx.RollbackAsync(); throw; }
            finally { unit.Close(); }
        }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull => action(token);
        private sealed class Owners(IReadOnlyList<SelectedSessionIdentity> owners) : IEffectFootprintRecorder
        {
            public void DemandEffects(EffectFootprintOwner owner) { if (owner.Session is { } session) DemandActive(session); }
            public void DemandActive(SelectedSessionIdentity owner) { if (!owners.Contains(owner)) throw new InvalidOperationException("Undeclared fixture owner."); }
            public void RecordEffects(EffectFootprintOwner owner, IReadOnlyList<AppliedEffect> applied) => DemandEffects(owner);
            public void RecordInventorySlots(SelectedSessionIdentity owner, IReadOnlyList<BusinessSlot> slots) => DemandActive(owner);
            public void RecordRentalState(SelectedSessionIdentity owner) => DemandActive(owner);
        }
    }
}

// Component fixtures adapt a controlled event service; production consumes ICommandProjections directly.
internal sealed class TestCommandProjections(IEventService events) : ICommandProjections
{
    public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience, T notification, CancellationToken cancellationToken = default)
        where T : class, INotification<T> => events.ProjectAsync(audience, notification, cancellationToken);
}
