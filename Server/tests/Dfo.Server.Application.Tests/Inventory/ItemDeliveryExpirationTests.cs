using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed partial class ItemDeliveryTests
{
    private sealed class LifetimeContent(ItemLifetimeDefinition lifetime) : IItemPlacementDefinitionSource
    {
        private readonly Content inner = new();
        public async ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct)
        {
            var result = await inner.GetAsync(id, ct);
            return result is not null && id == 9001 ? result with { Lifetime = lifetime } : result;
        }
    }

    [Fact]
    public async Task Daily_deadline_survives_overflow_mail_claim_and_does_not_merge_across_days()
    {
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(new LifetimeContent(new(0, 0, true))));
        await FillQuick(f);
        var expected = new DateTimeOffset(2026, 9, 9, 6, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        var receipt = await Grant(f, new ItemDeliveryGrant(9001, 15));
        Assert.Equal(expected, Assert.Single(receipt.Inventory).Snapshot.ExpireTime);
        Assert.Equal(expected, Assert.Single(receipt.MailedItems).ExpireTime);
        var inventories = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>();
        var bag = inventories.GetPersistentStore(new(InventoryOwnerType.Character, 1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.Material);
        await using (var tx = Scope(f).Begin())
        {
            var record = await bag.TakeAsync((await bag.GetAsync(0, default))!, null, default);
            await f.Services.GetRequiredService<IItemLifecycleDomainService>().DeleteItemAsync(record, default);
            await tx.CommitAsync(default);
        }
        f.Clock.Advance(TimeSpan.FromHours(1));
        var attachment = Assert.Single(Assert.Single(await Mail(f).InboxAsync(new(1))).Attachments);
        await Mail(f).ClaimAsync(new(1), attachment.Id);
        await using (var read = Scope(f).Begin())
            Assert.Equal(expected, (await bag.GetAsync(0, default))!.Snapshot.ExpireTime);
        var sameDay = await Grant(f, new ItemDeliveryGrant(9001, 3));
        Assert.Empty(sameDay.MailIds);
        Assert.Equal(expected, Assert.Single(sameDay.Inventory).Snapshot.ExpireTime);
        Assert.Equal(8, f.Count(9001));
        f.Clock.Advance(TimeSpan.FromDays(1));
        var nextDay = await Grant(f, new ItemDeliveryGrant(9001, 3));
        Assert.Empty(nextDay.Inventory);
        Assert.Equal(expected + 86400, Assert.Single(nextDay.MailedItems).ExpireTime);
        Assert.Equal(8, f.Count(9001));
    }

    [Fact]
    public async Task Direct_container_add_preserves_deadline_and_does_not_stack_with_legacy_permanent_row()
    {
        using var f = new ShopFixture(s => s.AddSingleton<IItemPlacementDefinitionSource>(new LifetimeContent(new(0, 7, false))));
        await f.Seed(121, 9001, 2, 3);
        await FillQuick(f);
        await using (var tx = Scope(f).Begin())
        {
            await Assert.ThrowsAsync<ContainerAdditionException>(async () =>
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(1), 9001, 3, default));
        }
        Assert.Equal(2, f.Count(9001));
    }
}
