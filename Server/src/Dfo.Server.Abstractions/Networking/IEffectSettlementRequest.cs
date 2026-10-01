using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Abstractions.Networking;

/// <summary>A network operation with one primary settlement and a bounded current-state replay.</summary>
public interface IEffectSettlementRequest : IClientNetworkMessage
{
    GameMessageMetadata Metadata { get; }
    ReplayProjectionNeeds MinimumReplayNeeds { get; }
}
