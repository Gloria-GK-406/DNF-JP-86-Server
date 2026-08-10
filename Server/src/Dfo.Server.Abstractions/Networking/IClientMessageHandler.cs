using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

public interface IClientMessageHandler
{
    bool CanHandle(IClientNetworkMessage message);

    ValueTask HandleAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken);
}

public abstract class ClientMessageHandler<TMessage> : IClientMessageHandler
    where TMessage : class, IClientNetworkMessage
{
    public bool CanHandle(IClientNetworkMessage message) => message is TMessage;

    public ValueTask HandleAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not TMessage typedMessage)
        {
            throw new InvalidOperationException(
                $"Handler '{GetType().Name}' cannot consume message CLR type '{message.GetType().Name}'.");
        }

        return HandleMessageAsync(context, typedMessage, cancellationToken);
    }

    protected abstract ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        TMessage message,
        CancellationToken cancellationToken);
}
