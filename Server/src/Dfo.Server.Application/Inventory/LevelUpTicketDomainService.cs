using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class LevelUpTicketDomainService(IOpeningInventoryDomainService inventory,
    ICharacterDomainService characters, ICharacterConditionTable conditions,
    IConsumableStateDomainService states, ILevelUpTicketRuleService rules) : ILevelUpTicketDomainService
{
    public async ValueTask<LevelUpTicketResult> UseAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, CancellationToken ct)
    {
        var item = await inventory.SourceAsync(session, source, at, ct).ConfigureAwait(false);
        var character = characters.Get(session.CharacterId);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        var condition = await conditions.FindAsync(session.CharacterId, ct).ConfigureAwait(false);
        if (identity is null || progress is null || condition is null)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character state unavailable.");
        var quests = await states.QuestsAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var plan = await rules.PlanAsync(source, item, identity, progress, condition.HardcoreMode != 0, quests, at, ct)
            .ConfigureAwait(false);
        var result = await inventory.ApplyAsync(session, plan.Effects, at, ct).ConfigureAwait(false);
        var points = await character.RemainingSkillPointsAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Updated skill points unavailable.");
        return new(result.Inventory, result.Applied.Changes<CharacterLevelChange>().Single().Progress, points, plan.CompletedQuestIds);
    }
}
