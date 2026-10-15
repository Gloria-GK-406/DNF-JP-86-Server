using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ExperienceItemDecision(ItemUsability Status, uint Gain,
    CharacterExperienceEffect? Effect);

public interface IExperienceItemRuleService
{
    ValueTask<ExperienceItemDefinition?> RuleForAsync(int itemId, CancellationToken cancellationToken);
    ExperienceItemDecision Decide(ExperienceItemDefinition definition, CharacterProgressionState progress);
}

public interface IExperienceItemDomainService
{
    /// <summary>
    /// Applies an experience item through the ordinary item-use effects transaction. Null means
    /// the item is not an experience item and the caller may continue with its generic behavior.
    /// </summary>
    ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source,
        ItemReference item, CancellationToken cancellationToken);
}
