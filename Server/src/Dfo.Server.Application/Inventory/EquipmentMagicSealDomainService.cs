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
    IEquipmentDomainService equipments,
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
        IEquipmentInstance? instance;
        try { instance = await equipments.Of(session.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false); }
        catch (InventorySlotMappingException)
        { return new(EquipmentMagicSealRejection.InvalidRequest); }
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (target is null)
            return new(EquipmentMagicSealRejection.ItemUnavailable);
        if (target.Core.ExpireTime > 0
            && target.Core.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentMagicSealRejection.ItemUnavailable);
        if (target.Core.EquipmentLockId != 0 || target.Core.SortLockFlag != 0)
            return new(EquipmentMagicSealRejection.Locked);
        var oldOptions = new[] { target.Core.RandomOption0, target.Core.RandomOption1,
            target.Core.RandomOption2 };
        var decision = await rules.DecideAsync(target.Core.ItemId, oldOptions,
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
        var changed = (await instance!.SetMagicSealOptionsAsync(nextOptions, ct).ConfigureAwait(false)).Core;
        return new(EquipmentMagicSealRejection.None,
            new(session, targetSlot, changed, gold, decision.Seed, clock.GetUtcNow()));
    }
}
