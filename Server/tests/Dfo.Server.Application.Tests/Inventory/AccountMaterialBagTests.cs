using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AccountMaterialBagTests
{
    [Fact]
    public void The_supported_mapping_is_complete_and_unique()
    {
        using var shop = new ShopFixture();
        var rules = shop.Services.GetRequiredService<IAccountMaterialRuleService>();

        Assert.Equal(
            [
                (3033, 354), (3034, 355), (3035, 356), (3036, 357), (3037, 358),
                (3262, 359), (10100115, 360), (10100116, 361), (10099773, 362),
                (10099774, 363), (10099775, 364),
            ],
            rules.Definitions.Select(definition =>
                (definition.ItemId, definition.VirtualSlotIndex)));
        Assert.Equal(rules.Definitions.Count,
            rules.Definitions.Select(definition => definition.Kind).Distinct().Count());
    }

    [Fact]
    public async Task Different_accounts_never_share_material_rows()
    {
        using var shop = new ShopFixture();
        shop.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'other')");
        await shop.SeedShared(3037, 99, 2);
        Assert.Equal(0, shop.SharedCount(3037));
        Assert.Equal(99, shop.SharedCount(3037, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_addition_is_rejected_without_changing_the_account(int count)
    {
        using var shop = new ShopFixture();
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => shop.Shared().AddAsync(3037, count, default).AsTask());
        Assert.Equal(0, shop.SharedCount(3037));
    }

    [Fact]
    public async Task An_overflow_is_rejected_and_the_existing_count_survives()
    {
        using var shop = new ShopFixture();
        await shop.SeedShared(3037, int.MaxValue);
        var scope = (DomainTransactionScope)shop.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        Assert.IsType<ItemPlacementOutcome.NoRoom>((await shop.Shared().AddAsync(3037, 1, default)).Placement);
        await tx.CommitAsync(default);
        Assert.Equal(int.MaxValue, shop.SharedCount(3037));
    }

    [Fact]
    public async Task A_non_disjoint_reward_uses_the_account_bag_and_never_creates_a_character_item()
    {
        using var shop = new ShopFixture();

        var result = await shop.Services.GetRequiredService<ICommandBus>().SendAsync(
            new GrantTutorialRewardCommand(
                new CharacterId(1),
                [new DeclaredRewardItem(3037, 7)],
                new AccountId(1)));

        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(7, shop.SharedCount(3037));
        Assert.Equal(new PlacedRewardItem(358, 3037, 7), Assert.Single(result.Placed));
        Assert.Equal(7, Assert.Single(result.AccountMaterials!).Value);
    }

    [Fact]
    public async Task A_failed_non_disjoint_account_reward_leaves_no_character_or_account_material()
    {
        using var shop = new ShopFixture();
        shop.Sql("CREATE TRIGGER refuse_tutorial_account_material BEFORE INSERT ON item_slots "
            + "WHEN NEW.owner_scope='account' AND NEW.list_type=240 BEGIN SELECT RAISE(ABORT,'forced account material failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            shop.Services.GetRequiredService<ICommandBus>().SendAsync(
                new GrantTutorialRewardCommand(
                    new CharacterId(1),
                    [new DeclaredRewardItem(3037, 7)],
                    new AccountId(1))).AsTask());

        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(0, shop.SharedCount(3037));
    }

    [Fact]
    public async Task A_material_core_with_wrong_kind_cannot_be_written_to_a_character_container()
    {
        using var shop = new ShopFixture();

        var scope = (Dfo.Server.Application.Transactions.DomainTransactionScope)shop.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>();
        await using var tx = scope.Begin();
        var items = shop.Services.GetRequiredService<IItemDomainStore>().SessionIn(scope.Current!);
        var malformed = await items.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2, 3037, 1), true), default);
        var container = shop.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(shop.Session.CharacterId, InventoryContainerType.Main);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => container.PutAsync(121, malformed, default).AsTask());
        await items.DeleteItemAsync(malformed, default);
        await tx.CommitAsync(default);

        Assert.Contains("cannot occupy", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(0, shop.SharedCount(3037));
    }
}
