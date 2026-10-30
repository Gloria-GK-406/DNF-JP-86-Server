using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Abstractions.World
{
    /// <summary>
    /// Test-only: every narrow dungeon run interface at once. Production code injects the narrow interface it uses
    /// and has no aggregate; tests drive a whole run through one object.
    /// </summary>
    public interface IDungeonRunDomainService :
        IDungeonRunQueryDomainService,
        IDungeonRunEntryDomainService,
        IDungeonRunCombatDomainService,
        IDungeonRunParticipantDomainService,
        IDungeonRunGroundDropDomainService,
        IDungeonRunSettlementDomainService,
        IDungeonRunCardDomainService,
        IDungeonRunTimerDomainService,
        IDungeonRunDepartureDomainService;
}

namespace Dfo.Server.Testing
{
    /// <summary>Routes each call of the test aggregate to the run service behind the interface that declares it.</summary>
    public class DungeonRunServicesProxy : DispatchProxy
    {
        internal Func<Type, object> Resolve { get; set; } = _ => throw new InvalidOperationException("Unbound run services.");

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            var target = Resolve(targetMethod.DeclaringType!);
            try { return targetMethod.Invoke(target, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error.InnerException);
                throw;
            }
        }
    }

    internal static class DungeonRunTestComposition
    {
        /// <summary>The test aggregate over run services resolved by narrow interface.</summary>
        public static IDungeonRunDomainService Compose(Func<Type, object> resolve)
        {
            var runs = DispatchProxy.Create<IDungeonRunDomainService, DungeonRunServicesProxy>();
            ((DungeonRunServicesProxy)(object)runs).Resolve = resolve;
            return runs;
        }

        /// <summary>The test aggregate a provider exposes: one a test registered, or the production run services.</summary>
        public static IDungeonRunDomainService DungeonRuns(this IServiceProvider provider) =>
            provider.GetService<IDungeonRunDomainService>() ?? Compose(provider.GetRequiredService);

        /// <summary>Registers the test aggregate over the run services already registered by narrow interface.</summary>
        public static IServiceCollection AddComposedDungeonRuns(this IServiceCollection services)
        {
            services.TryAddSingleton<IDungeonRunDomainService>(sp => Compose(sp.GetRequiredService));
            return services;
        }

        /// <summary>Registers a test aggregate and points every narrow run interface at it, replacing the production ones.</summary>
        public static IServiceCollection AddDungeonRunsAs(this IServiceCollection services, IDungeonRunDomainService runs) =>
            services.AddSingleton(runs).AddDungeonRunViews();

        /// <inheritdoc cref="AddDungeonRunsAs(IServiceCollection, IDungeonRunDomainService)"/>
        public static IServiceCollection AddDungeonRunsAs(this IServiceCollection services,
            Func<IServiceProvider, IDungeonRunDomainService> factory) =>
            services.AddSingleton(factory).AddDungeonRunViews();

        /// <summary>Points every narrow run interface at the registered test aggregate; the last registration wins.</summary>
        public static IServiceCollection AddDungeonRunViews(this IServiceCollection services)
        {
            services.AddSingleton<IDungeonRunQueryDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunEntryDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunCombatDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunParticipantDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunGroundDropDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunSettlementDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunCardDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunTimerDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            services.AddSingleton<IDungeonRunDepartureDomainService>(sp => sp.GetRequiredService<IDungeonRunDomainService>());
            return services;
        }

        /// <summary>The real run service behind a composed aggregate.</summary>
        public static T Service<T>(this IDungeonRunDomainService runs) where T : class =>
            runs is DungeonRunServicesProxy proxy ? (T)proxy.Resolve(typeof(T)) : runs as T
                ?? throw new InvalidOperationException($"{typeof(T).Name} is not behind this run aggregate.");

        // Overloads the services keep for tests only; they are not on the narrow interfaces.
        public static DungeonRunState? TryMove(this IDungeonRunDomainService runs, CharacterId characterId, MazeCell target) =>
            ((DungeonRunStore.EntryService)runs.Service<IDungeonRunEntryDomainService>()).TryMove(characterId, target);

        public static DungeonRunState? TrySettle(this IDungeonRunDomainService runs, CharacterId characterId) =>
            ((DungeonRunStore.SettlementService)runs.Service<IDungeonRunSettlementDomainService>()).TrySettle(characterId);

        public static bool End(this IDungeonRunDomainService runs, CharacterId characterId) =>
            ((DungeonRunStore.DepartureService)runs.Service<IDungeonRunDepartureDomainService>()).End(characterId);

        public static DungeonRunState? FixClearRewardCards(this IDungeonRunDomainService runs, CharacterId characterId,
            ClearRewardCardFace free, ClearRewardCardFace paid, int? paidCost) =>
            ((DungeonRunStore.CardService)runs.Service<IDungeonRunCardDomainService>()).FixClearRewardCards(characterId, free, paid, paidCost);

        /// <summary>A store and its run services built by hand, behind one test aggregate.</summary>
        public static IDungeonRunDomainService Build(
            IDomainTransactionAccessor transactions, IPartyService parties, IWeaknessDomainService weakness,
            ICreatureRuntimeDomainService creatures, IDungeonLifecycleDomainService lifecycle, TimeProvider clock,
            IDungeonRoomMechanismRuleService mechanismRules, IDungeonDeadlineWake deadlines,
            ISealedTreasureRuleService sealedTreasureRules, IDungeonParticipationDomainService participation,
            IGiantDragonRuleService giantDragons)
        {
            var store = new DungeonRunStore(parties, deadlines, participation, weakness, creatures);
            var services = new Dictionary<Type, object>
            {
                [typeof(IDungeonRunQueryDomainService)] = store,
                [typeof(IDungeonRunEntryDomainService)] = new DungeonRunStore.EntryService(store,
                    NullLogger<DungeonRunStore.EntryService>.Instance, transactions, parties, participation, weakness, creatures,
                    clock, mechanismRules, sealedTreasureRules, deadlines),
                [typeof(IDungeonRunCombatDomainService)] = new DungeonRunStore.CombatService(store,
                    NullLogger<DungeonRunStore.CombatService>.Instance, transactions, clock, mechanismRules, sealedTreasureRules,
                    deadlines, giantDragons, new LostLandFeverRuleService()),
                [typeof(IDungeonRunParticipantDomainService)] = new DungeonRunStore.ParticipantService(store, transactions),
                [typeof(IDungeonRunGroundDropDomainService)] = new DungeonRunStore.GroundDropService(store, transactions, clock,
                    sealedTreasureRules),
                [typeof(IDungeonRunSettlementDomainService)] = new DungeonRunStore.SettlementService(store, clock,
                    new DungeonSettlementRuleService()),
                [typeof(IDungeonRunCardDomainService)] = new DungeonRunStore.CardService(store, transactions),
                [typeof(IDungeonRunTimerDomainService)] = new DungeonRunStore.TimerService(store, transactions, weakness, lifecycle,
                    mechanismRules),
                [typeof(IDungeonRunDepartureDomainService)] = new DungeonRunStore.DepartureService(store, weakness, lifecycle),
            };
            return Compose(type => services[type]);
        }
    }
}
