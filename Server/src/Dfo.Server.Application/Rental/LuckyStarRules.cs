using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;

namespace Dfo.Server.Application.Rental;

internal sealed class LuckyStarRuleService(ILuckyStarRentalContentSource content, IDungeonDefinitionSource dungeons) : ILuckyStarRuleService
{
    public LuckyStarRentalCatalog Catalog => content.Load();
    public ValueTask<int?> RentPointsAsync(int itemId, CancellationToken ct) => content.RentPointsAsync(itemId, ct);
    public async ValueTask<int> ClearRewardAsync(int dungeonId, int level, CancellationToken ct) =>
        Catalog.ClearReward <= 0 || Catalog.ExcludedDungeons.Contains(dungeonId) || await dungeons.GetAsync(dungeonId, ct).ConfigureAwait(false) is not { } dungeon
            || !Marriage.CapsuleClearSubscriber.Recommended(dungeon, level) ? 0 : Catalog.ClearReward;
}
internal sealed class EquipmentRentalRuleService(ILuckyStarRentalContentSource content,
    IEquipmentDefinitionSource equipment,
    IItemDeliveryRuleService? delivery = null) : IEquipmentRentalRuleService
{
    public async ValueTask<RentalQuote> QuoteAsync(int itemId, byte job, int level, DateTimeOffset now, CancellationToken ct)
    {
        var offers = content.Load().Offers.Where(x => x.ItemId == itemId).ToArray();
        if (offers.Length == 0 || await equipment.GetEquipmentAsync(itemId, ct).ConfigureAwait(false) is null
            || await content.UsableJobsAsync(itemId, ct).ConfigureAwait(false) is not {} usableJobs)
            return new(EquipmentRentalRejection.InvalidItem, 0, now);
        var family = CharacterJobFamilies.Resolve(job);
        var offer = offers.FirstOrDefault(x => x.CharacterJob is {} exact ? exact == job : x.Job == family);
        if (offer is null || !usableJobs.Contains(job)) return new(EquipmentRentalRejection.WrongJob,0,now);
        // Rental may precede wearing; the existing wear service owns level and contract eligibility.
        var snapshot = delivery is null ? null : await delivery.TemplateAsync(itemId,ct).ConfigureAwait(false);
        if (snapshot is not null && await content.QualityTierAsync(itemId,ct).ConfigureAwait(false) is {} quality)
            snapshot = snapshot with {InstanceValue=EquipmentQualitySeeds.FixedFor(quality)};
        return new(EquipmentRentalRejection.None,offer.Price,now.AddHours(24)){Snapshot=snapshot};
    }
}
