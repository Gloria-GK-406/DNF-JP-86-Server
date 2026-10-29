using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Dfo.Server.Application.World;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// RQ-061 / RQ-011 / AC-006 (TASK-30 sections 2 and 4): every singleton the Application and Protocol modules
/// register that holds in-process mutable state, and every type of the two assemblies with mutable static state,
/// is either a write entry (<see cref="GameplayWriteEntryTableTests.Types"/>, whose write members assert) or an
/// exemption here that names its spec appendix B row and a reason. A row in either list that no longer holds
/// state the census sees is refused too.
/// </summary>
/// <remarks>
/// <para>
/// Mutable state, found by reflection on the implementation type and its base types in the same two assemblies:
/// (1) an instance field that is not readonly (a captured primary-constructor parameter counts by its declared
/// type, as if readonly, unless some method of the type or of its nested types stores to it outside the constructor:
/// the compiler leaves it writable either way; <c>GameplayOutbox+OrderedScope.set_Delivery</c> is such a store, on a
/// per-job object that is no singleton and no field of one); (2) a readonly field of a
/// mutable collection, an array, <see cref="AsyncLocal{T}"/> or <see cref="ThreadLocal{T}"/>; (3) a readonly field
/// of a class from the two assemblies that is itself (1) to (3) and is neither a registered singleton (that one is
/// counted under its own name) nor classified on its own (a component such as <c>TypedGameplayTimerSource</c>,
/// which is then a holder itself); (4) a handle: a type from the two assemblies that is (1) to (3) and that a
/// public or internal member of a listed write entry returns; (5) a static field that is not readonly, or is
/// readonly and (2), on any type of the two assemblies. Not state: readonly fields of interfaces, <c>Frozen*</c>,
/// <c>Immutable*</c>, delegates, loggers, clocks, <see cref="Lazy{T}"/> (the first index is
/// <c>ContentWarmupTests.W2</c>'s), value types held readonly.
/// </para>
/// <para>
/// Recognition boundary: state behind an <see cref="object"/> or interface-typed field; state in a type from
/// another assembly (Abstractions, Data, the BCL beyond the collections above, such as a <c>Random</c>, a
/// channel or a semaphore: the last two are appendix F's); state of a non-singleton object kept alive outside
/// the singletons, such as a collection captured by a closure; whether a static constant table is ever written
/// after its initializer. Reading the writers covered the last two.
/// </para>
/// <para>
/// The write entries stay listed only in <see cref="GameplayWriteEntryTableTests.Types"/>; together with
/// <see cref="Exemptions"/> it is the one source of appendix F's "move onto the executor" list.
/// </para>
/// </remarks>
public sealed class GameplayStateCensusTests(ITestOutputHelper output)
{
    internal enum Category
    {
        /// <summary>Execution machinery and call-chain metadata: reads and writes no gameplay state.</summary>
        E1,

        /// <summary>Immutable content, rule caches and constant tables.</summary>
        E2,

        /// <summary>A per-flow request frame (transition mechanism, root SC-12).</summary>

        /// <summary>
        /// RQ-061's own write unit of work, guarded by the transaction-site criterion rather than the table; the
        /// row names that criterion instead of an appendix B row.
        /// </summary>
        R061,
    }

    internal sealed record Exemption(Category Category, string Row, string Reason);

    private const string ProducerRow = "生产者的入队侧";

    private const string StartupRow = "开始监听前的启动期工作";

    private const string ContentRow = "PVF 内容源与缓存、规则服务的不可变规则";

    private const string EncodingRow = "发送端：编码、发送队列、交付、超时";


    private const string ConstantTable = "a static constant table: written by its initializer, only read afterwards (every use read in TASK-30)";

    private const string ConstantBytes = "constant bytes an encoder copies into a frame: written by the initializer, only read afterwards (every use read in TASK-30)";

    private const string LazyRules = "immutable rules loaded once from content (first index warmed before listening, ContentWarmupTests); never game state";

    private static Type ProtocolType(string name) => typeof(ProtocolModule).Assembly.GetType(name, throwOnError: true)!;

    internal static readonly IReadOnlyDictionary<Type, Exemption> Exemptions = new Dictionary<Type, Exemption>
    {
        // E1: execution machinery and call-chain metadata.
        [typeof(Dfo.Server.Application.Authentication.LoginSessionMaintenanceService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Application.Events.JoustMaintenanceService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Altar.AltarScheduler)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.GuardianAltar.GuardianAltarScheduler)] = new(Category.E1, ProducerRow, "typed source scheduling and deadlines; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.CharacterDeath.DungeonPartyWipeService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Creatures.CreatureClockService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.MarriageProposal.MarriageWeddingScheduler)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismMaintenanceService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Tournament.TournamentRewardSweepService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Weakness.WeaknessRecoveryService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Altar.AltarQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.DimensionQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.GoldSilverHornQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.ItemUsageDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.MirrorQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.PremiumDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.StateSynchronization.ReviveCoinQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Tournament.HerasQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),
        [typeof(Dfo.Server.Protocol.Game.Fatigue.FatigueDailyRefreshService)] = new(Category.E1, ProducerRow, "typed source scheduling, accepted rounds, coalescing and drain; owns no gameplay state"),

        [typeof(Dfo.Server.Protocol.Game.BossTowerReturnSweepService)] = new(Category.E1, ProducerRow,
            "typed timer round admission, coalescing and producer drain; owns no tower world state"),
        [typeof(Dfo.Server.Protocol.Game.IllusionTowerReturnSweepService)] = new(Category.E1, ProducerRow,
            "typed timer round admission, coalescing and producer drain; owns no tower world state"),
        [typeof(Dfo.Server.Application.Execution.GameplayExecutor)] = new(Category.E1, ProducerRow,
            "the executor's own started and abandoned marks"),
        [typeof(Dfo.Server.Application.Scheduling.ScheduledTaskHostedService)] = new(Category.E1, ProducerRow,
            "the scheduled tasks' due queue and in-flight executions: decides when a task's CMD is enqueued"),
        [typeof(Dfo.Server.Application.Execution.GameplayCloseCauses)] = new(Category.E1, ProducerRow,
            "call-chain metadata: the CMD that asked for a close, per connection context (TASK-29 section 3 item 9)"),
        [typeof(Dfo.Server.Application.DomainEvents.DomainEventCallChains)] = new(Category.E1, ProducerRow,
            "call-chain metadata: the CMD that published an event delivery (TASK-29 section 3 item 9)"),
        [typeof(Dfo.Server.Application.DomainEvents.FailedDomainEventAttempts)] = new(Category.E1, ProducerRow,
            "failed event attempt and settlement scheduling metadata; prevents duplicate queued consumption, owns no gameplay world state"),
        [typeof(Dfo.Server.Application.DomainEvents.DomainEventWakeups)] = new(Category.E1, ProducerRow,
            "the event group loops' wake-up channels, one per group"),
        [typeof(Dfo.Server.Application.DomainEvents.DomainEventRegistry)] = new(Category.E1, StartupRow,
            "event and subscriber registrations: filled by the composition root, validated at startup, only read afterwards"),

        // E2: immutable content, rule caches and constant tables.
        [typeof(Dfo.Server.Application.Characters.CharacterSkillRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(Dfo.Server.Application.Characters.CharacterStatRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(Dfo.Server.Application.Events.JoustRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(ExperienceRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(MonsterDeclaredDropRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(WorldDropRuleService)] = new(Category.E2, ContentRow, LazyRules),
        [typeof(Dfo.Server.Application.Accounts.AccountSettingsEntryProjection)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Characters.WeaknessRuleService)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Initialization.InitializationRuleService)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Inventory.EquipmentQualitySeeds)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Inventory.InventoryEntryProjection)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Mail.MailItemTransfers)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(Dfo.Server.Application.Quests.QuestObjectives)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(DimensionDungeonRuleService)] = new(Category.E2, ContentRow, ConstantTable),
        [typeof(SealedTreasureRuleService)] = new(Category.E2, ContentRow, ConstantTable),
        [ProtocolType("Dfo.Server.Protocol.Game.DungeonSelection.EnterSelectDungeonAcknowledgementEncoder")] = new(Category.E2, EncodingRow, ConstantBytes),
        [ProtocolType("Dfo.Server.Protocol.Game.GroundPickup.GroundPickupPayloadCodec")] = new(Category.E2, EncodingRow, ConstantBytes),
        [ProtocolType("Dfo.Server.Protocol.Game.MapEntryReports.SecurityStatusAcknowledgementEncoder")] = new(Category.E2, EncodingRow, ConstantBytes),
        [ProtocolType("Dfo.Server.Protocol.Game.MercenaryInfo.MercenaryInfoResponseEncoder")] = new(Category.E2, EncodingRow, ConstantBytes),
        [ProtocolType("Dfo.Server.Protocol.Game.MonsterDeath.MonsterDeathPayloadCodec")] = new(Category.E2, EncodingRow, ConstantBytes),
        [ProtocolType("Dfo.Server.Protocol.Game.StateSynchronization.StateSynchronizationPayloadCodec")] = new(Category.E2, EncodingRow, ConstantBytes),

        // R061: the write unit of work, RQ-061's first entry.
        [typeof(Dfo.Server.Application.Transactions.DomainTransactionScope)] = new(Category.R061,
            nameof(GameplayWriteEntryTableTests.Transactions_are_created_only_at_the_listed_sites_and_the_writes_assert),
            "its AsyncLocal holds the job's own transaction; Begin asserts before it opens a writable one, and every new DomainTransaction( is a listed site"),
    };

    /// <summary>Write entries that hold no state of their own: they work on state their callers pass in, or their owner's.</summary>
    internal static readonly IReadOnlyDictionary<Type, string> StatelessEntries = new Dictionary<Type, string>
    {
        [typeof(Dfo.Server.Protocol.Game.Joust.GameJoustBetCommandHandler)] =
            "writes through domain services and the request-owned EffectOperationEntry; has no static operation-id table or mutable state of its own",
        [typeof(AltarDungeonEngine)] = "named by appendix F; works on the AltarStateStore each caller passes in, which is a write entry of its own",
        [GameplayWriteEntryTableTests.WeaknessHandle] =
            "the handle WeaknessDomainService.For hands out; it writes its owner's entries and pending penalties, the owner being a write entry",
    };

    [Fact]
    public void Every_singleton_holding_mutable_state_is_a_write_entry_or_a_listed_exemption()
    {
        var census = Census.Take(ProductionSingletons(), ProductionAssemblies,
            ProductionAssemblies.SelectMany(Census.LoadableTypes), GameplayWriteEntryTableTests.Types, Exemptions, StatelessEntries);

        var violations = census.Violations();

        var text = census.Render(violations);
        output.WriteLine(text);
        if (Environment.GetEnvironmentVariable("DFO_STATE_CENSUS_OUT") is { Length: > 0 } path)
            File.WriteAllText(path, text);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("unlisted holder")]
    [InlineData("unreasoned exemption")]
    [InlineData("stale row")]
    [InlineData("static state")]
    [InlineData("reassigned parameter")]
    public void The_census_refuses_an_unlisted_holder_an_unreasoned_exemption_and_a_stale_row(string shape)
    {
        var (registrations, statics, entries, exemptions, expected) = shape switch
        {
            "unlisted holder" => (Singletons(typeof(FakeRoster), typeof(FakeClock)), Array.Empty<Type>(),
                Array.Empty<Type>(), NoExemptions,
                $"{typeof(FakeRoster).FullName}: holds mutable state (members (Dictionary<Int32, String>)) but is neither a write entry nor an exemption"),
            "unreasoned exemption" => (Singletons(typeof(FakeRoster)), [], [],
                new Dictionary<Type, Exemption> { [typeof(FakeRoster)] = new(Category.E1, ProducerRow, " ") },
                $"{typeof(FakeRoster).FullName}: an exemption must name its appendix B row and a reason"),
            "stale row" => (Singletons(typeof(FakeRoster), typeof(FakeClock)), [],
                [typeof(FakeRoster), typeof(FakeClock)], NoExemptions,
                $"{typeof(FakeClock).FullName}: stale write entry, holds no mutable state the census sees"),
            "reassigned parameter" => (Singletons(typeof(FakeCounter), typeof(FakeClock)), Array.Empty<Type>(), Array.Empty<Type>(), NoExemptions,
                $"{typeof(FakeCounter).FullName}: holds mutable state (start (reassigned captured parameter)) but is neither a write entry nor an exemption"),
            _ => (Singletons(typeof(FakeClock)), [typeof(FakeStatics)], [], NoExemptions,
                $"{typeof(FakeStatics).FullName}: holds mutable state (static Seen (HashSet<String>)) but is neither a write entry nor an exemption"),
        };
        var census = Census.Take(registrations, [typeof(GameplayStateCensusTests).Assembly], statics, entries, exemptions,
            new Dictionary<Type, string>());

        Assert.Equal([expected], census.Violations());
    }

    private static readonly IReadOnlyDictionary<Type, Exemption> NoExemptions = new Dictionary<Type, Exemption>();

    private static Registration[] Singletons(params Type[] types) =>
        types.Select(type => new Registration(type, type, "type")).ToArray();

    internal static readonly Assembly[] ProductionAssemblies =
        [typeof(ApplicationModule).Assembly, typeof(ProtocolModule).Assembly];

    /// <summary>The singletons the two production modules register, with the implementation each one builds.</summary>
    internal static IReadOnlyList<Registration> ProductionSingletons()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return Census.Registrations(services);
    }

    internal sealed record Registration(Type Service, Type Implementation, string How);

    internal sealed record Holder(Type Type, IReadOnlyList<string> Fields, string Why);

    /// <summary>The census itself: the rules above, shared by the criterion and its counterexamples.</summary>
    internal sealed class Census
    {
        private readonly IReadOnlyList<Registration> registrations;
        private readonly IReadOnlyCollection<Assembly> assemblies;
        private readonly IReadOnlyList<Type> staticScope;
        private readonly IReadOnlyList<Type> entries;
        private readonly IReadOnlyDictionary<Type, Exemption> exemptions;
        private readonly IReadOnlyDictionary<Type, string> stateless;
        private readonly HashSet<Type> singletons;
        private readonly Dictionary<Type, IReadOnlyList<string>> stateCache = new();
        private readonly Dictionary<Type, string> components = new();

        private Census(IReadOnlyList<Registration> registrations, IReadOnlyCollection<Assembly> assemblies,
            IEnumerable<Type> staticScope, IReadOnlyList<Type> entries, IReadOnlyDictionary<Type, Exemption> exemptions,
            IReadOnlyDictionary<Type, string> stateless)
        {
            this.registrations = registrations.Where(registration => assemblies.Contains(registration.Implementation.Assembly)).ToArray();
            this.assemblies = assemblies;
            this.staticScope = staticScope.Where(Counted).ToArray();
            this.entries = entries;
            this.exemptions = exemptions;
            this.stateless = stateless;
            singletons = this.registrations.Select(registration => registration.Implementation).ToHashSet();
            AllRegistrations = registrations;
        }

        public static Census Take(IReadOnlyList<Registration> registrations, IReadOnlyCollection<Assembly> assemblies,
            IEnumerable<Type> staticScope, IReadOnlyList<Type> entries, IReadOnlyDictionary<Type, Exemption> exemptions,
            IReadOnlyDictionary<Type, string> stateless) =>
            new(registrations, assemblies, staticScope, entries, exemptions, stateless);

        private IReadOnlyList<Registration> AllRegistrations { get; }

        private bool Classified(Type type) => entries.Contains(type) || exemptions.ContainsKey(type) || stateless.ContainsKey(type);

        /// <summary>Every holder: stateful singletons, the components they own, types with static state, handles of the write entries.</summary>
        public IReadOnlyList<Holder> Holders()
        {
            var holders = new Dictionary<Type, Holder>();
            foreach (var registration in registrations)
            {
                var fields = StateOf(registration.Implementation);
                if (fields.Count > 0 && !holders.ContainsKey(registration.Implementation))
                    holders[registration.Implementation] = new Holder(registration.Implementation, fields, "singleton (" + registration.How + ")");
            }
            foreach (var type in staticScope)
            {
                var fields = StaticStateOf(type);
                if (fields.Count == 0) continue;
                holders[type] = holders.TryGetValue(type, out var known)
                    ? known with { Fields = known.Fields.Concat(fields).ToArray(), Why = known.Why + " + static" }
                    : new Holder(type, fields, "static state");
            }
            foreach (var entry in entries)
                foreach (var handle in HandlesOf(entry))
                    if (!holders.ContainsKey(handle))
                        holders[handle] = new Holder(handle, OwnState(handle), "handle returned by " + entry.Name);
            foreach (var (component, owner) in components.ToArray())
                if (!holders.ContainsKey(component))
                    holders[component] = new Holder(component, OwnState(component), "component of " + owner);
            return holders.Values.OrderBy(holder => holder.Type.FullName, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<string> Violations()
        {
            var violations = new List<string>();
            var holders = Holders().ToDictionary(holder => holder.Type);
            foreach (var holder in holders.Values)
            {
                var listed = entries.Contains(holder.Type);
                var exempt = exemptions.ContainsKey(holder.Type);
                if (listed && exempt)
                    violations.Add($"{Name(holder.Type)}: both a write entry and an exemption");
                else if (!listed && !exempt)
                    violations.Add($"{Name(holder.Type)}: holds mutable state ({string.Join(", ", holder.Fields)}) but is neither a write entry nor an exemption");
            }
            foreach (var (type, exemption) in exemptions)
            {
                if (string.IsNullOrWhiteSpace(exemption.Row) || string.IsNullOrWhiteSpace(exemption.Reason))
                    violations.Add($"{Name(type)}: an exemption must name its appendix B row and a reason");
                if (!holders.ContainsKey(type))
                    violations.Add($"{Name(type)}: stale exemption, holds no mutable state the census sees");
            }
            foreach (var type in entries)
                if (!holders.ContainsKey(type) && !stateless.ContainsKey(type))
                    violations.Add($"{Name(type)}: stale write entry, holds no mutable state the census sees");
            foreach (var (type, reason) in stateless)
            {
                if (!entries.Contains(type)) violations.Add($"{Name(type)}: listed as a stateless write entry but not a write entry");
                if (holders.ContainsKey(type)) violations.Add($"{Name(type)}: listed as a stateless write entry but holds state");
                if (string.IsNullOrWhiteSpace(reason)) violations.Add($"{Name(type)}: a stateless write entry needs a reason");
            }
            return violations;
        }

        public string Render(IReadOnlyList<string> violations)
        {
            var text = new StringBuilder();
            text.AppendLine("# TASK-30 state census, rendered by GameplayStateCensusTests from the registrations it checks.");
            text.AppendLine("# columns: type | how found | mutable fields | classification | appendix B row — reason");
            var holders = Holders();
            int written = 0, exempted = 0, unlisted = 0;
            var byCategory = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var byHow = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var holder in holders)
            {
                string kind, row = "";
                var how = holder.Why.Split(' ')[0];
                byHow[how] = byHow.GetValueOrDefault(how) + 1;
                if (entries.Contains(holder.Type))
                {
                    kind = "write entry";
                    written++;
                }
                else if (exemptions.TryGetValue(holder.Type, out var exemption))
                {
                    kind = "exempt " + exemption.Category;
                    row = exemption.Row + " — " + exemption.Reason;
                    exempted++;
                    byCategory[exemption.Category.ToString()] = byCategory.GetValueOrDefault(exemption.Category.ToString()) + 1;
                }
                else
                {
                    kind = "UNLISTED";
                    unlisted++;
                }
                text.AppendLine($"{Name(holder.Type)} | {holder.Why} | {string.Join(", ", holder.Fields)} | {kind} | {row}");
            }
            foreach (var (type, reason) in stateless)
                text.AppendLine($"{Name(type)} | stateless | | write entry without state | {reason}");
            var implementations = registrations.Select(registration => registration.Implementation).Distinct().ToArray();
            var statefulSingletons = implementations.Count(type => StateOf(type).Count > 0);
            text.AppendLine();
            text.AppendLine("# factory registrations (service type -> what the factory constructs):");
            foreach (var registration in AllRegistrations.Where(registration => registration.How.StartsWith("factory", StringComparison.Ordinal)))
                text.AppendLine($"factory | {Name(registration.Service)} -> {(registration.How == "factory" ? Name(registration.Implementation) : registration.How)}");
            text.AppendLine("# collections a hosted service's long-running method captures (outside the rules; their writers were read):");
            foreach (var type in implementations.Where(type => type.IsAssignableTo(typeof(Microsoft.Extensions.Hosting.IHostedService)))
                         .OrderBy(Name, StringComparer.Ordinal))
                foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(IsCompilerGenerated))
                    foreach (var field in nested.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                 .Where(field => IsMutableContainer(field.FieldType)))
                        text.AppendLine($"captured | {Name(type)} | {nested.Name}.{field.Name} ({Format(field.FieldType)})");
            text.AppendLine($"# singleton registrations: {AllRegistrations.Count}, of which implemented in the two assemblies: {registrations.Count}");
            text.AppendLine($"# distinct implementation types in the two assemblies: {implementations.Length} = with state of their own {statefulSingletons} + without {implementations.Length - statefulSingletons}");
            text.AppendLine("# holders by how found: " + string.Join(" + ", byHow.Select(pair => $"{pair.Key} {pair.Value}")) + $" = {holders.Count}");
            text.AppendLine($"# holders: write entries {written} + exemptions {exempted} + unlisted {unlisted} = {written + exempted + unlisted}");
            text.AppendLine($"# write entries in the table: {entries.Count} = holders {written} + stateless {stateless.Count(pair => entries.Contains(pair.Key))} + stale {entries.Count - written - stateless.Count(pair => entries.Contains(pair.Key))}");
            text.AppendLine("# exemptions by category: " + string.Join(" + ", byCategory.Select(pair => $"{pair.Key} {pair.Value}")) + $" = {exempted}");
            text.AppendLine($"# violations: {violations.Count}");
            foreach (var violation in violations) text.AppendLine("#   " + violation);
            return text.ToString();
        }

        /// <summary>Rules (1) to (3) on an instance type and its base types in the two assemblies.</summary>
        public IReadOnlyList<string> StateOf(Type type) => StateOf(type, [], owner: null);

        /// <summary>The state of a type listed in its own right: its fields, without looking at its own classification.</summary>
        private IReadOnlyList<string> OwnState(Type type) => StateOf(type, [], owner: null);

        private IReadOnlyList<string> StateOf(Type type, HashSet<Type> visiting, Type? owner)
        {
            if (stateCache.TryGetValue(type, out var known)) return known;
            if (!visiting.Add(type)) return [];
            var fields = new List<string>();
            for (var current = type; current is not null && Owned(current); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var captured = field.Name.StartsWith('<') && field.Name.EndsWith(">P", StringComparison.Ordinal);
                    var fieldType = field.FieldType;
                    if (!field.IsInitOnly && !captured && !fieldType.IsAssignableTo(typeof(Delegate)))
                    {
                        fields.Add(FieldName(field) + " (writable)");
                    }
                    else if (captured && IsReassigned(field))
                    {
                        fields.Add(FieldName(field) + " (reassigned captured parameter)");
                    }
                    else if (IsMutableContainer(fieldType) && !IsServiceArray(fieldType))
                    {
                        fields.Add(FieldName(field) + " (" + Format(fieldType) + ")");
                    }
                    else if (fieldType.IsClass && Owned(fieldType) && !singletons.Contains(Open(fieldType))
                             && StateOf(fieldType, visiting, type).Count > 0)
                    {
                        components.TryAdd(Open(fieldType), type.Name);
                        if (!Classified(Open(fieldType)))
                            fields.Add(FieldName(field) + " (" + Format(fieldType) + ", holds state)");
                    }
                }
            }
            visiting.Remove(type);
            stateCache[type] = fields;
            return fields;
        }

        /// <summary>Rule (1) for a captured primary-constructor parameter: a <c>stfld</c> to it outside the constructors.</summary>
        private static bool IsReassigned(FieldInfo field)
        {
            foreach (var method in MethodsOf(field.DeclaringType!))
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;
                var typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
                var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                for (var offset = 0; offset < il.Length;)
                {
                    var code = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
                    var opCode = OpCodesByValue[code];
                    offset += opCode.Size;
                    var operand = offset;
                    offset += OperandSize(opCode.OperandType, il, offset);
                    if (opCode != OpCodes.Stfld) continue;
                    FieldInfo? target;
                    try
                    {
                        target = method.Module.ResolveField(BitConverter.ToInt32(il, operand), typeArguments, methodArguments);
                    }
                    catch (ArgumentException)
                    {
                        target = null;
                    }
                    if (target is not null && target.MetadataToken == field.MetadataToken && target.Module == field.Module)
                        return true;
                }
            }
            return false;
        }

        /// <summary>The methods of a type and of its nested types (lambdas and state machines), constructors aside.</summary>
        private static IEnumerable<MethodInfo> MethodsOf(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return method;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var method in MethodsOf(nested))
                    yield return method;
        }

        /// <summary>Rule (5).</summary>
        public static IReadOnlyList<string> StaticStateOf(Type type)
        {
            var fields = new List<string>();
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsLiteral || field.FieldType.IsAssignableTo(typeof(Delegate))) continue;
                if (!field.IsInitOnly) fields.Add("static " + field.Name + " (writable)");
                else if (IsMutableContainer(field.FieldType)) fields.Add("static " + field.Name + " (" + Format(field.FieldType) + ")");
            }
            return fields;
        }

        /// <summary>Rule (4): owned types with state that a public or internal member of <paramref name="entry"/> hands out.</summary>
        private IReadOnlyList<Type> HandlesOf(Type entry)
        {
            var found = new HashSet<Type>();
            foreach (var method in GameplayWriteEntryTableTests.Members(entry))
            {
                foreach (var type in Mentioned(method.ReturnType)
                             .Concat(method.GetParameters().Where(p => p.IsOut).SelectMany(p => Mentioned(p.ParameterType))))
                {
                    if (type != entry && type.IsClass && Owned(type) && OwnState(type).Count > 0) found.Add(type);
                }
            }
            return found.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        }

        private static IEnumerable<Type> Mentioned(Type type)
        {
            if (type.IsByRef || type.IsArray)
            {
                foreach (var inner in Mentioned(type.GetElementType()!)) yield return inner;
                yield break;
            }
            if (type.IsGenericParameter) yield break;
            yield return Open(type);
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    foreach (var inner in Mentioned(argument))
                        yield return inner;
        }

        private static Type Open(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

        private bool Owned(Type type) => assemblies.Contains(type.Assembly);

        private static bool Counted(Type type) =>
            !IsCompilerGenerated(type) && type.Namespace?.StartsWith("Dfo.", StringComparison.Ordinal) == true;

        private static bool IsCompilerGenerated(Type type)
        {
            for (var current = type; current is not null; current = current.DeclaringType)
                if (current.Name.Contains('<', StringComparison.Ordinal)
                    || current.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) return true;
            return false;
        }

        /// <summary>A readonly array of injected services (interfaces), built once in the constructor: not state.</summary>
        private static bool IsServiceArray(Type type) => type.IsArray && type.GetElementType()!.IsInterface;

        internal static bool IsMutableContainer(Type type)
        {
            if (type.IsArray) return true;
            if (!type.IsGenericType) return false;
            var definition = type.GetGenericTypeDefinition();
            return definition.Namespace == "System.Collections.Concurrent" || MutableDefinitions.Contains(definition);
        }

        private static readonly IReadOnlySet<Type> MutableDefinitions = new HashSet<Type>
        {
            typeof(Dictionary<,>), typeof(HashSet<>), typeof(List<>), typeof(Queue<>), typeof(Stack<>),
            typeof(SortedSet<>), typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(LinkedList<>),
            typeof(PriorityQueue<,>), typeof(ConditionalWeakTable<,>), typeof(AsyncLocal<>), typeof(ThreadLocal<>),
        };

        /// <summary>Every singleton descriptor with the implementation it builds; a factory's is read off its IL.</summary>
        public static IReadOnlyList<Registration> Registrations(IServiceCollection services)
        {
            var found = new List<Registration>();
            foreach (var descriptor in services.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton))
            {
                var keyed = descriptor.IsKeyedService;
                var type = keyed ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
                var instance = keyed ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance;
                var factory = keyed ? (Delegate?)descriptor.KeyedImplementationFactory : descriptor.ImplementationFactory;
                if (type is not null)
                    found.Add(new Registration(descriptor.ServiceType, type, "type"));
                else if (instance is not null)
                    found.Add(new Registration(descriptor.ServiceType, instance.GetType(), "instance"));
                else if (factory is not null)
                {
                    var constructed = Constructed(factory.Method, depth: 2).Distinct().ToArray();
                    if (constructed.Length == 0)
                        found.Add(new Registration(descriptor.ServiceType, descriptor.ServiceType, "factory, constructs nothing (forwards)"));
                    foreach (var built in constructed)
                        found.Add(new Registration(descriptor.ServiceType, built, "factory"));
                }
            }
            return found;
        }

        /// <summary>
        /// The classes a factory constructs with <c>newobj</c>, read off its IL (closures and delegates aside),
        /// following its calls into methods of <c>Dfo.*</c> assemblies down to <paramref name="depth"/>.
        /// </summary>
        private static IEnumerable<Type> Constructed(MethodBase factory, int depth)
        {
            var il = factory.GetMethodBody()?.GetILAsByteArray();
            if (il is null) yield break;
            var module = factory.Module;
            var typeArguments = factory.DeclaringType?.IsGenericType == true ? factory.DeclaringType.GetGenericArguments() : null;
            var methodArguments = factory.IsGenericMethod && !factory.IsConstructor ? factory.GetGenericArguments() : null;
            for (var offset = 0; offset < il.Length;)
            {
                var code = il[offset] == 0xFE ? (short)(0xFE00 | il[offset + 1]) : il[offset];
                var opCode = OpCodesByValue[code];
                offset += opCode.Size;
                var operand = offset;
                offset += OperandSize(opCode.OperandType, il, offset);
                if (opCode != OpCodes.Newobj && opCode != OpCodes.Call && opCode != OpCodes.Callvirt) continue;
                MethodBase? target;
                try
                {
                    target = module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                    target = null;
                }
                if (target?.DeclaringType is not { } declaring) continue;
                if (opCode == OpCodes.Newobj)
                {
                    if (!declaring.IsAssignableTo(typeof(Delegate)) && !IsCompilerGenerated(declaring))
                        yield return Open(declaring);
                }
                else if (depth > 0 && !target.IsAbstract
                         && declaring.Assembly.GetName().Name?.StartsWith("Dfo.", StringComparison.Ordinal) == true)
                {
                    foreach (var inner in Constructed(target, depth - 1)) yield return inner;
                }
            }
        }

        private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

        private static int OperandSize(OperandType type, byte[] il, int offset) => type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
            _ => 4,
        };

        public static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                return partial.Types.OfType<Type>();
            }
        }

        private static string FieldName(FieldInfo field) =>
            field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;

        internal static string Name(Type type) => type.FullName ?? type.Name;

        private static string Format(Type type)
        {
            if (type.IsArray) return Format(type.GetElementType()!) + "[]";
            if (!type.IsGenericType) return type.Name;
            // A type nested in a generic type is generic too, with no arity of its own in its name.
            var arity = type.Name.IndexOf('`', StringComparison.Ordinal);
            var name = arity < 0 ? type.Name : type.Name[..arity];
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
        }
    }

    private sealed class FakeRoster
    {
        private readonly Dictionary<int, string> members = [];

        public void Add(int id, string name) => members[id] = name;
    }

    private sealed class FakeCounter(int start)
    {
        public int Next() => ++start;
    }

    private sealed class FakeClock(TimeProvider time)
    {
        public DateTimeOffset Now => time.GetUtcNow();
    }

    private static class FakeStatics
    {
        private static readonly HashSet<string> Seen = [];

        public static bool See(string name) => Seen.Add(name);
    }
}
