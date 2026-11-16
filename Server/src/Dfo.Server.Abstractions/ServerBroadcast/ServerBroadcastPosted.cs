using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.ServerBroadcast;

/// <summary>A decided announcement; its audience is supplied to the notification mechanism separately.</summary>
public sealed record ServerBroadcastPosted(ImmutableArray<byte> ActorName, ServerBroadcastContent Content)
    : INotification<ServerBroadcastPosted>;

/// <summary>Business content, without client wording, protocol selection or a network message.</summary>
public abstract record ServerBroadcastContent
{
    private protected ServerBroadcastContent() { }

    public sealed record MaxLevel(int Level) : ServerBroadcastContent;
    public sealed record SecondAwakening : ServerBroadcastContent;
    public sealed record Upgrade(EquipmentUpgradeKind Kind, byte Level, string ItemName, bool Succeeded) : ServerBroadcastContent;
    public sealed record VendingPrize(string ItemName, int Count) : ServerBroadcastContent;
    public sealed record BoxReward(int BoxItemId, int ItemId) : ServerBroadcastContent;
    public sealed record Forging(byte ReinforcementLevel, string ItemName, byte Stage, bool Succeeded) : ServerBroadcastContent;
    public sealed record RecipeCraft(byte ReinforcementLevel, string ItemName) : ServerBroadcastContent;
    public sealed record DespairTower(int Floor) : ServerBroadcastContent;
    public sealed record AncientDungeonItem(string ItemName) : ServerBroadcastContent;
}
