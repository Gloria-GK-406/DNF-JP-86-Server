using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class GrowthCapsules(CapsuleState state, IMarriageCapsuleContentSource content, IItemDeliveryDomainService delivery,
    IOnlineCharacterRuntimeDomainService onlineCharacters, HousingState housing, IDungeonRunQueryDomainService runs, IEventService events)
{
    public async ValueTask<MarriageCapsuleResult> ExecuteAsync(SelectedSessionIdentity actor, MarriageRelation relation, MarriageCapsuleAction action, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(actor);
        if (onlineCharacters.StandingOf(actor) is null || runs.Find(actor.CharacterId) is not null) return new(action == MarriageCapsuleAction.Make ? -6 : action == MarriageCapsuleAction.Give ? -7 : -8);
        if (action == MarriageCapsuleAction.Take && (await housing.ReadAsync(relation.Id, ct).ConfigureAwait(false) is not { } room || !housing.IsInside(actor, room))) return new(-8);
        var rules = content.Load(); var own = await state.ReadAsync(relation.Id, actor.CharacterId, ct).ConfigureAwait(false);
        var peer = relation.Proposer.CharacterId == actor.CharacterId ? relation.Recipient : relation.Proposer;
        var other = await state.ReadAsync(relation.Id, peer.CharacterId, ct).ConfigureAwait(false);
        IReadOnlyList<MarriageCapsuleItem> items = [];
        switch (action)
        {
            case MarriageCapsuleAction.Make:
                if (own.Owned >= rules.MaximumOwned) return new(-5);
                if (own.Energy < rules.MaximumEnergy) return new(-4);
                own = own with { Energy = own.Energy - rules.MaximumEnergy, Owned = checked((byte)(own.Owned + 1)) }; break;
            case MarriageCapsuleAction.Give:
                if (own.Owned == 0) return new(-4);
                if (other.Pending >= rules.MaximumPending) return new(-6);
                own = own with { Owned = checked((byte)(own.Owned - 1)) };
                other = other with { Pending = checked((byte)(other.Pending + 1)) };
                footprint.DemandActive(actor);
                await state.SaveAsync(relation.Id, peer.CharacterId, other, ct).ConfigureAwait(false); break;
            case MarriageCapsuleAction.Take:
                if (own.Pending == 0) return new(-4);
                try
                {
                    footprint.DemandActive(actor);
                    var grant = await delivery.DeliverAsync(actor.AccountId, actor.CharacterId, [new(rules.ItemId, 1)], "marriage-capsule", ct, ItemDeliveryMode.InventoryOnly).ConfigureAwait(false);
                    items = grant.Inventory.Select(x => new MarriageCapsuleItem(x.Slot, x.Snapshot)).ToArray();
                    footprint.RecordInventorySlots(actor, items.Select(x => x.Slot).ToArray());
                }
                catch (ContainerAdditionException e) when (e.Reason == ContainerAdditionFailure.NoRoom) { throw new MarriageCapsuleRejectedException(-6); }
                own = own with { Pending = checked((byte)(own.Pending - 1)) }; break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
        footprint.DemandActive(actor);
        await state.SaveAsync(relation.Id, actor.CharacterId, own, ct).ConfigureAwait(false);
        if (action is MarriageCapsuleAction.Give or MarriageCapsuleAction.Take)
        {
            footprint.DemandActive(actor);
            await events.EnqueueAsync(new MarriageCapsuleGiftChanged(peer.CharacterId), ct).ConfigureAwait(false);
        }
        return new(0) { State = own, Partner = peer.CharacterId, PartnerState = new(other, own.Pending > 0), Items = items };
    }
}
