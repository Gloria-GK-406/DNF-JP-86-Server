using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Dispatching;

public enum MessageHandlerRouteKind { LegacyQueued, ProtocolCommand }
public interface IProtocolCommandMessageHandler<TCommand> : IClientMessageHandler where TCommand : class, IProtocolCommand;

/// <param name="IsTelemetry">
/// Pure telemetry (RQ-020): the handler only logs, so it runs off the gameplay executor. The registry
/// refuses a telemetry handler whose constructors inject anything but logging.
/// </param>
public sealed record MessageHandlerRegistration(ClientMessageType MessageType, Type HandlerType, bool IsTelemetry = false,
    MessageHandlerRouteKind Route = MessageHandlerRouteKind.LegacyQueued, Type? MessageClrType = null, Type? ProtocolCommandType = null);

/// <summary>Where a registered message goes: its handler, whether it is telemetry, and its inbound CMD type.</summary>
public sealed record MessageHandlerRoute(Type HandlerType, bool IsTelemetry, string CommandType,
    MessageHandlerRouteKind Kind = MessageHandlerRouteKind.LegacyQueued, Type? MessageClrType = null, Type? ProtocolCommandType = null);

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
        IEnumerable<ClientMessageProductionRegistration> productions,
        IEnumerable<CommandHandlerRegistration>? commands = null)
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

        var commandList = (commands ?? []).Select(item => item.Freeze()).ToArray();
        foreach (var item in registrationList)
        {
            if (!Enum.IsDefined(item.Route)) throw new InvalidOperationException("Unknown message route.");
            if (item.Route != MessageHandlerRouteKind.ProtocolCommand) continue;
            if (item.IsTelemetry || item.MessageClrType is null || item.ProtocolCommandType is null)
                throw new InvalidOperationException("A protocol route requires a typed message and command.");
            var match = commandList.Where(x => x.CommandType == item.ProtocolCommandType).ToArray();
            if (match.Length != 1 || match[0].Route != CommandRouteKind.Protocol
                || match[0].ProtocolMessages?.Any(x => x.MessageType == item.MessageType && x.MessageClrType == item.MessageClrType) != true)
                throw new InvalidOperationException("The message and protocol command registrations do not agree.");
            if (!typeof(IProtocolCommandMessageHandler<>).MakeGenericType(item.ProtocolCommandType).IsAssignableFrom(item.HandlerType)
                || MessageClrTypeOf(item.HandlerType) != item.MessageClrType)
                throw new InvalidOperationException("The thin handler has an incompatible message or command binding.");
            if (item.HandlerType.GetConstructors().SelectMany(x => x.GetParameters()).Any(x =>
                    x.ParameterType != typeof(ICommandBus) && !IsLogger(x.ParameterType)))
                throw new InvalidOperationException("A protocol command handler may inject only the command bus and logging.");
        }

        foreach (var command in commandList.Where(x => x.Route == CommandRouteKind.Protocol))
            foreach (var binding in command.ProtocolMessages ?? [])
                if (registrationList.Count(x => x.Route == MessageHandlerRouteKind.ProtocolCommand
                    && x.ProtocolCommandType == command.CommandType && x.MessageType == binding.MessageType
                    && x.MessageClrType == binding.MessageClrType) != 1)
                    throw new InvalidOperationException("A protocol command has no unique thin message route.");

        this.registrations = registrationList.ToDictionary(
            item => item.MessageType,
            item => new MessageHandlerRoute(item.HandlerType, item.IsTelemetry, CommandTypeOf(item.MessageType),
                item.Route, item.MessageClrType, item.ProtocolCommandType));
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

    private static Type? MessageClrTypeOf(Type handler)
    {
        for (var type = handler; type is not null; type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ClientMessageHandler<>))
                return type.GetGenericArguments()[0];
        return null;
    }

    private static bool IsLogger(Type type) => type == typeof(ILogger)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
}
