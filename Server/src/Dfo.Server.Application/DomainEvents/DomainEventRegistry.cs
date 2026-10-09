using System.Text.Json;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.DomainEvents;

internal interface IDomainEventRegistration
{
    Type PayloadType { get; }
    string Name { get; }
    int Version { get; }
    string Serialize(object payload);
    object Rehydrate(StoredDomainEvent stored);
}

internal sealed record DomainEventRegistration<TEvent>(string Name, int Version) : IDomainEventRegistration
    where TEvent : class, IDomainEvent
{
    public Type PayloadType => typeof(TEvent);
    public string Serialize(object payload) => JsonSerializer.Serialize((TEvent)payload);
    public object Rehydrate(StoredDomainEvent stored) => new DomainEventEnvelope<TEvent>(
        stored.EventId, stored.BatchId, stored.OccurredAt, stored.OriginInstanceId,
        JsonSerializer.Deserialize<TEvent>(stored.PayloadJson)
            ?? throw new InvalidDataException("Domain event payload is null."));
}

internal sealed record DomainSubscriberRegistration(
    Type PayloadType, Type HandlerType, string SubscriberId, DomainEventDeliveryPolicy Policy,
    Func<object, string?> CoalescingKey,
    Func<IServiceProvider, object, CancellationToken, ValueTask> Invoke);

internal sealed record RuntimeSubscriberRegistration(Type PayloadType, Type HandlerType,
    Func<IServiceProvider, object, CancellationToken, ValueTask> Invoke);

internal sealed class DomainEventRegistry
{
    private readonly Dictionary<Type, IDomainEventRegistration> types = [];
    private readonly Dictionary<string, IDomainEventRegistration> names = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type, string), DomainSubscriberRegistration> mappings = [];
    private readonly Dictionary<Type, RuntimeSubscriberRegistration[]> runtime;
    public IReadOnlyDictionary<string, DomainSubscriberRegistration[]> Groups { get; }

    public DomainEventRegistry(IEnumerable<IDomainEventRegistration> events,
        IEnumerable<DomainSubscriberRegistration> subscribers,
        IEnumerable<RuntimeSubscriberRegistration> runtimeSubscribers)
    {
        foreach (var registration in events)
        {
            ValidatePayloadType(registration.PayloadType);
            if (!types.TryAdd(registration.PayloadType, registration) || !names.TryAdd(registration.Name, registration))
                throw new InvalidOperationException($"Duplicate domain event type or stable name '{registration.Name}'.");
        }
        var grouped = new Dictionary<string, List<DomainSubscriberRegistration>>(StringComparer.Ordinal);
        foreach (var registration in subscribers)
        {
            if (!types.ContainsKey(registration.PayloadType))
                throw new InvalidOperationException($"Subscriber '{registration.SubscriberId}' has no durable event registration.");
            if (!mappings.TryAdd((registration.PayloadType, registration.SubscriberId), registration))
                throw new InvalidOperationException($"Duplicate subscription '{registration.SubscriberId}' for '{registration.PayloadType.Name}'.");
            if (registration.Policy == DomainEventDeliveryPolicy.Reliable
                && registration.HandlerType.GetConstructors().SelectMany(c => c.GetParameters())
                    .Any(p => p.ParameterType == typeof(Dfo.Server.Application.Commands.ICommandBus)))
                throw new InvalidOperationException($"Reliable subscriber '{registration.SubscriberId}' cannot reenter the command pipeline while holding database admission.");
            if (!grouped.TryGetValue(registration.SubscriberId, out var group))
                grouped.Add(registration.SubscriberId, group = []);
            if (group.Count != 0 && (group[0].Policy != registration.Policy || group[0].HandlerType != registration.HandlerType))
                throw new InvalidOperationException($"Subscriber group '{registration.SubscriberId}' must use one handler type and delivery policy.");
            group.Add(registration);
        }
        Groups = grouped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        var runtimeItems = runtimeSubscribers.ToArray();
        foreach (var registration in runtimeItems) ValidatePayloadType(registration.PayloadType);
        if (runtimeItems.GroupBy(r => (r.PayloadType, r.HandlerType)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Duplicate runtime event subscription.");
        runtime = runtimeItems.GroupBy(r => r.PayloadType).ToDictionary(g => g.Key, g => g.ToArray());
    }

    public IDomainEventRegistration For(Type type) => types.TryGetValue(type, out var item)
        ? item : throw new InvalidOperationException($"Domain event '{type.Name}' is not registered.");
    public IDomainEventRegistration For(string name, int version) =>
        names.TryGetValue(name, out var item) && item.Version == version
            ? item : throw new InvalidDataException($"Unknown domain event '{name}' version {version}; delivery retained.");
    public DomainSubscriberRegistration[] SubscribersFor(Type type) =>
        mappings.Values.Where(r => r.PayloadType == type).ToArray();
    public DomainSubscriberRegistration SubscriberFor(Type type, string subscriberId) =>
        mappings.TryGetValue((type, subscriberId), out var item)
            ? item : throw new InvalidDataException($"Unknown subscription '{subscriberId}' for '{type.Name}'.");
    public RuntimeSubscriberRegistration[] RuntimeFor(Type type) => runtime.TryGetValue(type, out var items)
        ? items : throw new InvalidOperationException($"Runtime event '{type.Name}' has no registered subscriber.");

    private static void ValidatePayloadType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type == typeof(IDomainEvent)
            || typeof(IServerNetworkMessage).IsAssignableFrom(type)
            || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotification<>)))
            throw new InvalidOperationException($"'{type.Name}' is not a concrete business event payload.");
        ValidateDataShape(type, []);
    }

    private static void ValidateDataShape(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type)) return;
        if (type == typeof(object) || type == typeof(Type) || typeof(Delegate).IsAssignableFrom(type)
            || typeof(IServiceProvider).IsAssignableFrom(type) || typeof(Task).IsAssignableFrom(type)
            || typeof(IServerNetworkMessage).IsAssignableFrom(type)
            || type.Name is "SelectedSessionIdentity" or "NetworkConnectionContext"
            || type.Namespace?.StartsWith("Dfo.Server.Data.Networking", StringComparison.Ordinal) == true
            || type.GetInterfaces().Append(type).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotification<>))
            || type.IsInterface && (type.Name.EndsWith("Service", StringComparison.Ordinal)
                || type.Name.EndsWith("Store", StringComparison.Ordinal) || type.Name.EndsWith("Repository", StringComparison.Ordinal)
                || type.Name.EndsWith("Persistence", StringComparison.Ordinal) || type.Name.EndsWith("Provider", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Domain event payload contains live capability or protocol type '{type.Name}'.");
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)) return;
        if (type.IsArray) { ValidateDataShape(type.GetElementType()!, visited); return; }
        foreach (var argument in type.GetGenericArguments()) ValidateDataShape(argument, visited);
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) return;
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            if (property.GetIndexParameters().Length == 0) ValidateDataShape(property.PropertyType, visited);
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            ValidateDataShape(field.FieldType, visited);
    }
}
