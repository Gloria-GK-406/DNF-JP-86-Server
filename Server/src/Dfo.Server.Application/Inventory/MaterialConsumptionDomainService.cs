using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class MaterialConsumptionDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots, IMaterialConsumptionRuleService rules) : IMaterialConsumptionDomainService
{
    public async ValueTask<MaterialConsumptionResult> ConsumeAsync(SelectedSessionIdentity session,
        IReadOnlyList<MaterialCost> costs, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new("NoSession", []);
        if (!rules.IsValidRequest(costs)) return new("InvalidRequest", []);

        // Validate every source before staging any debit; the command owns their transaction.
        var pending = new List<(MaterialCost Cost, BoundInventorySlot Bound, ContainerSlotItem Item)>();
        foreach (var cost in costs)
        {
            if (!(cost.Slot.Space.IsCarriedItemSpace() || cost.Slot.Space == BusinessInventorySpace.SharedMaterial))
                return new("InvalidLocation", []);
            var bound = slots.Find(session.CharacterId, cost.Slot);
            var item = bound is null ? null : await bound.Container.ReadSlotAsync(bound.Slot.Index, ct);
            if (item is null || !rules.IsConsumable(item, cost.ItemId)) return new("InvalidMaterial", []);
            if (item.Count < cost.Count) return new("InsufficientBalance", []);
            pending.Add((cost, bound!, item));
        }
        var debits = new List<MaterialDebit>();
        foreach (var (cost, bound, item) in pending)
        {
            var remaining = await ItemContainerOperations.ConsumeAsync(bound.Container, item, cost.Count, ct);
            debits.Add(new(cost, remaining == 0 ? null : item.Core with { Value = remaining }));
        }
        return new(null, debits);
    }
}
