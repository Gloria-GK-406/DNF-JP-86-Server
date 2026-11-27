namespace Dfo.Server.Data.Guilds;

/// <summary>The A21 guild hall town and its original entrance area.</summary>
public static class GuildHallTown
{
    public const byte TownId = 8;
    public const byte InitialAreaId = 0;
    public const byte DungeonGateAreaId = 1;
    public const byte BattleAreaId = 2;
    public const byte InitialEntranceStage = 1;
    public const int ContributionShopNpcId = 20192;
    public const int BattleSupplyNpcId = 443;
    public const int DungeonSupplyNpcId = 382;
    public const int DungeonMedalNpcId = 383;
    public const int MedalShopUnlockDungeonId = 8508;
    public const int FirstMedalShopId = 150;
    public const int MedalShopCount = 10;

    public static byte? ShopArea(int npcId) => npcId switch
    {
        ContributionShopNpcId => InitialAreaId,
        DungeonSupplyNpcId or DungeonMedalNpcId => DungeonGateAreaId,
        BattleSupplyNpcId => BattleAreaId,
        _ => null,
    };

    public static bool Is(int townId) => townId == TownId;
}

