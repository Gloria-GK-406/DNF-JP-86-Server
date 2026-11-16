using System.Text;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>Expresses a decided announcement without reading rules or selecting recipients.</summary>
/// <remarks>
/// A21's native frame can name unseen actors only for box rewards and second awakenings
/// (ClientVerified 2026-11-14); other announcements use its system-chat line wording.
/// The native top-level subtype hardcodes Lv85, so the decided level is worded here instead.
/// </remarks>
public sealed class ServerBroadcastPostedHandler : INotificationHandler<ServerBroadcastPosted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ServerBroadcastPosted notification, CancellationToken cancellationToken)
    {
        if (notification.ActorName.IsDefaultOrEmpty || notification.ActorName.Length > ServerBroadcastMessage.NameLimit)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var name = notification.ActorName.ToArray();
        var textName = Encoding.UTF8.GetString(name);
        IGameNotiMessage line = notification.Content switch
        {
            ServerBroadcastContent.MaxLevel level => Text(ServerBroadcastTexts.MaxLevel(textName, level.Level)),
            ServerBroadcastContent.SecondAwakening => new SecondAwakeningBroadcast(name),
            ServerBroadcastContent.Upgrade upgrade => Text(ServerBroadcastTexts.Upgrade(
                textName, upgrade.Kind, upgrade.Level, upgrade.ItemName, upgrade.Succeeded)),
            ServerBroadcastContent.VendingPrize prize => Text(ServerBroadcastTexts.VendingPrize(textName, prize.ItemName, prize.Count)),
            ServerBroadcastContent.BoxReward box => new BoxRewardBroadcast(name, box.BoxItemId, box.ItemId),
            ServerBroadcastContent.Forging forging => Text(ServerBroadcastTexts.Forging(
                textName, forging.ReinforcementLevel, forging.ItemName, forging.Stage, forging.Succeeded)),
            ServerBroadcastContent.RecipeCraft craft => Text(ServerBroadcastTexts.RecipeCraft(textName, craft.ReinforcementLevel, craft.ItemName)),
            ServerBroadcastContent.DespairTower tower => Text(ServerBroadcastTexts.DespairTower(textName, tower.Floor)),
            ServerBroadcastContent.AncientDungeonItem find => Text(ServerBroadcastTexts.AncientDungeonItem(textName, find.ItemName)),
            _ => throw new NotSupportedException($"Broadcast content '{notification.Content.GetType().Name}' has no projection."),
        };
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([line]);
    }

    private static GameServerNoticeMessage Text(string text) => new(text, GameServerNoticeModes.ChatLine);
}
