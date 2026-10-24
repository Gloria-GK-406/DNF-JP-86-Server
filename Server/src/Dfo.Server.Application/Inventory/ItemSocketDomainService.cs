using System.Collections.Immutable;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Inventory;

internal sealed class ItemSocketDomainService(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService slots,
    IEquipmentDomainService equipments, IAvatarDomainService avatars, IItemSocketRuleService rules,
    IItemLifecycleDomainService lifecycle, IDomainTransactionAccessor transactions,
    IEventService events, TimeProvider clock) : IItemSocketDomainService
{
    public async ValueTask<ItemSocketResult> ApplyAsync(SelectedSessionIdentity session, ItemSocketInput input, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(session);
        _ = transactions.Current ?? throw new InvalidOperationException("Socket operations require a transaction.");
        if (!sessions.IsInWorld(session))
            return new(ItemSocketRejection.NoSession);
        if (input.Avatar ? input.Target.Space is not (BusinessInventorySpace.Avatar or BusinessInventorySpace.WornAvatar)
            : input.Target.Space != BusinessInventorySpace.CarriedEquipment)
            return new(ItemSocketRejection.InvalidRequest);
        if (input.Open ? input.Drill is null || !input.Emblems.IsDefaultOrEmpty
            : input.Drill is not null || input.Emblems.IsDefaultOrEmpty || input.Emblems.Length > (input.Avatar ? 3 : 2))
            return new(ItemSocketRejection.InvalidRequest);
        IItemSocketInstance? instance;
        try
        {
            instance = input.Avatar ? await avatars.FindAtAsync(session.CharacterId, input.Target, ct).ConfigureAwait(false)
                : await equipments.Of(session.CharacterId).FindAtAsync(input.Target, ct).ConfigureAwait(false);
        }
        catch (InventorySlotMappingException) { return new(ItemSocketRejection.InvalidRequest); }
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (target is null || target.ItemId != input.ExpectedItemId || Unavailable(target.Snapshot, now))
            return new(ItemSocketRejection.MissingItem);
        if (Locked(target.Snapshot)) return new(ItemSocketRejection.Locked);
        var requests = input.Open ? new[] { new EmblemPlacement(input.Drill!.Value, 0, 0) } : input.Emblems.ToArray();
        var materials = new List<(BusinessSlot Slot, IBusinessItemContainer Container, ItemReference Item, int Count)>();
        foreach (var group in requests.GroupBy(x => x.Material))
        {
            var slot = group.Key;
            if (slot.Space is not (BusinessInventorySpace.QuickSlot or BusinessInventorySpace.Consumable
                or BusinessInventorySpace.Material or BusinessInventorySpace.AvatarEmblem))
                return new(ItemSocketRejection.InvalidRequest);
            BoundInventorySlot? bound;
            try { bound = slots.Find(session.CharacterId, slot); }
            catch (InventorySlotMappingException) { return new(ItemSocketRejection.InvalidRequest); }
            var material = bound is null ? null : await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
            if (material is not { IsPile: true } || material.Count < group.Count() || Unavailable(material.Snapshot, now)
                || Locked(material.Snapshot) || !input.Open && group.Any(x => x.ExpectedItemId != material.ItemId))
                return new(ItemSocketRejection.MissingMaterial);
            materials.Add((slot, bound!.Container, material, group.Count()));
        }
        ImmutableArray<ushort> types;
        ImmutableArray<SocketEmbedding> embeddings = [];
        var consume = true;
        if (input.Open)
        {
            var plan = await rules.OpenAsync(target!.Snapshot, input.Avatar, materials[0].Item.ItemId, now, ct).ConfigureAwait(false);
            if (plan.Rejection != ItemSocketRejection.None) return new(plan.Rejection);
            types = plan.Types; consume = plan.MaterialCount != 0;
        }
        else
        {
            var plan = await rules.EmbedAsync(target!.Snapshot, input.Avatar, input.Emblems, ct).ConfigureAwait(false);
            if (plan.Rejection != ItemSocketRejection.None) return new(plan.Rejection);
            types = plan.Types; embeddings = plan.Embeddings;
        }
        footprint.DemandActive(session);
        if (input.Open) await instance!.OpenSocketsAsync(types, ct).ConfigureAwait(false);
        else await instance!.EmbedAsync(types, embeddings, ct).ConfigureAwait(false);
        var remainders = ImmutableArray.CreateBuilder<SocketMaterialRemainder>();
        if (consume)
            foreach (var material in materials)
            {
                footprint.DemandActive(session);
                await ItemContainerOperations.ConsumeAsync(material.Container, material.Item, material.Count, lifecycle, ct).ConfigureAwait(false);
                remainders.Add(new(material.Slot, material.Item.Count - material.Count));
                footprint.DemandActive(session);
                await events.EnqueueAsync(new ItemUsed(session.CharacterId, material.Item.ItemId, material.Count), ct).ConfigureAwait(false);
            }
        footprint.RecordInventorySlots(session, [input.Target, .. materials.Select(x => x.Slot)]);
        return new(ItemSocketRejection.None, new(session, input, remainders.ToImmutable(), now));
    }
    private static bool Locked(ItemSnapshot item) => item.EquipmentLockId != 0 || item.SortLockFlag != 0;
    private static bool Unavailable(ItemSnapshot item, DateTimeOffset now) => item.IsMirror
        || item.ExpireTime > 0 && item.ExpireTime <= now.ToUnixTimeSeconds()
        || item.AvatarDetail is { } avatar && (avatar.ExpireDate > 0 && avatar.ExpireDate <= now.ToUnixTimeSeconds()
            || avatar.DeleteDate > 0 && avatar.DeleteDate <= now.ToUnixTimeSeconds());
}
