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
    ILogger<HiddenCharacterInfoHandler> logger)
    : ClientMessageHandler<HiddenCharacterInfoRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        HiddenCharacterInfoRequest message,
        CancellationToken cancellationToken)
    {
        // Deliberately silent. The reply is pushed with the roster, before this request is even
        // sent, and the reference server leaves the request itself unanswered -- answering it too
        // put a second copy on the wire that no working server sends.
        logger.LogInformation(
            "Hidden character info {ConnectionId}: already answered with the roster, not replying again",
            context.Connection.ConnectionId);
        return ValueTask.CompletedTask;
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
