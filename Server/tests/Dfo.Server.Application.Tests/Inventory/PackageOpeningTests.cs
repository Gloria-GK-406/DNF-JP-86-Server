using Dfo.Server.Abstractions.DomainEvents;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Dfo.Server.Protocol.Game.MagicBoxSingleOpening;
using Dfo.Server.Protocol.Game.PackageOpening;
using Dfo.Server.Protocol.Game.AvatarPackageOpening;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class PackageOpeningTests
{
    [Fact]
    public async Task Prepared_shared_rewards_use_the_fixed_slot_and_preserve_declared_expiry()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(3037, 2)]);
        opening.Content.Items[3037] = new(3037, new(11, 1000, new(BusinessInventorySpace.Material, 0, 55)),
            0, false, [], null, true, DefaultPeriodDays: 2);
        await opening.Host.Seed(80, 1001, 1);
        await opening.Package();
        Assert.Equal(0, opening.Host.Count(3037));
        Assert.Equal(2, opening.Host.SharedCount(3037));
        var item = (await opening.Host.Shared().GetAsync(4, default))!;
        Assert.Equal((int)opening.Host.Clock.GetUtcNow().AddDays(2).ToUnixTimeSeconds(), item.Snapshot.ToCore().ExpireTime);
        Assert.Equal(0, opening.Host.Count(1001));
    }

    [Fact]
    public async Task Permanent_source_remains_usable_after_2038()
    {
        using var opening = new OpeningFixture();
        await opening.Host.SeedCore(80, StackableItemCore.Create(2, 1001, 1) with { ExpireTime = int.MaxValue });
        opening.Host.Clock.Advance(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero) - opening.Host.Clock.GetUtcNow());
        await opening.Package();
        Assert.Equal(0, opening.Host.Count(1001));
        Assert.Equal(2, opening.Host.Count(3));
    }

    [Fact]
    public async Task Reusable_package_grants_and_starts_cooldown_without_consuming_itself()
    {
        using var opening = new OpeningFixture();
        opening.Content.UseRule = opening.Content.UseRule with { IsConsumedOnUse = false, CooldownMilliseconds = 5000 };
        await opening.Host.Seed(80, 1001, 1);
        await opening.Package();
        Assert.Equal(1, opening.Host.Count(1001));
        Assert.Equal(2, opening.Host.Count(3));
        var refusal = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Package());
        Assert.Equal(OpeningRejection.ItemUnusable, refusal.Reason);
        Assert.Equal(1, opening.Host.Count(1001));
        Assert.Equal(2, opening.Host.Count(3));
    }

    [Fact]
    public async Task Emblem_band_cannot_be_used_as_a_package_source_and_writes_nothing()
    {
        using var opening = new OpeningFixture();
        var before = opening.Host.Scalar("SELECT COUNT(*) FROM item_slots");

        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() =>
            opening.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
                new PackageOpeningCommand(opening.Host.OnlineUser, opening.Host.Session,
                    new(289, ImmutableArray<int>.Empty))).AsTask());

        Assert.Equal(OpeningRejection.InvalidRequest, error.Reason);
        Assert.Equal(before, opening.Host.Scalar("SELECT COUNT(*) FROM item_slots"));
    }

    [SkippableFact]
    public async Task Actual_seria_pool_has_readable_reward_creation_definitions()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var content = shop.Host.Services.GetRequiredService<IPackageOpeningDefinitionSource>();
        var definition = await content.GetAsync(2682272, default);
        Assert.NotNull(definition); Assert.Empty(definition.Diagnostics); Assert.True(definition.UsesSeriaLuck);
        var problems = new List<string>();
        foreach (var id in definition.Pools.SelectMany(p => p.Choices).Select(r => r.Reward.ItemId).Distinct())
        {
            var item = await content.GetRewardAsync(id, default);
            if (item is { Supported: true }) continue;
            var shopItem = await shop.Host.Services.GetRequiredService<ICeraShopDefinitionSource>().GetItemAsync(id, default);
            problems.Add($"{id}: {shopItem?.Kind}: {string.Join(';', shopItem?.Diagnostics.Select(d => d.Code + '=' + d.Detail) ?? [])}");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        var allRewards = definition.Pools.SelectMany(pool => pool.Choices).Select(row => row.Reward).Distinct().ToArray();
        var prepared = await shop.Host.Services.GetRequiredService<IPackageOpeningRuleService>().PrepareAsync(allRewards, shop.Host.Clock.GetUtcNow(), default);
        Assert.Equal(allRewards.Length, prepared.Length);
    }

    [SkippableFact]
    public async Task Actual_seria_batch_persists_sixty_openings_with_luck_and_account_rewards()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true, configure: services => services.AddSingleton<IPackageOpeningRuleService>(
            provider => new DeterministicOpeningRules(new PackageOpeningRuleService(provider.GetRequiredService<IPackageOpeningDefinitionSource>(), provider.GetRequiredService<IItemPlacementRuleService>()))));
        await shop.Host.Seed(66, 2682272, 60);
        var result = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new MagicBoxBatchOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, new(4, 66, 2682272, -1, -1, 60)));
        Assert.NotNull(result.Receipt); Assert.Equal(0, shop.Host.Count(2682272));
        Assert.Equal(0, result.LuckBefore); Assert.Equal(4, result.LuckAfter);
        Assert.Equal(4, shop.Host.Scalar("SELECT seria_luck_value FROM accounts WHERE account_id=1"));
        Assert.NotEmpty(result.Primary); Assert.NotEmpty(result.Bonus);
        Assert.Equal(100000, shop.Cera); Assert.Equal(100000, shop.Host.Gold);
    }

    [SkippableTheory]
    [InlineData(10008025, 9, 2015)]
    [InlineData(10015265, 5, 2026)]
    public async Task Actual_avatar_packages_create_every_selected_instance_and_leave_child_packages_closed(int itemId, int avatarCount, int year)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        if (year == 2015) shop.Host.Clock.Advance(new DateTimeOffset(2015, 8, 1, 0, 0, 0, TimeSpan.Zero) - shop.Host.Clock.GetUtcNow());
        shop.Host.Sql("UPDATE characters SET job=1 WHERE character_id=1");
        var content = shop.Host.Services.GetRequiredService<IPackageOpeningDefinitionSource>();
        var package = await content.GetAsync(itemId, default);
        Assert.NotNull(package); Assert.Empty(package.Diagnostics);
        var choices = ImmutableArray.CreateBuilder<AvatarPackageChoice>();
        foreach (var reward in package.FixedRewards)
        {
            var item = await content.GetRewardAsync(reward.ItemId, default);
            Assert.NotNull(item); Assert.True(item.Supported);
            if (item.Placement.WorldClass != ItemWorldClass.Avatar) continue;
            choices.Add(new(reward.ItemId, checked((byte)(item.Abilities.IsEmpty ? 0 : item.Abilities[0]))));
        }
        await shop.Host.Seed(66, itemId, 1);
        var result = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new AvatarPackageOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, new(66, choices.ToImmutable())));
        Assert.NotNull(result.Receipt); Assert.Equal(0, shop.Host.Count(itemId));
        var avatars = result.Receipt.Updates.Where(u => u.Slot.Space == BusinessInventorySpace.Avatar).ToArray();
        Assert.Equal(avatarCount, avatars.Length);
        Assert.All(avatars, item => Assert.NotNull(item.Identity));
        Assert.Equal(avatarCount, shop.Host.Scalar("SELECT count(*) FROM items WHERE avatar_expire_date IS NOT NULL"));
    }

    [SkippableFact]
    public async Task Archive_package_referencing_missing_equipment_is_refused_without_consumption()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        shop.Host.Sql("UPDATE characters SET job=1 WHERE character_id=1");
        await shop.Host.Seed(66, 10000606, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new AvatarPackageOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, new(66, [new(102550441, 0)]))).AsTask());
        Assert.Equal(OpeningRejection.UnsupportedContent, error.Reason);
        Assert.Equal(1, shop.Host.Count(10000606));
    }

    [Fact]
    public async Task Premium_rewards_extend_the_existing_account_term_and_do_not_create_physical_items()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(30, 2), new(3, 1)]);
        opening.Content.Items[30] = new(30,
            new(0, 1, new(BusinessInventorySpace.VirtualResource, 31, 31)),
            0, false, [], null, true, PremiumType: 31, PremiumDuration: TimeSpan.FromDays(3));
        var current = opening.Host.Clock.GetUtcNow().AddDays(1).ToUnixTimeSeconds();
        opening.Host.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,31,{current})");
        await opening.Host.Seed(80, 1001, 1);
        var result = await opening.Package();
        Assert.Equal(current + 6 * 86400, Assert.Single(result.Receipt!.ChangedPremiums).ExpiresAtUnixSeconds);
        Assert.Equal(current + 6 * 86400, opening.Host.Scalar("SELECT end_time FROM account_premiums WHERE account_id=1 AND premium_type=31"));
        Assert.Equal(0, opening.Host.Count(30)); Assert.Equal(1, opening.Host.Count(3)); Assert.Equal(0, opening.Host.Count(1001));
    }

    [Fact]
    public async Task Item_use_restrictions_and_successful_cooldown_are_checked_but_failed_openings_start_no_cooldown()
    {
        using var opening = new OpeningFixture();
        opening.Content.UseRule = opening.Content.UseRule with { MinimumLevel = 86, CooldownMilliseconds = 5000 };
        await opening.Host.Seed(80, 1001, 3);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Package());
        Assert.Equal(OpeningRejection.ItemUnusable, error.Reason); Assert.Equal(3, opening.Host.Count(1001));
        // The rule source is shared and cached by the character judgement, so use the current character's facts to clear this gate.
        opening.Host.Sql($"UPDATE characters SET level=86,exp={opening.Host.ExperienceForLevel(86)} WHERE character_id=1");
        await opening.Package();
        error = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Package());
        Assert.Equal(OpeningRejection.ItemUnusable, error.Reason); Assert.Contains("OnCooldown", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, opening.Host.Count(1001));
        opening.Host.Clock.Advance(TimeSpan.FromSeconds(5));
        await opening.Package(); Assert.Equal(1, opening.Host.Count(1001));
    }

    [Fact]
    public async Task Store_failure_does_not_start_the_opening_cooldown()
    {
        using var opening = new OpeningFixture();
        opening.Content.UseRule = opening.Content.UseRule with { CooldownMilliseconds = 5000 };
        await opening.Host.Seed(80, 1001, 1);
        opening.Host.Sql("CREATE TRIGGER fail_reward BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'forced reward failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => opening.Package());
        Assert.False(opening.Host.Services.GetRequiredService<ICharacterItemCooldownDomainService>()
            .IsCoolingDown(opening.Host.Session.CharacterId, ItemCooldownKey.ForItem(1001)));
        Assert.Equal(1, opening.Host.Count(1001));
        opening.Host.Sql("DROP TRIGGER fail_reward");
        await opening.Package(); Assert.Equal(0, opening.Host.Count(1001));
    }

    [SkippableFact]
    public async Task Actual_random_creature_package_uses_normal_package_domain_and_refreshes_creature_instances()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        await shop.Host.Seed(66, 160, 1);
        var result = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new PackageOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, new(66, [])));
        Assert.NotNull(result.Receipt); Assert.True(result.Receipt.CreaturesChanged); Assert.Equal(0, shop.Host.Count(160));
        var pet = Assert.Single(result.Receipt.Pets);
        var item = Assert.Single(result.Receipt.Updates);
        Assert.Equal(pet.PetKey, item.Identity!.Value.Value); Assert.Equal(5, item.Core.LegacyKind);
    }

    [SkippableFact]
    public async Task Actual_avatar_self_selection_from_booster_creates_only_the_selected_avatar()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        var content = shop.Host.Services.GetRequiredService<IPackageOpeningDefinitionSource>();
        var package = await content.GetAsync(2660500, default);
        Assert.NotNull(package); Assert.Empty(package.Diagnostics); Assert.Equal(1, package.SelectionCount);
        var selected = package.FixedRewards[0];
        var item = await content.GetRewardAsync(selected.ItemId, default);
        Assert.NotNull(item);
        var ability = checked((byte)(item.Abilities.IsEmpty ? 0 : item.Abilities[0]));
        await shop.Host.Seed(66, 2660500, 1);
        var result = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new PackageOpeningCommand(shop.Host.OnlineUser,
            shop.Host.Session, new(66, [selected.ItemId]), new(66, [new(selected.ItemId, ability)])));
        Assert.NotNull(result.Receipt);
        var avatar = Assert.Single(result.Receipt.Updates);
        Assert.Equal(selected.ItemId, avatar.Core.ItemId); Assert.Equal(ability, avatar.Core.Attribute);
    }

    [SkippableFact]
    public async Task Captured_sixty_box_opening_consumes_only_the_named_boxes_and_hammers_and_grants_both_pools()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var shop = new CeraShopFixture(useArchive: true);
        await shop.Host.Seed(65, 10007476, 1);
        await shop.Host.Seed(66, 10007368, 60);
        await shop.Host.Seed(122, 10007367, 60);
        var request = Assert.IsType<MagicBoxBatchOpeningRequest>(Decode(shop.Host, 0x0450,
            Convert.FromHexString("00420048B398007A0047B398003C00")));
        var result = await shop.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new MagicBoxBatchOpeningCommand(shop.Host.OnlineUser, shop.Host.Session, request.Input!));
        Assert.NotNull(result.Receipt);
        Assert.Equal(0, shop.Host.Count(10007368)); Assert.Equal(0, shop.Host.Count(10007367));
        Assert.Equal(1, shop.Host.Count(10007476));
        Assert.Equal(120, shop.Host.Count(2600014)); Assert.Equal(60, shop.Host.Count(2682272));
        Assert.Equal(100000, shop.Cera); Assert.Equal(100000, shop.Host.Gold);
        Assert.Null(result.LuckBefore); Assert.Null(result.LuckAfter);
        Assert.Equal(new[] { (2600014, 120), (2682272, 60) }, result.Primary.OrderBy(r => r.ItemId).Select(r => (r.ItemId, r.Count)));
    }

    [Fact]
    public async Task Batch_plan_handles_two_pools_and_materials_atomically()
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(80, 1000, 60); await opening.Host.Seed(120, 2000, 60);
        var result = await opening.Box(60);
        Assert.Equal(120, opening.Host.Count(3)); Assert.Equal(60, opening.Host.Count(6000));
        Assert.Equal(0, opening.Host.Count(1000)); Assert.Equal(0, opening.Host.Count(2000));
        Assert.NotNull(result.Receipt);
    }

    [Theory]
    [InlineData(8, 1, 1, 3)]
    [InlineData(7, 2, 1, 4)]
    [InlineData(0, 9, 1, 11)]
    public async Task Seria_luck_has_an_independent_persistent_counter_and_grants_base_plus_two_bonus_copies(int before, int count, int after, int rewardCount)
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1000] = opening.Content.Definitions[1000] with
        { UsesSeriaLuck = true, Pools = [new(1, [new(new(3, 1), 100)])], MaterialCount = 0, MaterialItemId = 0 };
        opening.Host.Sql($"UPDATE accounts SET seria_luck_value={before},lucky_star=37 WHERE account_id=1");
        await opening.Host.Seed(80, 1000, count);
        var result = await opening.Box(count, materialSlot: -1, materialId: 0);
        Assert.Equal(before, result.LuckBefore); Assert.Equal(after, result.LuckAfter);
        Assert.Equal(after, opening.Host.Scalar("SELECT seria_luck_value FROM accounts WHERE account_id=1"));
        Assert.Equal(37, opening.Host.Scalar("SELECT lucky_star FROM accounts WHERE account_id=1"));
        Assert.Equal(rewardCount, opening.Host.Count(3));
        Assert.Equal(count, result.Primary.Sum(r => r.Count)); Assert.Equal(1, result.Bonus.Sum(r => r.Count));
    }

    [Fact]
    public async Task Revive_reward_updates_virtual_slot_one_instead_of_creating_a_fake_consumable()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(42, 3)]);
        await opening.Host.Seed(80, 1001, 1);
        var result = await opening.Package();
        Assert.NotNull(result.Receipt);
        Assert.Equal(3, opening.Host.Count(1)); Assert.Equal(0, opening.Host.Count(42));
        Assert.Contains(result.Receipt.Updates, item => item.Slot == new BusinessSlot(BusinessInventorySpace.VirtualResource, 1) && item.Core.ItemId == 1 && item.Core.ToCore().Value == 3);
    }

    [Fact]
    public async Task Ordinary_fixed_and_random_rewards_create_equipment_and_preserve_quantity()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(3000, 1)]) with
        { Pools = [new(2, [new(new(3, 5), 100)])] };
        await opening.Host.Seed(80, 1001, 1);
        var result = await opening.Package();
        Assert.Equal(10, opening.Host.Count(3));
        var equipment = Assert.Single(result.Receipt!.Updates, i => i.Core.ItemId == 3000);
        Assert.NotNull(equipment.Identity); Assert.Equal(45, equipment.Core.Durability); Assert.Equal(1, equipment.Core.SealFlag);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Avatar_package_keeps_selected_ability_expiry_and_complete_instance_facts(int days)
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1002] = OpeningContent.Package(1002, [new(39032, 1, PeriodDays: days), new(3, 2)])
            with { Kind = OpeningPackageKind.AvatarPackage };
        await opening.Host.Seed(80, 1002, 1);
        var result = await opening.Avatar(2);
        var avatar = Assert.Single(result.Receipt!.Updates, u => u.Slot.Space == BusinessInventorySpace.Avatar);
        Assert.NotNull(avatar.Identity); Assert.Equal(2, avatar.Core.Attribute); Assert.Equal(0, avatar.Core.ExpireTime);
        Assert.Equal(days == 0 ? 0 : opening.Host.Clock.GetUtcNow().AddDays(days).ToUnixTimeSeconds(),
            opening.Host.Scalar("SELECT avatar_expire_date FROM items WHERE avatar_expire_date IS NOT NULL LIMIT 1"));
        Assert.Equal(2, opening.Host.Count(3));
        Assert.Equal(0, opening.Host.Count(1002));
    }

    [Fact]
    public async Task Avatar_selection_from_the_booster_protocol_uses_avatar_domain_rules()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(39032, 1)])
            with { Kind = OpeningPackageKind.Selection, SelectionCount = 1 };
        await opening.Host.Seed(80, 1001, 1);
        var body = new byte[] { 80, 0, 0, 0, 0x78, 0x98, 0, 0, 1, 0x78, 0x98, 0, 0, 2 };
        var request = Assert.IsType<PackageOpeningRequest>(Decode(opening.Host, 0x00A0, body));
        Assert.NotNull(request.AvatarInput);
        var result = await opening.Host.Services.GetRequiredService<ICommandBus>().SendAsync(
            new PackageOpeningCommand(opening.Host.OnlineUser, opening.Host.Session, request.Input!, request.AvatarInput));
        Assert.Equal(2, Assert.Single(result.Receipt!.Updates).Core.Attribute);
    }

    [Fact]
    public async Task Invalid_selection_and_ability_leave_the_package_and_inventory_untouched()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1001] = OpeningContent.Package(1001, [new(3, 2), new(6000, 1)])
            with { Kind = OpeningPackageKind.Selection, SelectionCount = 1 };
        await opening.Host.Seed(80, 1001, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Package(9999));
        Assert.Equal(OpeningRejection.InvalidChoice, error.Reason); Assert.Equal(1, opening.Host.Count(1001));
        Assert.Equal(0, opening.Host.Count(3));
        await opening.Host.Seed(80, 1002, 1);
        error = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Avatar(99));
        Assert.Equal(OpeningRejection.InvalidChoice, error.Reason); Assert.Equal(1, opening.Host.Count(1002));
    }

    [Theory]
    [InlineData(2, 2000, 1)]
    [InlineData(1, 2001, 2)]
    public async Task Insufficient_or_wrong_materials_never_consume_source(int count, int material, int held)
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(80, 1000, 5); await opening.Host.Seed(120, material, held);
        await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Box(count));
        Assert.Equal(5, opening.Host.Count(1000)); Assert.Equal(held, opening.Host.Count(material)); Assert.Equal(0, opening.Host.Count(3));
    }

    [Fact]
    public async Task Full_reward_band_does_not_consume_material_or_source()
    {
        using var opening = new OpeningFixture();
        opening.Content.Items[3] = opening.Content.Items[3] with {
            Placement = new(2, 1, new(BusinessInventorySpace.Consumable, 0, 0)) };
        // Quick slots also belong to the main consumable band.
        foreach (var slot in new[] { 3, 4, 5, 6, 7, 8, 65 }) await opening.Host.Seed(slot, 3, 1);
        await opening.Host.Seed(80, 1000, 1); await opening.Host.Seed(120, 2000, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => opening.Box(1));
        Assert.Equal(OpeningRejection.NoRoom, error.Reason); Assert.Equal(1, opening.Host.Count(1000)); Assert.Equal(1, opening.Host.Count(2000));
        Assert.Equal(0, opening.Host.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
    }

    [Fact]
    public async Task Failure_after_inventory_write_rolls_back_rewards_source_and_luck()
    {
        using var opening = new OpeningFixture();
        opening.Content.Definitions[1000] = opening.Content.Definitions[1000] with { UsesSeriaLuck = true };
        await opening.Host.Seed(80, 1000, 1); await opening.Host.Seed(120, 2000, 1);
        opening.Host.Sql("CREATE TRIGGER stop_luck BEFORE UPDATE OF seria_luck_value ON accounts BEGIN SELECT RAISE(ABORT,'forced opening failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => opening.Box(1));
        Assert.Equal(1, opening.Host.Count(1000)); Assert.Equal(1, opening.Host.Count(2000));
        Assert.Equal(0, opening.Host.Count(3)); Assert.Equal(0, opening.Host.Count(6000));
        Assert.Equal(0, opening.Host.Scalar("SELECT seria_luck_value FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task Concurrent_openings_cannot_spend_the_same_box_twice()
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(80, 1000, 1); await opening.Host.Seed(120, 2000, 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            try { await opening.Box(1); return true; } catch (OpeningRejectedException) { return false; }
        })));
        Assert.Single(results, r => r); Assert.Equal(2, opening.Host.Count(3)); Assert.Equal(1, opening.Host.Count(6000));
    }

    [Fact]
    public async Task Native_ack_precedes_inventory_deltas_and_stale_receipts_are_suppressed()
    {
        using var opening = new OpeningFixture();
        await opening.Host.Seed(80, 1000, 1); await opening.Host.Seed(120, 2000, 1);
        var result = await opening.Box(1);
        var receipt = result.Receipt!;
        var notification = new OpeningCompleted(receipt, [new MagicBoxBatchOpeningResponse(new(0, 80, 1000, 120, 2000, 1), result)]);
        var notifications = opening.Host.Services.GetRequiredService<IEventService>();
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]), notification, default);
        Assert.IsType<MagicBoxBatchOpeningResponse>(opening.Host.Sender.Messages[0]);
        Assert.All(opening.Host.Sender.Messages.Skip(1), message => Assert.IsAssignableFrom<IGameNotiMessage>(message));
        var sent = opening.Host.Sender.Messages.Count;
        opening.Host.Select(2);
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]), notification, default);
        Assert.Equal(sent, opening.Host.Sender.Messages.Count);
    }

    private static IClientNetworkMessage? Decode(ShopFixture host, ushort type, byte[] body)
    {
        var registration = host.Services.GetServices<GameMessageDecoderRegistration>().Single(r => r.Selector == 1 && r.Type == type);
        return ((IGameMessageDecoder)host.Services.GetRequiredService(registration.DecoderType)).Decode(GameMessageMetadata.Empty, body);
    }
}

internal sealed class DeterministicOpeningRules(IPackageOpeningRuleService inner) : IPackageOpeningRuleService
{
    private int sequence;
    public ValueTask<OpeningPackageDefinition> DefinitionAsync(int itemId, OpeningPackageKind kind, CancellationToken ct) => inner.DefinitionAsync(itemId, kind, ct);
    public ImmutableArray<OpeningReward> Roll(OpeningPackageDefinition definition, IReadOnlyList<int> choices, int seed) =>
        inner.Roll(definition, choices, 0x5eed + Interlocked.Increment(ref sequence));
    public ValueTask<ImmutableArray<OpeningPreparedGrant>> PrepareAsync(IReadOnlyList<OpeningReward> rewards, DateTimeOffset now, CancellationToken ct) => inner.PrepareAsync(rewards, now, ct);
    public int AdvanceLuck(int value) => inner.AdvanceLuck(value);
    public bool LuckBonus(int value) => inner.LuckBonus(value);
}

internal sealed class OpeningFixture : IDisposable
{
    public OpeningContent Content { get; } = new();
    private readonly CeraShopFixture shop;
    public ShopFixture Host => shop.Host;
    public OpeningFixture() => shop = new(configure: services =>
    {
        services.AddSingleton<IPackageOpeningDefinitionSource>(Content);
        services.AddSingleton<IItemPlacementDefinitionSource>(Content);
        services.AddSingleton<IItemUseRuleDefinitionSource>(Content);
    });
    public Task<MagicBoxOpeningResult> Box(int count, int materialSlot = 120, int materialId = 2000) =>
        Host.Services.GetRequiredService<ICommandBus>().SendAsync(new MagicBoxBatchOpeningCommand(Host.OnlineUser, Host.Session,
            new(0, 80, 1000, materialSlot, materialId, count))).AsTask();
    public Task<PackageOpeningResult> Package(params int[] selected) =>
        Host.Services.GetRequiredService<ICommandBus>().SendAsync(new PackageOpeningCommand(Host.OnlineUser, Host.Session,
            new(80, selected.ToImmutableArray()))).AsTask();
    public Task<PackageOpeningResult> Avatar(byte ability) =>
        Host.Services.GetRequiredService<ICommandBus>().SendAsync(new AvatarPackageOpeningCommand(Host.OnlineUser, Host.Session,
            new(80, [new(39032, ability)]))).AsTask();
    public void Dispose() => shop.Dispose();
}

internal sealed class OpeningContent : IPackageOpeningDefinitionSource, IItemPlacementDefinitionSource, IItemUseRuleDefinitionSource
{
    public Dictionary<int, OpeningPackageDefinition> Definitions { get; } = [];
    public Dictionary<int, OpeningRewardItemDefinition> Items { get; } = [];
    public ItemUseRule UseRule { get; set; } = new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere);
    public OpeningContent()
    {
        Definitions[1000] = Package(1000, []) with { Kind = OpeningPackageKind.MagicBox, MaterialItemId = 2000, MaterialCount = 1,
            Pools = [new(1, [new(new(3, 2), 100)]), new(1, [new(new(6000, 1), 100)])] };
        Definitions[1001] = Package(1001, [new(3, 2)]);
        Definitions[1002] = Package(1002, [new(39032, 1)]) with { Kind = OpeningPackageKind.AvatarPackage };
        foreach (var id in new[] { 3, 6000 }) Items[id] = new(id,
            new(2, 10000, new(BusinessInventorySpace.Consumable, 0, 35)),
            0, false, [], null, true);
        Items[3000] = new(3000,
            new(1, 1, new(BusinessInventorySpace.CarriedEquipment, 0, 47))
            { WorldClass = ItemWorldClass.Equipment }, 45, true, [], null, true);
        Items[39032] = new(39032,
            new(8, 1, new(BusinessInventorySpace.Avatar, 0, 209))
            { WorldClass = ItemWorldClass.Avatar }, 0, false, [0, 1, 2], null, true);
        Items[63022] = new(63022,
            new(5, 1, new(BusinessInventorySpace.CreatureInventory, 0, 139))
            { WorldClass = ItemWorldClass.Creature }, 0, false, [], 1, true);
        Items[42] = Items[1] = new(1,
            new(0, int.MaxValue, new(BusinessInventorySpace.VirtualResource, 1, 1)),
            0, false, [], null, true);
    }
    public static OpeningPackageDefinition Package(int id, ImmutableArray<OpeningReward> rewards) =>
        new(id, OpeningPackageKind.Package, rewards, [], 0, 0, 0, 1, false, []);
    public ValueTask<OpeningPackageDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(Definitions.GetValueOrDefault(id));
    public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int id, CancellationToken ct) => ValueTask.FromResult(Items.GetValueOrDefault(id));
    ValueTask<ItemPlacement?> IItemPlacementDefinitionSource.GetAsync(int id, CancellationToken ct) =>
        ValueTask.FromResult<ItemPlacement?>(AccountMaterialCatalog.TryGetSlotIndex(id, out var material)
            ? new(AccountMaterialCatalog.Definitions[material].ItemKind, 1000, new(BusinessInventorySpace.Material, 0, 55))
            : Items.TryGetValue(id, out var item)
            ? item.Placement
            : new(2, 10000, new(BusinessInventorySpace.Consumable, 0, 55)));
    ValueTask<ItemUseRule?> IItemUseRuleDefinitionSource.GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemUseRule?>(UseRule);
}
