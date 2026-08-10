using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Dispatching;

public sealed record MessageHandlerRegistration(ClientMessageType MessageType, Type HandlerType);

public sealed class MessageHandlerRegistry
{
    private readonly IReadOnlyDictionary<ClientMessageType, Type> registrations;

    public MessageHandlerRegistry(IEnumerable<MessageHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.MessageType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Client message type '{duplicate.Key}' was registered more than once.");
        }

        var missing = Enum.GetValues<ClientMessageType>()
            .Except(registrationList.Select(item => item.MessageType))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Client message types have no registered handler: {string.Join(", ", missing)}.");
        }

        this.registrations = registrationList.ToDictionary(item => item.MessageType, item => item.HandlerType);
    }

    public Type GetHandlerType(ClientMessageType messageType) => registrations[messageType];
}
