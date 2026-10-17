using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Wedding rows: the fee, the opening-time settlement or refund, departure coordinates and the cleanup queue.</summary>
internal sealed class WeddingSettlement(IMarriageWeddingDomainStore store, IMarriageChurchRuleService rules, IMarriageProposalRuleService genders,
    ISelectedSessionDomainService sessions, ICharacterDomainService characters, ICharacterPositionDomainService positions,
    ISelectedSessionPositionPersistence persistence, IMailboxDomainService mail, IItemDeliveryDomainService delivery,
    ICharacterTable owners, ICharacterDomainStore characterStore, IDomainTransactionAccessor transactions)
{
    public MarriageWeddingRules Rules(int tier) => rules.WeddingRules(tier);

    public ValueTask<MarriageWedding?> ReadAsync(Guid ceremony, CancellationToken ct) => store.ReadAsync(ceremony, ct);

    /// <summary>Weddings still preparing, or still owning a room or cleanup entries.</summary>
    public ValueTask<IReadOnlyList<MarriageWedding>> PendingAsync(CancellationToken ct) => store.PendingAsync(ct);

    public ValueTask<bool> FinishAsync(Guid ceremony, MarriageWeddingPhase phase, DateTimeOffset at, CancellationToken ct) =>
        store.FinishAsync(ceremony, phase, at, ct);

    public ValueTask QueueCleanupAsync(Guid ceremony, MarriageWeddingCleanup cleanup, CancellationToken ct) =>
        store.QueueCleanupAsync(ceremony, cleanup, ct);

    public ValueTask<IReadOnlyList<MarriageWeddingCleanup>> CleanupAsync(Guid ceremony, CancellationToken ct) => store.CleanupAsync(ceremony, ct);

    public ValueTask CompleteCleanupAsync(Guid ceremony, CharacterId character, CancellationToken ct) =>
        store.CompleteCleanupAsync(ceremony, character, ct);

    /// <exception cref="MarriageWeddingUnavailableException">The applicant cannot pay the fee.</exception>
    public async ValueTask OpenAsync(MarriageCeremony ceremony, MarriageWeddingRules rule, CancellationToken ct)
    {
        if (await characters.Get(ceremony.Applicant.CharacterId).TrySpendGoldAsync(rule.Gold, ct).ConfigureAwait(false) is null)
            throw new MarriageWeddingUnavailableException();
        await store.CreateAsync(new(ceremony, MarriageWeddingPhase.Preparing, rule.Gold), ct).ConfigureAwait(false);
    }

    /// <summary>The successful opening: ceremony positions, spouse gifts, the gift share and the guests' return gifts.</summary>
    public async ValueTask<IReadOnlyList<MarriageWeddingStanding>> CompleteAsync(MarriageCeremony ceremony, MarriageWeddingRules rule,
        IReadOnlyList<MarriageWeddingInvitation> guests, CancellationToken ct)
    {
        var standing = new List<MarriageWeddingStanding>();
        foreach (var owner in new[] { ceremony.Applicant, ceremony.Partner })
        {
            if (rule.CeremonyPositions.Count == 2)
            {
                var identity = await characters.Get(owner.CharacterId).IdentityAsync(ct).ConfigureAwait(false) ?? throw new MarriageWeddingUnavailableException();
                // Assign the two PVF ground positions consistently with our bride/groom projection.
                var spot = rule.CeremonyPositions[genders.IsFemale(identity.Job) ? 0 : 1];
                var actor = sessions.FindByCharacter(owner.CharacterId) ?? throw new MarriageWeddingUnavailableException();
                var position = positions.Find(owner.CharacterId) ?? throw new MarriageWeddingUnavailableException();
                position = position with { X = spot.X, Y = spot.Y };
                await persistence.PersistAsync(actor.AccountId, actor.CharacterId, position, ct).ConfigureAwait(false);
                standing.Add(new(actor, position));
            }
            if (rule.GiftItemId > 0) await delivery.DeliverAsync(owner.AccountId, owner.CharacterId,
                [new ItemDeliveryGrant(rule.GiftItemId, 1)], "普通婚礼奖励", ct, ItemDeliveryMode.MailOnly).ConfigureAwait(false);
            var share = guests.Sum(x => (long)x.Gold) * (100 - rule.GiftChargePercent) / 100 / 2;
            if (share > 0) await mail.SendAsync(MailBuilder.FromSystem(owner.CharacterId).WithText("婚礼礼金", "婚礼宾客礼金结算").AddGold(checked((int)share)).Build(), ct).ConfigureAwait(false);
        }
        foreach (var guest in guests.Where(x => x.Gold > 0))
        {
            var bracket = rule.GuestRewards.Where(x => x.MaximumGold >= guest.Gold).OrderBy(x => x.MaximumGold).FirstOrDefault()?.MaximumGold;
            var rewards = rule.GuestRewards.Where(x => x.MaximumGold == bracket).Select(x => new ItemDeliveryGrant(x.ItemId, x.Count)).ToArray();
            if (rewards.Length > 0 && await owners.FindOwnerAsync(guest.Guest, ct).ConfigureAwait(false) is { } owner)
                await delivery.DeliverAsync(owner, guest.Guest, rewards, "婚礼宾客回礼", ct, ItemDeliveryMode.MailOnly).ConfigureAwait(false);
        }
        return standing;
    }

    /// <summary>The cancelled opening: the fee back to the applicant, then each guest's gift.</summary>
    public async ValueTask RefundAsync(MarriageWedding wedding, IReadOnlyList<MarriageWeddingInvitation> guests, CancellationToken ct)
    {
        await RefundAsync(wedding.Ceremony.Applicant.CharacterId, wedding.PaidGold, "未能按时开场，退回婚礼费用。", ct).ConfigureAwait(false);
        foreach (var guest in guests) await RefundAsync(guest.Guest, guest.Gold, "婚礼取消，退回礼金。", ct).ConfigureAwait(false);
    }

    /// <summary>Rewrites the saved position of every listed character whose save still points into the room.</summary>
    public async ValueTask PersistExitAsync(MarriageChurchRoom room, IEnumerable<CharacterId> charactersToMove, MarriageChurchLocation exit, CancellationToken ct)
    {
        foreach (var character in charactersToMove)
            if (await owners.FindOwnerAsync(character, ct).ConfigureAwait(false) is { } account
                && await characterStore.SessionIn(transactions.Current ?? throw new InvalidOperationException("Wedding advancement requires a transaction.")).ReadPositionAsync(new(account, character), ct).ConfigureAwait(false) is { } saved
                && saved.TownId == room.TownId && saved.AreaId == room.AreaId)
                await persistence.PersistAsync(account, character, saved with { TownId = exit.TownId, AreaId = exit.AreaId, X = exit.X, Y = exit.Y }, ct).ConfigureAwait(false);
    }

    private async ValueTask RefundAsync(CharacterId character, int gold, string text, CancellationToken ct)
    {
        if (gold > 0) await mail.SendAsync(MailBuilder.FromSystem(character).WithText("婚礼退款", text).AddGold(gold).Build(), ct).ConfigureAwait(false);
    }
}
