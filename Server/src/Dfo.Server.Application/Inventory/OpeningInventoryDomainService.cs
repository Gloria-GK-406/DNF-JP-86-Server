using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Creatures;
namespace Dfo.Server.Application.Inventory;
internal sealed class OpeningInventoryDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICharacterTable characters,
    ICharacterBusinessInventoryDomainService businessInventories, IItemDomainStore store,
    IDomainTransactionAccessor transactions, ICharacterInventoryEntryDomainService inventory,
    IPremiumServiceDomainService premiums, ICharacterCreatureTable creatures,
    ICharacterDomainService characterDomain, IApplyEffectsDomainService effects,
    ICreatureDomainService? creatureDomains = null) : IOpeningInventoryDomainService
{
    public async ValueTask<ContainerSlotItem> SourceAsync(SelectedSessionIdentity session, BusinessSlot slot, DateTimeOffset now, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Opening requires a transaction.");
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, ct).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.NoSession, "No current owned selection.");
        var bag = businessInventories.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(slot.Space))
            .FindContainer(slot.Space)
            ?? throw new OpeningRejectedException(OpeningRejection.NoSession, "No source container.");
        var item = await bag.ReadSlotAsync(slot.Index, ct).ConfigureAwait(false);
        if (item is null || !item.IsPile || item.Count <= 0
            || item.Core.ExpireTime is > 0 and < int.MaxValue && item.Core.ExpireTime <= now.ToUnixTimeSeconds())
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "Missing, expired or nonstackable source.");
        var usability = await characterDomain.Get(session.CharacterId).CanUseAsync(item.ItemId, ct).ConfigureAwait(false);
        if (usability != ItemUsability.Allowed)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, $"Opening source is unusable: {usability}.");
        return item;
    }

    public ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadPremiumsAsync(SelectedSessionIdentity session, CancellationToken ct) =>
        premiums.For(session.AccountId).ReadAsync(ct);

    public async ValueTask<OpeningEffectsApplication> ApplyAsync(SelectedSessionIdentity session,
        IReadOnlyList<DomainEffect> plan, DateTimeOffset now, CancellationToken ct)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Opening requires a transaction.");
        ApplyEffectsResult applied;
        try { applied = await effects.ApplyAsync(new(session.AccountId, session.CharacterId, now, session), plan, ct).ConfigureAwait(false); }
        catch (InventoryEffectException exception)
        {
            var reason = exception.Rejection switch {
                InventoryEffectRejection.NoRoom => OpeningRejection.NoRoom,
                InventoryEffectRejection.UnsupportedItem => OpeningRejection.UnsupportedContent,
                _ => OpeningRejection.MissingMaterial,
            };
            throw new OpeningRejectedException(reason, exception.Message);
        }
        await store.SessionIn(transaction).ApplyDecidedWritesAsync(ct).ConfigureAwait(false);
        var touched = applied.Changes<ContainerConsumptionChange>().Select(x => x.Slot)
            .Concat(applied.Changes<PreparedItemGrantChange>().SelectMany(x => x.Slots))
            .Concat(applied.Changes<OpeningVirtualChange>().Select(x => x.Slot)).Distinct().ToArray();
        var snapshot = await inventory.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var updates = ImmutableArray.CreateBuilder<OpeningInventoryUpdate>();
        var cleared = ImmutableArray.CreateBuilder<BusinessSlot>();
        foreach (var slot in touched)
        {
            var storage = slot.Space == BusinessInventorySpace.VirtualResource
                ? new LegacyInventoryStorageCoordinate(InventoryContainerType.Main, slot.Index)
                : LegacyInventoryStorageCoordinates.ToStorage(slot);
            var item = snapshot.Containers.Single(b => b.ContainerType == storage.List).Items.SingleOrDefault(i => i.SlotIndex == storage.Index);
            if (item is null) cleared.Add(slot); else updates.Add(new(slot, item.Identity, item.Core));
        }
        var displayed = applied.Applied.SelectMany(x => x.Change switch {
            PreparedItemGrantChange physical => physical.Rewards.AsEnumerable(),
            OpeningVirtualChange virtualChange => new[] { virtualChange.Reward },
            OpeningPremiumChange premium => new[] { premium.Reward },
            _ => Enumerable.Empty<OpeningRewardDisplay>(),
        }).ToImmutableArray();
        var changedPremiums = applied.Changes<OpeningPremiumChange>().Select(x => x.Premium)
            .GroupBy(x => x.PremiumType).Select(x => x.Last()).ToImmutableArray();
        var creaturesChanged = applied.Applied.Any(x => x.Effect is GrantPreparedItemEffect g && g.Grant.Item.Placement.WorldClass == ItemWorldClass.Creature);
        ImmutableArray<CharacterPetRuntimeState> pets = [];
        if (creaturesChanged)
        {
            if (creatureDomains is not null)
            {
                var states = await creatureDomains.GetStore(session.CharacterId).ReadAllAsync(ct).ConfigureAwait(false);
                if (states.Count > byte.MaxValue)
                    throw new OpeningRejectedException(OpeningRejection.NoRoom, "Creature count cannot fit the protocol.");
                pets = states.Select(CreatureRuntimeProjection.Build).ToImmutableArray();
            }
            else
            {
                var rows = await creatures.ReadAsync(session.CharacterId, ct).ConfigureAwait(false);
                if (rows.Count > byte.MaxValue) throw new OpeningRejectedException(OpeningRejection.NoRoom, "Creature count cannot fit the protocol.");
                pets = rows.OrderBy(row => row.SortOrder).Select(row => new CharacterPetRuntimeState(row.CreatureKey,
                    row.Field04, checked((byte)row.ModeFlag), row.ProgressValue, row.Mode1Field0A, row.Mode1Field0B,
                    row.FieldAfterValue, [.. row.CreatureText], checked((byte)row.TailFlag))).ToImmutableArray();
            }
        }
        return new(new(session, updates.ToImmutable(), cleared.ToImmutable(), displayed, now, changedPremiums, pets, creaturesChanged), applied);
    }
}
