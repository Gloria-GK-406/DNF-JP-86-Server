using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Notifications;

/// <summary>Who one notification is for, said in characters.</summary>
/// <remarks>
/// A value rather than a query result. The same audience resolves to different recipients at
/// different moments, and that is the point: a caller names the people it means, and who is
/// reachable at the moment of sending is answered later, in one place.
///
/// Characters, never connections. A domain service that wants a group told something says which
/// characters and stops there; nothing above the notification service needs to know a socket
/// exists.
///
/// The shapes below are siblings, each sealed, so no audience is two of them at once, and the
/// only constructor that makes one out of nothing is private, so writing a new shape the ordinary
/// way does not compile. That is what to expect of this type, and it is less than "closed" -- so
/// here is exactly where it stops.
///
/// C# requires a record that is not sealed to expose its copy constructor at least protected
/// (CS8878, the build fails if you try to hide it). A record elsewhere holding any audience can
/// therefore derive through it and add fields of its own -- including the case this design is
/// meant to rule out:
///
/// <code>
/// public sealed record CharactersAndAChannel(NotificationAudience Source, int ListenerPort)
///     : NotificationAudience(Source);
/// </code>
///
/// That compiles, and both halves read back. So this hierarchy does not make an illegal
/// combination unrepresentable the way a real union would; what it does is keep every shape a
/// caller writes ordinarily inside the list below, and make anything else a deliberate act.
/// Adding a shape here will not fail anyone else's build either -- a switch over a class
/// hierarchy is never exhaustive to the compiler.
///
/// Both gaps land in the same place, which is where the actual protection is: the final arm of
/// <see cref="INotificationAudienceResolver"/> names the shape it was not taught, instead of
/// resolving to nobody and looking like a quiet evening.
/// </remarks>
public abstract record NotificationAudience
{
    private NotificationAudience()
    {
    }

    /// <summary>These characters: whichever of them is reachable when the notification goes out.</summary>
    /// <remarks>
    /// One character is this shape with one element rather than a shape of its own. A separate
    /// singular shape would express nothing this cannot and would cost every resolver an arm.
    ///
    /// The list is taken as given, in the order given and without removing repeats. A character
    /// named twice is a caller naming them twice, and inventing a rule here would hide that.
    /// </remarks>
    public sealed record TheseCharacters(IReadOnlyList<CharacterId> CharacterIds) : NotificationAudience;
}

/// <summary>One character an audience resolved to, and the connection they are on right now.</summary>
public sealed record NotificationRecipient(
    CharacterId CharacterId,
    NetworkConnectionId ConnectionId);

/// <summary>Turns an audience into the people who can actually be reached.</summary>
/// <remarks>
/// Before this existed, a domain service that needed to know who to notify had to look a
/// connection up itself, which is how a service that only wanted to name three characters ended up
/// knowing what a socket is. Callers ask for an audience and get people; the question "which
/// connection is this character on" stays inside the notification service, here and in the bus,
/// which asks it again at each recipient's turn to see whether the answer still holds.
/// </remarks>
public interface INotificationAudienceResolver
{
    /// <summary>The reachable recipients this audience names, in the order it named them.</summary>
    IReadOnlyList<NotificationRecipient> Resolve(NotificationAudience audience);
}
