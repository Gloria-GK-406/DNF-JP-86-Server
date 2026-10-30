namespace Dfo.Server.Protocol.Game.Exit;

internal sealed class GameExitResponseEncoder : GameCmdMessageEncoder<GameExitResponse>
{
    /// <remarks>
    /// The refusal reason is zero because nothing has told us what else it could be: the handler at
    /// <c>RVA 0x008C6260</c> takes no arguments at all and runs the same exit dialog either way, so
    /// no code is distinguishable from another by its behaviour.
    ///
    /// The byte is still sent. The receive layer reads it whenever the envelope says failure, and
    /// it reads from a stream that refills across frames -- omitting it would consume the leading
    /// byte of the next frame and misalign every frame after it, which is a far worse failure than
    /// an uninformative reason.
    /// </remarks>
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameExitResponse message) =>
        OnePayload(message,
            message.Succeeded ? GameCmdEnvelope.Accept() : GameCmdEnvelope.Refuse(0x00));
}
