using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Dispatching;

public sealed record MessageHandlerRegistration(ClientMessageType MessageType, Type HandlerType);

/// <summary>
/// Maps a client message type to the handler that consumes it.
/// </summary>
/// <remarks>
/// The catalogue this keys on is the server's own register of protocols, and it is deliberately
/// larger than the set that is wired: a member may be catalogued long before a decoder or a
/// handler exists for it. Requiring every member to have a handler would therefore be requiring
/// the catalogue to stop being a catalogue.
///
/// What is still checked is closure over what was actually wired. Every message type a registered
/// decoder declares it can produce must have a handler here, because a produced message with no
/// handler is a message that decodes successfully and then falls on the floor. That gap is a
/// wiring mistake, and it is found at startup rather than when the client first sends it.
/// </remarks>
public sealed class MessageHandlerRegistry
{
    private readonly IReadOnlyDictionary<ClientMessageType, Type> registrations;

    public MessageHandlerRegistry(
        IEnumerable<MessageHandlerRegistration> registrations,
        IEnumerable<ClientMessageProductionRegistration> productions)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(productions);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.MessageType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Client message type '{duplicate.Key}' was registered more than once.");
        }

        var handledTypes = registrationList.Select(item => item.MessageType).ToHashSet();
        // Every gap is reported at once: with a protocol catalogue this size, surfacing them one
        // startup at a time would turn wiring up a batch of protocols into a guessing loop.
        var gaps = productions
            .Where(production => !handledTypes.Contains(production.MessageType))
            .Select(production => $"'{production.MessageType}' (from '{production.DecoderType.Name}')")
            .ToArray();
        if (gaps.Length > 0)
        {
            throw new InvalidOperationException(
                "Decoders produce client message types that have no registered handler: " +
                $"{string.Join(", ", gaps)}.");
        }

        this.registrations = registrationList.ToDictionary(item => item.MessageType, item => item.HandlerType);
    }

    public Type GetHandlerType(ClientMessageType messageType) => registrations[messageType];
}
