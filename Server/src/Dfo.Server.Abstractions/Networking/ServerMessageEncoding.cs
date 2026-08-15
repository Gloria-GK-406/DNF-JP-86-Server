using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Abstractions.Networking;

/// <summary>Encodes one outbound message into the frames that carry it.</summary>
/// <remarks>
/// Encoders are keyed by the message's CLR type rather than by its catalogue member. An outbound
/// message is constructed by server code, so its type is statically known at the call site and no
/// dynamic key is needed -- the same reasoning that keys commands by their own type.
///
/// One message may need more than one frame, so the seam returns a list. The order it returns is
/// the order the frames go out.
/// </remarks>
public interface IServerMessageEncoder
{
    IReadOnlyList<byte[]> Encode(IServerNetworkMessage message);
}

/// <summary>
/// Base class that pins an encoder to the one message it encodes. Its shape matches
/// <see cref="ClientMessageHandler{TMessage}"/> on the inbound side.
/// </summary>
public abstract class ServerMessageEncoder<TMessage> : IServerMessageEncoder
    where TMessage : class, IServerNetworkMessage
{
    public IReadOnlyList<byte[]> Encode(IServerNetworkMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not TMessage typedMessage)
        {
            throw new InvalidOperationException(
                $"Encoder '{GetType().Name}' cannot encode message CLR type '{message.GetType().Name}'.");
        }

        return EncodeMessage(typedMessage);
    }

    protected abstract IReadOnlyList<byte[]> EncodeMessage(TMessage message);

    /// <summary>Convenience for the common case of a message that occupies exactly one frame.</summary>
    protected static IReadOnlyList<byte[]> One(byte[] frame) => [frame];
}

/// <summary>
/// Binds one outbound message type to its encoder and to the endpoint it may be sent to.
/// </summary>
/// <remarks>
/// The endpoint lives here rather than inside the encoder because "what bytes does this message
/// become" and "which endpoint may receive it" are separate facts. Keeping them separate lets the
/// endpoint rule be applied in one place and reviewed without reading any encoding logic.
/// </remarks>
public sealed record ServerMessageEncoderRegistration(
    Type MessageType,
    Type EncoderType,
    NetworkEndpointKind Endpoint);

public static class ServerMessageEncodingExtensions
{
    /// <summary>
    /// Registers the encoder for one outbound message. The two type arguments must agree: an
    /// encoder that does not encode this message does not compile.
    /// </summary>
    public static IServiceCollection AddServerMessageEncoder<TMessage, TEncoder>(
        this IServiceCollection services,
        NetworkEndpointKind endpoint)
        where TMessage : class, IServerNetworkMessage
        where TEncoder : ServerMessageEncoder<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TEncoder>();
        services.AddSingleton(new ServerMessageEncoderRegistration(
            typeof(TMessage),
            typeof(TEncoder),
            endpoint));
        return services;
    }
}
