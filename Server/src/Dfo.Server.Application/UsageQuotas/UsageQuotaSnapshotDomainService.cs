using Dfo.Server.Abstractions.Configuration;
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
    ILostLandDungeonDomainService lostLand,
    IDespairTowerDomainService despair, IDailyResetRuleService daily, IHerasQuotaDomainService heras, IAltarDungeonDomainService? altars = null, ITournamentDungeonDomainService? tournaments = null, IGoldSilverHornDomainService? goldSilverHorn = null, IReviveCoinDomainService? reviveCoins = null) : IUsageQuotaSnapshotDomainService
{
    public async ValueTask<UsageQuotaSnapshot> ReadAsync(AccountId accountId, CharacterId characterId,
        UsageQuotaFamily family, CancellationToken cancellationToken)
    {
        var at = clock.GetUtcNow();
        return family switch
        {
            // Quest list/completion projections already publish this claim's visible effects.
            UsageQuotaFamily.GiantDragonCrystalExchange => new(at, [], null, [], null),
            UsageQuotaFamily.LostLand => new(at, [], null, [], null, LostLand:
                await lostLand.ReadQuotaAtAsync(characterId, at, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.ReviveCoin => new(at, [], null, [], null, ReviveCoinClaimed:
                await (reviveCoins ?? throw new InvalidOperationException("Revive coin quotas unavailable.")).ClaimedAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.GoldSilverHorn => new(at, [], null, [], null, GoldSilverHorn:
                await (goldSilverHorn ?? throw new InvalidOperationException("Gold/silver horn quotas unavailable.")).ReadQuotaAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.HerasDungeon => new(at, [], null, [], null, Heras:
                await heras.ReadQuotaAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.TournamentDungeon => new(at, [], null, [], null, Tournaments:
                await (tournaments ?? throw new InvalidOperationException("Tournament quotas unavailable.")).ReadQuotaAsync(characterId, cancellationToken).ConfigureAwait(false)),
            UsageQuotaFamily.AltarDungeon => new(at, [], null, [], null, Altars:
                await (altars ?? throw new InvalidOperationException("Altar quotas unavailable.")).ReadQuotaAsync(characterId, cancellationToken).ConfigureAwait(false)),
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
                    daily.PeriodDay(at), cancellationToken).ConfigureAwait(false)),
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
