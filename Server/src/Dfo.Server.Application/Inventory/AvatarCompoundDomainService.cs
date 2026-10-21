using System.Collections.Immutable;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
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

internal sealed class AvatarCompoundDomainService(ISelectedSessionDomainService sessions,
    ICharacterDomainService characters,
    IBusinessInventorySlotDomainService slots, IAvatarCompoundRuleService rules,
    IItemLifecycleDomainService lifecycle, IDomainTransactionAccessor transactions,
    IRequestReplayProjectionDomainService replay, IEventService events, TimeProvider clock) : IAvatarCompoundDomainService
{
    public async ValueTask<AvatarCompoundResult> CompoundAsync(SelectedSessionIdentity session, AvatarCompoundInput input, CancellationToken ct)
    {
        _ = transactions.Current ?? throw new InvalidOperationException("Avatar compound requires a transaction.");
        if (!sessions.IsInWorld(session))
            return new(AvatarCompoundRejection.NoSession);
        if (input.First == input.Second || input.First.Space != BusinessInventorySpace.Avatar
            || input.Second.Space != BusinessInventorySpace.Avatar
            || input.Material.Space is not (BusinessInventorySpace.Consumable or BusinessInventorySpace.QuickSlot))
            return new(AvatarCompoundRejection.InvalidRequest);
        BoundInventorySlot? first; BoundInventorySlot? second; BoundInventorySlot? material;
        try { first = slots.Find(session.CharacterId, input.First); second = slots.Find(session.CharacterId, input.Second);
            material = slots.Find(session.CharacterId, input.Material); }
        catch (InventorySlotMappingException) { return new(AvatarCompoundRejection.InvalidRequest); }
        if (first is null || second is null || material is null) return new(AvatarCompoundRejection.MissingMaterial);
        var a = await first.Container.GetAsync(input.First.Index, ct).ConfigureAwait(false);
        var b = await second.Container.GetAsync(input.Second.Index, ct).ConfigureAwait(false);
        var box = await material.Container.GetAsync(input.Material.Index, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (a is null || b is null || a.ItemId != input.FirstItemId || b.ItemId != input.SecondItemId
            || a.IsPile || b.IsPile || a.Snapshot.IsMirror || b.Snapshot.IsMirror
            || !a.Type.HasFlag(ItemTraits.Avatar) || !b.Type.HasFlag(ItemTraits.Avatar)
            || a.Snapshot.AvatarDetail is null || b.Snapshot.AvatarDetail is null)
            return new(AvatarCompoundRejection.InvalidAvatar);
        if (Locked(a.Snapshot) || Locked(b.Snapshot) || box is not null && Locked(box.Snapshot))
            return new(AvatarCompoundRejection.Locked);
        if (!Permanent(a.Snapshot) || !Permanent(b.Snapshot)) return new(AvatarCompoundRejection.UnsupportedLifetime);
        if (HasJewels(a.Snapshot) || HasJewels(b.Snapshot)) return new(AvatarCompoundRejection.UnsupportedContent);
        if (box is not { IsPile: true, Count: > 0 } || box.Snapshot.IsMirror
            || box.Snapshot.ExpireTime is > 0 and < int.MaxValue && box.Snapshot.ExpireTime <= now.ToUnixTimeSeconds())
            return new(AvatarCompoundRejection.MissingMaterial);
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        if (identity is null) return new(AvatarCompoundRejection.NoSession);
        if (await character.CanUseAsync(box.ItemId, ct) != ItemUsability.Allowed)
            return new(AvatarCompoundRejection.InvalidAvatar);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        if (progress is null) return new(AvatarCompoundRejection.NoSession);
        var plan = await rules.PlanAsync(identity.Job, progress.Level, input, box.ItemId, a.Snapshot, b.Snapshot, Random.Shared.Next(), ct);
        if (plan.Rejection != AvatarCompoundRejection.None) return new(plan.Rejection);
        if (plan.Outputs.IsDefaultOrEmpty || plan.Outputs.Length > 2 || plan.MaterialCount <= 0)
            throw new InvalidDataException("Invalid avatar compound plan.");
        if (box.Count < plan.MaterialCount) return new(AvatarCompoundRejection.MissingMaterial);
        var held = await first.Container.GetAllAsync(ct).ConfigureAwait(false);
        var occupied = held.Where(x => x.SlotIndex != input.First.Index && x.SlotIndex != input.Second.Index)
            .Select(x => x.SlotIndex).ToHashSet();
        var capacity = await first.Container.OpenSlotCountAsync(ct).ConfigureAwait(false);
        var destinations = Enumerable.Range(0, Math.Max(0, capacity)).Where(i => !occupied.Contains(i)).Take(plan.Outputs.Length).ToArray();
        if (destinations.Length != plan.Outputs.Length) return new(AvatarCompoundRejection.NoRoom);
        var records = new List<ItemRecord>();
        foreach (var output in plan.Outputs)
            records.Add(await lifecycle.CreateAsync(output with { InstanceValue = ArrivingAvatar.NewIdentity() }, ct).ConfigureAwait(false));
        await ItemContainerOperations.ConsumeAsync(first.Container, a, 1, lifecycle, ct).ConfigureAwait(false);
        await ItemContainerOperations.ConsumeAsync(second.Container, b, 1, lifecycle, ct).ConfigureAwait(false);
        await ItemContainerOperations.ConsumeAsync(material.Container, box, plan.MaterialCount, lifecycle, ct).ConfigureAwait(false);
        var outputs = ImmutableArray.CreateBuilder<AvatarCompoundOutput>();
        for (var i = 0; i < records.Count; i++)
        {
            var placed = await first.Container.PutAsync(destinations[i], records[i], ct).ConfigureAwait(false);
            outputs.Add(new(new(BusinessInventorySpace.Avatar, destinations[i]), placed.Snapshot.DeepCopy()));
        }
        var deductions = ImmutableArray.Create(new AvatarCompoundDeduction(input.First, 1),
            new AvatarCompoundDeduction(input.Second, 1), new AvatarCompoundDeduction(input.Material, plan.MaterialCount));
        replay.RecordInventorySlots(session, [input.First, input.Second, input.Material, .. outputs.Select(x => x.Slot)]);
        await events.EnqueueAsync(new ItemUsed(session.CharacterId, box.ItemId, plan.MaterialCount), ct).ConfigureAwait(false);
        return new(AvatarCompoundRejection.None, new(session, deductions, outputs.ToImmutable(), plan.RareRate, now));
    }
    private static bool Locked(ItemSnapshot item) => item.SortLockFlag != 0 || item.EquipmentLockId != 0 || item.TradeRestriction != 0;
    private static bool Permanent(ItemSnapshot item) => item.ExpireTime is 0 or int.MaxValue
        && item.AvatarDetail is { ExpireDate: 0 or int.MaxValue, DeleteDate: 0 };
    private static bool HasJewels(ItemSnapshot item) => item.EmblemId1 != 0 || item.EmblemId2 != 0
        || item.AvatarDetail!.JewelSocket.Any(x => x != 0);
}
