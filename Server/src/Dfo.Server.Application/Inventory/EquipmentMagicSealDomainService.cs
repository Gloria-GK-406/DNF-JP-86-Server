using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
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
    IEquipmentDomainService equipments,
    IEquipmentEffectPlanRuleService rules,
    IApplyEffectsDomainService effects,
    IInventoryEffectPreflightDomainService preflight,
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
        if (!sessions.IsInWorld(session))
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
        if (target.Snapshot.ExpireTime > 0
            && target.Snapshot.ExpireTime <= clock.GetUtcNow().ToUnixTimeSeconds())
            return new(EquipmentMagicSealRejection.ItemUnavailable);
        if (target.Snapshot.EquipmentLockId != 0 || target.Snapshot.SortLockFlag != 0)
            return new(EquipmentMagicSealRejection.Locked);
        var plan = await rules.MagicSealAsync(target, changeIndex, seeds.NextSeed(), ct).ConfigureAwait(false);
        var decision = plan.Decision;
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
        var context = new EffectExecutionContext(session.AccountId, session.CharacterId, clock.GetUtcNow(), session);
        if ((await preflight.PreviewAsync(context, plan.Effects, ct).ConfigureAwait(false)).Rejection != InventoryEffectRejection.None)
            return new(EquipmentMagicSealRejection.InsufficientGold);
        var applied = await effects.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        var gold = applied.Changes<GoldEffectChange>().Single().Remaining;
        var changed = applied.Changes<EquipmentEffectChange>().Single().After!;
        return new(EquipmentMagicSealRejection.None,
            new(session, targetSlot, changed, gold, decision.Seed, context.At));
    }
}
