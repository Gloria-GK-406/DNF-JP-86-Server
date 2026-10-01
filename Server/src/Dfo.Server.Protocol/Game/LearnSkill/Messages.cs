using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed record LearnSkillRequest(GameMessageMetadata Metadata, byte PageIndex,
    IReadOnlyList<CharacterSkillLearningChange> Changes, byte Mode) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Skills;
    public ClientMessageType MessageType => ClientMessageType.GameLearnSkill;
}

public sealed record LearnSkillResponse(CharacterSkillLearningReceipt? Receipt) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLearnSkill;
}
