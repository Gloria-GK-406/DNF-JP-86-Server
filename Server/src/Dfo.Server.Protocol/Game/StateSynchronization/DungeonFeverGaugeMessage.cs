using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>A21 0264 replaces the current dungeon fever gauge; 100 starts its native timer.</summary>
internal sealed record DungeonFeverGaugeMessage(byte Percent) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0264;
    internal override byte[] EncodeBody() => Percent <= 100 ? [Percent]
        : throw new ArgumentOutOfRangeException(nameof(Percent));
}
