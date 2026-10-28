using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game;

/// <summary>Whether an inbound game request arrived out of turn and must be dropped.</summary>
internal static class GameRequestFreshness
{
    /// <summary>
    /// Stale means the transport numbered the frame but its sequence did not advance the
    /// connection's count by exactly one. Frames the transport never numbered (internal and
    /// test-built metadata) are never stale.
    /// </summary>
    internal static bool IsStale(GameMessageMetadata metadata) => metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh;
}
