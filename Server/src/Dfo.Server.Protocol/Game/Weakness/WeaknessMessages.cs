using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Weakness;

internal sealed record WeaknessProjectionNotification(byte Value) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameWeaknessProjection;
}

internal sealed class WeaknessProjectionEncoder : GameNotiMessageEncoder<WeaknessProjectionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeaknessProjectionNotification message)
        => OnePayload(message, new byte[] { message.Value });
}
