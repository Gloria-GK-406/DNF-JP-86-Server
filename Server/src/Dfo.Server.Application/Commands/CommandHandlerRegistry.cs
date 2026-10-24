using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Commands;

public enum CommandRouteKind { LegacyFineGrained, Protocol, Internal }
public sealed record ProtocolCommandMessageBinding(ClientMessageType MessageType, Type MessageClrType);

public sealed record CommandHandlerRegistration(
    Type CommandType,
    Type HandlerType,
    Type InvokerType,
    CommandExecutionKind ExecutionKind,
    CommandRouteKind Route = CommandRouteKind.LegacyFineGrained,
    IReadOnlyList<ProtocolCommandMessageBinding>? ProtocolMessages = null,
    IReadOnlyCollection<CommandRegistrationExit>? RegistrationExits = null)
{
    internal CommandHandlerRegistration Freeze() => ProtocolMessages is null && RegistrationExits is null ? this : this with
    { ProtocolMessages = ProtocolMessages is null ? null : Array.AsReadOnly(ProtocolMessages.ToArray()),
      RegistrationExits = RegistrationExits is null ? null : Array.AsReadOnly(RegistrationExits.ToArray()) };
}

/// <summary>
/// Maps a command type to how it is executed.
/// </summary>
/// <remarks>
/// Commands are keyed by their own CLR type. There is no completeness check: unlike a
/// client message, which arrives off the wire and must always find a handler, a command
/// only exists because code constructed it, so an unregistered one is a problem at
/// dispatch rather than at startup.
/// </remarks>
public sealed class CommandHandlerRegistry
{
    private readonly IReadOnlyDictionary<Type, CommandHandlerRegistration> registrations;

    public CommandHandlerRegistry(IEnumerable<CommandHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var registrationList = registrations.Select(item => item.Freeze()).ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.CommandType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Command '{duplicate.Key.Name}' was registered more than once.");
        }

        foreach (var item in registrationList)
        {
            if (!Enum.IsDefined(item.Route)) throw new InvalidOperationException("Unknown command route.");
            var protocol = typeof(IProtocolCommand).IsAssignableFrom(item.CommandType);
            if (protocol != (item.Route == CommandRouteKind.Protocol))
                throw new InvalidOperationException($"Command '{item.CommandType.Name}' has an incompatible source route.");
            if (item.Route == CommandRouteKind.Protocol)
            {
                if (item.ProtocolMessages is not { Count: > 0 } bindings
                    || bindings.Select(x => x.MessageType).Distinct().Count() != bindings.Count
                    || bindings.Any(x => !typeof(IClientNetworkMessage).IsAssignableFrom(x.MessageClrType)))
                    throw new InvalidOperationException("A protocol command requires unique typed message bindings.");
                if (item.RegistrationExits is { Count: > 0 } exits
                    && (exits.Any(exit => !Enum.IsDefined(exit)) || exits.Distinct().Count() != exits.Count
                        || bindings.Any(x => !typeof(IEffectSettlementRequest).IsAssignableFrom(x.MessageClrType))))
                    throw new InvalidOperationException("Registration exits require compatible effect bindings.");
            }
            else if (item.ProtocolMessages is { Count: > 0 } || item.RegistrationExits is { Count: > 0 })
                throw new InvalidOperationException("Only a protocol command may bind messages.");
        }
        this.registrations = registrationList.ToDictionary(item => item.CommandType);
    }

    public CommandHandlerRegistration GetRegistration(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);
        return registrations.TryGetValue(commandType, out var registration)
            ? registration
            : throw new InvalidOperationException(
                $"Command '{commandType.Name}' has no registered handler.");
    }
}
