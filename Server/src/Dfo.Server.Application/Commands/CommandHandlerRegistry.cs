namespace Dfo.Server.Application.Commands;

public sealed record CommandHandlerRegistration(
    ApplicationCommandType CommandType,
    Type HandlerType,
    CommandExecutionKind ExecutionKind,
    OnlineUserAccessMode OnlineUserAccessMode);

public sealed class CommandHandlerRegistry
{
    private readonly IReadOnlyDictionary<ApplicationCommandType, CommandHandlerRegistration> registrations;

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
                $"Application command type '{duplicate.Key}' was registered more than once.");
        }

        var missing = Enum.GetValues<ApplicationCommandType>()
            .Except(registrationList.Select(item => item.CommandType))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Application command types have no registered handler: {string.Join(", ", missing)}.");
        }

        this.registrations = registrationList.ToDictionary(item => item.CommandType);
    }

    public CommandHandlerRegistration GetRegistration(ApplicationCommandType commandType) =>
        registrations[commandType];
}
