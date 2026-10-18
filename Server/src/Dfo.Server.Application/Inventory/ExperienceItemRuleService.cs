using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Inventory;

internal sealed class ExperienceItemRuleService(
    IExperienceItemDefinitionSource definitions,
    IExperienceRuleService experience) : IExperienceItemRuleService
{
    public ValueTask<ExperienceItemDefinition?> RuleForAsync(int itemId, CancellationToken cancellationToken) =>
        definitions.GetAsync(itemId, cancellationToken);

    public ExperienceItemDecision Decide(
        ExperienceItemDefinition definition,
        CharacterProgressionState progress)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsReadable || progress.Level == 0)
            return new(ItemUsability.NotAUsableItem, 0, null);
        if (progress.Level < definition.MinimumLevel)
            return new(ItemUsability.LevelTooLow, 0, null);

        var scale = experience.CharacterScale();
        if (progress.Level > definition.MaximumLevel || progress.Level >= scale.TopLevel)
            return new(ItemUsability.NotAUsableItem, 0, null);

        var calculatedGain = definition.GrantKind switch
        {
            ExperienceItemGrantKind.Fixed => (ulong)definition.Value,
            ExperienceItemGrantKind.PercentOfCurrentLevel =>
                ((ulong)scale.ExperienceForLevel(checked((byte)(progress.Level + 1)))
                    - scale.ExperienceForLevel(progress.Level)) * definition.Value / 100UL,
            _ => throw new ArgumentOutOfRangeException(nameof(definition)),
        };
        if (calculatedGain is 0 or > uint.MaxValue)
            return new(ItemUsability.NotAUsableItem, 0, null);
        var gain = checked((uint)calculatedGain);

        var award = experience.DecideAward(progress, new ExperienceBreakdown(Base: gain));
        return new(ItemUsability.Allowed, gain, new CharacterExperienceEffect(award));
    }
}
