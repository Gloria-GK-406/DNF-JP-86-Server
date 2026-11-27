using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRenameDomainService(IGuildDomainStore guilds, GuildProductRuleService rules,
    IBusinessInventorySlotDomainService slots, IItemLifecycleDomainService lifecycle, ICharacterDomainService characters,
    TimeProvider clock)
    : IGuildRenameDomainService
{
    public async ValueTask<GuildRenameCheck> CheckAsync(CharacterId actor, string name, CancellationToken ct)
    {
        if (GuildProfileRuleService.NameKey(name) is not { } key)
            return new(GuildMutationStatus.InvalidName);
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership)
            return new(GuildMutationStatus.AlreadyMember);
        if (await guilds.NameTakenAsync(key, ct).ConfigureAwait(false))
            return new(GuildMutationStatus.NameTaken);

        return new(GuildMutationStatus.Success, new(membership.Guild.Id, membership.Guild.Name, name));
    }

    public async ValueTask<GuildRenameReceipt?> RenameAsync(SelectedSessionIdentity session,
        GuildRenameAuthorization authorization, BusinessSlot source, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (source.Space is not (BusinessInventorySpace.Consumable or BusinessInventorySpace.Material)
            || GuildProfileRuleService.NameKey(authorization.Name) is not { } key
            || await guilds.FindMembershipAsync(session.CharacterId, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership
            || membership.Guild.Id != authorization.Guild || membership.Guild.Name != authorization.OriginalName
            || slots.Find(session.CharacterId, source) is not { } bound
            || await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false) is not { } card
            || card.ItemId != GuildGoldRenameOffer.VoucherItemId || card.Count < 1 || card.Snapshot.IsMirror
            || card.Snapshot.EquipmentLockId != 0 || card.Snapshot.SortLockFlag != 0
            || card.Snapshot.Lifetime.IsExpired(clock.GetUtcNow()))
            return null;

        if (!await guilds.TryRenameAsync(authorization.Guild, authorization.OriginalName, authorization.Name, key, ct)
                .ConfigureAwait(false))
            return null;

        await ItemContainerOperations.ConsumeAsync(bound.Container, card, 1, lifecycle, ct).ConfigureAwait(false);
        footprint.RecordInventorySlots(session, [source]);
        return new((await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false))?.Snapshot);
    }

    public bool IsGoldProduct(uint productId) => rules.GoldRenameOffer(productId) is not null;

    public async ValueTask<GuildMutationResult> RenameWithGoldAsync(CharacterId actor, GuildRenameAuthorization authorization,
        uint productId, CancellationToken ct)
    {
        if (rules.GoldRenameOffer(productId) is not { } offer)
            return new(GuildMutationStatus.UnsupportedContent);
        if (GuildProfileRuleService.NameKey(authorization.Name) is not { } key)
            return new(GuildMutationStatus.InvalidName);
        if (await guilds.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { Member.IsLeader: true } membership
            || membership.Guild.Id != authorization.Guild)
            return new(GuildMutationStatus.LeaderRequired);
        if (membership.Guild.Name != authorization.OriginalName)
            return new(GuildMutationStatus.InvalidName);
        var character = characters.Get(actor);
        if (await character.GoldAsync(ct).ConfigureAwait(false) < offer.GoldCost)
        {
            return await guilds.NameTakenAsync(key, ct).ConfigureAwait(false)
                ? new(GuildMutationStatus.NameTaken)
                : new(GuildMutationStatus.InsufficientGold);
        }
        if (!await guilds.TryRenameAsync(authorization.Guild, authorization.OriginalName, authorization.Name, key, ct)
                .ConfigureAwait(false))
            return new(GuildMutationStatus.NameTaken);
        if (await character.TrySpendGoldAsync(offer.GoldCost, ct).ConfigureAwait(false) is not { } remaining)
            throw new InvalidOperationException("Gold changed during the guild rename transaction.");
        return new(GuildMutationStatus.Success, authorization.Guild, RemainingGold: remaining);
    }
}
