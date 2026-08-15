using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Protocol.Game.Exit;

internal sealed class GameExitResponseEncoder : ServerMessageEncoder<GameExitResponse>
{
    protected override IReadOnlyList<byte[]> EncodeMessage(GameExitResponse message) =>
        One(GameServerFrameEncoder.Encode(
            GameProtocol.Command,
            GameProtocol.ExitType,
            [message.Succeeded ? (byte)0x01 : (byte)0x00]));
}
