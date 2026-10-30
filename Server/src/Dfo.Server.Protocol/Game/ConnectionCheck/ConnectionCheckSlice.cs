using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ConnectionCheck;

/// <summary>
/// CMD 0x04DD <c>CHECK_USER_CONNECTION</c>: the A21 client's periodic liveness probe on the Game
/// endpoint, sent with no body.
/// </summary>
/// <remarks>
/// The known-good session answers every one of the thirty probes it received with the same command
/// type and a one-byte body of <c>0x01</c>, which is the CMD acceptance envelope and nothing else.
/// The probe carries no domain meaning here: the connection's liveness is already the transport's
/// business, and the client asks only to be told the socket is still answering.
///
/// It keeps arriving whether or not it is answered -- the same session shows all thirty answered
/// and the probes continuing to the end -- so an unregistered type here does not stall the client
/// by itself. What it does produce is a repeating unhandled-frame warning that looks like noise,
/// which is how it stayed unnoticed. Its own place in the sequence is not first: in both observed
/// sessions the client sends LOGIN before its first probe.
/// </remarks>
public sealed record ConnectionCheckRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameConnectionCheck;
}

public sealed record ConnectionCheckResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameConnectionCheck;
}

internal static class ConnectionCheckPayloadCodec
{
    /// <summary>The probe has no body at all.</summary>
    /// <remarks>
    /// Every observed probe is a bare fourteen-byte frame. The length is worth asserting rather
    /// than ignoring: a body here would mean the inbound header boundary is wrong again, and that
    /// is a failure worth surfacing at the codec instead of letting it travel further.
    /// </remarks>
    public const int BodyLength = 0;

    public static ConnectionCheckRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"CHECK_USER_CONNECTION carries no body; received '{body.Length}' bytes.");
        }

        return new ConnectionCheckRequest(metadata);
    }

    public static byte[] EncodeResponse() => GameCmdEnvelope.Accept();
}

internal sealed class ConnectionCheckMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        ConnectionCheckPayloadCodec.Decode(metadata, body);
}

public sealed class ConnectionCheckHandler(ICommandBus commands)
    : ClientMessageHandler<ConnectionCheckRequest>, IProtocolCommandMessageHandler<GameConnectionCheckCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ConnectionCheckRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameConnectionCheckCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class ConnectionCheckResponseEncoder
    : GameCmdMessageEncoder<ConnectionCheckResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ConnectionCheckResponse message) =>
        OnePayload(message,
            ConnectionCheckPayloadCodec.EncodeResponse());
}
