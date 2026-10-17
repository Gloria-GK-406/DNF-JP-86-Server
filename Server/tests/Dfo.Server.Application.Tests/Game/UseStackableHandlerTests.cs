using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Inventory;

using Dfo.Server.Application.Tests.Characters;

namespace Dfo.Server.Application.Tests.Game;

/// <summary>
/// What the server does when the player drinks a potion.
/// </summary>
public sealed class UseStackableHandlerTests
{
    /// <summary>The level on the character's own row, which is what a use is judged against.</summary>
    private const byte RowLevel = 50;

    [Theory]
    [InlineData(4, 2)]
    [InlineData(4, 1)]
    [InlineData(65, 2)]
    [InlineData(65, 1)]
    public async Task Use_publishes_absolute_slot_state_after_ack(short wireSlot, int count)
    {
        var inventory = new FakeInventory(wireSlot, 8474, count, 2);
        var (handler, sender) = Build(inventory);
        await handler.HandleAsync(Context, Request(wireSlot, 8474), default);

        Assert.Equal(count - 1, inventory.Count);
        Assert.Equal(2, sender.Sent.Count);
        var ack = Assert.IsType<UseStackableResponse>(sender.Sent[0]);
        Assert.Null(ack.ErrorCode);
        Assert.Equal(wireSlot, ack.Slot.Index);
        var state = Assert.IsType<ItemUseSlotState>(ack.InventoryState);
        if (count == 1) Assert.Null(state.Item);
        else Assert.Equal(count - 1, state.Item!.Count);
        Assert.IsAssignableFrom<IGameNotiMessage>(sender.Sent[1]);
    }

    [Fact]
    public async Task Repeated_use_of_an_exhausted_slot_resends_clear_without_consuming_again()
    {
        var inventory = new FakeInventory(4, 8474, 1, 2);
        var (handler, sender) = Build(inventory);
        await handler.HandleAsync(Context, Request(4, 8474), default);
        await handler.HandleAsync(Context, Request(4, 8474), default);

        Assert.True(inventory.IsEmpty);
        Assert.Equal(4, sender.Sent.Count);
        var ack = Assert.IsType<UseStackableResponse>(sender.Sent[2]);
        Assert.Equal(UseStackableResponse.Silent, ack.ErrorCode);
        Assert.Null(Assert.IsType<ItemUseSlotState>(ack.InventoryState).Item);
        Assert.IsAssignableFrom<IGameNotiMessage>(sender.Sent[3]);
    }

    [Fact]
    public async Task Remaining_pile_projection_preserves_item_metadata()
    {
        var inventory = new FakeInventory(4, 8474, 2, 2);
        var slots = new BusinessInventorySlotDomainService(inventory.Containers(), new InventorySlotMappingRuleService());
        await using (var tx = inventory.Scope.Begin())
        {
            var bound = slots.Get(new CharacterId(60), new(BusinessInventorySpace.QuickSlot, 1));
            var item = (await bound.Container.GetAsync(1, default))!;
            await bound.Container.UpdateAsync(item, x => x with { ExpireTime = int.MaxValue, SortLockFlag = 1 }, default);
            await tx.CommitAsync(default);
        }
        var (handler, sender) = Build(inventory);
        await handler.HandleAsync(Context, Request(4, 8474), default);
        var remaining = sender.Sent.OfType<UseStackableResponse>().Single().InventoryState!.Item!;
        Assert.Equal(1, remaining.Count);
        Assert.Equal(int.MaxValue, remaining.ExpireTime);
        Assert.Equal(1, remaining.SortLockFlag);
    }

    [Fact]
    public async Task Expired_stackable_is_refused_without_consumption()
    {
        var inventory = new FakeInventory(3, 8474, 2, 2);
        var slots = new BusinessInventorySlotDomainService(inventory.Containers(), new InventorySlotMappingRuleService());
        await using (var tx = inventory.Scope.Begin())
        {
            var bound = slots.Get(new CharacterId(60), new(BusinessInventorySpace.QuickSlot, 0));
            var item = (await bound.Container.GetAsync(0, default))!;
            await bound.Container.UpdateAsync(item, x => x with { ExpireTime = 1 }, default);
            await tx.CommitAsync(default);
        }
        var (handler, sender) = Build(inventory);
        await handler.HandleAsync(Context, Request(3, 8474), default);
        Assert.Equal(2, inventory.Count);
        Assert.Equal(UseStackableResponse.NotConsumable, ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    [Fact]
    public async Task Rolled_back_use_leaves_the_item_immediately_usable()
    {
        var inventory = new FakeInventory(3, 8474, 2, 2);
        var setup = BuildDetailed(inventory, (8474, ItemUseRules.Potion()));
        var command = new UseStackableCommand(OneSession.Session,
            new BusinessSlot(BusinessInventorySpace.QuickSlot, 0), 8474);
        await using (inventory.Scope.Begin())
            Assert.Equal(ItemUseStatus.Used,
                (await setup.Command.HandleAsync(UseContext, command, default)).Status);

        Assert.Equal(2, inventory.Count);
        await using (var tx = inventory.Scope.Begin())
        {
            Assert.Equal(ItemUseStatus.Used,
                (await setup.Command.HandleAsync(UseContext, command, default)).Status);
            await tx.CommitAsync(default);
        }
        Assert.Equal(1, inventory.Count);
    }

    [Fact]
    public async Task Spirit_potion_starts_only_after_consumption_commits()
    {
        var inventory = new FakeInventory(3, 7377, 2, 2);
        var setup = BuildDetailed(inventory, (7377, SpiritRule()));

        await using (var transaction = inventory.Scope.Begin())
        {
            var result = await setup.Command.HandleAsync(UseContext,
                SpiritCommand(), CancellationToken.None);
            Assert.Equal(ItemUseStatus.Used, result.Status);
            Assert.Equal(0, setup.Potion.GetBonusRate(new CharacterId(60)));
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(1, inventory.Count);
        Assert.Equal(1, setup.Potion.GetBonusRate(new CharacterId(60)));
    }

    [Fact]
    public async Task Rolled_back_consumption_does_not_start_spirit_potion()
    {
        var inventory = new FakeInventory(3, 7377, 2, 2);
        var setup = BuildDetailed(inventory, (7377, SpiritRule()));

        await using (inventory.Scope.Begin())
        {
            var result = await setup.Command.HandleAsync(UseContext, SpiritCommand(), CancellationToken.None);
            Assert.Equal(ItemUseStatus.Used, result.Status);
        }

        Assert.Equal(2, inventory.Count);
        Assert.Equal(0, setup.Potion.GetBonusRate(new CharacterId(60)));
    }

    [Fact]
    public async Task Late_commit_after_session_exit_does_not_restore_spirit_potion()
    {
        var inventory = new FakeInventory(3, 7377, 2, 2);
        var setup = BuildDetailed(inventory, (7377, SpiritRule()));

        await using (var transaction = inventory.Scope.Begin())
        {
            var result = await setup.Command.HandleAsync(UseContext, SpiritCommand(), CancellationToken.None);
            Assert.Equal(ItemUseStatus.Used, result.Status);
            Assert.True(setup.Sessions.ReleaseExact(OneSession.Session));
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(0, setup.Potion.GetBonusRate(new CharacterId(60)));
    }

    [Fact]
    public async Task Replaced_session_rejects_old_spirit_potion_commit()
    {
        var inventory = new FakeInventory(3, 7377, 2, 2);
        var setup = BuildDetailed(inventory, (7377, SpiritRule()));

        await using (var transaction = inventory.Scope.Begin())
        {
            var result = await setup.Command.HandleAsync(UseContext, SpiritCommand(), CancellationToken.None);
            Assert.Equal(ItemUseStatus.Used, result.Status);
            setup.Sessions.Select(OneSession.Session with
            {
                ConnectionId = new NetworkConnectionId("game-replacement"),
                ConnectionGeneration = 2,
            });
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(0, setup.Potion.GetBonusRate(new CharacterId(60)));
    }

    [Fact]
    public async Task Replaced_session_before_command_rejects_old_potion_use()
    {
        var inventory = new FakeInventory(3, 7377, 2, 2);
        var setup = BuildDetailed(inventory, (7377, SpiritRule()));
        setup.Sessions.Select(OneSession.Session with
        {
            ConnectionId = new NetworkConnectionId("game-replacement"),
            ConnectionGeneration = 2,
        });

        await using (var transaction = inventory.Scope.Begin())
        {
            var result = await setup.Command.HandleAsync(UseContext,
                SpiritCommand(), CancellationToken.None);
            Assert.Equal(ItemUseStatus.NoSession, result.Status);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(2, inventory.Count);
        Assert.Equal(0, setup.Potion.GetBonusRate(new CharacterId(60)));
    }

    [Fact]
    public async Task Shared_effect_checks_real_item_and_rule_consumes_once_and_rolls_back()
    {
        var inventory = new FakeInventory(3, 8474, 2, 2);
        var setup = BuildDetailed(inventory, (8474, ItemUseRules.Potion(cooldownMilliseconds: 0) with { SharedEffectSelector = 0 }));
        var slot = new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, 3));
        await using (var tx = inventory.Scope.Begin())
        {
            Assert.False((await setup.Shared.UseAsync(OneSession.Session, slot, 9000, default)).Succeeded);
            Assert.Equal(2, inventory.Count);
            var result = await setup.Shared.UseAsync(OneSession.Session, slot, 8474, default);
            Assert.True(result.Succeeded); Assert.Equal(1, result.State!.Item!.Count);
        }
        Assert.Equal(2, inventory.Count);
        await using (var tx = inventory.Scope.Begin())
        {
            Assert.True((await setup.Shared.UseAsync(OneSession.Session, slot, 8474, default)).Succeeded);
            await tx.CommitAsync(default);
        }
        Assert.Equal(1, inventory.Count);
        var ordinary = BuildDetailed(inventory, (8474, ItemUseRules.Potion(cooldownMilliseconds: 0)));
        await using (var tx = inventory.Scope.Begin())
            Assert.False((await ordinary.Shared.UseAsync(OneSession.Session, slot, 8474, default)).Succeeded);
        Assert.Equal(1, inventory.Count);
    }

    private static ItemUseRule SpiritRule() => ItemUseRules.Potion(cooldownMilliseconds: 0) with
    {
        ExpBonusRate = 1,
        EffectDurationMilliseconds = 7_200_000,
    };

    private static UseStackableCommand SpiritCommand() =>
        new(OneSession.Session, new BusinessSlot(BusinessInventorySpace.QuickSlot, 0), 7377);

    private static readonly CommandExecutionContext UseContext = new(
        Guid.Empty, typeof(UseStackableCommand), typeof(UseStackableCommandHandler),
        CommandExecutionKind.Transactional);

    [Fact]
    public async Task One_item_leaves_the_pile()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 2, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Null(((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    [Fact]
    public async Task A_large_pile_loses_only_one()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 99, itemKind: 2);
        var (handler, _) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(98, inventory.Count);
    }

    [Fact]
    public async Task The_last_of_a_pile_empties_the_slot()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.True(inventory.IsEmpty);
        Assert.Null(((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    /// <remarks>
    /// The client names what it believes it is using. A slot holding something else means the two
    /// disagree, and deleting anyway would settle that in favour of whichever one is wrong.
    /// <para>
    /// Answered with a code the client draws nothing for. Every sentence it owns for this type
    /// blames the player, and a disagreement about what is in a slot is not something they did.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_slot_holding_a_different_item_is_refused_without_a_message()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 9000, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.Silent,
            ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
        Assert.Equal(9000, sender.Sent.OfType<UseStackableResponse>().Single().InventoryState!.Item!.ItemId);
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task An_empty_slot_is_refused_without_a_message()
    {
        var (handler, sender) = Build(new FakeInventory(slot: 3, itemId: 8474, count: 0, itemKind: 2));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(
            UseStackableResponse.Silent,
            ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    /// <remarks>
    /// Gear can be dragged onto a quick slot like anything else, and a key press there used to
    /// destroy it one press at a time. It is turned away because the archive lists no usable item
    /// under its identifier -- equipment is not in the stackable index at all -- and the client's
    /// own sentence for code 23 is 「非消耗品， 无法使用」.
    /// </remarks>
    [Fact]
    public async Task Equipment_is_refused_as_not_consumable()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 10000, count: 1, itemKind: 1);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(Context, Request(3, 10000), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.NotConsumable,
            ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    [Fact]
    public async Task A_list_other_than_the_main_one_is_refused()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory);

        await handler.HandleAsync(
            Context,
            Request(3, 8474) with
            {
                Slot = new GameSlot(InventoryContainerType.Pet, 3),
            },
            CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.NotConsumable,
            ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    /// <remarks>
    /// Sixty-four <c>[unlimited waste]</c>, eighteen <c>[unlimited town and dungeon]</c> and two
    /// <c>[unlimited etc]</c>: eighty-four items the server used to destroy one press at a time.
    /// They are not exempted from the decrement -- they produce no decrement to be exempted from.
    /// </remarks>
    [Fact]
    public async Task An_unlimited_item_is_used_without_leaving_the_pile()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory, (8474, ItemUseRules.Unlimited()));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Null(((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    /// <remarks>
    /// 6,254 items declare a <c>[cool time]</c> and none of them was ever enforced, so a potion
    /// could be drained by holding the key down.
    /// </remarks>
    [Fact]
    public async Task A_second_use_inside_the_cooldown_is_refused()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 5, itemKind: 2);
        var (handler, sender) = Build(inventory, (8474, ItemUseRules.Potion()));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);
        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(4, inventory.Count);
        Assert.Equal(
            UseStackableResponse.CurrentlyUnusable,
            ((UseStackableResponse)sender.Sent.Last()).ErrorCode);
    }

    /// <summary>
    /// Using one member of a cooltime group makes the others wait.
    /// </summary>
    /// <remarks>
    /// The key that starts a wait and the key that is checked for one are worked out in two
    /// different places -- the effects start it, the judgement asks about it -- and they now come
    /// from the same method on the same service. Nothing said so: the item's own identifier is the
    /// key when a group is not declared, so an item-keyed test passes even if the two disagree
    /// about groups entirely, and an item could then be drained through its own group.
    /// </remarks>
    [Fact]
    public async Task Using_one_member_of_a_group_makes_the_others_wait()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 5, itemKind: 2);
        var (handler, sender) = Build(
            inventory,
            (8474, ItemUseRules.Potion(3_000, cooltimeGroup: 70)),
            (9000, ItemUseRules.Potion(3_000, cooltimeGroup: 70)));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);
        inventory.Reseed(slot: 3, itemId: 9000, count: 5, itemKind: 2);
        await handler.HandleAsync(Context, Request(3, 9000), CancellationToken.None);

        Assert.Equal(
            UseStackableResponse.CurrentlyUnusable,
            ((UseStackableResponse)sender.Sent.Last()).ErrorCode);
    }

    /// <remarks>
    /// The cooldown belongs to the effects, which run after the use is allowed. Started by the
    /// judgement instead, a refused press would lock the player out of an item they still hold.
    /// </remarks>
    [Fact]
    public async Task A_refused_use_does_not_start_the_cooldown()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 5, itemKind: 2);
        var (handler, sender) = Build(inventory, (8474, ItemUseRules.RequiringLevel(99)));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);
        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(5, inventory.Count);
        Assert.All(
            sender.Sent,
            message => Assert.Equal(
                UseStackableResponse.LevelTooLow, ((UseStackableResponse)message).ErrorCode));
    }

    /// <remarks>
    /// The client can say four different things. Answering every refusal with one of them, which
    /// is what the server used to do, throws away the part the player could act on.
    /// </remarks>
    [Fact]
    public async Task A_wrong_job_and_too_low_a_level_are_told_apart()
    {
        var wrongJob = Build(
            new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2),
            (8474, ItemUseRules.RequiringJob(QuestJobRequirement.Priest)));
        var tooLow = Build(
            new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2),
            (8474, ItemUseRules.RequiringLevel(99)));

        await wrongJob.Handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);
        await tooLow.Handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(
            UseStackableResponse.JobMismatch,
            ((UseStackableResponse)wrongJob.Sender.Sent.Single()).ErrorCode);
        Assert.Equal(
            UseStackableResponse.LevelTooLow,
            ((UseStackableResponse)tooLow.Sender.Sent.Single()).ErrorCode);
    }

    /// <remarks>
    /// The client owns no sentence for either the cooldown or the place, so both land on 19,
    /// 「目前无法使用物品」 -- which is true of both and is the closest true thing it can say.
    /// </remarks>
    [Fact]
    public async Task An_item_only_usable_underground_is_refused_in_town()
    {
        var inventory = new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2);
        var (handler, sender) = Build(inventory, (8474, ItemUseRules.UsableIn(ItemUsePlace.Dungeon)));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(1, inventory.Count);
        Assert.Equal(
            UseStackableResponse.CurrentlyUnusable,
            ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).ErrorCode);
    }

    /// <remarks>
    /// The reference server echoes it back untouched. Inventing a value here would answer a
    /// question the client did not ask.
    /// </remarks>
    [Fact]
    public async Task The_instance_value_comes_back_unchanged()
    {
        var (handler, sender) = Build(new FakeInventory(slot: 3, itemId: 8474, count: 1, itemKind: 2));

        await handler.HandleAsync(Context, Request(3, 8474), CancellationToken.None);

        Assert.Equal(25333, ((UseStackableResponse)sender.Sent.OfType<UseStackableResponse>().Single()).InstanceValue);
    }

    private static readonly NetworkDispatchContext Context = new(
        new NetworkConnectionContext(new NetworkConnectionId("game-use"), NetworkEndpointKind.Game, 10011));

    private static UseStackableRequest Request(short slot, int itemCode) =>
        new(GameMessageMetadata.Empty,
            new GameSlot(InventoryContainerType.Main, slot), 25333, itemCode);

    private static (UseStackableHandler Handler, RecordingSender Sender) Build(
        FakeInventory inventory,
        params (int ItemId, ItemUseRule Rule)[] rules)
    {
        var setup = BuildDetailed(inventory, rules);
        return (setup.Handler, setup.Sender);
    }

    private static (UseStackableHandler Handler, RecordingSender Sender,
        UseStackableCommandHandler Command, SpiritPotionDomainService Potion,
        OneSession Sessions, ISharedEffectDomainService Shared) BuildDetailed(
        FakeInventory inventory, params (int ItemId, ItemUseRule Rule)[] rules)
    {
        var declared = rules.Length == 0
            ? [(8474, ItemUseRules.Potion(cooldownMilliseconds: 0))]
            : rules;
        var content = new FakeItemUseRules(declared);
        var clock = new AdvanceableTimeProvider(
            new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        var cooldowns = new CharacterItemCooldownDomainService(clock);
        var potion = new SpiritPotionDomainService(clock);
        var sessions = new OneSession();
        var sender = new RecordingSender();

        var judgement = new ItemUseJudgementService(content);
        var mappings = new InventorySlotMappingRuleService();
        var containers = inventory.Containers();
        var slots = new BusinessInventorySlotDomainService(containers, mappings);
        var characters = new OneCharacter(new PlayingCharacter(
            new CharacterId(60), judgement, cooldowns,
            PresentCharacters.StandingInTown(new CharacterId(60)), new NoCarriedGold(),
            new StoredCarryLimitOnly(), CharacterRow.At(RowLevel),
            new NoSkillPointLedger(), null!, null!, experienceRules: new Dfo.Server.Application.Tests.Support.TestExperienceRules()));
        var apply = new Dfo.Server.Application.Effects.ApplyEffectsDomainService([
            new Dfo.Server.Application.Effects.ConsumeFromContainerEffectHandler(slots, inventory.Lifecycle),
            new Dfo.Server.Application.Effects.StartItemCooldownEffectHandler(cooldowns, sessions, inventory.Scope),
            new Dfo.Server.Application.Effects.StartSpiritPotionEffectHandler(potion, sessions, inventory.Scope)], inventory.Scope,
                new EffectCharacterStore(new(OneSession.Session.AccountId, OneSession.Session.CharacterId)), sessions, ConsumptionEffectPreflight.For(slots));
        var ruleService = new ItemUseEffectRuleService(judgement);
        var useEffects = new ItemUseEffectDomainService(characters, ruleService, apply, null!, clock);
        var commandHandler = new UseStackableCommandHandler(sessions, slots, useEffects, clock);
        var areas = new Dfo.Server.Application.World.TownAreaDomainService();
        areas.Enter(new(OneSession.Session.CharacterId, OneSession.Session.TownAreaLease(), new(1, 1, 1), new(1, 1, 0, 0, 0), true));
        var shared = new SharedEffectDomainService(sessions, areas, slots, ruleService, useEffects, clock);
        return (
            new UseStackableHandler(
                sessions,
                mappings,
                new DirectCommandBus(use: commandHandler, scope: inventory.Scope),
                sender,
                NullLogger<UseStackableHandler>.Instance),
            sender, commandHandler, potion, sessions, shared);
    }


    /// <summary>The character's main container, holding one pile in one slot.</summary>
    private sealed class FakeInventory : FakeMainInventory
    {
        private readonly int slot;

        public FakeInventory(int slot, int itemId, int count, byte itemKind)
        {
            this.slot = slot;
            if (count > 0)
                this.Seed(slot, itemId, count, itemKind);
        }

        public int Count => this.CountIn(this.slot);

        /// <summary>Puts something else in the slot, for a test that uses two items in turn.</summary>
        public void Reseed(int slot, int itemId, int count, byte itemKind) =>
            this.Seed(slot, itemId, count, itemKind);

        public bool IsEmpty => this.Count == 0;
    }

    private sealed class OneSession : ISelectedSessionDomainService
    {
        internal static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-use"), 1, 10011);

        private SelectedSessionIdentity? current = Session;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
        {
            var previous = current;
            current = session;
            return previous;
        }
        public bool ReleaseExact(SelectedSessionIdentity session)
        {
            if (current != session) return false;
            current = null;
            return true;
        }
        public SelectedSessionIdentity? Find(AccountId accountId) => current;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => current;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => current;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => current;
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The use acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The use acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}
