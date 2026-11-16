using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>The NOTI <c>0x01F7</c> modes this slice uses.</summary>
internal static class GameServerNoticeModes
{
    /// <summary>A chat-box system line (A21 <c>01A62DF0</c>, category 0x1E), the same line 0x0056 subtypes use.</summary>
    public const byte ChatLine = 2;
}

/// <summary>
/// The announcement wording. Each mirrors the A21 client's own Simplified Chinese text for the
/// matching 0x0056 subtype (dstr.dat table 102, ids in the comments), so a line reads as the client
/// would have worded it; amplification, the tower floor and ancient finds have no client text and
/// follow the same pattern.
/// </summary>
internal static class ServerBroadcastTexts
{
    /// <summary>After 0xBB8C, without its invitation-reward clause (#343).</summary>
    public static string MaxLevel(string name, int level) => $"恭喜玩家[{name}]达到Lv{level}！";

    /// <summary>0x689/0x68A; a failure names the level it was trying for, as the client does.</summary>
    public static string Upgrade(string name, EquipmentUpgradeKind kind, byte level, string item, bool succeeded)
    {
        var verb = kind == EquipmentUpgradeKind.Amplification ? "增幅" : "强化";
        return succeeded ? $"玩家[{name}]{verb} +{level} {item}成功" : $"玩家[{name}]{verb} +{level + 1} {item}失败";
    }

    /// <summary>0xB3B2 / 0xB3B0.</summary>
    public static string VendingPrize(string name, string item, int count) =>
        count > 1 ? $"[{name}]从黑钻售货机中获得了{count}个[{item}]。" : $"[{name}]从黑钻售货机中获得了[{item}]。";

    /// <summary>0x8941 / 0x8942.</summary>
    public static string Forging(string name, byte reinforcement, string item, byte stage, bool succeeded) =>
        $"玩家[{name}]锻造 +{reinforcement} {item} 第{stage}阶段{(succeeded ? "成功" : "失败")}";

    /// <summary>0xAD3D.</summary>
    public static string RecipeCraft(string name, byte reinforcement, string item) => $"玩家[{name}]利用设计图制作了 +{reinforcement} {item}";

    /// <summary>After 0x7D0E, without its guardian clause: this server's guardian is the climber's own image.</summary>
    public static string DespairTower(string name, int floor) => $"恭喜玩家[{name}]征服了绝望之塔的第{floor}层！";

    /// <summary>After 0x112FA, naming the dungeon by its kind.</summary>
    public static string AncientDungeonItem(string name, string item) => $"玩家[{name}]在远古地下城获得了[{item}]";
}
