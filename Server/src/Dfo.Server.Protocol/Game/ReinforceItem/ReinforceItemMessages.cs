using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReinforceItem;

public sealed record ReinforceItemRequest(
    GameMessageMetadata Metadata,
    ushort Method,
    short TargetSlot,
    int ExpectedItemId,
    short MaterialSlot,
    short OptionalTicketSlot,
    byte[] ReportedItemNameBytes) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameReinforceItem;
}

public sealed record ReinforceItemReward(GameSlot Slot, int ItemId, int Count);

public sealed record ReinforceItemResponse(
    bool Success,
    byte ErrorCode = 0,
    byte Method = 0,
    short MaterialSlot = -1,
    int MaterialRemaining = 0,
    short OptionalTicketSlot = -1,
    byte OldLevel = 0,
    EquipmentReinforcementOutcome Outcome = EquipmentReinforcementOutcome.Succeeded,
    byte NewLevel = 0,
    short TargetSlot = -1,
    IReadOnlyList<ReinforceItemReward>? DestroyRewards = null) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameReinforceItem;
}
