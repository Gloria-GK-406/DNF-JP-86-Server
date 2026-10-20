using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EplpRechallenge;

/// <summary>
/// Invites the native client to check and submit its cycle quest. This is not a completion ACK.
/// State 9 is the reference ready value; cycle-dungeon behavior awaits real-client acceptance.
/// </summary>
public sealed record EplpRechallengeNotification : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameEplpRechallenge;
}

internal sealed class EplpRechallengeEncoder : GameNotiMessageEncoder<EplpRechallengeNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(EplpRechallengeNotification message) =>
        OnePayload(GameProtocol.EplpRechallengeNotificationType, new byte[] { 9 });
}
