using System.Text;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.ServerBroadcast;

/// <summary>Tells every character in the world, on every channel, what one committed fact says.</summary>
/// <remarks>
/// Live only: an announcement is worth something only at its moment, so one that misses it (a crash,
/// an earlier process) is dropped, not replayed. The actor hears its own announcement too.
/// <para>
/// The A21 client words a NOTI <c>0x0056</c> itself, but most of its subtypes name the actor by a u16
/// user id that a client shows only if it has met that character on its current channel
/// (ClientVerified 2026-11-14). Those facts go out as a NOTI <c>0x01F7</c> system line instead, worded
/// as the client's own Simplified Chinese text. Only the subtypes that carry the name -- a box reward and
/// the second awakening -- keep the native frame. Subtype 6 (top level) is not used: the client prints
/// Lv85 into it, and this server's top level is 86.
/// </para>
/// </remarks>
internal sealed class ServerBroadcastSubscriber(
    IServerBroadcastRuleService rules,
    ICharacterDomainService characters,
    IItemCatalogRuleService items,
    IChatScopeDomainService scopes,
    IEventService events)
    : IDomainEventSubscriber<CharacterLevelChanged>,
      IDomainEventSubscriber<CharacterGrowTypeChanged>,
      IDomainEventSubscriber<EquipmentUpgradeAnnounced>,
      IDomainEventSubscriber<VendingPrizeAnnounced>,
      IDomainEventSubscriber<BoxRewardAnnounced>,
      IDomainEventSubscriber<EquipmentForgingAnnounced>,
      IDomainEventSubscriber<RecipeCraftAnnounced>,
      IDomainEventSubscriber<DespairTowerFloorAnnounced>,
      IDomainEventSubscriber<AncientDungeonItemAnnounced>
{
    public ValueTask HandleAsync(DomainEventEnvelope<CharacterLevelChanged> message, CancellationToken ct)
    {
        var change = message.Payload;
        return rules.TopLevelReached(change.Before, change.After) is { } top
            ? TextAsync(ServerBroadcastKind.MaxLevel, change.CharacterId, name => ServerBroadcastTexts.MaxLevel(name, top), ct)
            : ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(DomainEventEnvelope<CharacterGrowTypeChanged> message, CancellationToken ct)
    {
        var change = message.Payload;
        return rules.IsSecondAwakening(change.GrowType)
            ? NativeAsync(ServerBroadcastKind.SecondAwakening, change.CharacterId, name => new SecondAwakeningBroadcast(name), ct)
            : ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<EquipmentUpgradeAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        var kind = fact.Kind == EquipmentUpgradeKind.Amplification ? ServerBroadcastKind.Amplification : ServerBroadcastKind.Reinforcement;
        if (!rules.Announces(kind)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await TextAsync(kind, fact.Actor, name => ServerBroadcastTexts.Upgrade(name, fact.Kind, fact.Level, item, fact.Succeeded), ct)
            .ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<VendingPrizeAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.VendingPrize)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await TextAsync(ServerBroadcastKind.VendingPrize, fact.Actor, name => ServerBroadcastTexts.VendingPrize(name, item, fact.Count), ct)
            .ConfigureAwait(false);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<BoxRewardAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        return NativeAsync(ServerBroadcastKind.BoxReward, fact.Actor, name => new BoxRewardBroadcast(name, fact.BoxItemId, fact.ItemId), ct);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<EquipmentForgingAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.Forging)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await TextAsync(ServerBroadcastKind.Forging, fact.Actor,
            name => ServerBroadcastTexts.Forging(name, fact.ReinforcementLevel, item, fact.Stage, fact.Succeeded), ct).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<RecipeCraftAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.RecipeCraft)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await TextAsync(ServerBroadcastKind.RecipeCraft, fact.Actor,
            name => ServerBroadcastTexts.RecipeCraft(name, fact.ReinforcementLevel, item), ct).ConfigureAwait(false);
    }

    public ValueTask HandleAsync(DomainEventEnvelope<DespairTowerFloorAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        return TextAsync(ServerBroadcastKind.DespairTower, fact.Actor, name => ServerBroadcastTexts.DespairTower(name, fact.Floor), ct);
    }

    public async ValueTask HandleAsync(DomainEventEnvelope<AncientDungeonItemAnnounced> message, CancellationToken ct)
    {
        var fact = message.Payload;
        if (!rules.Announces(ServerBroadcastKind.AncientDungeonItem)) return;
        var item = await ItemNameAsync(fact.ItemId, ct).ConfigureAwait(false);
        await TextAsync(ServerBroadcastKind.AncientDungeonItem, fact.Actor, name => ServerBroadcastTexts.AncientDungeonItem(name, item), ct)
            .ConfigureAwait(false);
    }

    private ValueTask TextAsync(ServerBroadcastKind kind, CharacterId actor, Func<string, string> text, CancellationToken ct) =>
        PostAsync(kind, actor, name => new GameServerNoticeMessage(text(Encoding.UTF8.GetString(name)), GameServerNoticeModes.ChatLine), ct);

    private ValueTask NativeAsync(ServerBroadcastKind kind, CharacterId actor, Func<byte[], ServerBroadcastMessage> line,
        CancellationToken ct) => PostAsync(kind, actor, line, ct);

    private async ValueTask PostAsync(ServerBroadcastKind kind, CharacterId actor, Func<byte[], IGameNotiMessage> line,
        CancellationToken ct)
    {
        if (!rules.Announces(kind)) return;
        var name = (await characters.Get(actor).IdentityAsync(ct).ConfigureAwait(false))?.Name;
        if (name is not { Length: > 0 and <= ServerBroadcastMessage.NameLimit }) return;
        var members = scopes.MembersOf(new ChatScope.Server());
        if (members.Count == 0) return;
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters(members), new ServerBroadcastPosted(line(name)), ct)
            .ConfigureAwait(false);
    }

    // An item the catalog cannot name is still announced, under its identifier.
    private async ValueTask<string> ItemNameAsync(int itemId, CancellationToken ct) =>
        await items.ItemNameAsync(itemId, ct).ConfigureAwait(false) is { Length: > 0 } name ? name : itemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

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
