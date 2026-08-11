namespace Dfo.Server.Application.Commands;

public sealed record CommandHandlerRegistration(
    Type CommandType,
    Type HandlerType,
    Type InvokerType,
    CommandExecutionKind ExecutionKind,
    UserAccessMode OnlineUserAccessMode,
    UserAccessMode AccountAccessMode);

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
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.CommandType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Command '{duplicate.Key.Name}' was registered more than once.");
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
