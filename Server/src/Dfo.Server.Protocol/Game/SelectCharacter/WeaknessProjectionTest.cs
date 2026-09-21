using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>Opt-in live experiment; never persists or changes server attributes.</summary>
public sealed record WeaknessProjectionTestOptions(byte? Value);

internal sealed record WeaknessProjectionNotification(byte Value) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameWeaknessProjection;
}

internal sealed class WeaknessProjectionEncoder : GameNotiMessageEncoder<WeaknessProjectionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeaknessProjectionNotification message)
        => OnePayload(0x0021, new byte[] { message.Value });
}
