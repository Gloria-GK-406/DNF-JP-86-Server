using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.ServerBroadcast;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.ServerBroadcast;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ServerBroadcast;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.ServerBroadcast;

[Trait("Category", "Unit")]
public sealed class ServerBroadcastTests
{
    private static readonly CharacterId Actor = new(1);
    private static readonly byte[] ProbeName = Encoding.UTF8.GetBytes("探针测试名");

    [Fact]
    public void Box_reward_encodes_the_layout_the_client_displayed()
    {
        // protocol/game/server-to-client/samples/NOTI-0x0056-20261114-08.hex
        Assert.Equal(Convert.FromHexString("120F000000E68EA2E99288E6B58BE8AF95E5908DC800000046EC2900"),
            ServerBroadcastEncoder<BoxRewardBroadcast>.Body(new BoxRewardBroadcast(ProbeName, 200, 2747462)));
    }

    [Fact]
    public void Second_awakening_encodes_the_layout_the_client_displayed()
    {
        // protocol/game/server-to-client/samples/NOTI-0x0056-20261114-09.hex
        Assert.Equal(Convert.FromHexString("140F000000E68EA2E99288E6B58BE8AF95E5908D"),
            ServerBroadcastEncoder<SecondAwakeningBroadcast>.Body(new SecondAwakeningBroadcast(ProbeName)));
    }

    [Fact]
    public void A_name_the_client_would_not_read_is_never_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecondAwakeningBroadcast([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SecondAwakeningBroadcast(new byte[256]));
        Assert.Equal(260, ServerBroadcastEncoder<SecondAwakeningBroadcast>.Body(new SecondAwakeningBroadcast(new byte[255])).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoxRewardBroadcast([1], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoxRewardBroadcast([1], 1, 0));
    }

    [Fact]
    public void Texts_follow_the_clients_own_wording()
    {
        Assert.Equal("玩家[甲]强化 +12 圣愈光剑成功",
            ServerBroadcastTexts.Upgrade("甲", EquipmentUpgradeKind.Reinforcement, 12, "圣愈光剑", true));
        // A failure names the level it tried for, as the client prints its own subtype 1.
        Assert.Equal("玩家[甲]强化 +13 圣愈光剑失败",
            ServerBroadcastTexts.Upgrade("甲", EquipmentUpgradeKind.Reinforcement, 12, "圣愈光剑", false));
        Assert.Equal("玩家[甲]增幅 +12 圣愈光剑成功",
            ServerBroadcastTexts.Upgrade("甲", EquipmentUpgradeKind.Amplification, 12, "圣愈光剑", true));
        Assert.Equal("[甲]从黑钻售货机中获得了[圣愈光剑]。", ServerBroadcastTexts.VendingPrize("甲", "圣愈光剑", 1));
        Assert.Equal("[甲]从黑钻售货机中获得了2个[圣愈光剑]。", ServerBroadcastTexts.VendingPrize("甲", "圣愈光剑", 2));
        Assert.Equal("玩家[甲]锻造 +12 圣愈光剑 第6阶段成功", ServerBroadcastTexts.Forging("甲", 12, "圣愈光剑", 6, true));
        Assert.Equal("玩家[甲]锻造 +12 圣愈光剑 第6阶段失败", ServerBroadcastTexts.Forging("甲", 12, "圣愈光剑", 6, false));
        Assert.Equal("玩家[甲]利用设计图制作了 +10 释魂", ServerBroadcastTexts.RecipeCraft("甲", 10, "释魂"));
        Assert.Equal("恭喜玩家[甲]达到Lv86！", ServerBroadcastTexts.MaxLevel("甲", 86));
        Assert.Equal("恭喜玩家[甲]征服了绝望之塔的第50层！", ServerBroadcastTexts.DespairTower("甲", 50));
        Assert.Equal("玩家[甲]在远古地下城获得了[圣愈光剑]", ServerBroadcastTexts.AncientDungeonItem("甲", "圣愈光剑"));
    }

    [Fact]
    public async Task An_announcement_reaches_everyone_in_the_world_the_server_scope_names()
    {
        var events = new ProjectedNotifications();
        CharacterId[] everyone = [Actor, new(2), new(3)];
        ChatScope? asked = null;
        var subscriber = Subscriber(events, members: scope => { asked = scope; return everyone; });

        await subscriber.HandleAsync(Envelope(new EquipmentUpgradeAnnounced(Actor, 2747462, EquipmentUpgradeKind.Reinforcement, 12, true)), default);

        Assert.IsType<ChatScope.Server>(asked);
        var (audience, notification) = Assert.Single(events.Projected);
        Assert.Equal(everyone, Assert.IsType<NotificationAudience.TheseCharacters>(audience).CharacterIds);
        var line = Assert.IsType<GameServerNoticeMessage>(await Compose(notification));
        Assert.Equal(GameServerNoticeModes.ChatLine, line.Mode);
        Assert.Equal("玩家[探针测试名]强化 +12 圣愈光剑成功", line.Text);
    }

    [Fact]
    public async Task The_handler_composes_the_same_payload_for_every_recipient()
    {
        var notification = new ServerBroadcastPosted(ProbeName.ToImmutableArray(), new ServerBroadcastContent.SecondAwakening());
        foreach (var character in new[] { new CharacterId(9), new CharacterId(10) })
        {
            var composed = await new ServerBroadcastPostedHandler().ComposeAsync(new(character, default), notification, default);
            Assert.Equal(Convert.FromHexString("140F000000E68EA2E99288E6B58BE8AF95E5908D"),
                ServerBroadcastEncoder<SecondAwakeningBroadcast>.Body(Assert.IsType<SecondAwakeningBroadcast>(Assert.Single(composed))));
        }
    }

    [Fact]
    public async Task A_kind_switched_off_tells_nothing()
    {
        var events = new ProjectedNotifications();
        var subscriber = Subscriber(events, enabled: kind => kind != ServerBroadcastKind.VendingPrize);

        await subscriber.HandleAsync(Envelope(new VendingPrizeAnnounced(Actor, 2747462, 2)), default);
        Assert.Empty(events.Projected);

        await subscriber.HandleAsync(Envelope(new VendingPrizeAnnounced(Actor, 2747462, 2)), default);
        await subscriber.HandleAsync(Envelope(new DespairTowerFloorAnnounced(Actor, 10)), default);
        Assert.Single(events.Projected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 2)]
    [InlineData(256, 0)]
    public async Task The_name_boundary_is_kept_for_native_and_text_broadcasts(int length, int expectedMessages)
    {
        var events = new ProjectedNotifications();
        var subscriber = Subscriber(events, actorName: new byte[length]);
        await subscriber.HandleAsync(Envelope(new BoxRewardAnnounced(Actor, 200, 2747462)), default);
        await subscriber.HandleAsync(Envelope(new DespairTowerFloorAnnounced(Actor, 10)), default);

        var messages = new List<IServerNetworkMessage>();
        foreach (var (_, notification) in events.Projected)
            messages.AddRange(await new ServerBroadcastPostedHandler().ComposeAsync(
                new(Actor, default), (ServerBroadcastPosted)notification, default));
        Assert.Equal(expectedMessages, messages.Count);
    }

    [Fact]
    public async Task An_announcement_with_no_online_audience_is_not_projected()
    {
        var events = new ProjectedNotifications();
        await Subscriber(events, members: _ => [])
            .HandleAsync(Envelope(new BoxRewardAnnounced(Actor, 200, 2747462)), default);
        Assert.Empty(events.Projected);
    }

    [Fact]
    public async Task All_kinds_switched_off_tell_nothing()
    {
        var events = new ProjectedNotifications();
        var subscriber = Subscriber(events, enabled: _ => false, topLevel: (_, _) => 86, secondAwakening: _ => true);
        await subscriber.HandleAsync(Envelope(new CharacterLevelChanged(Actor, 85, 86)), default);
        await subscriber.HandleAsync(Envelope(new CharacterGrowTypeChanged(Actor, 0x21)), default);
        await subscriber.HandleAsync(Envelope(new EquipmentUpgradeAnnounced(Actor, 2747462, EquipmentUpgradeKind.Reinforcement, 12, true)), default);
        await subscriber.HandleAsync(Envelope(new EquipmentUpgradeAnnounced(Actor, 2747462, EquipmentUpgradeKind.Amplification, 12, true)), default);
        await subscriber.HandleAsync(Envelope(new VendingPrizeAnnounced(Actor, 2747462, 2)), default);
        await subscriber.HandleAsync(Envelope(new BoxRewardAnnounced(Actor, 200, 2747462)), default);
        await subscriber.HandleAsync(Envelope(new EquipmentForgingAnnounced(Actor, 2747462, 12, 6, true)), default);
        await subscriber.HandleAsync(Envelope(new RecipeCraftAnnounced(Actor, 2747462, 10)), default);
        await subscriber.HandleAsync(Envelope(new DespairTowerFloorAnnounced(Actor, 10)), default);
        await subscriber.HandleAsync(Envelope(new AncientDungeonItemAnnounced(Actor, 1, 2747462)), default);
        Assert.Empty(events.Projected);
    }

    [Theory]
    [InlineData(85, 86, true)]
    [InlineData(84, 85, false)]
    public async Task A_level_rise_is_announced_when_the_rules_say_it_reached_the_top(int before, int after, bool told)
    {
        var events = new ProjectedNotifications();
        await Subscriber(events, topLevel: (b, a) => b < 86 && a == 86 ? 86 : null)
            .HandleAsync(Envelope(new CharacterLevelChanged(Actor, before, after)), default);

        Assert.Equal(told, events.Projected.Count == 1);
        if (told)
            Assert.Equal("恭喜玩家[探针测试名]达到Lv86！",
                Assert.IsType<GameServerNoticeMessage>(await Compose(events.Projected[0].Notification)).Text);
    }

    [Theory]
    [InlineData(0x21, true)]
    [InlineData(0x11, false)]
    public async Task The_second_awakening_the_rules_name_is_announced_in_the_native_frame(int growType, bool told)
    {
        var events = new ProjectedNotifications();
        await Subscriber(events, secondAwakening: grow => grow == 0x21)
            .HandleAsync(Envelope(new CharacterGrowTypeChanged(Actor, growType)), default);

        Assert.Equal(told, events.Projected.Count == 1);
        if (told)
            Assert.Equal(ProbeName, Assert.IsType<SecondAwakeningBroadcast>(await Compose(events.Projected[0].Notification)).Name);
    }

    [Fact]
    public async Task A_box_reward_keeps_the_native_frame_and_an_unnamed_item_its_identifier()
    {
        var events = new ProjectedNotifications();
        var subscriber = Subscriber(events, itemName: _ => null);

        await subscriber.HandleAsync(Envelope(new BoxRewardAnnounced(Actor, 200, 2747462)), default);
        await subscriber.HandleAsync(Envelope(new AncientDungeonItemAnnounced(Actor, 1, 2747462)), default);

        var box = Assert.IsType<BoxRewardBroadcast>(await Compose(events.Projected[0].Notification));
        Assert.Equal((200, 2747462), (box.BoxItemId, box.ItemId));
        Assert.Equal("玩家[探针测试名]在远古地下城获得了[2747462]",
            Assert.IsType<GameServerNoticeMessage>(await Compose(events.Projected[1].Notification)).Text);
    }

    [Fact]
    public async Task Item_and_tower_facts_keep_their_system_line_values()
    {
        var events = new ProjectedNotifications();
        var subscriber = Subscriber(events);
        await subscriber.HandleAsync(Envelope(new EquipmentUpgradeAnnounced(Actor, 2747462, EquipmentUpgradeKind.Amplification, 12, false)), default);
        await subscriber.HandleAsync(Envelope(new VendingPrizeAnnounced(Actor, 2747462, 2)), default);
        await subscriber.HandleAsync(Envelope(new EquipmentForgingAnnounced(Actor, 2747462, 12, 6, false)), default);
        await subscriber.HandleAsync(Envelope(new RecipeCraftAnnounced(Actor, 2747462, 10)), default);
        await subscriber.HandleAsync(Envelope(new DespairTowerFloorAnnounced(Actor, 50)), default);

        var lines = new List<string>();
        foreach (var (_, notification) in events.Projected)
        {
            var line = Assert.IsType<GameServerNoticeMessage>(await Compose(notification));
            Assert.Equal(GameServerNoticeModes.ChatLine, line.Mode);
            lines.Add(line.Text);
        }
        Assert.Equal([
            "玩家[探针测试名]增幅 +13 圣愈光剑失败",
            "[探针测试名]从黑钻售货机中获得了2个[圣愈光剑]。",
            "玩家[探针测试名]锻造 +12 圣愈光剑 第6阶段失败",
            "玩家[探针测试名]利用设计图制作了 +10 圣愈光剑",
            "恭喜玩家[探针测试名]征服了绝望之塔的第50层！",
        ], lines);
    }

    [Fact]
    public void Every_subscription_is_live_only_and_the_frames_are_registered()
    {
        var services = new ServiceCollection();
        services.AddServerBroadcast();
        services.AddServerBroadcastSlice();

        var registrations = services.Select(descriptor => descriptor.ImplementationInstance).OfType<DomainSubscriberRegistration>().ToArray();
        Assert.Equal(9, registrations.Length);
        Assert.All(registrations, registration => Assert.Equal(DomainEventDeliveryPolicy.LiveOnly, registration.Policy));
        Assert.Equal(9, registrations.Select(registration => registration.PayloadType).Distinct().Count());
        Assert.Equal(new[]
        {
            "broadcast.max-level", "broadcast.second-awakening", "broadcast.equipment-upgrade", "broadcast.vending-prize",
            "broadcast.box-reward", "broadcast.equipment-forging", "broadcast.recipe-craft", "broadcast.despair-tower-floor",
            "broadcast.ancient-dungeon-item",
        }.OrderBy(id => id, StringComparer.Ordinal), registrations.Select(registration => registration.SubscriberId).OrderBy(id => id, StringComparer.Ordinal));
    }

    private static async Task<IServerNetworkMessage> Compose(object notification) =>
        Assert.Single(await new ServerBroadcastPostedHandler().ComposeAsync(new(Actor, default), (ServerBroadcastPosted)notification, default));

    private static ServerBroadcastSubscriber Subscriber(ProjectedNotifications events,
        Func<ServerBroadcastKind, bool>? enabled = null, Func<ChatScope, IReadOnlyList<CharacterId>>? members = null,
        Func<int, string?>? itemName = null, Func<int, int, int?>? topLevel = null, Func<int, bool>? secondAwakening = null,
        byte[]? actorName = null)
    {
        var rules = TestRequired.Absent<IServerBroadcastRuleService>((method, args) => method.Name switch
        {
            nameof(IServerBroadcastRuleService.Announces) => (enabled ?? (_ => true))((ServerBroadcastKind)args![0]!),
            nameof(IServerBroadcastRuleService.TopLevelReached) => (topLevel ?? ((_, _) => null))((int)args![0]!, (int)args[1]!),
            nameof(IServerBroadcastRuleService.IsSecondAwakening) => (secondAwakening ?? (_ => false))((int)args![0]!),
            _ => null,
        });
        var character = TestRequired.Absent<ICharacter>((method, _) =>
            method.Name == nameof(ICharacter.IdentityAsync)
                ? ValueTask.FromResult<CharacterIdentityState?>(new(Actor, 0, actorName ?? ProbeName, 0, 0)) : null);
        var characters = TestRequired.Absent<ICharacterDomainService>((method, _) =>
            method.Name == nameof(ICharacterDomainService.Get) ? character : null);
        var items = TestRequired.Absent<IItemCatalogRuleService>((method, args) =>
            method.Name == nameof(IItemCatalogRuleService.ItemNameAsync)
                ? ValueTask.FromResult((itemName ?? (_ => "圣愈光剑"))((int)args![0]!)) : null);
        var scopes = TestRequired.Absent<IChatScopeDomainService>((method, args) =>
            method.Name == nameof(IChatScopeDomainService.MembersOfAsync)
                ? ValueTask.FromResult((members ?? (_ => [Actor]))((ChatScope)args![0]!)) : null);
        return new(rules, characters, items, scopes, events);
    }

    private static DomainEventEnvelope<TEvent> Envelope<TEvent>(TEvent payload) where TEvent : class, IDomainEvent =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), payload);

    private sealed class ProjectedNotifications : IEventService
    {
        public List<(NotificationAudience Audience, object Notification)> Projected { get; } = [];

        public ValueTask<Guid> EnqueueAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, IDomainEvent => throw new NotSupportedException();

        public ValueTask DirectAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
            where TEvent : class, IDomainEvent => throw new NotSupportedException();

        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience, TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification<TNotification>
        {
            Projected.Add((audience, notification));
            return ValueTask.FromResult<NotificationDelivery>(default!);
        }
    }
}
