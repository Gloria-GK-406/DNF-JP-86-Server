using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidRewardGrantDomainService(IAntonRaidRewardLedger ledger, ICharacterDomainService characters,
    IItemDeliveryDomainService items, IMailboxDomainService mailbox, IQuestProgressDomainService quests,
    IEventService events) : IAntonRaidRewardGrantDomainService
{
    private const string RewardTitle = "安徒恩攻坚奖励";

    public async ValueTask<AntonRaidRewardReceipt> GrantAsync(AntonRaidRewardGrant grant, bool online, CancellationToken ct)
    {
        Validate(grant);
        var recipient = grant.Recipient;
        var character = recipient.Character;
        var key = new AntonRaidRewardKey(grant.Instance,
            grant.Kind == AntonRaidRewardKind.RetryCertificate ? (byte)0 : grant.Phase, character, grant.Kind);
        if (!await ledger.TryClaimAsync(key, ct).ConfigureAwait(false))
            return new(false);

        var deliveryKey = grant.Kind == AntonRaidRewardKind.RetryCertificate
            ? $"raid:{grant.Instance:N}:retry:{character.Value}"
            : $"raid:{grant.Instance:N}:{grant.Phase}:{character.Value}:{(byte)grant.Kind}";
        switch (grant.Kind)
        {
            case AntonRaidRewardKind.RetryCertificate:
                await mailbox.SendAsync(MailBuilder.FromSystem(character)
                    .WithDeliveryKey(deliveryKey)
                    .WithText("安徒恩攻坚支援", "请查收本次攻坚的皇室支援证书。")
                    .AddStackable(grant.Item!.ItemId, grant.Item.Quantity).Build(), ct).ConfigureAwait(false);
                return new(true);

            case AntonRaidRewardKind.Gold:
                var quantity = grant.Item!.Quantity;
                int? balance = null;
                if (online)
                {
                    var owner = characters.Get(character);
                    var before = await owner.GoldAsync(ct).ConfigureAwait(false);
                    balance = await owner.AddGoldAsync(quantity, ct).ConfigureAwait(false);
                    quantity -= Math.Clamp(balance.Value - before, 0, quantity);
                }
                if (quantity > 0)
                    await mailbox.SendAsync(MailBuilder.FromSystem(character)
                        .WithDeliveryKey(deliveryKey).WithText(RewardTitle, "请查收本次攻坚的金币奖励。")
                        .AddGold(quantity).Build(), ct).ConfigureAwait(false);
                return new(true, Gold: balance);

            case AntonRaidRewardKind.PartyCard:
            case AntonRaidRewardKind.SquadItem:
                var delivery = await items.DeliverAsync(recipient.AccountId, character,
                    [new ItemDeliveryGrant(grant.Item!.ItemId, grant.Item.Quantity)], RewardTitle, ct,
                    online ? ItemDeliveryMode.InventoryThenMail : ItemDeliveryMode.MailOnly,
                    new ItemDeliveryOptions(null, deliveryKey)).ConfigureAwait(false);
                return new(true, Delivery: delivery);

            case AntonRaidRewardKind.Quest:
                var quest = await quests.ReportAsync(recipient.AccountId, character,
                    [new QuestRaidPhaseCleared(grant.Phase, recipient.Role, grant.EndedAt)], ct).ConfigureAwait(false);
                await events.EnqueueAsync(new AntonRaidAchievementClear(character, grant.Phase, recipient.Role), ct)
                    .ConfigureAwait(false);
                return new(true, Quests: quest.NotificationSnapshot);

            default:
                throw new ArgumentOutOfRangeException(nameof(grant));
        }
    }

    private static void Validate(AntonRaidRewardGrant grant)
    {
        if (grant.Instance == Guid.Empty || grant.Phase > 1 || grant.Recipient.Role > 2
            || grant.Recipient.AccountId.Value <= 0 || grant.Recipient.Character.Value <= 0)
            throw new ArgumentException("Invalid Anton raid reward recipient or phase.", nameof(grant));

        var valid = grant.Kind switch
        {
            AntonRaidRewardKind.Quest => grant.Item is null,
            AntonRaidRewardKind.Gold => grant.Item is { ItemId: 0, Quantity: > 0 },
            AntonRaidRewardKind.PartyCard or AntonRaidRewardKind.SquadItem or AntonRaidRewardKind.RetryCertificate
                => grant.Item is { ItemId: > 0, Quantity: > 0 },
            _ => false,
        };
        if (!valid)
            throw new ArgumentException("Invalid Anton raid reward asset.", nameof(grant));
    }
}
