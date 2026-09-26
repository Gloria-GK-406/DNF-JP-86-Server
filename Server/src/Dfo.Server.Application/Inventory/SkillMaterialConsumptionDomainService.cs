using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class SkillMaterialConsumptionDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots, IAccountMaterialBagDomainService materials,
    IAccountMaterialRuleService materialRules, ISkillMaterialConsumptionRuleService rules) : ISkillMaterialConsumptionDomainService
{
    public async ValueTask<SkillMaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,
        IReadOnlyList<SkillMaterialCost> costs, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new("NoSession", []);
        if (!rules.IsValidRequest(costs))
            return new("InvalidRequest", []);

        // Validate the whole batch before staging any debit. The command owns the transaction.
        var pending = new List<(SkillMaterialCost Cost, BoundInventorySlot? Bound, InventoryItemCoreState? Remaining)>();
        foreach (var cost in costs)
        {
            if (cost.Location is SkillMaterialLocation.Account account)
            {
                if (materialRules.Get(account.Kind).ItemId != cost.ItemId)
                    return new("ItemMismatch", []);
                if ((await materials.Get(session.AccountId).SnapshotAsync(ct)).Count(account.Kind) < cost.Count)
                    return new("InsufficientBalance", []);
                pending.Add((cost, null, null));
            }
            else if (cost.Location is SkillMaterialLocation.Carried carried && carried.Slot.Space.IsCarriedItemSpace())
            {
                var bound = slots.Find(session.CharacterId, carried.Slot);
                var item = bound is null ? null : await bound.Container.ReadSlotAsync(bound.StorageIndex, ct);
                if (item is null || !rules.IsConsumable(item, cost.ItemId))
                    return new("InvalidMaterial", []);
                if (item.Count < cost.Count) return new("InsufficientBalance", []);
                pending.Add((cost, bound, item.Count == cost.Count ? null : item.Core with { Value = item.Count - cost.Count }));
            }
            else return new("InvalidLocation", []);
        }
        var debits = new List<SkillMaterialDebit>();
        foreach (var (cost, bound, remaining) in pending)
        {
            AccountMaterialChange? change = null;
            if (cost.Location is SkillMaterialLocation.Account account)
                change = await materials.Get(session.AccountId).ConsumeAsync(account.Kind, cost.Count, ct)
                    ?? throw new InvalidOperationException("Validated material balance changed during consumption.");
            else if (remaining is null) await bound!.Container.ClearAsync(bound.StorageIndex, ct);
            else await bound!.Container.PutAsync(bound.StorageIndex, remaining, ct);
            debits.Add(new(cost, remaining, change));
        }
        return new(null, debits);
    }
}
