using Dfo.Server.Data.Channel;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>Business ownership of an admitted duel, independent of its combat rules.</summary>
public enum PvpDuelBusiness
{
    FreeAndMatchmaking,
    Traditional,
}

internal static class PvpDuelBusinessRoute
{
    public static PvpDuelBusiness Of(ChannelEntryIdentity channel, PvpRoomOrigin origin, PvpBattleMode mode) =>
        channel.Kind == ChannelEntryKind.TraditionalDuel
        || (channel.Kind == ChannelEntryKind.TotalMatchDuel
            && origin == PvpRoomOrigin.Matchmaking && mode == PvpBattleMode.TotalMatch)
            ? PvpDuelBusiness.Traditional
            : PvpDuelBusiness.FreeAndMatchmaking;
}
