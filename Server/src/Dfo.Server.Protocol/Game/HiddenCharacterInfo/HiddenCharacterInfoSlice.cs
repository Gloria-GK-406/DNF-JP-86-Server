using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.HiddenCharacterInfo;

internal sealed class HiddenCharacterInfoMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new HiddenCharacterInfoRequest(metadata);
}

/// <summary>
/// Answers CMD 0x0286 with the same two constant bytes the legacy server sent.
/// </summary>
/// <remarks>
/// There is no domain behind this one, so it stays entirely in the handler: nothing is looked up,
/// nothing is stored, and the reply never varies. The legacy server did not even decode the
/// request -- it pushed this response unprompted right after the roster. Replying to the request we
/// can actually observe is the same thing from the client's side and needs no assumption about
/// ordering.
///
/// The body is <c>00 04</c>, which under the CMD convention reads as "refused, code 4". That the
/// correct answer is a refusal is not a placeholder: the account has no hidden characters to show,
/// and the legacy server shipped this exact constant while character creation worked.
/// </remarks>
public sealed class HiddenCharacterInfoHandler(
    IGameMessageSender sender,
    ILogger<HiddenCharacterInfoHandler> logger)
    : ClientMessageHandler<HiddenCharacterInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        HiddenCharacterInfoRequest message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Hidden character info {ConnectionId}: replying with the constant refusal",
            context.Connection.ConnectionId);
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new HiddenCharacterInfoResponse(),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class HiddenCharacterInfoResponseEncoder
    : GameCmdMessageEncoder<HiddenCharacterInfoResponse>
{
    private static readonly byte[] ConstantRefusal = [0x00, 0x04];

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        HiddenCharacterInfoResponse message) =>
        OnePayload(GameProtocol.HiddenCharacterInfoType, ConstantRefusal);
}
