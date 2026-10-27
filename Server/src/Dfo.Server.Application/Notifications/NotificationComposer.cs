using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Notifications;

/// <summary>Asks a handler for one recipient's messages without naming its notification type.</summary>
/// <remarks>
/// Delivery walks recipients and is not generic. The registration binds the notification, its handler and this
/// composer together, so the cast below is the concrete type the registration was made with.
/// </remarks>
internal interface INotificationComposer
{
    ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        object notification,
        CancellationToken cancellationToken);
}

internal sealed class NotificationComposer<TNotification>(
    INotificationHandler<TNotification> handler) : INotificationComposer
    where TNotification : class, INotification<TNotification>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient,
        object notification,
        CancellationToken cancellationToken) =>
        handler.ComposeAsync(recipient, (TNotification)notification, cancellationToken);
}
