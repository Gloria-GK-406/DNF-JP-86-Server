using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentMagicSealDomainService(
    ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers,
    IBusinessInventorySlotDomainService slots,
    IEquipmentMagicSealRuleService rules,
    IEquipmentMagicSealSeedSource seeds,
    ICharacterDomainService characters,
    TimeProvider clock) : IEquipmentMagicSealDomainService
{
    public ValueTask<EquipmentMagicSealResult> UnsealAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, BusinessSlot? scrollSlot,
        CancellationToken ct) => ApplyAsync(session, targetSlot, scrollSlot, null, ct);

    public ValueTask<EquipmentMagicSealResult> ChangeAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, BusinessSlot? materialSlot,
        byte optionIndex, CancellationToken ct) =>
        ApplyAsync(session, targetSlot, materialSlot, optionIndex, ct);

    private async ValueTask<EquipmentMagicSealResult> ApplyAsync(
        SelectedSessionIdentity session, BusinessSlot targetSlot, BusinessSlot? materialSlot,
        byte? changeIndex, CancellationToken ct)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentMagicSealRejection.NoSession);
        if (targetSlot.Space != BusinessInventorySpace.CarriedEquipment
            || materialSlot is not null)
            return new(EquipmentMagicSealRejection.InvalidRequest);
        BoundInventorySlot? bound;
        try { bound = slots.Find(session.CharacterId, targetSlot); }
        catch (InventorySlotMappingException)
        { return new(EquipmentMagicSealRejection.InvalidRequest); }
        var target = bound is null ? null
            : await bound.Container.ReadSlotAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (target is null || target.WorldClass != ItemWorldClass.Equipment)
            return new(EquipmentMagicSealRejection.ItemUnavailable);
        if (target.Core.ExpireTime > 0
            && target.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentMagicSealRejection.ItemUnavailable);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentMagicSealRejection.Locked);
        var oldOptions = new[] { target.Core.RandomOption0, target.Core.RandomOption1,
            target.Core.RandomOption2 };
        var decision = await rules.DecideAsync(target.ItemId, oldOptions,
            changeIndex, seeds.NextSeed(), ct).ConfigureAwait(false);
        if (decision.Rejection != EquipmentMagicSealRejection.None)
            return new(decision.Rejection);
        var nextOptions = decision.Options
            ?? throw new InvalidOperationException("Magic-seal success lacks options.");
        if (nextOptions.Count is < 1 or > 3 || decision.GoldCost < 0)
            throw new InvalidOperationException("Magic-seal rule returned an invalid settlement.");
        var character = characters.Get(session.CharacterId);
        if (await character.IdentityAsync(ct).ConfigureAwait(false) is null)
            return new(EquipmentMagicSealRejection.NoSession);
        if (await character.GoldAsync(ct).ConfigureAwait(false) < decision.GoldCost)
            return new(EquipmentMagicSealRejection.InsufficientGold);
        var gold = await character.TrySpendGoldAsync(decision.GoldCost, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Validated magic-seal gold changed during settlement.");
        var changed = target.Core with
        {
            RandomOption0 = nextOptions[0],
            RandomOption1 = nextOptions.Count > 1 ? nextOptions[1] : new(0, 0, 0),
            RandomOption2 = nextOptions.Count > 2 ? nextOptions[2] : new(0, 0, 0),
            RandomOptionState = 0,
            RandomOptionChangedIndex = byte.MaxValue,
            RandomOptionChangeState = 0,
            RandomOptionChange = new(0, 0, 0),
        };
        await bound!.Container.PutAsync(bound.Slot.Index, changed, ct).ConfigureAwait(false);
        return new(EquipmentMagicSealRejection.None,
            new(session, targetSlot, changed, gold, decision.Seed, clock.GetUtcNow()));
    }
}
