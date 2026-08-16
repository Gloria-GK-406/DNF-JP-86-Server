namespace Dfo.Server.Protocol.Game.InitialConnection;

internal sealed class GameInitialConnectionNoticeEncoder
    : GameNotiMessageEncoder<GameInitialConnectionNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        GameInitialConnectionNotice message) =>
        OnePayload(
            GameProtocol.LoginType,
            GameInitialConnectionPayloadCodec.EncodeBody(message));
}
