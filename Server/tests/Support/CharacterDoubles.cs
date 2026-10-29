using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Testing;

/// <summary>Hands out whatever character the test built, for services that only reach characters through the entity.</summary>
internal sealed class CharacterDomainStub(Func<CharacterId, ICharacter> characters) : ICharacterDomainService
{
    public ICharacter Get(CharacterId characterId) => characters(characterId);
}

/// <summary>Accepts and drops domain facts, for character tests that settle progression without a dispatcher.</summary>
internal sealed class DiscardingEventService : IEventService
{
    public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent => ValueTask.FromResult(Guid.NewGuid());

    public ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent => ValueTask.CompletedTask;

    public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience,
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : class, INotification<TNotification> =>
        throw new InvalidOperationException("This character test does not project notifications.");
}
