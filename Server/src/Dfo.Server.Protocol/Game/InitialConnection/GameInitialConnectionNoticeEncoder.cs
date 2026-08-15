using Dfo.Server.Networking.Configuration;

namespace Dfo.Server.Protocol.Game.InitialConnection;

internal sealed class GameInitialConnectionNoticeEncoder(GameLoginOptions options)
    : GameNotiMessageEncoder<GameInitialConnectionNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        GameInitialConnectionNotice message) =>
        OnePayload(
            GameProtocol.LoginType,
            GameInitialConnectionPayloadCodec.EncodeBody(message, options));
}
