using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Application.Rental;
using Xunit;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LuckyStarRentalRuleTests
{
    [Fact]
    public async Task Rental_offer_checks_both_catalog_and_actual_equipment_and_expires_after_24_hours()
    {
        var content = new Content();
        var rules = new EquipmentRentalRuleService(content, content, delivery: TestRequired.Absent<Dfo.Server.Application.Inventory.IItemDeliveryRuleService>());
        var now = DateTimeOffset.Parse("2026-10-20T15:00:00+08:00");
        var quote = await rules.QuoteAsync(9001, 0, 30, now, default);
        Assert.Equal(EquipmentRentalRejection.None, quote.Rejection);
        Assert.Equal(10, quote.Price);
        Assert.Equal(now.AddHours(24), quote.ExpiresAt);
        Assert.Equal(EquipmentRentalRejection.None, (await rules.QuoteAsync(9001, 0, 29, now, default)).Rejection);
        Assert.Equal(EquipmentRentalRejection.None, (await rules.QuoteAsync(9001, 0, 1, now, default)).Rejection);
        Assert.Equal(EquipmentRentalRejection.WrongJob, (await rules.QuoteAsync(9001, 2, 30, now, default)).Rejection);
        Assert.Equal(EquipmentRentalRejection.InvalidItem, (await rules.QuoteAsync(9002, 0, 85, now, default)).Rejection);
    }
    internal sealed class Content : ILuckyStarRentalContentSource, IEquipmentDefinitionSource, IItemUseRuleDefinitionSource
    {
        public LuckyStarRentalCatalog Load() => new(400,100000,1,new HashSet<int>{18},[new(9001,QuestJobRequirement.Swordman,20,10)]);
        public ValueTask<int?> RentPointsAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<int?>(itemId == 8001 ? 5 : null);
        public ValueTask<IReadOnlySet<byte>?> UsableJobsAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<IReadOnlySet<byte>?>(new HashSet<byte>{0});
        public ValueTask<int> InitializeAsync(CancellationToken ct) => ValueTask.FromResult(1);
        public ValueTask<EquipmentDefinition?> GetEquipmentAsync(int id,CancellationToken ct) => ValueTask.FromResult<EquipmentDefinition?>(id==9001?new(30,30,2,"[sword]",50):null);
        public ValueTask<ItemUseRule?> GetAsync(int id,CancellationToken ct) => ValueTask.FromResult<ItemUseRule?>(new(false,0,0,30,new(false,[QuestJobRequirement.Swordman]),ItemPlaceRestriction.Anywhere));
    }
}
