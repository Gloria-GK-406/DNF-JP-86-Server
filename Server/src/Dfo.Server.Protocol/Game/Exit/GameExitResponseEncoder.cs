namespace Dfo.Server.Protocol.Game.Exit;

internal sealed class GameExitResponseEncoder : GameCmdMessageEncoder<GameExitResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameExitResponse message) =>
        OnePayload(
            GameProtocol.ExitType,
            new byte[] { message.Succeeded ? (byte)0x01 : (byte)0x00 });
}
