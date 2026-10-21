using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class QuestInventoryEventTests
{
    private static OrdinaryQuestEffectTests.Rules Content()
    {
        var rules = new OrdinaryQuestEffectTests.Rules();
        rules.Quest = rules.Quest with { ObjectiveTypeCode = "seeking", SeekingItems = [new(1000, 10)] };
        return rules;
    }

    private static ContainerId Bag => ContainerId.ForCharacter(new(71), InventoryContainerType.Main);
    private static int Trigger(LearningFixture f, int quest = 2001, int character = 71) =>
        f.Scalar($"SELECT trigger_value FROM character_active_quests WHERE character_id={character} AND quest_id={quest}");
    private static int Notifications(LearningFixture f) => f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'");
    private static async Task Drain(LearningFixture f) =>
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("quest.inventory");
    private static async Task Write(LearningFixture f, Func<IItemDomainStoreSession, ValueTask> write, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        await write(f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!));
        if (commit) await tx.CommitAsync(default);
    }
    private static async ValueTask Put(IItemDomainStoreSession items, ContainerId container, int slot, int id, int count) =>
        _ = await items.PutAsync(container, slot, await items.CreateAsync(new() { ItemId = id, Count = count,
            IsPile = true, LegacyKind = 2, Type = ItemTraitClassification.For(2, id) }, default), default);

    [Fact]
    public async Task Relevant_changes_update_partial_progress_and_regress_ready_quests_but_ignore_unrelated_quests()
    {
        var content = Content();
        content.Others.Add(content.Quest with { QuestId = 2002, SeekingItems = [new(2000, 2)] });
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,10),(71,1,2002,99)");
        await Write(f, items => Put(items, Bag, 121, 1000, 4));
        await Drain(f);
        Assert.Equal(6, Trigger(f));
        Assert.Equal(99, Trigger(f, 2002)); // Deliberately stale unrelated row must not be calibrated.
        await Write(f, items => Put(items, Bag, 121, 1000, 6));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        await Write(f, async items => await items.DeleteItemAsync(
            await items.TakeAsync((await items.GetAsync(Bag, 121, default))!, 1, default), default));
        await Drain(f);
        Assert.Equal(1, Trigger(f));
        Assert.Equal(3, Notifications(f));
        await Write(f, items => Put(items, Bag, 122, 3000, 2));
        await Drain(f);
        Assert.Equal(99, Trigger(f, 2002));
        Assert.Equal(3, Notifications(f));
    }

    [Fact]
    public async Task Multiple_changed_ids_are_not_lost_and_same_item_moves_or_replays_do_not_notify()
    {
        var content = Content();
        content.Others.Add(content.Quest with { QuestId = 2002, SeekingItems = [new(2000, 2)] });
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,10),(71,1,2002,2)");
        await Write(f, async items =>
        {
            await Put(items, Bag, 121, 1000, 10);
            await Put(items, Bag, 122, 2000, 2);
            var current = (await items.GetAsync(Bag, 121, default))!;
            await items.UpdateAsync(current, x => x with { Count = 11 }, default);
        });
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='inventory.contents-changed'"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(DISTINCT j.value) FROM domain_events e, json_each(e.payload_json,'$.ChangedItemIds') j WHERE e.event_type='inventory.contents-changed'"));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        Assert.Equal(0, Trigger(f, 2002));
        var before = Notifications(f);
        await Write(f, async items =>
        {
            var item = await items.TakeAsync((await items.GetAsync(Bag, 121, default))!, null, default);
            await items.PutAsync(Bag, 123, item, default);
        });
        await Drain(f);
        f.Sql("UPDATE domain_event_deliveries SET state=0 WHERE subscriber_id='quest.inventory'");
        await Drain(f);
        Assert.Equal(before, Notifications(f));
        Assert.Equal(11, f.Scalar("SELECT count FROM item_slots WHERE owner_id=71 AND slot_index=123"));
    }

    [Fact]
    public async Task Shared_material_changes_refresh_all_account_characters_without_touching_other_accounts()
    {
        var content = Content();
        content.Quest = content.Quest with { SeekingItems = [new(3034, 10)] };
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await f.SeedAsync();
        f.Sql("INSERT INTO characters(character_id,account_id,slot_index,name,job,level) VALUES(73,1,1,'same account',12,85); INSERT INTO character_active_quests VALUES(71,0,2001,10),(72,0,2001,10),(73,0,2001,10)");
        await Write(f, items => Put(items, ContainerId.ForAccount(new(1), InventoryContainerType.SharedMaterial), 0, 3034, 10));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        Assert.Equal(0, Trigger(f, character: 73));
        Assert.Equal(10, Trigger(f, character: 72));
        Assert.Equal(2, Notifications(f));
    }

    [Fact]
    public async Task Rollback_produces_no_fact_restart_recovers_queued_changes_and_settled_quests_stay_removed()
    {
        var content = Content();
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,10)");
        await Write(f, items => Put(items, Bag, 121, 1000, 10), commit: false);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        await Write(f, items => Put(items, Bag, 121, 1000, 10));
        await using var restarted = new LearningFixture(existing: f.Path, configure: s => s.AddSingleton<IQuestContentRuleService>(content));
        await Drain(restarted);
        Assert.Equal(0, Trigger(f));
        f.Sql("DELETE FROM character_active_quests WHERE character_id=71; INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(71,2001,1)");
        await Write(restarted, async items => await items.DeleteItemAsync(
            await items.TakeAsync((await items.GetAsync(Bag, 121, default))!, null, default), default));
        await Drain(restarted);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        Assert.Equal(1, Notifications(f));
    }

    [Fact]
    public async Task Failed_consumption_rolls_back_progress_and_retries_with_a_single_notification()
    {
        var clock = new AdvanceableTimeProvider(DateTimeOffset.UtcNow);
        await using var f = new LearningFixture(configure: s =>
        {
            s.AddSingleton<IQuestContentRuleService>(Content());
            s.AddSingleton<TimeProvider>(clock);
        });
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,10)");
        await Write(f, items => Put(items, Bag, 121, 1000, 10));
        f.Sql("CREATE TRIGGER fail_inventory_quest BEFORE UPDATE ON character_active_quests BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Drain(f);
        Assert.Equal(10, Trigger(f));
        Assert.Equal(0, Notifications(f));
        Assert.Equal(1, f.Scalar("SELECT attempt_count FROM domain_event_deliveries WHERE subscriber_id='quest.inventory'"));
        f.Sql("DROP TRIGGER fail_inventory_quest");
        clock.Advance(TimeSpan.FromMinutes(1));
        await Drain(f);
        Assert.Equal(0, Trigger(f));
        Assert.Equal(1, Notifications(f));
    }

    [Fact]
    public async Task Legacy_payload_without_item_ids_uses_full_material_check_while_warehouse_changes_are_ignored()
    {
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(Content()));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,99)");
        await Write(f, items => Put(items, ContainerId.ForCharacter(new(71), InventoryContainerType.PersonalCargo), 0, 1000, 10));
        await Drain(f);
        Assert.Equal(99, Trigger(f));
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<IEventService>().EnqueueAsync(new InventoryContentsChanged(Bag));
            await tx.CommitAsync(default);
        }
        // Reproduce the old persisted JSON contract, not just a new payload with an empty array.
        f.Sql("UPDATE domain_events SET payload_json=json_remove(payload_json,'$.ChangedItemIds') WHERE event_type='inventory.contents-changed'");
        await Drain(f);
        Assert.Equal(10, Trigger(f));
        Assert.Equal(1, Notifications(f));
    }

    [Fact]
    public async Task Mail_claim_updates_items_and_queues_one_encoded_quest_notification_only_when_progress_changes()
    {
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<IQuestContentRuleService>(Content());
            s.AddSingleton<IMailItemDefinitionSource>(new FreeMailItems());
        });
        f.Sql("INSERT INTO character_active_quests VALUES(1,0,2001,10)");
        f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(f.Session);
        var mail = f.Services.GetRequiredService<IMailboxDomainService>();
        var sent = await mail.SendAsync(MailBuilder.FromSystem(new(1)).AddStackable(1000, 10).Build());
        var attachment = Assert.Single((await mail.ReadAsync(new(1), sent.MailId)).Attachments);
        Assert.Equal(0, f.Count(1000));
        await mail.ClaimAsync(new(1), attachment.Id);
        Assert.Equal(10, f.Count(1000));
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await dispatcher.DrainOnceAsync("quest.inventory");
        await dispatcher.DrainOnceAsync("quest.progress-notification");
        var registrations = f.Services.GetServices<ServerMessageEncoderRegistration>();
        var frames = f.Sender.Messages.SelectMany(message =>
        {
            var encoder = registrations.Single(x => x.MessageType == message.GetType());
            return ((IServerMessageEncoder)f.Services.GetRequiredService(encoder.EncoderType)).Encode(message);
        }).Where(frame => BitConverter.ToUInt16(frame, 1) == 0x023F).ToArray();
        var frame = Assert.Single(frames);
        Assert.Equal(0, frame[0]);
        Assert.Equal(2001, BitConverter.ToUInt16(frame, 19));
        Assert.Equal(0u, BitConverter.ToUInt32(frame, 21));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
    }

    private sealed class FreeMailItems : IMailItemDefinitionSource
    {
        public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<MailItemDefinition?>(new(MailItemAttachType.Free, "free", null));
    }

    [Fact]
    public async Task Committed_item_receipt_refreshes_accepted_quest_without_online_container_bindings()
    {
        await using var f = new LearningFixture(configure: s => s.AddSingleton<IQuestContentRuleService>(Content()));
        await f.SeedAsync();
        f.Sql("INSERT INTO character_active_quests VALUES(71,0,2001,10)");
        await using (var tx = f.Scope.Begin())
        {
            var items = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var item = await items.CreateAsync(new() { ItemId = 1000, Count = 10, IsPile = true,
                LegacyKind = 2, Type = ItemTraits.Consumable }, default);
            await items.PutAsync(ContainerId.ForCharacter(new(71), InventoryContainerType.Main), 121, item, default);
            await tx.CommitAsync(default);
        }
        Assert.Equal(10, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
        await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync();
        Assert.Equal(0, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
    }
}
