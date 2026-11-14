namespace Dfo.Server.Abstractions.ServerBroadcast;

/// <summary>The kinds of server-wide announcement, each switched on or off by configuration.</summary>
public enum ServerBroadcastKind
{
    MaxLevel,
    SecondAwakening,
    Reinforcement,
    Amplification,
    VendingPrize,
    BoxReward,
    Forging,
    RecipeCraft,
    DespairTower,
    AncientDungeonItem,
}

/// <summary>
/// The server's own announcement rules: the switches, and the thresholds the archive does not declare.
/// Thresholds the archive does declare (the reinforcement notice level, pool broadcast flags, recipe
/// broadcast types) stay with the content that declares them.
/// </summary>
public interface IServerBroadcastRuleService
{
    bool Announces(ServerBroadcastKind kind);

    /// <summary>The server's top level when this rise reaches it from below; null for any other rise.</summary>
    int? TopLevelReached(int before, int after);

    /// <summary>Whether a grow type is the second awakening, the stage announced.</summary>
    bool IsSecondAwakening(int growType);

    /// <summary>The lowest forging stage announced; the 70 server announced stages above 5.</summary>
    int ForgingMinimumStage { get; }

    /// <summary>A lottery prize rare enough to announce; pot pools carry no broadcast flag.</summary>
    ValueTask<bool> IsNotableLotteryPrizeAsync(int itemId, CancellationToken ct);

    /// <summary>A pickup in a dungeon the archive tags ancient, rare enough to announce.</summary>
    ValueTask<bool> IsNotableAncientFindAsync(int dungeonId, int itemId, CancellationToken ct);
}
