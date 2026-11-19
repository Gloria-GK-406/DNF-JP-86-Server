using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Raids;

public sealed record AntonRaidRewardParticipant(AccountId AccountId, CharacterId Character, byte Squad, byte Role);

public sealed record AntonRaidRewardGrant(Guid Instance, byte Phase, AntonRaidRewardParticipant Recipient,
    AntonRaidRewardKind Kind, AntonRaidRewardItem? Item, DateTimeOffset EndedAt);

public sealed record AntonRaidRewardReceipt(bool Claimed, int? Gold = null,
    ItemDeliveryReceipt? Delivery = null, QuestProgressSnapshot? Quests = null);

public interface IAntonRaidRewardGrantDomainService
{
    /// <summary>Commits the claim and its effects together inside the caller's character write unit.</summary>
    ValueTask<AntonRaidRewardReceipt> GrantAsync(AntonRaidRewardGrant grant, bool online, CancellationToken cancellationToken);
}
