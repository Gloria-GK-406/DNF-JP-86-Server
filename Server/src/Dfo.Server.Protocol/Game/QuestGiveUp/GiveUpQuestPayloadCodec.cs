using System.Buffers.Binary;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

internal static class GiveUpQuestPayloadCodec
{
    internal static GiveUpQuestRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 4)
            throw new InvalidDataException("GIVEUP_QUEST requires a two-byte prefix and a u16 quest id.");

        return new GiveUpQuestRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body[2..]));
    }

    internal static byte[] Encode(GiveUpQuestAcknowledgement message) => message.GivenUp
        ? [1, (byte)message.QuestId, (byte)(message.QuestId >> 8)]
        : [0, message.RefusalCode];
}
