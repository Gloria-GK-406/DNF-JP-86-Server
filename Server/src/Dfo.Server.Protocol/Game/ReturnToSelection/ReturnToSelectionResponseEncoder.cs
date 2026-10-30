namespace Dfo.Server.Protocol.Game.ReturnToSelection;

internal sealed class ReturnToSelectionResponseEncoder : GameCmdMessageEncoder<ReturnToSelectionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ReturnToSelectionResponse message) =>
        OnePayload(message, GameCmdEnvelope.Accept());
}
