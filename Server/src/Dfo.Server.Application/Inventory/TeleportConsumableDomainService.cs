using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class TeleportConsumableDomainService(ISelectedSessionDomainService sessions,
    IDungeonRunDomainService runs, IOnlineCharacterRuntimeDomainService onlineCharacters,
    IBusinessInventorySlotDomainService slots, ICharacterDomainService characters,
    ITeleportConsumableRuleService rules, IItemUseJudgementService judgement,
    IApplyEffectsDomainService effects, TimeProvider clock) : ITeleportConsumableDomainService
{
    public async ValueTask<TeleportConsumableResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        int expectedItemId, byte townId, ChannelEntryIdentity channel, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (!sessions.IsInWorld(session) || runs.Find(session.CharacterId) is not null
            || onlineCharacters.StandingOf(session) is not { } member
            || onlineCharacters.PositionOf(session) is not { } standing)
            return new(ItemUsability.CharacterStateUnavailable);
        var bound = slots.Find(session.CharacterId, source);
        var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (item is null || item.ItemId != expectedItemId) return new(ItemUsability.NotAUsableItem);
        var rule = await rules.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
        if (rule is null) return new(ItemUsability.NotAUsableItem);
        var facts = await characters.Get(session.CharacterId)
            .ItemUseFactsAsync(judgement.CooldownKeyFor(item.ItemId, rule.Use), ct).ConfigureAwait(false);
        if (facts is null) return new(ItemUsability.CharacterStateUnavailable);
        var at = clock.GetUtcNow();
        var plan = await rules.BuildAsync(rule, source, item, facts, standing, townId, channel, member.Area, at, ct).ConfigureAwait(false);
        if (plan.Status != ItemUsability.Allowed) return new(plan.Status);
        var applied = await effects.ApplyAsync(new(session.AccountId, session.CharacterId, at, footprint, session), plan.Effects, ct).ConfigureAwait(false);
        var remaining = applied.Changes<ContainerConsumptionChange>().Single().RemainingCount;
        return new(ItemUsability.Allowed, new(session, source, item.ItemId, remaining,
            applied.Changes<TownPositionChange>().Single().After, TimeSpan.FromMilliseconds(rule.Use.CooldownMilliseconds), at,
            remaining == 0 ? null : item.Snapshot with { Count = remaining }, item.ItemUid));
    }
}
