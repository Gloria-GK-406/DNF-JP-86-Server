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
    public async Task Different_accounts_never_share_an_instance_or_balance()
    {
        using var shop = new ShopFixture();
        shop.Sql("INSERT INTO accounts(account_id,m_id,cube_clear) VALUES(2,'other',99)");
        var service = shop.Services.GetRequiredService<IAccountMaterialBagDomainService>();
        var first = service.Get(new AccountId(1));
        var second = service.Get(new AccountId(2));

        Assert.NotSame(first, second);
        Assert.Equal(0, (await first.SnapshotAsync(default)).ClearCube);
        Assert.Equal(99, (await second.SnapshotAsync(default)).ClearCube);
    }

    [Fact]
    public async Task A_non_positive_addition_is_rejected_without_changing_the_account()
    {
        using var shop = new ShopFixture();
        var bag = shop.Services.GetRequiredService<IAccountMaterialBagDomainService>()
            .Get(new AccountId(1));
        var scope = Assert.IsType<DomainTransactionScope>(
            shop.Services.GetRequiredService<IDomainTransactionAccessor>());

        await using (scope.Begin())
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bag.AddAsync(
                new Dictionary<AccountMaterialKind, int>
                {
                    [AccountMaterialKind.ClearCube] = 0,
                },
                default).AsTask());
        }

        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task An_unknown_material_kind_is_rejected_without_changing_the_account()
    {
        using var shop = new ShopFixture();
        var bag = shop.Services.GetRequiredService<IAccountMaterialBagDomainService>()
            .Get(new AccountId(1));
        var scope = Assert.IsType<DomainTransactionScope>(
            shop.Services.GetRequiredService<IDomainTransactionAccessor>());

        await using (scope.Begin())
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bag.AddAsync(
                new Dictionary<AccountMaterialKind, int>
                {
                    [(AccountMaterialKind)999] = 1,
                },
                default).AsTask());
        }

        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task An_overflow_is_rejected_and_the_existing_balance_survives()
    {
        using var shop = new ShopFixture();
        shop.Sql($"UPDATE accounts SET cube_clear={int.MaxValue} WHERE account_id=1");
        var bag = shop.Services.GetRequiredService<IAccountMaterialBagDomainService>()
            .Get(new AccountId(1));
        var scope = Assert.IsType<DomainTransactionScope>(
            shop.Services.GetRequiredService<IDomainTransactionAccessor>());

        await using (scope.Begin())
        {
            await Assert.ThrowsAsync<OverflowException>(() => bag.AddAsync(
                new Dictionary<AccountMaterialKind, int>
                {
                    [AccountMaterialKind.ClearCube] = 1,
                },
                default).AsTask());
        }

        Assert.Equal(int.MaxValue,
            shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
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
        Assert.Equal(7, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(new PlacedRewardItem(358, 3037, 7), Assert.Single(result.Placed));
        Assert.Equal(7, Assert.Single(result.AccountMaterials!).FinalCount);
    }

    [Fact]
    public async Task A_failed_non_disjoint_account_reward_leaves_no_character_or_account_material()
    {
        using var shop = new ShopFixture();
        shop.Sql("CREATE TRIGGER refuse_tutorial_account_material BEFORE UPDATE OF cube_clear ON accounts "
            + "WHEN NEW.cube_clear <> OLD.cube_clear BEGIN SELECT RAISE(ABORT,'forced account material failure'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            shop.Services.GetRequiredService<ICommandBus>().SendAsync(
                new GrantTutorialRewardCommand(
                    new CharacterId(1),
                    [new DeclaredRewardItem(3037, 7)],
                    new AccountId(1))).AsTask());

        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task A_direct_character_container_write_cannot_bypass_account_material_routing()
    {
        using var shop = new ShopFixture();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            shop.Seed(121, 3037, 1));

        Assert.Contains("IItemGrantDomainService", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, shop.Count(3037));
        Assert.Equal(0, shop.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }
}
