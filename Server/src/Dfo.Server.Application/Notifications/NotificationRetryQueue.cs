using System.Collections.Concurrent;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Notifications;

/// <summary>One recipient's second chance, and how many it has already used.</summary>
/// <remarks>
/// A character, not a connection, and not a composed message. Both omissions are the point: by the
/// time this is drained, where they are and what they should be told are both questions with newer
/// answers than the ones this entry could have carried. Keeping the frame would be the natural
/// thing to keep, and it is the one that quietly delivers an old value over a newer one -- the
/// first attempt fails, a second publish succeeds, and then the first retries on top of it.
/// </remarks>
internal sealed record NotificationRetry(
    NotificationHandlerRegistration Registration,
    object Notification,
    CharacterId CharacterId,
    int Attempts);

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
