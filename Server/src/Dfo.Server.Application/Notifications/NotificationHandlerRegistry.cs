using System.Reflection;
using Dfo.Server.Abstractions.Notifications;

namespace Dfo.Server.Application.Notifications;

public sealed record NotificationHandlerRegistration(
    Type NotificationType,
    Type HandlerType,
    Type ComposerType);

/// <summary>An assembly whose notification types must all have a handler.</summary>
/// <remarks>
/// Declared rather than discovered. The set of assemblies that may define notifications is a
/// wiring fact, and searching everything loaded would make a test's throwaway notification into a
/// startup failure for the server.
/// </remarks>
public sealed record NotificationCatalogRegistration(Assembly Assembly);

/// <summary>
/// Maps a notification type to the handler that composes it, and refuses a catalogued
/// notification that has none.
/// </summary>
/// <remarks>
/// The command registry deliberately has no completeness check, on the grounds that a command only
/// exists because code constructed it, so an unregistered one is a problem at dispatch. That
/// reasoning does not carry over, and the difference is the point of this check: a command that
/// finds no handler throws into the request that asked for it, where somebody is waiting and will
/// see it. A notification is best effort by design -- nothing is required to arrive -- so an
/// unregistered one would be indistinguishable from an evening when nobody happened to be online.
/// It has to be found at startup, because it will not be found later.
///
/// Which is also why the gaps are reported together rather than one per boot: wiring a batch of
/// notifications should not become a guessing loop.
/// </remarks>
public sealed class NotificationHandlerRegistry
{
    private readonly IReadOnlyDictionary<Type, NotificationHandlerRegistration> registrations;

    public NotificationHandlerRegistry(
        IEnumerable<NotificationHandlerRegistration> registrations,
        IEnumerable<NotificationCatalogRegistration> catalogues)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(catalogues);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.NotificationType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Notification '{duplicate.Key.Name}' was registered more than once.");
        }

        var handled = registrationList.Select(item => item.NotificationType).ToHashSet();
        var gaps = catalogues
            .SelectMany(catalogue => catalogue.Assembly.GetTypes())
            .Where(IsNotification)
            .Where(type => !handled.Contains(type))
            .Select(type => $"'{type.Name}' (from '{type.Assembly.GetName().Name}')")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (gaps.Length > 0)
        {
            throw new InvalidOperationException(
                "Notifications are declared with no registered handler: " +
                $"{string.Join(", ", gaps)}.");
        }

        this.registrations = registrationList.ToDictionary(item => item.NotificationType);
    }

    public NotificationHandlerRegistration GetRegistration(Type notificationType)
    {
        ArgumentNullException.ThrowIfNull(notificationType);
        return registrations.TryGetValue(notificationType, out var registration)
            ? registration
            : throw new InvalidOperationException(
                $"Notification '{notificationType.Name}' has no registered handler.");
    }

    /// <remarks>
    /// Abstract is the only exclusion, and it covers interfaces too -- every interface reports
    /// itself abstract. A declaration nobody can construct is a declaration nobody can publish, so
    /// asking it to have a handler would refuse to boot over something that cannot be sent.
    /// </remarks>
    private static bool IsNotification(Type type) =>
        !type.IsAbstract
        && type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(INotification<>));
}
