using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Testing;

/// <summary>Explicit stand-ins for dependencies a production constructor now requires.</summary>
/// <remarks>
/// Services no longer default an absent dependency to <c>null</c>. A test that has no use for one of them passes the
/// stand-in named here, so what it leaves out is written at the call site rather than implied by a default.
/// </remarks>
internal static class TestRequired
{
    /// <summary>A transaction scope outside any job: no ambient outbox, so a write opens no outbox unit.</summary>
    public static DomainTransactionScope TransactionScope() =>
        new(NullLogger<DomainTransaction>.Instance, Outboxes());

    /// <summary>Outboxes with no current job.</summary>
    public static GameplayOutboxes Outboxes() => new(NullLogger<GameplayOutbox>.Instance);

    /// <summary>Records no inventory contents event; for stores whose test does not observe them.</summary>
    public static IInventoryContentsEventDomainService NoContentsEvents { get; } = new IgnoredContentsEvents();

    /// <summary>An archive that declares no dungeon admission data; difficulty falls back to the character's maximum.</summary>
    public static IDungeonAdmissionDefinitionSource NoDungeonAdmissions { get; } = new NoAdmissions();

    /// <summary>Admission data read from structural dungeon definitions, the way the archive source derives it.</summary>
    public static IDungeonAdmissionDefinitionSource Admissions(IDungeonDefinitionSource definitions) => new DerivedAdmissions(definitions);

    /// <summary>Admission rules read from the structural definitions, the way entry derived them before they had a source of their own.</summary>
    public static IDungeonAdmissionRuleService AdmissionRulesFrom(Dfo.Server.Abstractions.World.IDungeonStructureRuleService structures) =>
        new StructuralAdmissionRules(structures);

    /// <summary>Quest holdings read from the character's bound containers, as the quest facts service reads them.</summary>
    public static Dfo.Server.Abstractions.Quests.IQuestFactsDomainService QuestFacts(ICharacterContainerDomainService containers,
        Dfo.Server.Abstractions.Characters.ICharacterDomainService characters, Dfo.Server.Abstractions.Quests.IQuestStateRuleService rules) =>
        new Dfo.Server.Application.Quests.QuestFactsDomainService(containers, characters, Absent<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>(),
            rules, Absent<Dfo.Server.Abstractions.Persistence.ICharacterTable>(), Absent<Dfo.Server.Abstractions.Creatures.ICreatureDomainStore>(),
            new Dfo.Server.Application.Characters.AmbientStoreAccess(TransactionScope()), Absent<IItemDomainStore>(),
            Absent<Dfo.Server.Abstractions.Quests.IGiantDragonCrystalExchangeDomainService>(),
            Absent<Dfo.Server.Abstractions.Marriage.IMarriageQuestDomainService>(), Absent<Dfo.Server.Abstractions.World.ISouthernDaleQuestDomainService>());

    private sealed class StructuralAdmissionRules(Dfo.Server.Abstractions.World.IDungeonStructureRuleService structures) : IDungeonAdmissionRuleService
    {
        public async ValueTask<DungeonAdmissionDefinition?> GetAsync(int dungeonId, CancellationToken ct) =>
            await structures.GetDefinitionAsync(dungeonId, ct).ConfigureAwait(false) is { } definition
                ? DungeonAdmissionDefinition.From(definition) : null;
    }

    private sealed class NoAdmissions : IDungeonAdmissionDefinitionSource
    {
        public ValueTask<DungeonAdmissionDefinition?> GetAdmissionAsync(int dungeonId, CancellationToken ct) =>
            ValueTask.FromResult<DungeonAdmissionDefinition?>(null);
    }

    private sealed class DerivedAdmissions(IDungeonDefinitionSource definitions) : IDungeonAdmissionDefinitionSource
    {
        public async ValueTask<DungeonAdmissionDefinition?> GetAdmissionAsync(int dungeonId, CancellationToken ct) =>
            await definitions.GetAsync(dungeonId, ct).ConfigureAwait(false) is { } definition ? DungeonAdmissionDefinition.From(definition) : null;
    }

    /// <summary>
    /// A service the test does not exercise, answering every question with "nothing": no records, false, empty
    /// collections, completed work. This is what the service's absence used to mean to the class under test.
    /// </summary>
    public static T Absent<T>() where T : class => AbsentService.Create<T>();

    /// <summary>
    /// An absent service whose every answer is null, for a caller that copes with a missing answer and whose test
    /// is about what the caller produces without one.
    /// </summary>
    public static T Unanswered<T>() where T : class
    {
        var service = AbsentService.Create<T>();
        ((AbsentService)(object)service).Silent = true;
        return service;
    }

    /// <summary>An absent service that answers the methods <paramref name="answer"/> names; null leaves a method absent.</summary>
    public static T Absent<T>(Func<System.Reflection.MethodInfo, object?[]?, object?> answer) where T : class
    {
        var service = AbsentService.Create<T>();
        ((AbsentService)(object)service).Answer = answer;
        return service;
    }

    /// <summary>Entry-time worn-item normalization over a character with nothing worn: never moves anything.</summary>
    public static Dfo.Server.Application.Inventory.LegacyWornItemNormalizer NoLegacyWornItems() =>
        new(Absent<IItemDomainStore>(), Absent<IItemPlacementRuleService>(),
            new Dfo.Server.Application.Characters.AmbientStoreAccess(TransactionScope()));

    /// <summary>
    /// Business slots bound straight to the registered containers, with no placement, arrival, capacity or lifecycle
    /// rules: the bare binding a slot lookup needs. Containers built from it check nothing on the way in.
    /// </summary>
    public static Dfo.Server.Application.Inventory.BusinessInventorySlotDomainService SlotsOver(ICharacterContainerDomainService containers) =>
        new(new Dfo.Server.Application.Inventory.CharacterBusinessInventoryDomainService(
            containers, null!, null!, null!, null!, null!, null!, null!, null!));

    /// <summary>
    /// For a fixture that composes a dungeon run by type: what the run reaches beyond the services the fixture
    /// registers stands absent, and the run's own rules are the real ones. Registered only where nothing else is.
    /// </summary>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddAbsentDungeonRunDependencies(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.Characters.IWeaknessDomainService>());
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>());
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.World.IDungeonLifecycleDomainService>());
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.World.IDungeonDeadlineWake>());
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());
        services.TryAddSingleton(Absent<Dfo.Server.Abstractions.World.IGiantDragonRuleService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.World.IDungeonRoomMechanismRuleService>(new Dfo.Server.Application.World.DungeonRoomMechanismRuleService());
        services.TryAddSingleton<Dfo.Server.Abstractions.World.ISealedTreasureRuleService>(
            new Dfo.Server.Application.World.SealedTreasureRuleService(Absent<IDungeonDefinitionSource>()));
        services.TryAddSingleton<Dfo.Server.Abstractions.World.IDungeonSettlementRuleService>(new Dfo.Server.Application.World.DungeonSettlementRuleService());
        services.TryAddSingleton<Dfo.Server.Abstractions.World.ILostLandFeverRuleService>(new Dfo.Server.Application.World.LostLandFeverRuleService());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>No expiration rule: an item keeps the lifetime it arrives with, as when the rule was left out.</summary>
    public static Dfo.Server.Application.Inventory.IItemExpirationRuleService NoExpiration { get; } = new KeptLifetimes();

    private sealed class KeptLifetimes : Dfo.Server.Application.Inventory.IItemExpirationRuleService
    {
        public ItemSnapshot Prepare(ItemSnapshot item, ItemLifetimeDefinition lifetime, DateTimeOffset at) => item;
    }

    /// <summary>Appearance drawn from the worn pieces alone, with no account or character settings behind it.</summary>
    public static Dfo.Server.Abstractions.Characters.ICharacterAppearanceDomainService WornAppearances { get; } = new WornOnly();

    private sealed class WornOnly : Dfo.Server.Abstractions.Characters.ICharacterAppearanceDomainService
    {
        public ValueTask<Dfo.Server.Data.Characters.CharacterAppearance> ResolveAsync(Dfo.Server.Data.Accounts.AccountId accountId,
            Dfo.Server.Data.Characters.CharacterId characterId, IReadOnlyList<Dfo.Server.Data.Inventory.InventoryItemState> equipment,
            CancellationToken ct) =>
            ValueTask.FromResult(Dfo.Server.Application.Characters.CharacterAppearanceRuleService.Shared.Resolve(new(new(0)), equipment, new(null, 0)));
    }

    private class AbsentService : System.Reflection.DispatchProxy
    {
        public static T Create<T>() where T : class => System.Reflection.DispatchProxy.Create<T, AbsentService>();

        public Func<System.Reflection.MethodInfo, object?[]?, object?>? Answer { get; set; }

        public bool Silent { get; set; }

        /// <remarks>
        /// An attempt the absent service would have been asked to approve goes ahead, as it did when the service was
        /// skipped: a <c>Try</c> method with no out value answers true. Every other question answers "nothing".
        /// </remarks>
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method is null) return null;
            if (Answer?.Invoke(method, args) is { } answered) return answered;
            if (Silent)
                return method.ReturnType == typeof(void) ? null
                    : method.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask
                    : method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)
                        ? Activator.CreateInstance(method.ReturnType)
                        : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
            var approves = method.Name.StartsWith("Try", StringComparison.Ordinal)
                && !method.GetParameters().Any(x => x.IsOut || x.ParameterType.IsByRef);
            if (approves && method.ReturnType == typeof(bool)) return true;
            if (approves && method.ReturnType == typeof(ValueTask<bool>)) return ValueTask.FromResult(true);
            if (approves && method.ReturnType == typeof(Task<bool>)) return Task.FromResult(true);
            // A transformation of something the caller passed in hands it back unchanged.
            var returned = method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)
                ? method.ReturnType.GetGenericArguments()[0] : method.ReturnType;
            var passed = method.GetParameters().Select((x, i) => (x, i)).Where(p => p.x.ParameterType == returned).Select(p => p.i).ToArray();
            if (args is not null && passed.Length == 1 && returned != typeof(bool) && !returned.IsEnum)
                return returned == method.ReturnType ? args[passed[0]]
                    : method.ReturnType.GetConstructor([returned])!.Invoke([args[passed[0]]]);
            // A lookup that may find nothing finds nothing; one that always answers hands back an absent view.
            var nullability = new System.Reflection.NullabilityInfoContext().Create(method.ReturnParameter);
            if (!method.ReturnType.IsValueType && nullability.ReadState == System.Reflection.NullabilityState.Nullable)
                return null;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() is var awaited
                && (awaited == typeof(ValueTask<>) || awaited == typeof(Task<>))
                && !method.ReturnType.GetGenericArguments()[0].IsValueType
                && nullability.GenericTypeArguments[0].ReadState == System.Reflection.NullabilityState.Nullable)
                return awaited == typeof(Task<>)
                    ? typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(method.ReturnType.GetGenericArguments()).Invoke(null, [null])
                    : Activator.CreateInstance(method.ReturnType);
            return Nothing(method.ReturnType, method, args);
        }

        private static object? Nothing(Type type, System.Reflection.MethodInfo? method = null, object?[]? args = null)
        {
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type == typeof(ValueTask)) return ValueTask.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type.GetGenericArguments())
                    .Invoke(null, [Nothing(type.GetGenericArguments()[0])]);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return type.GetConstructor([type.GetGenericArguments()[0]])!.Invoke([Nothing(type.GetGenericArguments()[0], method, args)]);
            if (type == typeof(string)) return null;
            if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsGenericType && type.GetGenericArguments().Length == 1
                && type.IsAssignableFrom(type.GetGenericArguments()[0].MakeArrayType()))
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Immutable.ImmutableArray<>))
                return type.GetField("Empty")!.GetValue(null);
            // A session or view handed out by an absent service is just as empty.
            if (type.IsInterface && type.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal))
                return System.Reflection.DispatchProxy.Create(type, typeof(AbsentService));
            if (type.IsValueType) return Activator.CreateInstance(type);
            // An answer of this server's own value type is the emptiest one it can build, carrying through any input
            // the question named for it (a fallback, say).
            if (type.IsClass && type.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal)
                && type.GetConstructors().OrderBy(x => x.GetParameters().Length).FirstOrDefault() is { } constructor)
                try
                {
                    return constructor.Invoke(constructor.GetParameters().Select(p =>
                    method?.GetParameters().Select((x, i) => (x, i)).FirstOrDefault(q => q.x.ParameterType == p.ParameterType
                        && q.x.Name is { } n && p.Name is { } m && (n.Contains(m, StringComparison.OrdinalIgnoreCase) || m.Contains(n, StringComparison.OrdinalIgnoreCase))) is { x: not null } hit && args is not null
                        ? args[hit.i]
                        : !p.ParameterType.IsValueType && new System.Reflection.NullabilityInfoContext().Create(p).WriteState
                            == System.Reflection.NullabilityState.Nullable ? null : Nothing(p.ParameterType)).ToArray());
                }
                catch (System.Reflection.TargetInvocationException)
                {
                    return null;
                }
            return null;
        }
    }

    private sealed class IgnoredContentsEvents : IInventoryContentsEventDomainService
    {
        public ValueTask RecordAsync(ContainerId container, IReadOnlyCollection<int> changedItemIds, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }
}
