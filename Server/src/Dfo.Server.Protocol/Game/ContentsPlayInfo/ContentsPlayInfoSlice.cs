using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ContentsPlayInfo;

/// <summary>
/// CMD 0x02C1 <c>CONTENTS_PLAY_INFO</c>: a one-byte query the client sends from the roster screen,
/// immediately before SELECT_CHARACTER. The client registers a receive handler for the same type
/// (RVA 0x008C6DD0), so the server must answer.
/// </summary>
/// <remarks>
/// First observed 2026-08-16 with body <c>01</c>. The static census reports a closed one-byte
/// layout at the single builder site, so the byte is the whole request; its meaning is unknown and
/// the upstream server has the name in its enum but no handler for it.
/// </remarks>
public sealed record ContentsPlayInfoRequest(
    GameMessageMetadata Metadata,
    byte Selector) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameContentsPlayInfo;
}

public sealed record ContentsPlayInfoResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameContentsPlayInfo;
}

internal static class ContentsPlayInfoPayloadCodec
{
    public const int BodyLength = 1;

    public static ContentsPlayInfoRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"CONTENTS_PLAY_INFO requires exactly {BodyLength} body byte; "
                    + $"received '{body.Length}'.");
        }

        return new ContentsPlayInfoRequest(metadata, body[0]);
    }

    /// <summary>
    /// The <see cref="GameCmdEnvelope"/> acceptance followed by eighteen zero bytes, matching the
    /// field widths the client's receive handler reads.
    /// </summary>
    /// <remarks>
    /// Walking the handler at RVA 0x008C6DD0 recovers <c>u8, u8, u32, u32, u32, u32</c> before the
    /// trail enters the packed region, so eighteen is a floor rather than a proven total. That is
    /// the safe side to be wrong on: the inbound stream refills across frames, so a body shorter
    /// than the handler reads pulls bytes out of the next frame -- the failure already documented
    /// for the bare EXIT refusal -- while a longer one is dropped when the next frame re-bases the
    /// cursor.
    ///
    /// The values are zero because none of the six fields has a known meaning. Replace them with
    /// evidence, not with plausible-looking numbers.
    /// </remarks>
    public const int ResponsePayloadLength = 18;

    public static byte[] EncodeResponse() =>
        GameCmdEnvelope.Accept(new byte[ResponsePayloadLength]);
}

internal sealed class ContentsPlayInfoMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        ContentsPlayInfoPayloadCodec.Decode(metadata, body);
}

public sealed class ContentsPlayInfoHandler(ICommandBus commands)
    : ClientMessageHandler<ContentsPlayInfoRequest>, IProtocolCommandMessageHandler<GameContentsPlayInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ContentsPlayInfoRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameContentsPlayInfoCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class ContentsPlayInfoResponseEncoder
    : GameCmdMessageEncoder<ContentsPlayInfoResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ContentsPlayInfoResponse message) =>
        OnePayload(message,
            ContentsPlayInfoPayloadCodec.EncodeResponse());
}
