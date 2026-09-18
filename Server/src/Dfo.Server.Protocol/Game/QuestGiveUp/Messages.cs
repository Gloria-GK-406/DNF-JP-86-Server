using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

public sealed record GiveUpQuestRequest(GameMessageMetadata Metadata, ushort QuestId)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGiveUpQuest;
}

public sealed record GiveUpQuestAcknowledgement(bool GivenUp, ushort QuestId, byte RefusalCode)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGiveUpQuest;
}
