using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Dispatching;

/// <param name="IsTelemetry">
/// Pure telemetry (RQ-020): the handler only logs, so it runs off the gameplay executor. The registry
/// refuses a telemetry handler whose constructors inject anything but logging.
/// </param>
public sealed record MessageHandlerRegistration(ClientMessageType MessageType, Type HandlerType, bool IsTelemetry = false);

/// <summary>Where a registered message goes: its handler, whether it is telemetry, and its inbound CMD type.</summary>
public sealed record MessageHandlerRoute(Type HandlerType, bool IsTelemetry, string CommandType);

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
    private readonly IReadOnlyDictionary<ClientMessageType, MessageHandlerRoute> registrations;

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

        var injectsMoreThanLogging = registrationList
            .Where(item => item.IsTelemetry)
            .SelectMany(item => NonLoggingParameters(item.HandlerType)
                .Select(parameter => $"'{item.HandlerType.Name}' for '{item.MessageType}' injects '{parameter}'"))
            .ToArray();
        if (injectsMoreThanLogging.Length > 0)
        {
            throw new InvalidOperationException(
                "Telemetry handlers run off the gameplay executor and may only inject logging: " +
                $"{string.Join(", ", injectsMoreThanLogging)}.");
        }

        this.registrations = registrationList.ToDictionary(
            item => item.MessageType,
            item => new MessageHandlerRoute(item.HandlerType, item.IsTelemetry, CommandTypeOf(item.MessageType)));
    }

    public Type GetHandlerType(ClientMessageType messageType) => registrations[messageType].HandlerType;

    public bool TryGet(ClientMessageType messageType, [MaybeNullWhen(false)] out MessageHandlerRoute route) =>
        registrations.TryGetValue(messageType, out route);

    /// <summary>The CMD type an inbound message runs as on the executor.</summary>
    public static string CommandTypeOf(ClientMessageType messageType) => $"inbound:{messageType}";

    private static IEnumerable<string> NonLoggingParameters(Type handlerType) => handlerType.GetConstructors()
        .SelectMany(constructor => constructor.GetParameters())
        .Where(parameter => !IsLogger(parameter.ParameterType))
        .Select(parameter => parameter.ParameterType.Name)
        .Distinct();

    private static bool IsLogger(Type type) => type == typeof(ILogger)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
}
