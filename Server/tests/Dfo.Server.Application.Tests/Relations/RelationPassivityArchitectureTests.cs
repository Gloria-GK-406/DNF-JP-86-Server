using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Relations;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Relations;

/// <summary>The relation service only keeps relations; telling anyone about a change is its caller's job.</summary>
/// <remarks>
/// A relation service that could send or raise events would have two places deciding who hears about
/// a change, and the protocol paths that already push would push twice. So its constructor may take
/// exactly the store, the identity read, the relation rules, the transaction accessor and the clock,
/// and nothing it reaches may be a way to notify. Each rule has a case that feeds it a violation, so a rule that lets
/// everything through cannot pass for a clean repository.
/// </remarks>
public sealed class RelationPassivityArchitectureTests
{
    private static readonly Type[] Allowed =
    [
        typeof(ICharacterRelationDomainStore), typeof(ICharacterIdentityQueryDomainService),
        typeof(ICharacterRelationRuleService), typeof(IDomainTransactionAccessor), typeof(TimeProvider),
    ];

    /// <summary>Ways to tell someone something: events, notifications, projections, outboxes and senders.</summary>
    private static readonly Type[] Announcing =
    [
        typeof(IEventService), typeof(INotificationBus), typeof(ICommandProjections),
        typeof(ICommandMessageOutbox), typeof(IOutboundSender), typeof(INetworkMessageSender),
    ];

    private static IReadOnlyList<string> ConstructorViolations(Type implementation) =>
        ServiceGraph.Parameters(implementation).Where(x => !Allowed.Contains(x))
            .Select(x => $"{implementation.Name} takes {x.Name}").ToArray();

    [Fact]
    public void The_relation_service_takes_only_its_store_the_identity_read_its_rules_the_transaction_and_the_clock() =>
        Assert.Empty(ConstructorViolations(typeof(CharacterRelationDomainService)));

    [Fact]
    public void The_constructor_rule_refuses_a_service_that_can_announce() =>
        Assert.Equal(["AnnouncingRelations takes IEventService", "AnnouncingRelations takes INotificationBus",
                "AnnouncingRelations takes ICommandProjections"],
            ConstructorViolations(typeof(AnnouncingRelations)));

    /// <summary>What <paramref name="root"/> reaches through the container, not passing through the transaction.</summary>
    /// <remarks>
    /// The transaction accessor is a leaf here. Behind it the use case's transaction opens a unit of
    /// the job's outbox, so every writer reaches the sender that way; that outbox is the caller's,
    /// settled by the caller's use case, and it is no way for the relation service to say anything.
    /// </remarks>
    private static IReadOnlyList<string> ReachViolations(ServiceGraph graph, Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            if (!seen.Add(type) || type == typeof(IDomainTransactionAccessor)) continue;
            foreach (var next in graph.Next(type)) queue.Enqueue(next);
        }
        return Announcing.Where(seen.Contains).Select(x => $"{root.Name} reaches {x.Name}").ToArray();
    }

    [Fact]
    public void Neither_relation_face_reaches_a_way_to_announce()
    {
        using var production = new ServiceGraph.Production();
        Assert.Empty(ReachViolations(production.Graph, typeof(ICharacterRelationDomainService)));
        Assert.Empty(ReachViolations(production.Graph, typeof(ICharacterRelationQueryDomainService)));
        // Without the leaf the transaction's outbox would be reached, which is what makes the leaf a choice.
        Assert.Contains(typeof(IOutboundSender), production.Graph.Reach(typeof(ICharacterRelationDomainService)));
    }

    [Fact]
    public void The_reach_rule_finds_an_announcement_behind_the_identity_read()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICharacterIdentityQueryDomainService, AnnouncingIdentities>();
        services.AddSingleton<ICharacterRelationRuleService>(TestRequired.Absent<ICharacterRelationRuleService>());
        services.AddSingleton(TestRequired.Absent<IEventService>());
        using var provider = services.BuildServiceProvider();
        Assert.Equal(["CharacterRelationDomainService reaches IEventService"],
            ReachViolations(new ServiceGraph(services, provider), typeof(CharacterRelationDomainService)));
    }

    [Fact]
    public void Both_relation_faces_resolve_to_one_instance_in_the_production_modules()
    {
        using var production = new ServiceGraph.Production();
        var writer = production.Provider.GetRequiredService<ICharacterRelationDomainService>();
        Assert.IsType<CharacterRelationDomainService>(writer);
        Assert.Same(writer, production.Provider.GetRequiredService<ICharacterRelationQueryDomainService>());
    }

    private sealed class AnnouncingRelations(ICharacterRelationDomainStore store, IEventService events,
        INotificationBus notifications, ICommandProjections projections, TimeProvider clock)
    {
        public object[] Held => [store, events, notifications, projections, clock];
    }

    private sealed class AnnouncingIdentities(IEventService events) : ICharacterIdentityQueryDomainService
    {
        public object Held => events;

        public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<Dfo.Server.Data.Characters.CharacterId, CharacterPublicIdentity>> ReadAsync(
            IReadOnlyCollection<Dfo.Server.Data.Characters.CharacterId> characters, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
