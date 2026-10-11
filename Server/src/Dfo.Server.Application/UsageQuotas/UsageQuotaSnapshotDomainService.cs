using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.UsageQuotas;

internal sealed class UsageQuotaSnapshotDomainService(IUsageQuotaDomainService quotas,
    IItemUsageDomainService items, IPremiumServiceDomainService premiums,
    IPremiumServiceUsageDomainService premiumUsage, TimeProvider clock, IDimensionDungeonDomainService dimensions,
    IDespairTowerDomainService despair) : IUsageQuotaSnapshotDomainService
{
    public async ValueTask<UsageQuotaSnapshot> ReadAsync(AccountId accountId, CharacterId characterId,
        UsageQuotaFamily family, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        return family switch
        {
            UsageQuotaFamily.DespairTower => new(at, [], null, [], null, Despair:
                await despair.ReadStatusAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.DimensionDungeon => new(at, [], null, [], null,
                await dimensions.ReadQuotaAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.Consumable => new(at,
                await items.GetSnapshotAtAsync(characterId, at, cancellationToken).ConfigureAwait(false), null, [], null),
            UsageQuotaFamily.Premium => new(at, [],
                new(await premiums.For(accountId).ReadAtAsync(at, cancellationToken).ConfigureAwait(false)),
                await premiumUsage.ReadAtAsync(characterId, at, cancellationToken).ConfigureAwait(false), null),
            UsageQuotaFamily.MirrorDungeon => new(at, [], null, [],
                await quotas.ReadMirrorAsync(characterId,
                    DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(8)).DateTime), cancellationToken).ConfigureAwait(false)),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }
}

internal sealed class UsageQuotaNotificationSubscriber(IEventService events) : IDomainEventSubscriber<UsageQuotaChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<UsageQuotaChanged> message, CancellationToken cancellationToken)
    {
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new UsageQuotaViewChanged(message.Payload.CharacterId, message.Payload.Family), cancellationToken).ConfigureAwait(false);
    }
}
