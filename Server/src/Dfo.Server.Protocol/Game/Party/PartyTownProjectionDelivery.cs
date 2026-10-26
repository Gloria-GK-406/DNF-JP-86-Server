using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

public interface IPartyTownProjectionDelivery
{
    ValueTask RefreshObservedPartiesAsync(
        IReadOnlyList<SelectedSessionIdentity> recipients,
        CancellationToken cancellationToken);

    ValueTask RefreshMovedPartyAsync(
        SelectedSessionIdentity mover,
        PartyDirectoryScope? previousScope,
        CancellationToken cancellationToken);
}

public sealed class PartyTownProjectionDelivery : IPartyTownProjectionDelivery
{
    private readonly IPartyService parties;
    private readonly ITownAreaDomainService townAreas;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService streams;

    public PartyTownProjectionDelivery(IPartyService parties, ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions, IEventService notifications,
        PartyProjectionStreamRegistry streams)
        : this(parties, townAreas, selectedSessions, new LegacyCommandProjections(notifications), streams) { }

    internal PartyTownProjectionDelivery(IPartyService parties, ITownAreaDomainService townAreas,
        ISelectedSessionDomainService selectedSessions, ICommandProjections projections,
        IPartyProjectionStateDomainService streams)
    {
        this.parties = parties;
        this.townAreas = townAreas;
        this.selectedSessions = selectedSessions;
        this.projections = projections;
        this.streams = streams;
    }

    public ValueTask RefreshMovedPartyAsync(SelectedSessionIdentity mover,
        PartyDirectoryScope? previousScope, CancellationToken cancellationToken) =>
        PartyTownProjection.PartyTownRefreshMovedPartyAsync(selectedSessions, townAreas, projections, streams, parties, mover, previousScope, cancellationToken);

    public ValueTask RefreshObservedPartiesAsync(IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken cancellationToken) =>
        PartyTownProjection.PartyTownRefreshObservedPartiesAsync(townAreas, projections, streams, parties, recipients, cancellationToken);
}

internal readonly record struct PartyExactNotificationDeliveryResult(
    ImmutableArray<SelectedSessionIdentity> Registered,
    bool HasFailures);

internal static class PartyExactNotificationDelivery
{
    public static ValueTask<PartyExactNotificationDeliveryResult> PublishWithRetryAsync<T>(
        IEventService notifications, IEnumerable<SelectedSessionIdentity> intendedAudience,
        T notification, CancellationToken cancellationToken)
        where T : class, INotification<T> => PublishWithRetryAsync(
            new LegacyCommandProjections(notifications), intendedAudience, notification, cancellationToken);

    // Party handlers deliberately have no detached NotificationBus retry. The causal owner retries
    // only exact recipients that failed, so a town 0x0002 retry can still be followed by its 0x0009
    // and a successful recipient is never replayed merely because its neighbour failed.
    private const int MaximumAttempts = 2;

    public static async ValueTask<PartyExactNotificationDeliveryResult> PublishWithRetryAsync<
        TNotification>(
        ICommandProjections notifications,
        IEnumerable<SelectedSessionIdentity> intendedAudience,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : class, INotification<TNotification>
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(intendedAudience);
        ArgumentNullException.ThrowIfNull(notification);

        var exactAudience = intendedAudience
            .Distinct()
            .ToArray();
        var pending = exactAudience;
        var registered = new HashSet<SelectedSessionIdentity>();

        for (var attempt = 0; attempt < MaximumAttempts && pending.Length > 0; attempt++)
        {
            var result = await notifications.ProjectAsync(
                new NotificationAudience.TheseSessions(pending),
                notification,
                cancellationToken).ConfigureAwait(false);
            var registeredCharacters = result.With(NotificationOutcome.Registered).ToHashSet();
            foreach (var session in pending.Where(
                         session => registeredCharacters.Contains(session.CharacterId)))
            {
                registered.Add(session);
            }

            var failedCharacters = result.With(NotificationOutcome.Failed).ToHashSet();
            pending = pending
                .Where(session => failedCharacters.Contains(session.CharacterId))
                .ToArray();
        }

        return new PartyExactNotificationDeliveryResult(
            exactAudience.Where(registered.Contains).ToImmutableArray(),
            pending.Length > 0);
    }
}
