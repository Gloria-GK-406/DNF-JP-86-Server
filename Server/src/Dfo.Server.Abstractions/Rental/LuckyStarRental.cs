using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Rental;

public enum LuckyStarRejection { None, InvalidCount, UnknownOwner, LimitReached, InsufficientGold, InsufficientStars, InvalidItem }
public sealed record LuckyStarMutation(LuckyStarRejection Rejection, int Balance, int Added = 0, int? Gold = null)
{ public bool Succeeded => Rejection == LuckyStarRejection.None; }
public sealed record LuckyStarChanged(AccountId AccountId, int Balance, bool NotifyGain = false) : IDomainEvent;
public interface IAccountLuckyStarDomainService
{
    ValueTask<int> BalanceAsync(AccountId account, CancellationToken ct);
    ValueTask<LuckyStarMutation> PurchaseAsync(AccountId account, CharacterId character, int count, CancellationToken ct);
    ValueTask<LuckyStarMutation> GrantAsync(AccountId account, int amount, bool allowPartial, CancellationToken ct);
    ValueTask<LuckyStarMutation> SpendAsync(AccountId account, int amount, CancellationToken ct);
    ValueTask<LuckyStarMutation> GrantForClearAsync(AccountId account, CharacterId character, DungeonRunId run, CancellationToken ct);
    ValueTask<LuckyStarMutation> GrantFromItemAsync(AccountId account, int itemId, CancellationToken ct);
}
public enum EquipmentRentalRejection { None, UnknownOwner, InvalidItem, WrongJob, LevelTooLow, InsufficientStars, InventoryFull, TooManyRentals }
public sealed record EquipmentRentalItem(ItemIdentity Uid, int ItemId, int ExpireTime);
public sealed record EquipmentRentalPanel(int LuckyStar, IReadOnlyList<EquipmentRentalItem> Items);
public sealed record EquipmentRentalReceipt(EquipmentRentalRejection Rejection, EquipmentRentalPanel Panel,
    IReadOnlyList<InventoryContainerType> ChangedContainers, bool Renewed = false)
{
    public bool Succeeded => Rejection == EquipmentRentalRejection.None;
    public IReadOnlyList<BusinessSlot> TouchedSlots { get; init; } = [];
}
public interface IEquipmentRentalDomainService
{
    ValueTask<EquipmentRentalPanel> PanelAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<EquipmentRentalReceipt> RentAsync(AccountId account, CharacterId character, int itemId, CancellationToken ct);
}
public sealed record RentalOffer(int ItemId, QuestJobRequirement Job, int MinimumLevel, int Price)
{ public byte? CharacterJob { get; init; } }
public sealed record LuckyStarRentalCatalog(int Limit, int GoldPerStar, int ClearReward,
    IReadOnlySet<int> ExcludedDungeons, IReadOnlyList<RentalOffer> Offers);
public interface ILuckyStarRentalContentSource
{
    LuckyStarRentalCatalog Load();
    ValueTask<int?> RentPointsAsync(int itemId, CancellationToken ct);
    ValueTask<int?> QualityTierAsync(int itemId, CancellationToken ct) => ValueTask.FromResult<int?>(null);
    ValueTask<IReadOnlySet<byte>?> UsableJobsAsync(int itemId, CancellationToken ct);
}
public interface ILuckyStarRuleService
{
    LuckyStarRentalCatalog Catalog { get; }
    ValueTask<int?> RentPointsAsync(int itemId, CancellationToken ct);
    ValueTask<int> ClearRewardAsync(int dungeonId, int level, CancellationToken ct);
}
public sealed record RentalQuote(EquipmentRentalRejection Rejection, int Price, DateTimeOffset ExpiresAt)
{ public ItemSnapshot? Snapshot { get; init; } }
public interface IEquipmentRentalRuleService
{
    ValueTask<RentalQuote> QuoteAsync(int itemId, byte job, int level, DateTimeOffset now, CancellationToken ct);
}
public interface ILuckyStarDomainStore
{
    ValueTask AcquireWriteAsync(CancellationToken ct);
    ValueTask<int?> ReadAsync(AccountId account, CancellationToken ct);
    ValueTask<bool> CompareExchangeAsync(AccountId account, int expected, int next, CancellationToken ct);
    ValueTask<bool> RecordClearAsync(DungeonRunId run, CharacterId character, CancellationToken ct);
}
public sealed record EquipmentRentalLink(ItemIdentity Uid, int ItemId);
public interface IEquipmentRentalDomainStore
{
    ValueTask<IReadOnlyList<EquipmentRentalLink>> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask AddAsync(CharacterId character, ItemIdentity uid, int itemId, CancellationToken ct);
}
