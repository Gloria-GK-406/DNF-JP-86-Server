using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game;
internal static class TowerProtocol
{
    internal static bool IsStale(GameMessageMetadata metadata) => metadata.InboundOrdinal is > 0 && !metadata.SequenceIsFresh;
}
