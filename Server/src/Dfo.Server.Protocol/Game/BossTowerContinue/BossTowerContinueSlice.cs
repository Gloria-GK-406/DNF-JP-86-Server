using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.BossTowerContinue;

internal sealed record BossTowerContinueNotification(bool Eligible) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameBossTowerContinue;
}

internal sealed class BossTowerContinueEncoder : GameNotiMessageEncoder<BossTowerContinueNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(BossTowerContinueNotification message) =>
        OnePayload(message,
            new byte[] { message.Eligible ? (byte)1 : (byte)0 });
}
