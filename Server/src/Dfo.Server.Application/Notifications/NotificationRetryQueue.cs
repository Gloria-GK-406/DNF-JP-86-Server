using System.Collections.Concurrent;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Notifications;

/// <summary>One recipient's second chance, and how many it has already used.</summary>
/// <remarks>
/// A current-character retry names only the character and follows its established replacement
/// semantics. An exact-session retry additionally keeps the expected generation so state frozen
/// for one session cannot follow the character to another. Neither keeps a composed message: the
/// handler is asked again so a retry never overlays a cached frame on a newer publication.
/// </remarks>
internal sealed record NotificationRetry(
    NotificationHandlerRegistration Registration,
    object Notification,
    CharacterId CharacterId,
    int Attempts,
    SelectedSessionIdentity? ExpectedSession = null);

/// <summary>The failed recipients waiting for another go.</summary>
/// <remarks>
/// In memory, and lost when the process stops. Nothing here is worth a database: a notification is
/// best effort, and a server that just restarted has bigger news for everyone than the frame it
/// was retrying.
/// </remarks>
internal sealed class NotificationRetryQueue
{
    private readonly ConcurrentQueue<NotificationRetry> waiting = new();

    public int Count => waiting.Count;

    /// <summary>Puts a recipient at the back, behind whatever is already waiting.</summary>
    public void Add(NotificationRetry entry) => waiting.Enqueue(entry);

    public bool TryTake(out NotificationRetry entry) => waiting.TryDequeue(out entry!);
}
