using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed record LearnSkillRequest(GameMessageMetadata Metadata, byte PageIndex,
    IReadOnlyList<CharacterSkillLearningChange> Changes, byte Mode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLearnSkill;
}

public sealed record LearnSkillResponse(CharacterSkillLearningReceipt? Receipt) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLearnSkill;
}
