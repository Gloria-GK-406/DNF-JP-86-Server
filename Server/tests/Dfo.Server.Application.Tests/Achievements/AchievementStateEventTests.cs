using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.Achievements;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Achievements;

public sealed partial class AchievementTitleBookTests
{
    [PvfFact]
    public async Task Quest_completion_checks_final_career_without_relogin()
    {
        await using var f = new Fixture(definitions: new FakeDefinitions([
            new(9001, 1, 99, new("first grow", -1, [], 1), []),
            new(9002, 1, 99, new("awakening", -1, [], 1), [])]));
        f.Sql("UPDATE characters SET grow_type=17 WHERE character_id=1");
        await f.WriteAsync(async () => await f.Events.EnqueueAsync(new QuestCompleted(Character, 123)));
        await f.DrainAsync();
        Assert.Equal(2, (await f.Achievements.GetAsync(Character, default)).Count(x => x.P1 == 0));
    }

    [PvfFact]
    public async Task Inventory_commit_unlocks_possession_without_relogin_or_consuming_materials()
    {
        await using var f = new Fixture();
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            foreach (var (id, slot) in new[] { (7463, 65), (7464, 66) })
            {
                var item = await s.CreateAsync(new() { ItemId = id, Count = 1000, IsPile = true,
                    LegacyKind = 2, Type = ItemTraits.Consumable }, default);
                await s.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Main), slot, item, default);
            }
        });
        await f.DrainAsync();
        Assert.Single(f.ItemUids(26688));
        Assert.Equal(2000, f.Scalar("SELECT SUM(count) FROM item_slots WHERE list_type=0"));
        await f.DrainAsync();
        Assert.Single(f.ItemUids(26688));
    }

    [PvfFact]
    public async Task Contents_events_merge_within_transaction_and_rollback_with_item_writes()
    {
        await using var f = new Fixture();
        var container = ContainerId.ForCharacter(Character, InventoryContainerType.Main);
        await using (var tx = f.Scope.Begin())
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            await s.PutAsync(container, 65, await s.CreateAsync(Material(7463, 1000), default), default);
        }
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events"));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM item_slots"));
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var item = await s.PutAsync(container, 65, await s.CreateAsync(Material(7463, 500), default), default);
            await s.PutAsync(container, 65, await s.CreateAsync(Material(7463, 500), default), default);
            var current = (await s.GetAsync(container, 65, default))!;
            await s.UpdateAsync(current, x => x with { Count = 1001 }, default);
        });
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='inventory.contents-changed'"));
        var before = f.Scalar("SELECT COUNT(*) FROM domain_events");
        _ = await f.Achievements.GetAsync(Character, default);
        _ = await f.Titles.GetAsync(Character, default);
        Assert.Equal(before, f.Scalar("SELECT COUNT(*) FROM domain_events"));
    }

    [PvfFact]
    public async Task Warehouse_changes_do_not_unlock_possession_but_transfer_to_bag_does()
    {
        await using var f = new Fixture(definitions: new FakeDefinitions([
            new(9001, 1, 99, new("seeking", -1, [7463, 1000], 1), [])]));
        var cargo = ContainerId.ForCharacter(Character, InventoryContainerType.PersonalCargo);
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            await s.PutAsync(cargo, 0, await s.CreateAsync(Material(7463, 1000), default), default);
        });
        await f.DrainAsync();
        Assert.Empty(await f.Achievements.GetAsync(Character, default));
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var record = await s.TakeAsync((await s.GetAsync(cargo, 0, default))!, null, default);
            await s.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Main), 65, record, default);
        });
        await f.DrainAsync();
        Assert.Equal(0, (await f.Achievements.GetAsync(Character, default)).Single().P1);
    }

    [PvfFact]
    public async Task Shared_material_event_checks_all_owned_characters_and_replay_is_idempotent()
    {
        await using var f = new Fixture(definitions: new FakeDefinitions([
            new(9001, 1, 99, new("seeking", -1, [7463, 1000], 1), []),
            new(9002, 1, 99, new("clear quest", -1, [9001], 1), []),
            new(9003, 1, 99, new("clear quest", -1, [9002], 1), [])]));
        f.Sql("INSERT INTO characters(character_id,account_id,slot_index,name,job,level,exp) VALUES(2,1,1,'second',0,80,0)");
        var container = ContainerId.ForAccount(new AccountId(1), InventoryContainerType.SharedMaterial);
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            await s.PutAsync(container, 0, await s.CreateAsync(Material(7463, 1000), default), default);
        });
        await f.DrainAsync();
        foreach (var character in new[] { Character, new CharacterId(2) })
        {
            var progress = await f.Achievements.GetAsync(character, default);
            Assert.Equal(3, progress.Count);
            Assert.All(progress, x => Assert.Equal(0, x.P1));
        }
        var envelope = new DomainEventEnvelope<InventoryContentsChanged>(Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, Guid.NewGuid(), new(container));
        var before = f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='achievement.completed'");
        for (var i = 0; i < 2; i++)
            await f.WriteAsync(async () => await f.Services.GetRequiredService<AchievementDomainService>().HandleAsync(envelope, default));
        Assert.Equal(before, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='achievement.completed'"));
    }

    [PvfFact]
    public async Task Avatar_and_worn_equipment_changes_unlock_only_relevant_conditions()
    {
        await using var f = new Fixture(definitions: new FakeDefinitions([
            new(9001, 1, 99, new("seeking", -1, [-7, 1], 1), []),
            new(9002, 1, 99, new("equipped item", 0, [3, 55], 1), []) { EquipmentSlots = ["weapon"] },
            new(9003, 1, 99, new("level up", -1, [70], 1), [])]));
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            await s.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Avatar), 0,
                await s.CreateAsync(new() { ItemId = 500, LegacyKind = 8, Type = ItemTraits.Avatar,
                    InstanceValue = 12345, AvatarDetail = new(0, 0, new byte[30], 0, 0, 0) }, default), default);
        });
        await f.DrainAsync();
        Assert.Equal(new[] {9001}, (await f.Achievements.GetAsync(Character, default)).Where(x => x.P1 == 0).Select(x => x.AchievementId));
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            await s.PutAsync(ContainerId.ForCharacter(Character, InventoryContainerType.Equipment), 12,
                await s.CreateAsync(new() { ItemId = 2747466, LegacyKind = 1, Type = ItemTraits.Equipment }, default), default);
        });
        await f.DrainAsync();
        Assert.Equal(new[] {9001,9002}, (await f.Achievements.GetAsync(Character, default)).Where(x => x.P1 == 0).Select(x => x.AchievementId).Order());
    }

    [PvfFact]
    public async Task Creature_store_transformation_reports_hatching_even_when_it_opens_item_facet_first()
    {
        await using var f = new Fixture(definitions: new FakeDefinitions([
            new(9001, 1, 99, new("seeking", 25, [-7, 1], 1), [])]));
        var bag = ContainerId.ForCharacter(Character, InventoryContainerType.Pet);
        int uid = 0;
        await f.WriteAsync(async () =>
        {
            var s = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(f.Scope.Current!);
            var egg = await s.PutAsync(bag, 0, await s.CreateAsync(new() { ItemId = 63022,
                LegacyKind = 5, Type = ItemTraits.Creature,
                CreatureDetail = new(0, 0, 1, 0, 0, 0, [], 0) }, default), default);
            uid = egg.ItemUid!.Value.Value;
        });
        await f.DrainAsync();
        Assert.DoesNotContain(await f.Achievements.GetAsync(Character, default), x => x.P1 == 0);
        await f.WriteAsync(async () =>
        {
            var pets = f.Services.GetRequiredService<ICreatureDomainStore>().SessionIn(f.Scope.Current!);
            var egg = (await pets.FindItemAsync(Character, uid, default))!;
            var creature = (await pets.ReadAsync(Character, uid, default))!;
            await pets.WriteAsync(egg, creature with { ItemTemplateId = 63000 }, null, default);
        });
        await f.DrainAsync();
        Assert.Equal(0, (await f.Achievements.GetAsync(Character, default)).Single().P1);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='inventory.contents-changed'"));
    }

    private static ItemSnapshot Material(int id, int count) => new()
    {
        ItemId = id, Count = count, IsPile = true, LegacyKind = 2, Type = ItemTraits.Consumable,
    };
}
