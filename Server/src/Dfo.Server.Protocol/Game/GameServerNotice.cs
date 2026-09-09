using System.Text;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

internal sealed record GameServerNoticeMessage(string Text, byte Mode = 0) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameServerNotice;
}

internal sealed class GameServerNoticeEncoder : GameNotiMessageEncoder<GameServerNoticeMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GameServerNoticeMessage message) =>
        [EncodePayload(message.Text, message.Mode)];

    internal static GameMessagePayload EncodePayload(string text, byte mode)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(mode);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        return new(0x01F7, stream.ToArray());
    }
}
