using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.IllusionTowerContinue;

internal sealed record IllusionTowerContinueNotification(bool Eligible) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameIllusionTowerContinue;
}

internal sealed class IllusionTowerContinueEncoder : GameNotiMessageEncoder<IllusionTowerContinueNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(IllusionTowerContinueNotification message) =>
        OnePayload(GameProtocol.IllusionTowerContinueType,
            new byte[] { message.Eligible ? (byte)1 : (byte)0 });
}
