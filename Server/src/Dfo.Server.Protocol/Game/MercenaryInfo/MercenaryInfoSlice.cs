using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MercenaryInfo;

internal sealed class MercenaryInfoMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new MercenaryInfoRequest(metadata);
}

/// <summary>
/// Answers CMD 0x01BA with a well-formed but empty mercenary roster.
/// </summary>
/// <remarks>
/// Mercenary expeditions are not implemented, so there is no domain to consult and this stays in
/// the handler. It is answered anyway because the client blocks waiting on it: the query is sent
/// unprompted from the roster screen and its receive handler reads a leading status byte.
///
/// Reporting an empty roster rather than the legacy error code is deliberate. An error is a claim
/// that the request failed; "you have no mercenaries out" is true, and it is what a fresh account
/// would get from a working server. The shape is the legacy success layout with a zero record
/// count -- status 1, manage level 0, manage point 0, zero records.
/// </remarks>
public sealed class MercenaryInfoHandler(
    IGameMessageSender sender,
    ILogger<MercenaryInfoHandler> logger)
    : ClientMessageHandler<MercenaryInfoRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MercenaryInfoRequest message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Mercenary info {ConnectionId}: replying with an empty expedition roster",
            context.Connection.ConnectionId);
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            new MercenaryInfoResponse(),
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class MercenaryInfoResponseEncoder : GameCmdMessageEncoder<MercenaryInfoResponse>
{
    /// <summary>
    /// The <see cref="GameCmdEnvelope"/> acceptance, then manageLevel=0, managePoint=0 (u32) and
    /// recordCount=0.
    /// </summary>
    /// <remarks>
    /// The leading byte was previously described as a status of this message. It is not: it is the
    /// family-wide envelope, and the payload proper starts at manageLevel.
    /// </remarks>
    private static readonly byte[] EmptyRoster = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        MercenaryInfoResponse message) =>
        OnePayload(GameProtocol.MercenaryInfoType, GameCmdEnvelope.Accept(EmptyRoster));
}
