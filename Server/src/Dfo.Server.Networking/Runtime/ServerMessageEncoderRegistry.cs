using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Runtime;

/// <summary>
/// Maps an outbound message type to its encoder and the endpoint that may receive it.
/// </summary>
/// <remarks>
/// The key space here is closed, unlike the inbound wire key space: every outbound message exists
/// because server code constructed it. An unregistered message is therefore a wiring mistake rather
/// than ordinary traffic, and the sender reports it as one instead of dropping it.
/// </remarks>
internal sealed class ServerMessageEncoderRegistry
{
    private readonly IReadOnlyDictionary<Type, Entry> encoders;

    public ServerMessageEncoderRegistry(
        IEnumerable<ServerMessageEncoderRegistration> registrations,
        IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(provider);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.MessageType)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Server message '{duplicate.Key.Name}' was registered more than once.");
        }

        encoders = registrationList.ToDictionary(
            item => item.MessageType,
            item => new Entry(
                (IServerMessageEncoder)provider.GetRequiredService(item.EncoderType),
                item.Endpoint));
    }

    public Entry? Find(Type messageType) =>
        encoders.TryGetValue(messageType, out var entry) ? entry : null;

    internal sealed record Entry(IServerMessageEncoder Encoder, NetworkEndpointKind Endpoint);
}
